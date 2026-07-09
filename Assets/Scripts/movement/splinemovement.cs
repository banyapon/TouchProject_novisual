using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

//ตามราง spline ของ RoadNetworkSplineCreator ด้วย touchpad
[RequireComponent(typeof(Rigidbody))]
public class splinemovement : MonoBehaviour
{
    private const float ScaleResearch = 65f / 40f;
    private const float RawVerticalDistance = 912f;
    private const float TouchPadVerticalCmDistance = 8f;
    private const float VerticalCmPerRaw = TouchPadVerticalCmDistance / RawVerticalDistance;
    private const float SwipeDeadZoneRaw = 24f;
    private const float RotateDeadZoneRaw = 8f;
    private const float TwoFingerRotateDegrees = 90f;
    private const float JunctionPreviewLength = 0.2f;

    public enum SwipeState
    {
        None,
        Up,
        Down,
        Left,
        Right
    }

    private struct LaneOption
    {
        public int LaneIndex;
        public int NextRoadNo;
        public int EnterNode;
        public float SignedAngle;
    }

    [SerializeField] private TouchpadManager touchManager;
    [SerializeField] private RoadNetworkSplineCreator roadNetwork;
    [SerializeField] private GameObject player;
    [SerializeField] private Transform worldRotateTarget;

    [Header("Driving")]
    [Tooltip("หมุน Player ให้หันตามทิศทาง spline กล้องที่เป็นลูกของ Player จะได้มุมมองแบบขับรถ")]
    [SerializeField] private bool alignToRoadForward = true;
    [SerializeField] private float headingTurnSpeed = 240f;

    [Header("Highlight")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.1f, 1f);
    [SerializeField] private float lineWidth = 0.24f;
    [SerializeField, Min(4)] private int samplesPerRoad = 24;
    [SerializeField] private float lineHeightOffset = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool showSwipeDebug = true;

    private Rigidbody rb;
    private SplineContainer splineContainer;
    private RoadNetworkSplineCreator.CarState carState;

    private TouchpadManager.TouchMode lastMode = TouchpadManager.TouchMode.None;
    private Vector2? lastDragPosition;
    private Vector2? swipeStartPosition;
    private SwipeState currentSwipe = SwipeState.None;

    private LineRenderer routeLine;
    private Material routeMaterial;
    private GUIStyle debugStyle;
    private int cachedRoadNo = -1;
    private int cachedLane = -1;
    private int cachedDir = -1;
    private float cachedPos = -1f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        ResolveReferences();
        EnsureRouteRenderer();
        EnsureCarState();
    }

    private void Start()
    {
        ResolveReferences();
        EnsureCarState();
        SnapToRoad();
        UpdateRoutePreview(force: true);
    }

    private void FixedUpdate()
    {
        ResolveReferences();
        if (touchManager == null || roadNetwork == null || splineContainer == null)
        {
            return;
        }

        EnsureCarState();
        HandleTrackpadInput();
        SnapToRoad();
        UpdateRoutePreview(force: false);
    }

    private void OnDestroy()
    {
        if (routeMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(routeMaterial);
            }
            else
            {
                DestroyImmediate(routeMaterial);
            }
        }
    }

    /// Debug swipe มุมซ้ายล่างของจอ บอกสถานะการปัด
    private void OnGUI()
    {
        if (!showSwipeDebug || carState == null || roadNetwork == null)
        {
            return;
        }

        if (debugStyle == null)
        {
            debugStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold
            };
        }

        bool atJunction = IsAtJunction();
        debugStyle.normal.textColor = atJunction ? Color.yellow : Color.white;

        string text = $"Swipe: {currentSwipe}  |  Road {carState.roadNo}  Lane {carState.currentLane}"
                      + (atJunction ? "  [JUNCTION]" : "");
        GUI.Label(new Rect(16f, Screen.height - 48f, 800f, 40f), text, debugStyle);
    }

    private void ResolveReferences()
    {
        if (touchManager == null)
        {
            touchManager = TouchpadManager.Instance;
        }

        if (roadNetwork == null)
        {
            roadNetwork = FindFirstObjectByType<RoadNetworkSplineCreator>();
        }

        if (player == null)
        {
            player = gameObject;
        }

        splineContainer = roadNetwork != null
            ? roadNetwork.GetComponent<SplineContainer>()
            : null;
    }

    private void EnsureCarState()
    {
        if (carState != null)
        {
            return;
        }

        carState = new RoadNetworkSplineCreator.CarState
        {
            roadNo = 1,
            currentPos = 0f,
            dir = 0,
            currentLane = 0
        };

        SelectDefaultLane();
    }

//Input TouchPad
    private void HandleTrackpadInput()
    {
        if (!touchManager.IsTouching)
        {
            ResetTouchState();
            return;
        }

        TouchpadManager.TouchMode mode = touchManager.CurrentMode;
        Vector2 position = touchManager.GetCurrentTouch();

        // เริ่มแตะใหม่ หรือเปลี่ยนจำนวนนิ้ว: ตั้งต้นตำแหน่งใหม่ กันค่ากระโดด
        if (touchManager.Status == TouchpadManager.TouchStatus.OnTouch || mode != lastMode)
        {
            lastMode = mode;
            lastDragPosition = position;
            swipeStartPosition = position;
            currentSwipe = SwipeState.None;
            return;
        }

        if (touchManager.Status != TouchpadManager.TouchStatus.OnDrag || lastDragPosition == null)
        {
            lastDragPosition = position;
            return;
        }

        Vector2 dragDelta = position - lastDragPosition.Value;
        lastDragPosition = position;

        if (mode == TouchpadManager.TouchMode.Rotate)
        {
            RotateWorld(dragDelta);
            return;
        }

        if (mode == TouchpadManager.TouchMode.Translate && touchManager.TouchCount == 1)
        {
            UpdateSwipe(position);
            MoveAlongRoad(dragDelta);
        }
    }

    private void ResetTouchState()
    {
        lastMode = TouchpadManager.TouchMode.None;
        lastDragPosition = null;
        swipeStartPosition = null;
        currentSwipe = SwipeState.None;
    }

    //Swipr ตรงนี้
    private void UpdateSwipe(Vector2 position)
    {
        if (swipeStartPosition == null)
        {
            swipeStartPosition = position;
            return;
        }

        currentSwipe = DetectSwipe(position - swipeStartPosition.Value);

        if (currentSwipe != SwipeState.Left && currentSwipe != SwipeState.Right)
        {
            return;
        }

        swipeStartPosition = position;

        if (IsAtJunction() && StepLaneBySwipe(currentSwipe))
        {
            UpdateRoutePreview(force: true);
        }
    }

    private SwipeState DetectSwipe(Vector2 delta)
    {
        if (delta.magnitude < SwipeDeadZoneRaw)
        {
            return SwipeState.None;
        }

        if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
        {
            return delta.x < 0f ? SwipeState.Left : SwipeState.Right;
        }

        return delta.y < 0f ? SwipeState.Up : SwipeState.Down;
    }
    private void MoveAlongRoad(Vector2 dragDelta)
    {
        float distance = Mathf.Abs(ScaleResearch * VerticalCmPerRaw * dragDelta.y);
        if (distance <= 0f)
        {
            return;
        }

        RoadNetworkSplineCreator.MoveMode moveMode = dragDelta.y > 0f
            ? RoadNetworkSplineCreator.MoveMode.Forward
            : RoadNetworkSplineCreator.MoveMode.Backward;

        int previousRoadNo = carState.roadNo;
        roadNetwork.MoveCarLoop(carState, distance, moveMode);

        if (carState.roadNo != previousRoadNo)
        {
            Debug.Log($"Spline changed: Road {previousRoadNo} -> Road {carState.roadNo}");
            SelectDefaultLane();
        }
    }

    /// ลาก 2 นิ้วแนวนอน หมุนโลกรอบแกน Y
    private void RotateWorld(Vector2 dragDelta)
    {
        if (Mathf.Abs(dragDelta.x) <= RotateDeadZoneRaw || Mathf.Abs(dragDelta.x) <= Mathf.Abs(dragDelta.y))
        {
            return;
        }

        Transform rotateTarget = worldRotateTarget != null ? worldRotateTarget : player.transform;
        float rotationDegrees = -dragDelta.x * (TwoFingerRotateDegrees / RawVerticalDistance);
        rotateTarget.Rotate(Vector3.up, rotationDegrees, Space.World);
    }

    private void SnapToRoad()
    {
        if (carState == null)
        {
            return;
        }

        Vector3 worldPosition = roadNetwork.EvaluateRoadPosition(carState);
        worldPosition.y = player.transform.position.y;
        rb.MovePosition(worldPosition);
        AlignToRoadForward();
    }

    private void AlignToRoadForward()
    {
        if (!alignToRoadForward)
        {
            return;
        }

        Vector3 forward = roadNetwork.EvaluateRoadForward(carState);
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        Quaternion nextRotation = Quaternion.RotateTowards(
            player.transform.rotation,
            targetRotation,
            headingTurnSpeed * Time.fixedDeltaTime);

        if (player == gameObject)
        {
            rb.MoveRotation(nextRotation);
        }
        else
        {
            player.transform.rotation = nextRotation;
        }
    }

    /// อยู่ในช่วงท้ายถนน (20%) และมีทางให้เลือกไหม
    private bool IsAtJunction()
    {
        RoadNetworkSplineCreator.RoadData road = roadNetwork.GetRoadData(carState.roadNo);
        if (road == null || road.length <= Mathf.Epsilon)
        {
            return false;
        }

        float normalizedPos = carState.currentPos / road.length;
        bool nearEnd = carState.dir == 0
            ? normalizedPos >= 1f - JunctionPreviewLength
            : normalizedPos <= JunctionPreviewLength;

        return nearEnd && GetLaneOptions().Count > 0;
    }

    /// ค่าเริ่มต้นเลือกทางที่ตรงที่สุด (มุมเลี้ยวน้อยสุด)
    private void SelectDefaultLane()
    {
        List<LaneOption> options = GetLaneOptions();
        if (options.Count == 0)
        {
            carState.currentLane = 0;
            return;
        }

        LaneOption best = options[0];
        for (int i = 1; i < options.Count; i++)
        {
            if (Mathf.Abs(options[i].SignedAngle) < Mathf.Abs(best.SignedAngle))
            {
                best = options[i];
            }
        }

        carState.currentLane = best.LaneIndex;
    }

    /// ขยับตัวเลือก 1 ขั้นตามทิศ swipe บนรายการที่เรียงจากซ้ายสุดไปขวาสุด
    /// (SignedAngle ลบสุด = ซ้ายสุด, บวกสุด = ขวาสุด) ชนขอบแล้วอยู่ที่เดิม
    private bool StepLaneBySwipe(SwipeState swipe)
    {
        List<LaneOption> options = GetLaneOptions();
        if (options.Count == 0)
        {
            return false;
        }

        options.Sort((a, b) => a.SignedAngle.CompareTo(b.SignedAngle));

        int currentIndex = 0;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].LaneIndex == carState.currentLane)
            {
                currentIndex = i;
                break;
            }
        }

        int step = swipe == SwipeState.Left ? -1 : 1;
        int newIndex = Mathf.Clamp(currentIndex + step, 0, options.Count - 1);
        if (newIndex == currentIndex)
        {
            return false;
        }

        LaneOption selected = options[newIndex];
        carState.currentLane = selected.LaneIndex;
        Debug.Log($"Junction swipe {swipe}: lane={selected.LaneIndex}, nextRoad={selected.NextRoadNo}, angle={selected.SignedAngle:0.0}");
        return true;
    }

    /// ทางเลือกทั้งหมดที่ปลายถนนปัจจุบัน พร้อมมุมเลี้ยวเทียบทิศรถ
    private List<LaneOption> GetLaneOptions()
    {
        List<LaneOption> options = new List<LaneOption>();
        RoadNetworkSplineCreator.RoadData road = roadNetwork.GetRoadData(carState.roadNo);
        if (road == null)
        {
            return options;
        }

        RoadNetworkSplineCreator.RoadConnection[] lanes = carState.dir == 0 ? road.laneE : road.laneS;
        if (lanes == null)
        {
            return options;
        }

        Vector3 currentForward = roadNetwork.EvaluateRoadForward(carState);
        currentForward.y = 0f;
        if (currentForward.sqrMagnitude <= 0.001f)
        {
            return options;
        }

        currentForward.Normalize();

        for (int i = 0; i < lanes.Length; i++)
        {
            RoadNetworkSplineCreator.RoadConnection connection = lanes[i];
            if (!connection.IsValid)
            {
                continue;
            }

            Vector3 nextForward = EvaluateConnectionForward(connection);
            nextForward.y = 0f;
            if (nextForward.sqrMagnitude <= 0.001f)
            {
                continue;
            }

            nextForward.Normalize();

            options.Add(new LaneOption
            {
                LaneIndex = i,
                NextRoadNo = connection.roadNo,
                EnterNode = connection.enterNode,
                SignedAngle = Vector3.SignedAngle(currentForward, nextForward, Vector3.up)
            });
        }

        return options;
    }

    private Vector3 EvaluateConnectionForward(RoadNetworkSplineCreator.RoadConnection connection)
    {
        RoadNetworkSplineCreator.RoadData nextRoad = roadNetwork.GetRoadData(connection.roadNo);
        if (nextRoad == null)
        {
            return Vector3.zero;
        }

        RoadNetworkSplineCreator.CarState nextState = new RoadNetworkSplineCreator.CarState
        {
            roadNo = connection.roadNo,
            dir = connection.enterNode == 0 ? 0 : 1,
            currentLane = 0,
            // อ่านทิศทางที่กลางถนนถัดไป เพราะช่วงต้นของโค้ง Bezier ยังชี้ขนานกับทางตรง
            currentPos = Mathf.Max(nextRoad.length * 0.5f, 0.01f)
        };

        return roadNetwork.EvaluateRoadForward(nextState);
    }

    // ------------------------------------------------------------------
    // Route highlight: ระบายเส้นทางที่จะไป
    // ------------------------------------------------------------------
    private void EnsureRouteRenderer()
    {
        if (routeLine != null)
        {
            return;
        }

        routeLine = GetComponent<LineRenderer>();
        if (routeLine == null)
        {
            routeLine = gameObject.AddComponent<LineRenderer>();
        }

        Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineShader == null) lineShader = Shader.Find("Unlit/Color");
        if (lineShader == null) lineShader = Shader.Find("Sprites/Default");
        if (lineShader == null) lineShader = Shader.Find("Standard");

        routeMaterial = lineShader != null
            ? new Material(lineShader)
            : new Material(routeLine.sharedMaterial);

        routeMaterial.color = highlightColor;
        routeLine.sharedMaterial = routeMaterial;
        routeLine.startColor = highlightColor;
        routeLine.endColor = highlightColor;
        routeLine.startWidth = lineWidth;
        routeLine.endWidth = lineWidth;
        routeLine.widthMultiplier = 1f;
        routeLine.useWorldSpace = true;
        routeLine.positionCount = 0;
        routeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        routeLine.receiveShadows = false;
    }

    private void UpdateRoutePreview(bool force)
    {
        if (routeLine == null || splineContainer == null || carState == null)
        {
            return;
        }

        if (!force
            && cachedRoadNo == carState.roadNo
            && cachedLane == carState.currentLane
            && cachedDir == carState.dir
            && Mathf.Abs(cachedPos - carState.currentPos) < 0.05f)
        {
            return;
        }

        List<Vector3> points = BuildPreviewPoints();
        routeLine.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
        {
            routeLine.SetPosition(i, points[i]);
        }

        cachedRoadNo = carState.roadNo;
        cachedLane = carState.currentLane;
        cachedDir = carState.dir;
        cachedPos = carState.currentPos;
    }

    private List<Vector3> BuildPreviewPoints()
    {
        List<Vector3> points = new List<Vector3>();
        AppendRoadSegment(points, carState.roadNo, GetCurrentRoadStartT(), carState.dir == 0 ? 1f : 0f);

        LaneOption? selectedLane = GetSelectedLaneOption();
        if (selectedLane != null)
        {
            float startT = selectedLane.Value.EnterNode == 0 ? 0f : 1f;
            AppendRoadSegment(points, selectedLane.Value.NextRoadNo, startT, 1f - startT);
        }

        if (points.Count == 0)
        {
            Vector3 fallback = roadNetwork.EvaluateRoadPosition(carState);
            fallback.y += lineHeightOffset;
            points.Add(fallback);
        }

        return points;
    }

    private LaneOption? GetSelectedLaneOption()
    {
        if (!IsAtJunction())
        {
            return null;
        }

        List<LaneOption> options = GetLaneOptions();
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].LaneIndex == carState.currentLane)
            {
                return options[i];
            }
        }

        return null;
    }

    private float GetCurrentRoadStartT()
    {
        RoadNetworkSplineCreator.RoadData road = roadNetwork.GetRoadData(carState.roadNo);
        if (road == null || road.length <= Mathf.Epsilon)
        {
            return 0f;
        }

        return Mathf.Clamp01(carState.currentPos / road.length);
    }

    private void AppendRoadSegment(List<Vector3> points, int roadNo, float startT, float endT)
    {
        int splineIndex = roadNo - 1;
        if (splineIndex < 0 || splineIndex >= splineContainer.Splines.Count)
        {
            return;
        }

        Spline spline = splineContainer.Splines[splineIndex];
        int steps = Mathf.Max(2, samplesPerRoad);

        for (int i = 0; i < steps; i++)
        {
            float t = Mathf.Lerp(startT, endT, i / (float)(steps - 1));
            float3 localPoint = spline.EvaluatePosition(t);
            Vector3 worldPoint = splineContainer.transform.TransformPoint((Vector3)localPoint);
            worldPoint.y += lineHeightOffset;

            if (points.Count > 0 && Vector3.Distance(points[points.Count - 1], worldPoint) < 0.01f)
            {
                continue;
            }

            points.Add(worldPoint);
        }
    }
}
