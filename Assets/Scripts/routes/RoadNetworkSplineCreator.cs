using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.Splines.ExtrusionShapes;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// สร้าง Spline และเก็บ Road Graph สำหรับให้รถวิ่งข้ามถนนด้วย NodeS / NodeE / LaneS / LaneE
/// แนวคิด:
/// - NodeS = t=0, NodeE = t=1
/// - dir=0 วิ่งจาก NodeS ไป NodeE
/// - dir=1 วิ่งจาก NodeE ไป NodeS
/// - currentPos วัดจาก NodeS เสมอ
/// - LaneS / LaneE คือ mapping จากเลนที่เลือก ไปยังถนนถัดไป
[ExecuteAlways]
[RequireComponent(typeof(SplineContainer), typeof(SplineExtrude))]
public class RoadNetworkSplineCreator : MonoBehaviour
{
    public enum MoveMode
    {
        Forward,
        Backward
    }

    [Serializable]
    public struct RoadConnection
    {
        [Tooltip("หมายเลขถนนปลายทาง ใช้แบบ 1-based เช่น Road 1, Road 2")]
        public int roadNo;

        [Tooltip("0 = เข้า NodeS ของถนนปลายทาง, 1 = เข้า NodeE ของถนนปลายทาง")]
        public int enterNode;

        public RoadConnection(int roadNo, int enterNode)
        {
            this.roadNo = roadNo;
            this.enterNode = enterNode;
        }

        public bool IsValid => roadNo > 0 && (enterNode == 0 || enterNode == 1);

        public override string ToString()
        {
            return $"Road[{roadNo}], enterNode={enterNode}";
        }
    }

    [Serializable]
    public class RoadData
    {
        public int id;
        public float length;

        [Header("Physical Connections")]
        public RoadConnection[] nodeS = Array.Empty<RoadConnection>();
        public RoadConnection[] nodeE = Array.Empty<RoadConnection>();

        [Header("Lane Routing")]
        public RoadConnection[] laneS = Array.Empty<RoadConnection>();
        public RoadConnection[] laneE = Array.Empty<RoadConnection>();

        [Header("Default Lane")]
        public int defaultLaneS;
        public int defaultLaneE;
    }

    [Serializable]
    public class CarState
    {
        [Tooltip("หมายเลขถนนปัจจุบัน ใช้แบบ 1-based")]
        public int roadNo = 1;

        [Tooltip("ตำแหน่งบนถนน วัดจาก NodeS เสมอ")]
        public float currentPos = 0f;

        [Tooltip("0 = หันไปทาง NodeE, 1 = หันไปทาง NodeS")]
        public int dir = 0;

        [Tooltip("เลน/ทางเลือกปัจจุบัน ใช้เป็น index ของ LaneS หรือ LaneE")]
        public int currentLane = 0;
    }

    [Header("Auto Route")]
    [SerializeField] private bool rebuildOnStart = true;
    [SerializeField] private bool rebuildInEditor = true;
    [SerializeField] private float routeScale = 1f;
    [SerializeField] private bool roundCorners = true;
    [SerializeField] private float cornerRadius = 8f;
    [SerializeField, Range(1, 16)] private int cornerSegments = 8;

    [Header("Road Extrude")]
    [SerializeField] private bool createRoadMesh = true;
    [SerializeField] private float laneWidth = 3f;
    [SerializeField] private int laneCount = 2;
    [SerializeField] private float segmentsPerUnit = 1f;
    [SerializeField] private Material roadMaterial;
    [SerializeField] private string roadLayerName = "Road";
    [SerializeField] private bool addRoadCollider = true;

    [Header("Debug Car")]
    [SerializeField] private Transform debugCar;
    [SerializeField] private CarState debugCarState = new CarState();
    [SerializeField] private float debugMoveDistance = 10f;

    private SplineContainer splineContainer;
    private SplineExtrude splineExtrude;

    private const float RoadShapeWidth = 1.2f;
    private static readonly FieldInfo ShapeField = typeof(SplineExtrude).GetField(
        "m_Shape",
        BindingFlags.Instance | BindingFlags.NonPublic);

    // -------------------------------------------------------------------------
    // 1) Geometry Layer: จุด spline ของถนนแต่ละเส้น
    // -------------------------------------------------------------------------
    private static readonly Vector3[][] RouteSegments =
    {
        // Road 1: ถนนกลางเมือง ซ้าย -> ขวา
        new[]
        {
            Point(-50f, 0f, 0f),
            Point(0f, 0f, 0f),
            Point(50f, 0f, 0f)
        },

        // Road 2: ต่อจาก Road 1 ไปทางขวา
        new[]
        {
            Point(50f, 0f, 0f),
            Point(120f, 0f, 0f)
        },

        // Road 3: ต่อจาก Road 1 ไปทางซ้าย
        new[]
        {
            Point(-50f, 0f, 0f),
            Point(-120f, 0f, 0f)
        },

        // Road 4: ถนนโค้งด้านขวาบน
        new[]
        {
            Point(120f, 0f, 0f),
            Point(120f, 0f, 70f),
            Point(50f, 0f, 70f)
        },

        // Road 5: ถนนลงล่างจากกลางเมือง
        new[]
        {
            Point(0f, 0f, 0f),
            Point(0f, 0f, -80f)
        },

        // Road 6: ถนนขึ้นบนจากกลางเมือง
        new[]
        {
            Point(0f, 0f, 0f),
            Point(0f, 0f, 90f)
        }
    };

    // -------------------------------------------------------------------------
    // 2) Graph Layer: NodeS / NodeE / LaneS / LaneE
    // -------------------------------------------------------------------------
    private static readonly RoadData[] Roads =
    {
        new RoadData
        {
            id = 1,
            length = 100f,
            nodeS = new[] { new RoadConnection(3, 0) },
            nodeE = new[] { new RoadConnection(2, 0), new RoadConnection(4, 0) },
            laneS = new[] { new RoadConnection(3, 0) },
            laneE = new[] { new RoadConnection(2, 0), new RoadConnection(4, 0) },
            defaultLaneS = 0,
            defaultLaneE = 0
        },

        new RoadData
        {
            id = 2,
            length = 70f,
            nodeS = new[] { new RoadConnection(1, 1) },
            nodeE = new[] { new RoadConnection(4, 0) },
            laneS = new[] { new RoadConnection(1, 1) },
            laneE = new[] { new RoadConnection(4, 0) },
            defaultLaneS = 0,
            defaultLaneE = 0
        },

        new RoadData
        {
            id = 3,
            length = 70f,
            nodeS = Array.Empty<RoadConnection>(),
            nodeE = new[] { new RoadConnection(1, 0) },
            laneS = Array.Empty<RoadConnection>(),
            laneE = new[] { new RoadConnection(1, 0) },
            defaultLaneS = 0,
            defaultLaneE = 0
        },

        new RoadData
        {
            id = 4,
            length = 140f,
            nodeS = new[] { new RoadConnection(2, 1), new RoadConnection(1, 1) },
            nodeE = new[] { new RoadConnection(6, 1) },
            laneS = new[] { new RoadConnection(2, 1), new RoadConnection(1, 1) },
            laneE = new[] { new RoadConnection(6, 1) },
            defaultLaneS = 0,
            defaultLaneE = 0
        },

        new RoadData
        {
            id = 5,
            length = 80f,
            nodeS = new[] { new RoadConnection(1, 0) },
            nodeE = Array.Empty<RoadConnection>(),
            laneS = new[] { new RoadConnection(1, 0) },
            laneE = Array.Empty<RoadConnection>(),
            defaultLaneS = 0,
            defaultLaneE = 0
        },

        new RoadData
        {
            id = 6,
            length = 90f,
            nodeS = new[] { new RoadConnection(1, 0) },
            nodeE = new[] { new RoadConnection(4, 1) },
            laneS = new[] { new RoadConnection(1, 0) },
            laneE = new[] { new RoadConnection(4, 1) },
            defaultLaneS = 0,
            defaultLaneE = 0
        }
    };

    private void Awake()
    {
        CacheComponents();
    }

    private void Start()
    {
        if (Application.isPlaying && rebuildOnStart)
        {
            RebuildRoute();
            SnapDebugCarToRoad();
        }
    }

    private void Reset()
    {
        RebuildRoute();
    }

    private void OnValidate()
    {
        if (!rebuildInEditor || Application.isPlaying)
        {
            return;
        }

#if UNITY_EDITOR
        EditorApplication.delayCall -= RebuildRouteInEditor;
        EditorApplication.delayCall += RebuildRouteInEditor;
#endif
    }

    private void Update()
    {
        if (!Application.isPlaying || debugCar == null)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            MoveCarLoop(debugCarState, debugMoveDistance, MoveMode.Forward);
            SnapDebugCarToRoad();
        }

        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            MoveCarLoop(debugCarState, debugMoveDistance, MoveMode.Backward);
            SnapDebugCarToRoad();
        }

        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            ChangeLane(debugCarState, -1);
            Debug.Log($"Change lane left: lane={debugCarState.currentLane}");
        }

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            ChangeLane(debugCarState, 1);
            Debug.Log($"Change lane right: lane={debugCarState.currentLane}");
        }
    }

    [ContextMenu("Rebuild Route")]
    public void RebuildRoute()
    {
        CacheComponents();

        List<Spline> splines = new List<Spline>(RouteSegments.Length);
        for (int i = 0; i < RouteSegments.Length; i++)
        {
            splines.Add(CreateSpline(RouteSegments[i]));
        }

        splineContainer.Splines = splines;
        SetupRoadExtrude();
    }

    [ContextMenu("Debug Move Forward")]
    public void DebugMoveForward()
    {
        MoveCarLoop(debugCarState, debugMoveDistance, MoveMode.Forward);
        SnapDebugCarToRoad();
    }

    [ContextMenu("Debug Move Backward")]
    public void DebugMoveBackward()
    {
        MoveCarLoop(debugCarState, debugMoveDistance, MoveMode.Backward);
        SnapDebugCarToRoad();
    }

    /// <summary>
    /// MoveCarLoop ใช้ while เพื่อใช้ remain ข้ามได้หลายถนนในคำสั่งเดียว
    /// </summary>
    public void MoveCarLoop(CarState car, float distance, MoveMode mode)
    {
        float remain = Mathf.Max(0f, distance);
        int safety = 0;

        while (remain > 0.001f && safety < 32)
        {
            remain = MoveCarOneRoad(car, remain, mode);
            safety++;
        }

        if (safety >= 32)
        {
            Debug.LogWarning("MoveCarLoop stopped by safety limit. Check road graph for circular zero-length routes.");
        }
    }

    /// <summary>
    /// ขยับรถภายในถนนเดียว ถ้าเกินปลายถนนจะย้ายไปถนนใหม่แล้วคืน remain
    /// </summary>
    private float MoveCarOneRoad(CarState car, float distance, MoveMode mode)
    {
        RoadData currentRoad = GetRoad(car.roadNo);
        if (currentRoad == null)
        {
            Debug.LogWarning($"Invalid roadNo: {car.roadNo}");
            return 0f;
        }

        float signedDistance = GetSignedDistance(car.dir, distance, mode);
        float newPos = car.currentPos + signedDistance;

        if (newPos < 0f)
        {
            float remain = -newPos;
            car.currentPos = 0f;

            RoadConnection next = GetNextConnection(currentRoad, towardNodeE: false, car.currentLane);
            if (!next.IsValid)
            {
                return 0f;
            }

            EnterNewRoad(car, next, remain);
            return remain;
        }

        if (newPos > currentRoad.length)
        {
            float remain = newPos - currentRoad.length;
            car.currentPos = currentRoad.length;

            RoadConnection next = GetNextConnection(currentRoad, towardNodeE: true, car.currentLane);
            if (!next.IsValid)
            {
                return 0f;
            }

            EnterNewRoad(car, next, remain);
            return remain;
        }

        car.currentPos = newPos;
        return 0f;
    }

    private float GetSignedDistance(int dir, float distance, MoveMode mode)
    {
        bool forwardToNodeE = dir == 0;

        if (mode == MoveMode.Forward)
        {
            return forwardToNodeE ? distance : -distance;
        }

        return forwardToNodeE ? -distance : distance;
    }

    private RoadConnection GetNextConnection(RoadData road, bool towardNodeE, int currentLane)
    {
        RoadConnection[] lanes = towardNodeE ? road.laneE : road.laneS;

        if (lanes == null || lanes.Length == 0)
        {
            return default;
        }

        int index = Mathf.Clamp(currentLane, 0, lanes.Length - 1);
        return lanes[index];
    }

    private void EnterNewRoad(CarState car, RoadConnection connection, float remain)
    {
        RoadData nextRoad = GetRoad(connection.roadNo);
        if (nextRoad == null)
        {
            Debug.LogWarning($"Invalid next road: {connection.roadNo}");
            return;
        }

        car.roadNo = connection.roadNo;

        if (connection.enterNode == 0)
        {
            car.currentPos = Mathf.Clamp(remain, 0f, nextRoad.length);
            car.dir = 0;
            car.currentLane = Mathf.Clamp(nextRoad.defaultLaneE, 0, Mathf.Max(0, nextRoad.laneE.Length - 1));
        }
        else
        {
            car.currentPos = Mathf.Clamp(nextRoad.length - remain, 0f, nextRoad.length);
            car.dir = 1;
            car.currentLane = Mathf.Clamp(nextRoad.defaultLaneS, 0, Mathf.Max(0, nextRoad.laneS.Length - 1));
        }

        Debug.Log($"Enter {connection}. pos={car.currentPos:0.00}, dir={car.dir}, lane={car.currentLane}");
    }

    public void ChangeLane(CarState car, int delta)
    {
        RoadData road = GetRoad(car.roadNo);
        if (road == null)
        {
            return;
        }

        RoadConnection[] lanes = car.dir == 0 ? road.laneE : road.laneS;
        if (lanes == null || lanes.Length == 0)
        {
            car.currentLane = 0;
            return;
        }

        car.currentLane = Mathf.Clamp(car.currentLane + delta, 0, lanes.Length - 1);
    }

    public Vector3 EvaluateRoadPosition(CarState car)
    {
        int index = car.roadNo - 1;
        if (index < 0 || index >= RouteSegments.Length)
        {
            return transform.position;
        }

        RoadData road = GetRoad(car.roadNo);
        if (road == null || road.length <= Mathf.Epsilon)
        {
            return transform.position;
        }

        float t = Mathf.Clamp01(car.currentPos / road.length);
        Vector3 localPosition = EvaluatePolyline(RouteSegments[index], t) * routeScale;
        return transform.TransformPoint(localPosition);
    }

    private void SnapDebugCarToRoad()
    {
        if (debugCar == null)
        {
            return;
        }

        debugCar.position = EvaluateRoadPosition(debugCarState);

        RoadData road = GetRoad(debugCarState.roadNo);
        if (road != null)
        {
            Vector3 forward = EvaluateRoadForward(debugCarState);
            if (forward.sqrMagnitude > 0.001f)
            {
                debugCar.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }
    }

    public Vector3 EvaluateRoadForward(CarState car)
    {
        int index = car.roadNo - 1;
        if (index < 0 || index >= RouteSegments.Length)
        {
            return transform.forward;
        }

        RoadData road = GetRoad(car.roadNo);
        if (road == null || road.length <= Mathf.Epsilon)
        {
            return transform.forward;
        }

        float t = Mathf.Clamp01(car.currentPos / road.length);
        float t2 = Mathf.Clamp01(t + 0.01f);
        Vector3 a = EvaluatePolyline(RouteSegments[index], t) * routeScale;
        Vector3 b = EvaluatePolyline(RouteSegments[index], t2) * routeScale;

        Vector3 forward = transform.TransformDirection((b - a).normalized);
        return car.dir == 0 ? forward : -forward;
    }

    public RoadData GetRoadData(int roadNo) => GetRoad(roadNo);

    private RoadData GetRoad(int roadNo)
    {
        int index = roadNo - 1;
        if (index < 0 || index >= Roads.Length)
        {
            return null;
        }

        return Roads[index];
    }

    private Spline CreateSpline(IReadOnlyList<Vector3> points)
    {
        List<Vector3> renderPoints = BuildRenderPoints(points);
        Spline spline = new Spline(renderPoints.Count, false);

        for (int i = 0; i < renderPoints.Count; i++)
        {
            Vector3 scaledPoint = renderPoints[i] * routeScale;
            spline.Add(ToFloat3(scaledPoint), TangentMode.Linear);
        }

        return spline;
    }

    private List<Vector3> BuildRenderPoints(IReadOnlyList<Vector3> points)
    {
        List<Vector3> renderPoints = new List<Vector3>();

        if (!roundCorners || points.Count < 3 || cornerRadius <= 0f)
        {
            renderPoints.AddRange(points);
            return renderPoints;
        }

        renderPoints.Add(points[0]);

        for (int i = 1; i < points.Count - 1; i++)
        {
            AddRoundedCorner(renderPoints, points[i - 1], points[i], points[i + 1]);
        }

        renderPoints.Add(points[points.Count - 1]);
        return renderPoints;
    }

    private void AddRoundedCorner(List<Vector3> renderPoints, Vector3 previous, Vector3 corner, Vector3 next)
    {
        Vector3 incoming = corner - previous;
        Vector3 outgoing = next - corner;
        incoming.y = 0f;
        outgoing.y = 0f;

        float incomingLength = incoming.magnitude;
        float outgoingLength = outgoing.magnitude;

        if (incomingLength <= Mathf.Epsilon || outgoingLength <= Mathf.Epsilon)
        {
            renderPoints.Add(corner);
            return;
        }

        Vector3 incomingDirection = incoming / incomingLength;
        Vector3 outgoingDirection = outgoing / outgoingLength;
        float turnAngle = Vector3.Angle(incomingDirection, outgoingDirection);

        if (turnAngle <= 1f || turnAngle >= 179f)
        {
            renderPoints.Add(corner);
            return;
        }

        float trimDistance = Mathf.Min(cornerRadius, incomingLength * 0.45f, outgoingLength * 0.45f);
        Vector3 arcStart = corner - incomingDirection * trimDistance;
        Vector3 arcEnd = corner + outgoingDirection * trimDistance;

        if (renderPoints.Count == 0 || Vector3.Distance(renderPoints[renderPoints.Count - 1], arcStart) > 0.001f)
        {
            renderPoints.Add(arcStart);
        }

        int segmentCount = Mathf.Max(1, cornerSegments);
        for (int segment = 1; segment <= segmentCount; segment++)
        {
            float t = segment / (float)segmentCount;
            renderPoints.Add(QuadraticBezier(arcStart, corner, arcEnd, t));
        }
    }

    private static Vector3 EvaluatePolyline(IReadOnlyList<Vector3> points, float t)
    {
        if (points == null || points.Count == 0)
        {
            return Vector3.zero;
        }

        if (points.Count == 1)
        {
            return points[0];
        }

        float totalLength = GetPolylineLength(points);
        if (totalLength <= Mathf.Epsilon)
        {
            return points[0];
        }

        float targetDistance = Mathf.Clamp01(t) * totalLength;
        float walked = 0f;

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[i + 1];
            float segmentLength = Vector3.Distance(a, b);

            if (walked + segmentLength >= targetDistance)
            {
                float localT = (targetDistance - walked) / segmentLength;
                return Vector3.Lerp(a, b, localT);
            }

            walked += segmentLength;
        }

        return points[points.Count - 1];
    }

    private static float GetPolylineLength(IReadOnlyList<Vector3> points)
    {
        float length = 0f;

        for (int i = 0; i < points.Count - 1; i++)
        {
            length += Vector3.Distance(points[i], points[i + 1]);
        }

        return length;
    }

    private void CacheComponents()
    {
        splineContainer = GetOrAddComponent<SplineContainer>();
        splineExtrude = GetOrAddComponent<SplineExtrude>();
    }

    private void SetupRoadExtrude()
    {
        if (splineExtrude == null)
        {
            return;
        }

        splineExtrude.enabled = createRoadMesh;
        splineExtrude.Container = splineContainer;
        splineExtrude.RebuildOnSplineChange = true;
        splineExtrude.RebuildFrequency = 30;
        splineExtrude.SegmentsPerUnit = segmentsPerUnit;
        splineExtrude.Capped = true;
        splineExtrude.Range = new Vector2(0f, 1f);

        float roadWidth = Mathf.Max(0.1f, laneWidth * Mathf.Max(1, laneCount));
        splineExtrude.Radius = roadWidth / RoadShapeWidth;
        SetRoadExtrudeShape(splineExtrude);

        if (TryGetComponent(out MeshRenderer meshRenderer) && roadMaterial != null)
        {
            meshRenderer.sharedMaterial = roadMaterial;
        }

        SetRoadLayer();
        SetupRoadCollider();
        splineExtrude.Rebuild();
    }

    private void SetRoadLayer()
    {
        EnsureRoadLayerExists();

        int roadLayer = LayerMask.NameToLayer(roadLayerName);
        if (roadLayer < 0)
        {
            Debug.LogWarning($"Layer '{roadLayerName}' was not found. Create it in Project Settings > Tags and Layers, then rebuild the route.", this);
            return;
        }

        gameObject.layer = roadLayer;
    }

    private void EnsureRoadLayerExists()
    {
#if UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(roadLayerName) || LayerMask.NameToLayer(roadLayerName) >= 0)
        {
            return;
        }

        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);

            if (!string.IsNullOrEmpty(layer.stringValue))
            {
                continue;
            }

            layer.stringValue = roadLayerName;
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            return;
        }

        Debug.LogWarning($"Could not create layer '{roadLayerName}' because all user layers are full.", this);
#endif
    }

    private void SetupRoadCollider()
    {
        if (!addRoadCollider)
        {
            return;
        }

        GetOrAddComponent<MeshCollider>();
    }

    private static void SetRoadExtrudeShape(SplineExtrude extrude)
    {
        if (ShapeField == null)
        {
            return;
        }

        ShapeField.SetValue(extrude, new Road());
    }

    private static Vector3 Point(float x, float y, float z)
    {
        return new Vector3(x, y, z);
    }

    private static float3 ToFloat3(Vector3 value)
    {
        return new float3(value.x, value.y, value.z);
    }

    private static Vector3 QuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
    {
        float oneMinusT = 1f - t;
        return oneMinusT * oneMinusT * start +
               2f * oneMinusT * t * control +
               t * t * end;
    }

    private T GetOrAddComponent<T>() where T : Component
    {
        if (TryGetComponent(out T component))
        {
            return component;
        }

        return gameObject.AddComponent<T>();
    }

#if UNITY_EDITOR
    private void RebuildRouteInEditor()
    {
        if (this == null || Application.isPlaying)
        {
            return;
        }

        RebuildRoute();
    }
#endif
}
