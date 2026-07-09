using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

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
    private const float StraightAngleThreshold = 25f;
    private const float JunctionPreviewLength = 0.2f;
    private const float EndpointOffsetNormalized = 0.02f;

    private enum TurnKind
    {
        Right,
        Straight,
        Left
    }

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
        public TurnKind TurnKind;
        public bool IsValid;
    }

    [SerializeField] private TouchpadManager touchManager;
    [SerializeField] private RoadNetworkSplineCreator roadNetwork;
    [SerializeField] private GameObject player;
    [SerializeField] private Transform worldRotateTarget;

    [Header("Highlight")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.1f, 1f);
    [SerializeField] private float lineWidth = 0.24f;
    [SerializeField, Min(4)] private int samplesPerRoad = 24;
    [SerializeField] private float lineHeightOffset = 0.08f;

    private Rigidbody rb;
    private SplineContainer splineContainer;
    private RoadNetworkSplineCreator.CarState carState;
    private Vector2? lastOneFingerPosition;
    private Vector2? lastTwoFingerPosition;
    private Vector2? swipeStartPosition;
    private bool suppressNextOneFingerDragFrame;
    private bool suppressNextTwoFingerDragFrame;
    private SwipeState consumedJunctionSwipe = SwipeState.None;
    private int swipeDirection;
    private SwipeState swipeDebugState = SwipeState.None;
    private LineRenderer routeLine;
    private Material routeMaterial;
    private int cachedRoadNo = -1;
    private int cachedLane = -1;
    private int cachedDir = -1;
    private float cachedPos = -1f;

    public int SwipeDirection => swipeDirection;
    public SwipeState CurrentSwipeState => swipeDebugState;

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
        HandleTrackpadMovement();
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

    private void HandleTrackpadMovement()
    {
        if (!touchManager.IsTouching)
        {
            ResetTouchState();
            return;
        }

        TouchpadManager.TouchMode mode = touchManager.CurrentMode;
        TouchpadManager.TouchStatus status = touchManager.Status;
        Vector2 currentPosition = touchManager.GetCurrentTouch();

        if (status == TouchpadManager.TouchStatus.OnTouch || mode == TouchpadManager.TouchMode.Change)
        {
            lastOneFingerPosition = currentPosition;
            lastTwoFingerPosition = currentPosition;
            swipeStartPosition = currentPosition;
            suppressNextOneFingerDragFrame = true;
            suppressNextTwoFingerDragFrame = true;
            consumedJunctionSwipe = SwipeState.None;
            SetSwipeDebugState(SwipeState.None);
            return;
        }

        if (status != TouchpadManager.TouchStatus.OnDrag)
        {
            return;
        }

        if (mode == TouchpadManager.TouchMode.Rotate)
        {
            SetSwipeDebugState(SwipeState.None);
            HandleTwoFingerRotation(currentPosition);
            return;
        }

        if (mode == TouchpadManager.TouchMode.Translate && touchManager.TouchCount == 1)
        {
            HandleOneFingerMovement(currentPosition);
            return;
        }

        lastOneFingerPosition = null;
        swipeStartPosition = null;
        suppressNextOneFingerDragFrame = true;
        SetSwipeDebugState(SwipeState.None);
    }

    private void HandleOneFingerMovement(Vector2 currentPosition)
    {
        lastTwoFingerPosition = null;
        suppressNextTwoFingerDragFrame = true;

        if (lastOneFingerPosition == null)
        {
            lastOneFingerPosition = currentPosition;
            return;
        }

        if (suppressNextOneFingerDragFrame)
        {
            lastOneFingerPosition = currentPosition;
            suppressNextOneFingerDragFrame = false;
            return;
        }

        Vector2 dragDelta = currentPosition - lastOneFingerPosition.Value;
        lastOneFingerPosition = currentPosition;

        UpdateSwipeDebugState(currentPosition);
        HandleJunctionSwipe(dragDelta);
        MoveByDogpaddle(dragDelta);
    }

    private void UpdateSwipeDebugState(Vector2 currentPosition)
    {
        if (swipeStartPosition == null)
        {
            swipeStartPosition = currentPosition;
            return;
        }

        Vector2 swipeDelta = currentPosition - swipeStartPosition.Value;
        SwipeState detectedState = DetectSwipeState(swipeDelta);

        if (detectedState == SwipeState.Left || detectedState == SwipeState.Right)
        {
            SetSwipeDebugState(detectedState);
        }
        else
        {
            SetSwipeDebugState(SwipeState.None);
        }
    }

    private void SetSwipeDebugState(SwipeState newState)
    {
        int newDirection = 0;
        if (newState == SwipeState.Left)
        {
            newDirection = -1;
        }
        else if (newState == SwipeState.Right)
        {
            newDirection = 1;
        }

        if (swipeDebugState == newState && swipeDirection == newDirection)
        {
            return;
        }

        swipeDebugState = newState;
        swipeDirection = newDirection;
        Debug.Log($"Swipe Detect: {swipeDebugState} | Direction={swipeDirection}");
    }

    private void HandleTwoFingerRotation(Vector2 currentPosition)
    {
        lastOneFingerPosition = null;
        suppressNextOneFingerDragFrame = true;

        if (lastTwoFingerPosition == null)
        {
            lastTwoFingerPosition = currentPosition;
            return;
        }

        if (suppressNextTwoFingerDragFrame)
        {
            lastTwoFingerPosition = currentPosition;
            suppressNextTwoFingerDragFrame = false;
            return;
        }

        Vector2 dragDelta = currentPosition - lastTwoFingerPosition.Value;
        lastTwoFingerPosition = currentPosition;

        if (Mathf.Abs(dragDelta.x) <= RotateDeadZoneRaw || Mathf.Abs(dragDelta.x) <= Mathf.Abs(dragDelta.y))
        {
            return;
        }

        Transform rotateTarget = worldRotateTarget != null ? worldRotateTarget : player.transform;
        float rotationDegrees = -dragDelta.x * (TwoFingerRotateDegrees / RawVerticalDistance);
        rotateTarget.Rotate(Vector3.up, rotationDegrees, Space.World);

        if (player.transform != rotateTarget && !player.transform.IsChildOf(rotateTarget))
        {
            player.transform.Rotate(Vector3.up, rotationDegrees, Space.World);
        }
    }

    private void HandleJunctionSwipe(Vector2 dragDelta)
    {
        if (!IsAtJunction())
        {
            consumedJunctionSwipe = SwipeState.None;
            return;
        }

        SwipeState swipeState = DetectSwipeState(dragDelta);
        if (swipeState == SwipeState.None)
        {
            consumedJunctionSwipe = SwipeState.None;
            return;
        }

        if (consumedJunctionSwipe == swipeState)
        {
            return;
        }

        if (SelectLaneBySwipe(swipeState))
        {
            consumedJunctionSwipe = swipeState;
            UpdateRoutePreview(force: true);
        }
    }

    private SwipeState DetectSwipeState(Vector2 dragDelta)
    {
        if (dragDelta.magnitude < SwipeDeadZoneRaw)
        {
            return SwipeState.None;
        }

        if (Mathf.Abs(dragDelta.x) > Mathf.Abs(dragDelta.y))
        {
            return dragDelta.x < 0f ? SwipeState.Left : SwipeState.Right;
        }

        return dragDelta.y < 0f ? SwipeState.Up : SwipeState.Down;
    }

    private void MoveByDogpaddle(Vector2 dragDelta)
    {
        if (Mathf.Approximately(dragDelta.y, 0f))
        {
            return;
        }

        float dragDeltaY = VerticalCmPerRaw * dragDelta.y;
        float distance = Mathf.Abs(ScaleResearch * dragDeltaY);
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

    private void ResetTouchState()
    {
        lastOneFingerPosition = null;
        lastTwoFingerPosition = null;
        swipeStartPosition = null;
        suppressNextOneFingerDragFrame = false;
        suppressNextTwoFingerDragFrame = false;
        consumedJunctionSwipe = SwipeState.None;
        SetSwipeDebugState(SwipeState.None);
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
    }

    private bool IsAtJunction()
    {
        RoadNetworkSplineCreator.RoadData road = roadNetwork.GetRoadData(carState.roadNo);
        if (road == null || road.length <= Mathf.Epsilon)
        {
            return false;
        }

        float normalizedPos = carState.currentPos / road.length;
        if (carState.dir == 0)
        {
            return normalizedPos >= 1f - JunctionPreviewLength && GetLaneOptions().Count > 0;
        }

        return normalizedPos <= JunctionPreviewLength && GetLaneOptions().Count > 0;
    }

    private void SelectDefaultLane()
    {
        List<LaneOption> options = GetLaneOptions();
        if (options.Count == 0)
        {
            carState.currentLane = 0;
            return;
        }

        LaneOption best = options[0];
        float bestAbsAngle = Mathf.Abs(best.SignedAngle);

        for (int i = 1; i < options.Count; i++)
        {
            float absAngle = Mathf.Abs(options[i].SignedAngle);
            if (absAngle < bestAbsAngle)
            {
                best = options[i];
                bestAbsAngle = absAngle;
            }
        }

        carState.currentLane = best.LaneIndex;
    }

    private bool SelectLaneBySwipe(SwipeState swipeState)
    {
        List<LaneOption> options = GetLaneOptions();
        if (options.Count == 0)
        {
            return false;
        }

        if (options.Count == 1)
        {
            carState.currentLane = options[0].LaneIndex;
            return true;
        }

        LaneOption selected;
        switch (swipeState)
        {
            case SwipeState.Left:
                selected = GetLeftMostOption(options);
                break;
            case SwipeState.Right:
                selected = GetRightMostOption(options);
                break;
            case SwipeState.Down:
            case SwipeState.Up:
                selected = GetStraightestOption(options);
                break;
            default:
                return false;
        }

        carState.currentLane = selected.LaneIndex;
        Debug.Log($"Junction swipe {swipeState}: lane={selected.LaneIndex}, nextRoad={selected.NextRoadNo}, angle={selected.SignedAngle:0.0}");
        return true;
    }

    private LaneOption GetLeftMostOption(List<LaneOption> options)
    {
        LaneOption selected = options[0];
        for (int i = 1; i < options.Count; i++)
        {
            if (options[i].SignedAngle > selected.SignedAngle)
            {
                selected = options[i];
            }
        }

        return selected;
    }

    private LaneOption GetRightMostOption(List<LaneOption> options)
    {
        LaneOption selected = options[0];
        for (int i = 1; i < options.Count; i++)
        {
            if (options[i].SignedAngle < selected.SignedAngle)
            {
                selected = options[i];
            }
        }

        return selected;
    }

    private LaneOption GetStraightestOption(List<LaneOption> options)
    {
        LaneOption selected = options[0];
        float bestAbsAngle = Mathf.Abs(selected.SignedAngle);

        for (int i = 1; i < options.Count; i++)
        {
            float absAngle = Mathf.Abs(options[i].SignedAngle);
            if (absAngle < bestAbsAngle)
            {
                selected = options[i];
                bestAbsAngle = absAngle;
            }
        }

        return selected;
    }

    private void SelectTurn(TurnKind desiredTurn)
    {
        List<LaneOption> options = GetLaneOptions();
        if (options.Count == 0)
        {
            return;
        }

        LaneOption? selected = null;

        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].TurnKind == desiredTurn)
            {
                if (selected == null)
                {
                    selected = options[i];
                    continue;
                }

                bool shouldReplace =
                    Mathf.Abs(options[i].SignedAngle) < Mathf.Abs(selected.Value.SignedAngle);

                if (shouldReplace)
                {
                    selected = options[i];
                }
            }
        }

        if (selected == null && desiredTurn != TurnKind.Straight)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].TurnKind == TurnKind.Straight)
                {
                    selected = options[i];
                    break;
                }
            }
        }

        if (selected != null)
        {
            carState.currentLane = selected.Value.LaneIndex;
        }
    }

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
            float signedAngle = Vector3.SignedAngle(currentForward, nextForward, Vector3.up);

            options.Add(new LaneOption
            {
                LaneIndex = i,
                NextRoadNo = connection.roadNo,
                EnterNode = connection.enterNode,
                SignedAngle = signedAngle,
                TurnKind = ClassifyTurn(signedAngle),
                IsValid = true
            });
        }

        return options;
    }

    private TurnKind ClassifyTurn(float signedAngle)
    {
        if (signedAngle > StraightAngleThreshold)
        {
            return TurnKind.Left;
        }

        if (signedAngle < -StraightAngleThreshold)
        {
            return TurnKind.Right;
        }

        return TurnKind.Straight;
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
            currentPos = connection.enterNode == 0
                ? Mathf.Max(nextRoad.length * EndpointOffsetNormalized, 0.01f)
                : Mathf.Max(nextRoad.length * (1f - EndpointOffsetNormalized), 0.01f)
        };

        return roadNetwork.EvaluateRoadForward(nextState);
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
        AppendRoadSegment(points, carState.roadNo, GetCurrentRoadStartT(), GetCurrentRoadEndT());

        LaneOption? selectedLane = GetSelectedLaneOption();
        if (selectedLane != null)
        {
            float startT = selectedLane.Value.EnterNode == 0 ? 0f : 1f;
            float endT = selectedLane.Value.EnterNode == 0 ? 1f : 0f;
            AppendRoadSegment(points, selectedLane.Value.NextRoadNo, startT, endT);
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

        float currentT = Mathf.Clamp01(carState.currentPos / road.length);
        return currentT;
    }

    private float GetCurrentRoadEndT()
    {
        return carState.dir == 0 ? 1f : 0f;
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
            float lerp = i / (float)(steps - 1);
            float t = Mathf.Lerp(startT, endT, lerp);
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
