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
        [Tooltip("หมายเลขถนนปลายทาง ใช้ Road 1, Road 2")]
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

        [Header("Default Road")]
        public int defaultRoadS;
        public int defaultRoadE;
    }

    [Serializable]
    private class RoadNetworkJson
    {
        public RoadJson[] roads = Array.Empty<RoadJson>();
    }

    [Serializable]
    private class RoadJson
    {
        public int id;
        public RoadConnection[] nodeS = Array.Empty<RoadConnection>();
        public RoadConnection[] nodeE = Array.Empty<RoadConnection>();
        public int defaultRoadS;
        public int defaultRoadE;
    }

    [Serializable]
    public class RouteHistoryEntry
    {
        public int roadNo;

        [Tooltip("node ที่รถเข้าตอนเดินหน้า (0 = NodeS, 1 = NodeE)")]
        public int enterNode;

        [Tooltip("ทิศที่รถหันตอนเดินหน้าบนรางนี้ (0 = หัน NodeE, 1 = หัน NodeS)")]
        public int dirOnEnter;

        public RouteHistoryEntry(int roadNo, int enterNode, int dirOnEnter)
        {
            this.roadNo = roadNo;
            this.enterNode = enterNode;
            this.dirOnEnter = dirOnEnter;
        }

        public override string ToString()
        {
            return $"Rail {roadNo} (enter={enterNode}, dir={dirOnEnter})";
        }
    }

    [Serializable]
    public class CarState
    {
        [Tooltip("หมายเลขถนนปัจจุบัน")]
        public int roadNo = 1;

        [Tooltip("ตำแหน่งบนถนน วัดจาก NodeS เสมอ")]
        public float currentPos = 0f;

        [Tooltip("0 = หันไปทาง NodeE, 1 = หันไปทาง NodeS")]
        public int dir = 0;

        [Tooltip("เลน/ทางเลือกปัจจุบัน ใช้เป็น index ของ LaneS หรือ LaneE")]
        public int currentLane = 0;

        [Header("Route History")]
        [Tooltip("รางที่ผ่านมาแล้วตามลำดับจริง historyIndex ชี้รางปัจจุบัน")]
        public List<RouteHistoryEntry> history = new List<RouteHistoryEntry>();
        public int historyIndex = -1;

        [Header("Pending Selection (ยังไม่ commit จนกว่าจะข้ามเข้ารางใหม่จริง)")]
        public int pendingNextRoad = -1;
        public int pendingEnterNode = -1;
        public bool hasPendingSelection;

        [Tooltip("ทางเลือกที่ swipe ต่างจาก history ด้านหน้าหรือไม่")]
        public bool routeChoiceChanged;
    }

    [Header("Auto Route")]
    [SerializeField] private string roadDataResourcePath = "data/road_data";
    [SerializeField] private TextAsset roadDataJson;
    [SerializeField] private bool generateConnectionsFromSpline = true;
    [SerializeField] private bool rebuildOnStart = true;
    [SerializeField] private bool rebuildInEditor = true;

    [Header("Road Model Fitting")]
    [SerializeField] private bool fitToRoadModel = true;
    [SerializeField] private Transform roadModel;
    [SerializeField] private string roadModelObjectName = "FBXRoad";
    [SerializeField, Min(0f)] private float roadEdgeInset = 3.5f;
    [Tooltip("Additional offset after fitting to the road model, in this object's local X/Z axes.")]
    [SerializeField] private Vector2 routeOffset;
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
    // จุด spline ของถนนแต่ละเส้น: สี่แยกกลางเมือง
    //
    //                 | Road 3 (เหนือ)
    //                 |
    //          7 ~~ N ~~ 8
    //   Road 1 ---- W-+-E ---- Road 2
    //         10 ~~ S ~~ 9
    //                 |
    //                 | Road 4 (ใต้)
    //
    // - Road 1-4  = ทิศถนนจากขอบแผนที่มาจบที่ปากแยก (W/E/N/S)
    // - Road 5-6  = ทางตรงวิ่งข้ามแยก (W->E และ N->S)
    // - Road 7-10 = โค้ง Bezier มุมแยก ใช้จุดกลางแยกเป็น control point
    // -------------------------------------------------------------------------
    private const float ArmLength = 100f;
    private const float JunctionRadius = 12f;
    private const int JunctionCurveSamples = 12;
    private const float DestinationCornerRadius = 16f;
    private const float InnerLoopHalfSize = 50f;
    private const float NewJunctionRadius = 8f;
    private const float InnerCornerRadius = 8f;
    private const float EndpointMatchSqrDistance = 0.0001f;

    private static readonly Vector3 JunctionCenter = Point(0f, 0f, 0f);
    private static readonly Vector3 WestJunction = Point(-InnerLoopHalfSize, 0f, 0f);
    private static readonly Vector3 NorthJunction = Point(0f, 0f, InnerLoopHalfSize);
    private static readonly Vector3 EastJunction = Point(InnerLoopHalfSize, 0f, 0f);
    private static readonly Vector3 SouthJunction = Point(0f, 0f, -InnerLoopHalfSize);
    private static readonly Vector3 WestInner = Point(-JunctionRadius, 0f, 0f);
    private static readonly Vector3 EastInner = Point(JunctionRadius, 0f, 0f);
    private static readonly Vector3 NorthInner = Point(0f, 0f, JunctionRadius);
    private static readonly Vector3 SouthInner = Point(0f, 0f, -JunctionRadius);
    private static readonly Vector3 WestDestination = Point(-ArmLength, 0f, 0f);
    private static readonly Vector3 EastDestination = Point(ArmLength, 0f, 0f);
    private static readonly Vector3 NorthDestination = Point(0f, 0f, ArmLength);
    private static readonly Vector3 SouthDestination = Point(0f, 0f, -ArmLength);

    private static readonly Vector3[][] LegacyRouteSegments =
    {
        // Road 1: ทิศตะวันตก NodeS = ขอบแผนที่, NodeE = ปากแยก
        new[] { WestDestination, WestInner },

        // Road 2: ทิศตะวันออก NodeS = ปากแยก, NodeE = ขอบแผนที่
        new[] { EastInner, EastDestination },

        // Road 3: ทิศเหนือ NodeS = ปากแยก, NodeE = ขอบแผนที่
        new[] { NorthInner, NorthDestination },

        // Road 4: ทิศใต้ NodeS = ปากแยก, NodeE = ขอบแผนที่
        new[] { SouthInner, SouthDestination },

        // Road 5: ทางตรงข้ามแยก ตะวันตก -> ตะวันออก
        new[] { WestInner, EastInner },

        // Road 6: ทางตรงข้ามแยก เหนือ -> ใต้
        new[] { NorthInner, SouthInner },

        // Road 7: โค้ง เชื่อม ตะวันตก <-> เหนือ
        BezierCurvePoints(WestInner, JunctionCenter, NorthInner),

        // Road 8: โค้ง เชื่อม เหนือ <-> ตะวันออก
        BezierCurvePoints(NorthInner, JunctionCenter, EastInner),

        // Road 9: โค้ง เชื่อม ตะวันออก <-> ใต้
        BezierCurvePoints(EastInner, JunctionCenter, SouthInner),

        // Road 10: โค้ง เชื่อม ใต้ <-> ตะวันตก
        BezierCurvePoints(SouthInner, JunctionCenter, WestInner),

        // Outer loop: every destination becomes a real junction.
        RoundedCornerPoints(
            WestDestination,
            Point(-ArmLength, 0f, ArmLength),
            NorthDestination,
            DestinationCornerRadius),
        RoundedCornerPoints(
            NorthDestination,
            Point(ArmLength, 0f, ArmLength),
            EastDestination,
            DestinationCornerRadius),
        RoundedCornerPoints(
            EastDestination,
            Point(ArmLength, 0f, -ArmLength),
            SouthDestination,
            DestinationCornerRadius),
        RoundedCornerPoints(
            SouthDestination,
            Point(-ArmLength, 0f, -ArmLength),
            WestDestination,
            DestinationCornerRadius)
    };

    // เส้นที่ sample จาก เชื่อม มาแล้ว ไม่ต้อง round corner ซ้ำตอนสร้าง spline
    private static readonly bool[] LegacyPreSampledCurves =
    {
        false, false, false, false, false, false, true, true, true, true,
        true, true, true, true
    };

    // 1-14 retain their previous meaning. Roads 15-22 add the inner
    // radial/loop segments, and Roads 23-46 add four complete junctions.
    private static readonly Vector3[][] RouteSegments = BuildRouteSegments();
    private static readonly bool[] PreSampledCurves = BuildPreSampledCurves();

    //NodeS / NodeE / LaneS / LaneE
    private RoadData[] roads;
    private Vector3[][] activeRouteSegments;

    private RoadData[] BuildRoads()
    {
        Vector3[][] routes = GetActiveRouteSegments();
        RoadData[] generatedRoads = new RoadData[routes.Length];

        for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
        {
            generatedRoads[routeIndex] = new RoadData
            {
                id = routeIndex + 1,
                nodeS = FindEndpointConnections(routes, routeIndex, 0),
                nodeE = FindEndpointConnections(routes, routeIndex, 1)
            };
        }

        PrepareRoads(generatedRoads);
        return generatedRoads;
    }

    private static RoadConnection[] FindEndpointConnections(
        Vector3[][] routes,
        int sourceRouteIndex,
        int sourceNode)
    {
        Vector3[] sourceRoute = routes[sourceRouteIndex];
        Vector3 endpoint = sourceNode == 0
            ? sourceRoute[0]
            : sourceRoute[sourceRoute.Length - 1];
        List<RoadConnection> connections = new List<RoadConnection>();

        for (int targetRouteIndex = 0; targetRouteIndex < routes.Length; targetRouteIndex++)
        {
            if (targetRouteIndex == sourceRouteIndex)
            {
                continue;
            }

            Vector3[] targetRoute = routes[targetRouteIndex];
            if ((targetRoute[0] - endpoint).sqrMagnitude <= EndpointMatchSqrDistance)
            {
                connections.Add(new RoadConnection(targetRouteIndex + 1, 0));
            }

            if ((targetRoute[targetRoute.Length - 1] - endpoint).sqrMagnitude
                <= EndpointMatchSqrDistance)
            {
                connections.Add(new RoadConnection(targetRouteIndex + 1, 1));
            }
        }

        return connections.ToArray();
    }

    private RoadData[] BuildLegacyRoads()
    {
        RoadData[] roads =
        {
            // Road 1: ทิศตะวันตก เข้าแยกที่ NodeE เลือกได้ ตรง/เลี้ยวเหนือ/เลี้ยวใต้
            new RoadData
            {
                id = 1,
                nodeS = new[] { new RoadConnection(11, 0), new RoadConnection(14, 1) },
                nodeE = new[] { new RoadConnection(5, 0), new RoadConnection(7, 0), new RoadConnection(10, 1) },
                laneS = new[] { new RoadConnection(11, 0), new RoadConnection(14, 1) },
                laneE = new[] { new RoadConnection(5, 0), new RoadConnection(7, 0), new RoadConnection(10, 1) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 2: ทิศตะวันออก เข้าแยกที่ NodeS เลือกได้ ตรง/เลี้ยวใต้/เลี้ยวเหนือ
            new RoadData
            {
                id = 2,
                nodeS = new[] { new RoadConnection(5, 1), new RoadConnection(9, 0), new RoadConnection(8, 1) },
                nodeE = new[] { new RoadConnection(12, 1), new RoadConnection(13, 0) },
                laneS = new[] { new RoadConnection(5, 1), new RoadConnection(9, 0), new RoadConnection(8, 1) },
                laneE = new[] { new RoadConnection(12, 1), new RoadConnection(13, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 3: ทิศเหนือ เข้าแยกที่ NodeS เลือกได้ ตรง/เลี้ยวตะวันออก/เลี้ยวตะวันตก
            new RoadData
            {
                id = 3,
                nodeS = new[] { new RoadConnection(6, 0), new RoadConnection(8, 0), new RoadConnection(7, 1) },
                nodeE = new[] { new RoadConnection(11, 1), new RoadConnection(12, 0) },
                laneS = new[] { new RoadConnection(6, 0), new RoadConnection(8, 0), new RoadConnection(7, 1) },
                laneE = new[] { new RoadConnection(11, 1), new RoadConnection(12, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 4: ทิศใต้ เข้าแยกที่ NodeS เลือกได้ ตรง/เลี้ยวตะวันออก/เลี้ยวตะวันตก
            new RoadData
            {
                id = 4,
                nodeS = new[] { new RoadConnection(6, 1), new RoadConnection(9, 1), new RoadConnection(10, 0) },
                nodeE = new[] { new RoadConnection(13, 1), new RoadConnection(14, 0) },
                laneS = new[] { new RoadConnection(6, 1), new RoadConnection(9, 1), new RoadConnection(10, 0) },
                laneE = new[] { new RoadConnection(13, 1), new RoadConnection(14, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 5: ทางตรงข้ามแยก W->E ปลายทางออกทิศตะวันตก/ตะวันออก
            new RoadData
            {
                id = 5,
                nodeS = new[] { new RoadConnection(1, 1) },
                nodeE = new[] { new RoadConnection(2, 0) },
                laneS = new[] { new RoadConnection(1, 1) },
                laneE = new[] { new RoadConnection(2, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 6: ทางตรงข้ามแยก N->S ปลายทางออกทิศเหนือ/ใต้
            new RoadData
            {
                id = 6,
                nodeS = new[] { new RoadConnection(3, 0) },
                nodeE = new[] { new RoadConnection(4, 0) },
                laneS = new[] { new RoadConnection(3, 0) },
                laneE = new[] { new RoadConnection(4, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 7: โค้ง W<->N
            new RoadData
            {
                id = 7,
                nodeS = new[] { new RoadConnection(1, 1) },
                nodeE = new[] { new RoadConnection(3, 0) },
                laneS = new[] { new RoadConnection(1, 1) },
                laneE = new[] { new RoadConnection(3, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 8: โค้ง N<->E
            new RoadData
            {
                id = 8,
                nodeS = new[] { new RoadConnection(3, 0) },
                nodeE = new[] { new RoadConnection(2, 0) },
                laneS = new[] { new RoadConnection(3, 0) },
                laneE = new[] { new RoadConnection(2, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 9: โค้ง E<->S
            new RoadData
            {
                id = 9,
                nodeS = new[] { new RoadConnection(2, 0) },
                nodeE = new[] { new RoadConnection(4, 0) },
                laneS = new[] { new RoadConnection(2, 0) },
                laneE = new[] { new RoadConnection(4, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 10: โค้ง S<->W
            new RoadData
            {
                id = 10,
                nodeS = new[] { new RoadConnection(4, 0) },
                nodeE = new[] { new RoadConnection(1, 1) },
                laneS = new[] { new RoadConnection(4, 0) },
                laneE = new[] { new RoadConnection(1, 1) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 11: west destination <-> north destination.
            new RoadData
            {
                id = 11,
                nodeS = new[] { new RoadConnection(1, 0), new RoadConnection(14, 1) },
                nodeE = new[] { new RoadConnection(3, 1), new RoadConnection(12, 0) },
                laneS = new[] { new RoadConnection(1, 0), new RoadConnection(14, 1) },
                laneE = new[] { new RoadConnection(3, 1), new RoadConnection(12, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 12: north destination <-> east destination.
            new RoadData
            {
                id = 12,
                nodeS = new[] { new RoadConnection(3, 1), new RoadConnection(11, 1) },
                nodeE = new[] { new RoadConnection(2, 1), new RoadConnection(13, 0) },
                laneS = new[] { new RoadConnection(3, 1), new RoadConnection(11, 1) },
                laneE = new[] { new RoadConnection(2, 1), new RoadConnection(13, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 13: east destination <-> south destination.
            new RoadData
            {
                id = 13,
                nodeS = new[] { new RoadConnection(2, 1), new RoadConnection(12, 1) },
                nodeE = new[] { new RoadConnection(4, 1), new RoadConnection(14, 0) },
                laneS = new[] { new RoadConnection(2, 1), new RoadConnection(12, 1) },
                laneE = new[] { new RoadConnection(4, 1), new RoadConnection(14, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            },

            // Road 14: south destination <-> west destination.
            new RoadData
            {
                id = 14,
                nodeS = new[] { new RoadConnection(4, 1), new RoadConnection(13, 1) },
                nodeE = new[] { new RoadConnection(1, 0), new RoadConnection(11, 0) },
                laneS = new[] { new RoadConnection(4, 1), new RoadConnection(13, 1) },
                laneE = new[] { new RoadConnection(1, 0), new RoadConnection(11, 0) },
                defaultLaneS = 0,
                defaultLaneE = 0
            }
        };

        // ความยาวถนน
        int[] defaultRoadS = { 11, 5, 6, 6, 1, 3, 1, 3, 2, 4, 14, 11, 12, 13 };
        int[] defaultRoadE = { 5, 13, 12, 14, 2, 4, 3, 2, 4, 1, 12, 13, 14, 11 };
        for (int i = 0; i < roads.Length; i++)
        {
            roads[i].defaultRoadS = defaultRoadS[i];
            roads[i].defaultRoadE = defaultRoadE[i];
        }

        PrepareRoads(roads);

        return roads;
    }

    /// สุ่มจุดตาม Quadratic Bezier
    [ContextMenu("Reload Road Data")]
    public void LoadRoadData()
    {
        RebuildActiveRouteSegments();
        roads = generateConnectionsFromSpline
            ? BuildRoads()
            : TryLoadRoadDataFromJson();
        if (roads == null)
        {
            roads = BuildRoads();
        }
    }

    private RoadData[] TryLoadRoadDataFromJson()
    {
        if (string.IsNullOrWhiteSpace(roadDataResourcePath))
        {
            return null;
        }

        TextAsset jsonAsset = roadDataJson != null
            ? roadDataJson
            : Resources.Load<TextAsset>(roadDataResourcePath);
        if (jsonAsset == null)
        {
            Debug.LogWarning(
                $"Road data was not found at Resources/{roadDataResourcePath}.json. Using built-in data.",
                this);
            return null;
        }

        RoadNetworkJson json;
        try
        {
            json = JsonUtility.FromJson<RoadNetworkJson>(jsonAsset.text);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Invalid road data JSON: {exception.Message}. Using built-in data.", this);
            return null;
        }

        if (json == null || json.roads == null || json.roads.Length != RouteSegments.Length)
        {
            Debug.LogWarning(
                $"Road data must contain exactly {RouteSegments.Length} roads. Using built-in data.",
                this);
            return null;
        }

        RoadData[] loadedRoads = new RoadData[RouteSegments.Length];
        for (int i = 0; i < json.roads.Length; i++)
        {
            RoadJson source = json.roads[i];
            int index = source.id - 1;
            if (index < 0 || index >= loadedRoads.Length || loadedRoads[index] != null)
            {
                Debug.LogWarning($"Road data has an invalid or duplicate id: {source.id}. Using built-in data.", this);
                return null;
            }

            loadedRoads[index] = new RoadData
            {
                id = source.id,
                nodeS = source.nodeS ?? Array.Empty<RoadConnection>(),
                nodeE = source.nodeE ?? Array.Empty<RoadConnection>(),
                defaultRoadS = source.defaultRoadS,
                defaultRoadE = source.defaultRoadE
            };
        }

        if (!ValidateConnections(loadedRoads))
        {
            Debug.LogWarning("Road data contains an invalid or non-reciprocal connection. Using built-in data.", this);
            return null;
        }

        PrepareRoads(loadedRoads);
        return loadedRoads;
    }

    private static bool ValidateConnections(RoadData[] loadedRoads)
    {
        for (int roadIndex = 0; roadIndex < loadedRoads.Length; roadIndex++)
        {
            RoadData road = loadedRoads[roadIndex];
            if (road == null || road.id != roadIndex + 1)
            {
                return false;
            }

            if (!ValidateNodeConnections(loadedRoads, road, road.nodeS, 0)
                || !ValidateNodeConnections(loadedRoads, road, road.nodeE, 1))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateNodeConnections(
        RoadData[] loadedRoads,
        RoadData sourceRoad,
        RoadConnection[] connections,
        int sourceNode)
    {
        for (int i = 0; i < connections.Length; i++)
        {
            RoadConnection connection = connections[i];
            if (!connection.IsValid || connection.roadNo > loadedRoads.Length)
            {
                return false;
            }

            RoadData targetRoad = loadedRoads[connection.roadNo - 1];
            if (targetRoad == null)
            {
                return false;
            }

            RoadConnection[] reciprocalConnections = connection.enterNode == 0
                ? targetRoad.nodeS
                : targetRoad.nodeE;
            bool foundReciprocal = false;
            for (int reciprocalIndex = 0;
                 reciprocalIndex < reciprocalConnections.Length;
                 reciprocalIndex++)
            {
                RoadConnection reciprocal = reciprocalConnections[reciprocalIndex];
                if (reciprocal.roadNo == sourceRoad.id && reciprocal.enterNode == sourceNode)
                {
                    foundReciprocal = true;
                    break;
                }
            }

            if (!foundReciprocal)
            {
                return false;
            }
        }

        return true;
    }

    private void PrepareRoads(RoadData[] roadData)
    {
        Vector3[][] routeSegments = GetActiveRouteSegments();
        for (int i = 0; i < roadData.Length; i++)
        {
            RoadData road = roadData[i];
            road.length = GetPolylineLength(routeSegments[i]);
            road.laneS = SortConnectionsLeftToRight(road.id, false, road.nodeS);
            road.laneE = SortConnectionsLeftToRight(road.id, true, road.nodeE);
            road.defaultLaneS = GetDefaultLaneIndex(
                road.id,
                false,
                road.laneS,
                road.defaultRoadS);
            road.defaultLaneE = GetDefaultLaneIndex(
                road.id,
                true,
                road.laneE,
                road.defaultRoadE);

            // A junction maneuver must leave the junction through an external
            // connector. Chaining directly into another junction maneuver can
            // create a loop back into the intersection.
            if (IsJunctionTraversalRoad(road.id))
            {
                road.defaultLaneS = GetJunctionExitLane(
                    road.laneS,
                    road.defaultLaneS);
                road.defaultLaneE = GetJunctionExitLane(
                    road.laneE,
                    road.defaultLaneE);
            }

            road.defaultRoadS = GetRoadNoAtLane(road.laneS, road.defaultLaneS);
            road.defaultRoadE = GetRoadNoAtLane(road.laneE, road.defaultLaneE);
        }
    }

    private int GetJunctionExitLane(RoadConnection[] lanes, int fallbackLane)
    {
        if (lanes == null || lanes.Length == 0)
        {
            return 0;
        }

        for (int i = 0; i < lanes.Length; i++)
        {
            if (!IsJunctionTraversalRoad(lanes[i].roadNo))
            {
                return i;
            }
        }

        return Mathf.Clamp(fallbackLane, 0, lanes.Length - 1);
    }

    private static int GetRoadNoAtLane(RoadConnection[] lanes, int laneIndex)
    {
        return lanes != null && laneIndex >= 0 && laneIndex < lanes.Length
            ? lanes[laneIndex].roadNo
            : 0;
    }

    private RoadConnection[] SortConnectionsLeftToRight(
        int roadNo,
        bool towardNodeE,
        RoadConnection[] connections)
    {
        RoadConnection[] sorted = connections != null
            ? (RoadConnection[])connections.Clone()
            : Array.Empty<RoadConnection>();
        Vector3 currentForward = GetTravelDirection(roadNo, towardNodeE);

        Array.Sort(sorted, (a, b) =>
        {
            float angleA = Vector3.SignedAngle(
                currentForward,
                GetConnectionExitDirection(a),
                Vector3.up);
            float angleB = Vector3.SignedAngle(
                currentForward,
                GetConnectionExitDirection(b),
                Vector3.up);
            return angleA.CompareTo(angleB);
        });

        return sorted;
    }

    private Vector3 GetTravelDirection(int roadNo, bool towardNodeE)
    {
        Vector3[] points = GetActiveRouteSegments()[roadNo - 1];
        if (towardNodeE)
        {
            return (points[points.Length - 1] - points[points.Length - 2]).normalized;
        }

        return (points[0] - points[1]).normalized;
    }

    private Vector3 GetConnectionExitDirection(RoadConnection connection)
    {
        return GetTravelDirection(connection.roadNo, connection.enterNode == 0);
    }

    private int GetDefaultLaneIndex(
        int roadNo,
        bool towardNodeE,
        RoadConnection[] lanes,
        int preferredRoadNo)
    {
        if (lanes == null || lanes.Length <= 1)
        {
            return 0;
        }

        if (preferredRoadNo > 0)
        {
            for (int i = 0; i < lanes.Length; i++)
            {
                if (lanes[i].roadNo == preferredRoadNo)
                {
                    return i;
                }
            }
        }

        Vector3 forward = GetTravelDirection(roadNo, towardNodeE);
        int bestIndex = 0;
        float bestAngle = float.PositiveInfinity;
        for (int i = 0; i < lanes.Length; i++)
        {
            float angle = Mathf.Abs(Vector3.SignedAngle(
                forward,
                GetConnectionExitDirection(lanes[i]),
                Vector3.up));
            if (angle < bestAngle)
            {
                bestAngle = angle;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static Vector3[][] BuildRouteSegments()
    {
        Vector3 centerW = JunctionPort(JunctionCenter, -1f, 0f);
        Vector3 centerE = JunctionPort(JunctionCenter, 1f, 0f);
        Vector3 centerN = JunctionPort(JunctionCenter, 0f, 1f);
        Vector3 centerS = JunctionPort(JunctionCenter, 0f, -1f);

        Vector3 westW = JunctionPort(WestJunction, -1f, 0f);
        Vector3 westE = JunctionPort(WestJunction, 1f, 0f);
        Vector3 westN = JunctionPort(WestJunction, 0f, 1f);
        Vector3 westS = JunctionPort(WestJunction, 0f, -1f);

        Vector3 northW = JunctionPort(NorthJunction, -1f, 0f);
        Vector3 northE = JunctionPort(NorthJunction, 1f, 0f);
        Vector3 northN = JunctionPort(NorthJunction, 0f, 1f);
        Vector3 northS = JunctionPort(NorthJunction, 0f, -1f);

        Vector3 eastW = JunctionPort(EastJunction, -1f, 0f);
        Vector3 eastE = JunctionPort(EastJunction, 1f, 0f);
        Vector3 eastN = JunctionPort(EastJunction, 0f, 1f);
        Vector3 eastS = JunctionPort(EastJunction, 0f, -1f);

        Vector3 southW = JunctionPort(SouthJunction, -1f, 0f);
        Vector3 southE = JunctionPort(SouthJunction, 1f, 0f);
        Vector3 southN = JunctionPort(SouthJunction, 0f, 1f);
        Vector3 southS = JunctionPort(SouthJunction, 0f, -1f);

        List<Vector3[]> routes = new List<Vector3[]>
        {
            // Roads 1-4: from the outer loop to the nearest inner junction.
            new[] { WestDestination, westW },
            new[] { eastE, EastDestination },
            new[] { northN, NorthDestination },
            new[] { southS, SouthDestination },

            // Roads 5-10: center junction, including two straight defaults.
            new[] { centerW, centerE },
            new[] { centerN, centerS },
            BezierCurvePoints(centerW, JunctionCenter, centerN),
            BezierCurvePoints(centerN, JunctionCenter, centerE),
            BezierCurvePoints(centerE, JunctionCenter, centerS),
            BezierCurvePoints(centerS, JunctionCenter, centerW),

            // Roads 11-14: outer loop.
            RoundedCornerPoints(
                WestDestination,
                Point(-ArmLength, 0f, ArmLength),
                NorthDestination,
                DestinationCornerRadius),
            RoundedCornerPoints(
                NorthDestination,
                Point(ArmLength, 0f, ArmLength),
                EastDestination,
                DestinationCornerRadius),
            RoundedCornerPoints(
                EastDestination,
                Point(ArmLength, 0f, -ArmLength),
                SouthDestination,
                DestinationCornerRadius),
            RoundedCornerPoints(
                SouthDestination,
                Point(-ArmLength, 0f, -ArmLength),
                WestDestination,
                DestinationCornerRadius),

            // Roads 15-18: center-to-inner-junction radial segments.
            new[] { westE, centerW },
            new[] { centerE, eastW },
            new[] { centerN, northS },
            new[] { centerS, southN },

            // Roads 19-22: rounded inner loop.
            RoundedCornerPoints(
                westN,
                Point(-InnerLoopHalfSize, 0f, InnerLoopHalfSize),
                northW,
                InnerCornerRadius),
            RoundedCornerPoints(
                northE,
                Point(InnerLoopHalfSize, 0f, InnerLoopHalfSize),
                eastN,
                InnerCornerRadius),
            RoundedCornerPoints(
                eastS,
                Point(InnerLoopHalfSize, 0f, -InnerLoopHalfSize),
                southE,
                InnerCornerRadius),
            RoundedCornerPoints(
                southW,
                Point(-InnerLoopHalfSize, 0f, -InnerLoopHalfSize),
                westS,
                InnerCornerRadius)
        };

        AddJunctionRoutes(routes, WestJunction);
        AddJunctionRoutes(routes, NorthJunction);
        AddJunctionRoutes(routes, EastJunction);
        AddJunctionRoutes(routes, SouthJunction);
        return routes.ToArray();
    }

    private static void AddJunctionRoutes(List<Vector3[]> routes, Vector3 center)
    {
        Vector3 west = JunctionPort(center, -1f, 0f);
        Vector3 east = JunctionPort(center, 1f, 0f);
        Vector3 north = JunctionPort(center, 0f, 1f);
        Vector3 south = JunctionPort(center, 0f, -1f);

        routes.Add(new[] { west, east });
        routes.Add(new[] { north, south });
        routes.Add(BezierCurvePoints(west, center, north));
        routes.Add(BezierCurvePoints(north, center, east));
        routes.Add(BezierCurvePoints(east, center, south));
        routes.Add(BezierCurvePoints(south, center, west));
    }

    private static bool[] BuildPreSampledCurves()
    {
        bool[] curves = new bool[RouteSegments.Length];
        for (int i = 0; i < RouteSegments.Length; i++)
        {
            curves[i] = RouteSegments[i].Length > 2;
        }

        return curves;
    }

    private static Vector3 JunctionPort(Vector3 center, float xDirection, float zDirection)
    {
        return center + Point(
            xDirection * NewJunctionRadius,
            0f,
            zDirection * NewJunctionRadius);
    }

    private static Vector3[] BezierCurvePoints(Vector3 start, Vector3 control, Vector3 end)
    {
        Vector3[] points = new Vector3[JunctionCurveSamples + 1];
        for (int i = 0; i <= JunctionCurveSamples; i++)
        {
            points[i] = QuadraticBezier(start, control, end, i / (float)JunctionCurveSamples);
        }

        return points;
    }

    private static Vector3[] RoundedCornerPoints(
        Vector3 start,
        Vector3 corner,
        Vector3 end,
        float radius)
    {
        Vector3 incomingDirection = (corner - start).normalized;
        Vector3 outgoingDirection = (end - corner).normalized;
        float incomingLength = Vector3.Distance(start, corner);
        float outgoingLength = Vector3.Distance(corner, end);
        float trim = Mathf.Min(radius, incomingLength * 0.45f, outgoingLength * 0.45f);
        Vector3 curveStart = corner - incomingDirection * trim;
        Vector3 curveEnd = corner + outgoingDirection * trim;

        Vector3[] points = new Vector3[JunctionCurveSamples + 3];
        points[0] = start;
        for (int i = 0; i <= JunctionCurveSamples; i++)
        {
            points[i + 1] = QuadraticBezier(
                curveStart,
                corner,
                curveEnd,
                i / (float)JunctionCurveSamples);
        }

        points[points.Length - 1] = end;
        return points;
    }

    private void Awake()
    {
        CacheComponents();
        LoadRoadData();
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
        LoadRoadData();

        Vector3[][] routeSegments = GetActiveRouteSegments();
        List<Spline> splines = new List<Spline>(routeSegments.Length);
        for (int i = 0; i < routeSegments.Length; i++)
        {
            splines.Add(CreateSpline(routeSegments[i], PreSampledCurves[i]));
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

    /// MoveCarLoop ใช้ while เพื่อใช้ remain ข้ามได้หลายถนนในคำสั่งเดียว
    public void MoveCarLoop(CarState car, float distance, MoveMode mode)
    {
        EnsureHistory(car);

        float remain = Mathf.Max(0f, distance);
        int safety = 0;

        while (remain > 0.001f && safety < 32)
        {
            remain = MoveCarOneRoad(car, remain, mode);
            safety++;
        }

        if (safety >= 32)
        {
            Debug.LogWarning("MoveCarLoop stopped limit.");
        }
    }

    /// ขยับรถภายในถนนเดียว ถ้าเกินปลายถนนจะย้ายไปถนนใหม่แล้วคืน remain
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
            return CrossNode(car, currentRoad, towardNodeE: false, remain, mode);
        }

        if (newPos > currentRoad.length)
        {
            float remain = newPos - currentRoad.length;
            car.currentPos = currentRoad.length;
            return CrossNode(car, currentRoad, towardNodeE: true, remain, mode);
        }

        car.currentPos = newPos;
        return 0f;
    }

    /// ข้าม node ปลายราง: เดินหน้า = เลือกรางใหม่ (pending > history > lane)
    /// ถอยหลัง = ย้อนตาม history จริงเท่านั้น ไม่ค้นหารางเชื่อมใหม่จาก node
    private float CrossNode(CarState car, RoadData road, bool towardNodeE, float remain, MoveMode mode)
    {
        if (mode == MoveMode.Backward)
        {
            return BacktrackHistory(car, remain);
        }

        RoadConnection next = ResolveForwardConnection(car, road, towardNodeE);
        if (!next.IsValid || GetRoad(next.roadNo) == null)
        {
            return 0f;
        }

        CommitForwardHistory(car, next);
        EnterNewRoad(car, next, remain, mode);
        return remain;
    }

    // History

    /// เริ่มต้น history ด้วยรางปัจจุบันเป็น entry แรก
    public void EnsureHistory(CarState car)
    {
        if (car.history == null)
        {
            car.history = new List<RouteHistoryEntry>();
        }

        if (car.history.Count == 0 || car.historyIndex < 0 || car.historyIndex >= car.history.Count)
        {
            car.history.Clear();
            car.history.Add(new RouteHistoryEntry(car.roadNo, car.dir == 0 ? 0 : 1, car.dir));
            car.historyIndex = 0;
        }
    }

    /// History entry ถัดไปด้านหน้า (null ถ้าไม่มีเ)
    public RouteHistoryEntry GetForwardHistory(CarState car)
    {
        if (car.history == null || car.historyIndex < 0 || car.historyIndex + 1 >= car.history.Count)
        {
            return null;
        }

        return car.history[car.historyIndex + 1];
    }

    public void ClearPendingSelection(CarState car)
    {
        car.pendingNextRoad = -1;
        car.pendingEnterNode = -1;
        car.hasPendingSelection = false;
        car.routeChoiceChanged = false;
    }

    /// ลำดับความสำคัญตอนเดินหน้าข้ามทางแยก 1 pending จากการ swipe  2 history ด้านหน้า  3 เลนที่เลือก/default
    private RoadConnection ResolveForwardConnection(CarState car, RoadData road, bool towardNodeE)
    {
        // Once the car is already on a junction traversal spline, the turn was
        // decided on the approach road. Exit through the geometrically
        // straight/default connection; never reuse a stale junction history
        // entry that can send the car back into the same junction.
        if (IsJunctionTraversalRoad(road.id))
        {
            int defaultLane = towardNodeE
                ? road.defaultLaneE
                : road.defaultLaneS;
            return GetNextConnection(road, towardNodeE, defaultLane);
        }

        if (car.hasPendingSelection && car.pendingNextRoad > 0)
        {
            RoadConnection pending = new RoadConnection(
                car.pendingNextRoad,
                car.pendingEnterNode);
            if (ContainsConnection(road, towardNodeE, pending))
            {
                return pending;
            }

            ClearPendingSelection(car);
        }

        RouteHistoryEntry forward = GetForwardHistory(car);
        if (forward != null)
        {
            RoadConnection historyConnection = new RoadConnection(
                forward.roadNo,
                forward.enterNode);
            if (ContainsConnection(road, towardNodeE, historyConnection))
            {
                return historyConnection;
            }

            // The topology or route choice changed. Remove only the invalid
            // future branch and continue using this road's straight default.
            int removeCount = car.history.Count - (car.historyIndex + 1);
            if (removeCount > 0)
            {
                car.history.RemoveRange(car.historyIndex + 1, removeCount);
            }
        }

        return GetNextConnection(road, towardNodeE, car.currentLane);
    }

    private static bool ContainsConnection(
        RoadData road,
        bool towardNodeE,
        RoadConnection candidate)
    {
        RoadConnection[] connections = towardNodeE ? road.nodeE : road.nodeS;
        if (connections == null)
        {
            return false;
        }

        for (int i = 0; i < connections.Length; i++)
        {
            if (connections[i].roadNo == candidate.roadNo
                && connections[i].enterNode == candidate.enterNode)
            {
                return true;
            }
        }

        return false;
    }

    /// Commit ตอนข้ามเข้ารางใหม่จริงเท่านั้น:
    /// - ตรงกับ history ด้านหน้า  เลื่อน index (ไม่เพิ่ม/ไม่ลบ entry)
    /// - ต่างจาก history  ลบเฉพาะ entry หลัง index แล้ว append รางใหม่
    private void CommitForwardHistory(CarState car, RoadConnection next)
    {
        RouteHistoryEntry forward = GetForwardHistory(car);

        if (forward != null && forward.roadNo == next.roadNo && forward.enterNode == next.enterNode)
        {
            car.historyIndex++;
            Debug.Log(car.hasPendingSelection
                ? $"[KEEP_EXISTING_CHOICE] index={car.historyIndex} {forward}"
                : $"[REUSE_FORWARD_HISTORY] index={car.historyIndex} {forward}");
        }
        else
        {
            if (forward != null)
            {
                int removeCount = car.history.Count - (car.historyIndex + 1);
                car.history.RemoveRange(car.historyIndex + 1, removeCount);
                Debug.Log($"[REPLACE_FORWARD_HISTORY] removed {removeCount} entries after index {car.historyIndex}");
            }

            car.history.Add(new RouteHistoryEntry(next.roadNo, next.enterNode, next.enterNode == 0 ? 0 : 1));
            car.historyIndex = car.history.Count - 1;
            Debug.Log($"[APPEND_HISTORY] index={car.historyIndex} {car.history[car.historyIndex]}");
        }

        ClearPendingSelection(car);
    }

    /// ถอยข้ามต้นราง: กลับไป entry ก่อนหน้าใน history โดยไม่ลบอะไรเลย
    /// ตอนเดินหน้า รางก่อนหน้าถูกออกที่ node ฝั่งตรงข้าม enterNode จึงถอยกลับเข้าที่ node นั้น
    private float BacktrackHistory(CarState car, float remain)
    {
        if (car.history == null || car.historyIndex <= 0)
        {
            return 0f; // สุดประวัติ หยุดที่ต้นราง
        }

        car.historyIndex--;
        RouteHistoryEntry prev = car.history[car.historyIndex];
        RoadData prevRoad = GetRoad(prev.roadNo);
        if (prevRoad == null)
        {
            Debug.LogWarning($"[BACKTRACK_HISTORY] invalid rail {prev.roadNo}");
            car.historyIndex++;
            return 0f;
        }

        car.roadNo = prev.roadNo;
        car.dir = prev.dirOnEnter;
        car.currentPos = prev.dirOnEnter == 0
            ? Mathf.Clamp(prevRoad.length - remain, 0f, prevRoad.length)
            : Mathf.Clamp(remain, 0f, prevRoad.length);

        ClearPendingSelection(car);
        SyncLaneWithForwardHistory(car);

        Debug.Log($"[BACKTRACK_HISTORY] index={car.historyIndex} -> {prev}, pos={car.currentPos:0.00}");
        return remain;
    }

    /// ตั้ง currentLane ให้ชี้ตาม history ด้านหน้าถ้ามี (เตือนเผื่อ อ.ถาม history สำคัญกว่า default)
    /// ไม่มี history ด้านหน้าจึงค่อยใช้เลน default ใช้แทนการ reset default ทุกครั้ง
    public void SyncLaneWithForwardHistory(CarState car)
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

        RouteHistoryEntry forward = GetForwardHistory(car);
        if (forward != null)
        {
            for (int i = 0; i < lanes.Length; i++)
            {
                if (lanes[i].roadNo == forward.roadNo && lanes[i].enterNode == forward.enterNode)
                {
                    car.currentLane = i;
                    return;
                }
            }
        }

        car.currentLane = Mathf.Clamp(
            car.dir == 0 ? road.defaultLaneE : road.defaultLaneS,
            0,
            lanes.Length - 1);
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

    private void EnterNewRoad(CarState car, RoadConnection connection, float remain, MoveMode mode)
    {
        RoadData nextRoad = GetRoad(connection.roadNo);
        if (nextRoad == null)
        {
            Debug.LogWarning($"Invalid next road: {connection.roadNo}");
            return;
        }

        car.roadNo = connection.roadNo;

        car.currentPos = connection.enterNode == 0
            ? Mathf.Clamp(remain, 0f, nextRoad.length)
            : Mathf.Clamp(nextRoad.length - remain, 0f, nextRoad.length);

        // เดินหน้าข้าม node = หันออกจาก node ที่เข้า, ถอยหลังข้าม node = ยังหันเข้าหา node ที่เข้า
        int facingDir = connection.enterNode == 0 ? 0 : 1;
        if (mode == MoveMode.Backward)
        {
            facingDir = 1 - facingDir;
        }

        car.dir = facingDir;

        // ห้าม reset เป็น default ทับทางเลือกเดิม — sync กับ history ด้านหน้าก่อน
        SyncLaneWithForwardHistory(car);

        Debug.Log($"[ENTER_NEW_RAIL] {connection}. pos={car.currentPos:0.00}, dir={car.dir}, lane={car.currentLane}, mode={mode}");
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
        Vector3 localPosition = EvaluatePolyline(GetActiveRouteSegments()[index], t);
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
        Vector3[] route = GetActiveRouteSegments()[index];
        Vector3 a = EvaluatePolyline(route, t);
        Vector3 b = EvaluatePolyline(route, t2);

        Vector3 forward = transform.TransformDirection((b - a).normalized);
        return car.dir == 0 ? forward : -forward;
    }

    public RoadData GetRoadData(int roadNo) => GetRoad(roadNo);

    /// Roads inside a junction are traversal pieces. Route selection happens
    /// on the approach road, so their exit must continue by default without
    /// presenting the same junction choices a second time.
    public bool IsJunctionTraversalRoad(int roadNo)
    {
        return (roadNo >= 5 && roadNo <= 10)
            || (roadNo >= 23 && roadNo <= 46);
    }

    private RoadData GetRoad(int roadNo)
    {
        if (roads == null)
        {
            LoadRoadData();
        }

        int index = roadNo - 1;
        if (index < 0 || index >= roads.Length)
        {
            return null;
        }

        return roads[index];
    }

    private Spline CreateSpline(IReadOnlyList<Vector3> points, bool preSampledCurve = false)
    {
        List<Vector3> renderPoints = preSampledCurve
            ? new List<Vector3>(points)
            : BuildRenderPoints(points);
        Spline spline = new Spline(renderPoints.Count, false);

        for (int i = 0; i < renderPoints.Count; i++)
        {
            spline.Add(ToFloat3(renderPoints[i]), TangentMode.Linear);
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

    private Vector3[][] GetActiveRouteSegments()
    {
        if (activeRouteSegments == null || activeRouteSegments.Length != RouteSegments.Length)
        {
            RebuildActiveRouteSegments();
        }

        return activeRouteSegments;
    }

    private void RebuildActiveRouteSegments()
    {
        Vector2 fitScale = Vector2.one;
        Vector2 fitCenter = Vector2.zero;

        if (fitToRoadModel && TryGetRoadModelLocalBounds(out Bounds localBounds))
        {
            float halfWidth = Mathf.Max(0.01f, localBounds.extents.x - roadEdgeInset);
            float halfDepth = Mathf.Max(0.01f, localBounds.extents.z - roadEdgeInset);
            fitScale = new Vector2(halfWidth / ArmLength, halfDepth / ArmLength);
            fitCenter = new Vector2(localBounds.center.x, localBounds.center.z);
        }

        float safeRouteScale = Mathf.Max(0.0001f, routeScale);
        Vector2 finalScale = fitScale * safeRouteScale;
        Vector2 finalOffset = fitCenter + routeOffset;
        activeRouteSegments = new Vector3[RouteSegments.Length][];

        for (int routeIndex = 0; routeIndex < RouteSegments.Length; routeIndex++)
        {
            Vector3[] source = RouteSegments[routeIndex];
            Vector3[] fitted = new Vector3[source.Length];
            for (int pointIndex = 0; pointIndex < source.Length; pointIndex++)
            {
                Vector3 point = source[pointIndex];
                fitted[pointIndex] = new Vector3(
                    finalOffset.x + point.x * finalScale.x,
                    point.y * safeRouteScale,
                    finalOffset.y + point.z * finalScale.y);
            }

            activeRouteSegments[routeIndex] = fitted;
        }
    }

    private bool TryGetRoadModelLocalBounds(out Bounds localBounds)
    {
        localBounds = default;
        Transform model = ResolveRoadModel();
        if (model == null)
        {
            return false;
        }

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Vector3 localMin = Vector3.zero;
        Vector3 localMax = Vector3.zero;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null || renderer.transform == transform)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldCorner = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 localCorner = transform.InverseTransformPoint(worldCorner);

                if (!hasBounds)
                {
                    localMin = localCorner;
                    localMax = localCorner;
                    hasBounds = true;
                }
                else
                {
                    localMin = Vector3.Min(localMin, localCorner);
                    localMax = Vector3.Max(localMax, localCorner);
                }
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        localBounds.SetMinMax(localMin, localMax);
        return true;
    }

    private Transform ResolveRoadModel()
    {
        if (roadModel != null)
        {
            return roadModel;
        }

        if (string.IsNullOrWhiteSpace(roadModelObjectName))
        {
            return null;
        }

        GameObject modelObject = GameObject.Find(roadModelObjectName);
        return modelObject != null ? modelObject.transform : null;
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

        if (TryGetComponent(out MeshRenderer meshRenderer))
        {
            meshRenderer.enabled = createRoadMesh;
            if (roadMaterial != null)
            {
                meshRenderer.sharedMaterial = roadMaterial;
            }
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
        if (TryGetComponent(out MeshCollider meshCollider))
        {
            meshCollider.enabled = addRoadCollider;
            return;
        }

        if (addRoadCollider)
        {
            GetOrAddComponent<MeshCollider>();
        }
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
