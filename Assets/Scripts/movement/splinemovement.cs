using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;
using UnityEngine.UI;

// Touch controls shared by RoadNetworkSplineCreator and SplineJson routes.
[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
public class splinemovement : MonoBehaviour
{
    private const float ScaleResearch = 65f / 40f;
    private const float RawVerticalDistance = 912f;
    private const float TouchPadVerticalCmDistance = 8f;
    private const float VerticalCmPerRaw = TouchPadVerticalCmDistance / RawVerticalDistance;
    //เพิ่มใหม่--- ใช้ dead zone เล็ก ๆ กันสัญญาณสั่น ไม่ใช้ระยะสะสม 16 raw แล้ว
    private const float SwipeDeadZoneRaw = 3f;
    private const float FastSwipeDeltaRaw = 6f;
    private const float HorizontalSwipeBias = 0.7f;
    private const float RotateDeadZoneRaw = 8f;
    private const float TwoFingerRotateDegrees = 90f;
    private const string JunctionHighlightSliderName = "Junction Highlight Slider";
    private const string JunctionHighlightOffsetTextName = "Text (Offset)";

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
    [Tooltip("Assign a SplineJson source to follow JSON roads. Takes priority over Road Network.")]
    [SerializeField] private SplineJson splineJson;
    [SerializeField] private GameObject player;
    [FormerlySerializedAs("worldRotateTarget")]
    [SerializeField] private Transform cameraRotateTarget;

    [Header("Start")]
    [SerializeField] private Vector3 startPosition = new Vector3(0f, 0f, -25f);
    [SerializeField] private Vector3 startEulerAngles = Vector3.zero;
    [SerializeField, Min(1)] private int startRoadNo = 4;
    [SerializeField, Min(0f)] private float startRoadPosition = 13f;
    [SerializeField, Range(0, 1)] private int startDirection = 1;

    [Header("Rotation")]
    [SerializeField] private bool alignToRoadForward = true;
    [SerializeField, Range(0f, 180f)] private float maxTwoFingerYaw = 180f;

    [Header("Highlight")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.1f, 1f);
    [Tooltip("Normalized progress where route highlights become visible near a junction.")]
    [SerializeField, Range(0f, 1f)] private float junctionHighlightStart = 0.7f;
    [SerializeField] private Slider junctionHighlightSlider;
    [SerializeField] private TMP_Text junctionHighlightOffsetText;
    [SerializeField] private float lineWidth = 0.24f;
    [SerializeField, Min(4)] private int samplesPerRoad = 24;
    [SerializeField] private float lineHeightOffset = 0.08f;

    [Header("Another Routes")]
    [Tooltip("สีเส้นทางเลือกที่ไม่ได้เลือก (ขาวจางๆ บนถนน)")]
    [SerializeField] private Color alternativeColor = new Color(1f, 1f, 1f, 0.55f);
    [SerializeField] private float alternativeLineWidth = 0.18f;

    [Header("Debug")]
    [SerializeField] private bool showSwipeDebug = true;

    private Rigidbody rb;
    private SplineContainer splineContainer;
    private ISplineMovementRoute routeNetwork;
    private SplineJsonMovementRoute jsonRoute;
    private bool reportedJsonError;
    public SplineJson SplineSource { get => splineJson; set { splineJson = value; jsonRoute = null; } }
    protected ISplineMovementRoute ActiveRoute => routeNetwork;
    public RoadNetworkSplineCreator.CarState CurrentState => carState;
    public virtual string CurrentRoadLabel => routeNetwork is SplineJsonMovementRoute json
        ? json.GetRoadLabel(carState) : carState != null ? carState.roadNo.ToString() : "-";
    public string CurrentConnection => carState != null && routeNetwork != null ? routeNetwork.GetConnectionLabel(carState) : "";
    private RoadNetworkSplineCreator.CarState carState;

    private TouchpadManager.TouchMode lastMode = TouchpadManager.TouchMode.None;
    private Vector2? lastDragPosition;
    private Vector2? swipeStartPosition;
    private SwipeState currentSwipe = SwipeState.None;
    //เพิ่มใหม่--- ปัดหนึ่งครั้งเลือกทางเพียงหนึ่งครั้ง
    private bool swipeConsumed;
    private float twoFingerYawOffset;
    private Vector2 twoFingerDrag;
    private bool rotatingCamera;

    private LineRenderer routeLine;
    private Material routeMaterial;
    private Material alternativeMaterial;
    private readonly List<LineRenderer> alternativeLines = new List<LineRenderer>();
    private GUIStyle debugStyle;
    private int cachedRoadNo = -1;
    private int cachedLane = -1;
    private int cachedDir = -1;
    private float cachedPos = -1f;
    private bool cachedTraversingConnection;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        ResolveReferences();
        ApplyStartTransform();
        EnsureRouteRenderer();
        EnsureCarState();
    }

    protected virtual void Start()
    {
        ResolveReferences();
        EnsureCarState();
        SetupJunctionHighlightSlider();
        SnapToRoad();
        UpdateRoutePreview(force: true);
    }

    protected virtual void FixedUpdate()
    {
        ResolveReferences();
        if (touchManager == null || routeNetwork == null || splineContainer == null)
        {
            return;
        }

        EnsureCarState();

        //เพิ่มใหม่--- ถ้า TouchManager ควบคุมอยู่ จะไม่อ่าน Touch ซ้ำในสคริปต์นี้
        // ป้องกัน Avatar เคลื่อนที่สองเท่าจาก dragDelta เดียวกัน
        if (!touchManager.ControlsSplineMovement(this))
        {
            HandleTrackpadInput();
            SnapToRoad();
            UpdateRoutePreview(force: false);
        }
    }

    protected virtual void OnDestroy()
    {
        DestroyRuntimeObject(routeMaterial);
        DestroyRuntimeObject(alternativeMaterial);
    }

    private static void DestroyRuntimeObject(Object obj)
    {
        if (obj == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    /// Debug swipe มุมซ้ายล่างของจอ บอกสถานะการปัดนิ้ว
    protected virtual void OnGUI()
    {
        if (!showSwipeDebug || carState == null || routeNetwork == null)
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

        string text = $"Swipe: {currentSwipe}  |  Road {CurrentRoadLabel}  Pos {carState.currentPos:0.0}"
                      + $"  Direction {carState.dir}  Lane {carState.currentLane}"
                      + (atJunction ? "  [JUNCTION]" : "")
                      + (string.IsNullOrEmpty(CurrentConnection) ? "" : "  [" + CurrentConnection + "]");
        GUI.Label(new Rect(16f, Screen.height - 84f, Mathf.Max(900f, debugStyle.CalcSize(new GUIContent(text)).x), 40f), text, debugStyle);

        // บรรทัด 2สถานะ Route History
        List<RoadNetworkSplineCreator.RouteHistoryEntry> history = carState.history;
        int historyCount = history != null ? history.Count : 0;
        string prevRoad = carState.historyIndex > 0 && history != null
            ? history[carState.historyIndex - 1].roadNo.ToString()
            : "-";
        RoadNetworkSplineCreator.RouteHistoryEntry forward = routeNetwork.GetForwardHistory(carState);
        string storedNext = forward != null ? forward.roadNo.ToString() : "-";
        string pendingNext = carState.hasPendingSelection ? carState.pendingNextRoad.ToString() : "-";

        string historyText = $"History {carState.historyIndex}/{historyCount}"
                             + $"  Prev {prevRoad}  StoredNext {storedNext}  Pending {pendingNext}"
                             + $"  Changed {(carState.routeChoiceChanged ? "YES" : "no")}";
        GUI.Label(new Rect(16f, Screen.height - 48f, 900f, 40f), historyText, debugStyle);
    }

    private void ResolveReferences()
    {
        if (touchManager == null || !touchManager.isActiveAndEnabled)
        {
            touchManager = TouchpadManager.Instance;
        }

        routeNetwork = ResolveMovementRoute();

        if (player == null)
        {
            player = gameObject;
        }

        if (cameraRotateTarget == null || cameraRotateTarget == player.transform
            || player.transform.IsChildOf(cameraRotateTarget))
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.transform != player.transform)
            {
                cameraRotateTarget = mainCamera.transform;
            }
        }

        splineContainer = routeNetwork != null ? routeNetwork.Container : null;
    }

    protected virtual ISplineMovementRoute ResolveMovementRoute()
    {
        if (splineJson != null) return ResolveJsonMovementRoute();
        if (roadNetwork == null) roadNetwork = FindAnyObjectByType<RoadNetworkSplineCreator>();
        if (roadNetwork == null) return ResolveJsonMovementRoute();
        if (routeNetwork is LegacySplineMovementRoute existing && existing.Source == roadNetwork) return existing;
        return new LegacySplineMovementRoute(roadNetwork);
    }

    protected ISplineMovementRoute ResolveJsonMovementRoute()
    {
        if (splineJson == null) splineJson = FindAnyObjectByType<SplineJson>();
        if (splineJson == null) return null;
        if (jsonRoute == null || jsonRoute.Source != splineJson) jsonRoute = new SplineJsonMovementRoute(splineJson);
        try
        {
            if (!jsonRoute.EnsureReady()) return null;
            reportedJsonError = false;
            return jsonRoute;
        }
        catch (System.Exception exception)
        {
            if (!reportedJsonError) Debug.LogError("Spline movement JSON route: " + exception.Message, this);
            reportedJsonError = true;
            return null;
        }
    }

    private void EnsureCarState()
    {
        if (carState != null)
        {
            return;
        }

        carState = new RoadNetworkSplineCreator.CarState
        {
            roadNo = startRoadNo,
            currentPos = startRoadPosition,
            dir = startDirection,
            currentLane = 0
        };

        if (routeNetwork != null)
        {
            routeNetwork.EnsureHistory(carState);
        }

        SelectDefaultLane();
    }

    private void ApplyStartTransform()
    {
        if (player == null)
        {
            return;
        }

        player.transform.SetPositionAndRotation(
            startPosition,
            Quaternion.Euler(startEulerAngles));
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

        // เริ่มแตะใหม่ หรือเปลี่ยนจำนวนนิ้ว
        if (touchManager.Status == TouchpadManager.TouchStatus.OnTouch || mode != lastMode)
        {
            lastMode = mode;
            lastDragPosition = position;
            BeginTouchFromManager(position);
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
            //เพิ่มใหม่--- Swipe แนวนอนเลือกทางเท่านั้น ไม่ทำให้ Avatar เคลื่อนที่
            bool blockMovementForSwipe = UpdateSwipe(position, dragDelta);
            if (!blockMovementForSwipe)
            {
                MoveAlongRoad(dragDelta);
            }
        }
    }

    private void ResetTouchState()
    {
        twoFingerDrag = Vector2.zero;
        rotatingCamera = false;
        lastMode = TouchpadManager.TouchMode.None;
        lastDragPosition = null;
        swipeStartPosition = null;
        currentSwipe = SwipeState.None;
        swipeConsumed = false;
    }

    //เพิ่มใหม่--- TouchManager เรียกตอนเริ่มแตะหรือเปลี่ยนจำนวน Touch
    public void BeginTouchFromManager(Vector2 touchPosition)
    {
        twoFingerDrag = Vector2.zero;
        rotatingCamera = false;
        swipeStartPosition = touchPosition;
        currentSwipe = SwipeState.None;
        swipeConsumed = false;
    }

    //เพิ่มใหม่--- TouchManager เรียกตอนปล่อยนิ้ว
    public void EndTouchFromManager()
    {
        ResetTouchState();
    }

    //เพิ่มใหม่--- รับ dragDelta ที่ TouchManager คำนวณแล้ว
    // การลากแนวตั้งจะเดินบน spline และการลากแนวนอนจะตรวจ Swipe เลือกทาง
    public void MoveFromTouchManager(
        Vector2 currentTouchPosition,
        Vector2 dragDelta,
        TouchpadManager.TouchMode mode,
        int touchCount)
    {
        ResolveReferences();
        if (routeNetwork == null || splineContainer == null || player == null)
        {
            return;
        }

        EnsureCarState();

        if (mode == TouchpadManager.TouchMode.Rotate)
        {
            RotateWorld(dragDelta);
        }
        else if (mode == TouchpadManager.TouchMode.Translate && touchCount == 1)
        {
            //เพิ่มใหม่--- Swipe แนวนอนเลือกทางเท่านั้น ไม่ทำให้ Avatar เคลื่อนที่
            bool blockMovementForSwipe = UpdateSwipe(currentTouchPosition, dragDelta);
            if (!blockMovementForSwipe)
            {
                MoveAlongRoad(dragDelta);
            }
        }

        // ตำแหน่งใหม่ = ตำแหน่งบน spline หลังเพิ่มระยะทางจาก dragDelta
        SnapToRoad();
        UpdateRoutePreview(force: false);
    }

    //Swipr ตรงนี้
    //เพิ่มใหม่--- คืนค่า true เมื่อเป็น Swipe เพื่อบล็อกการเคลื่อนที่บน spline
    private bool UpdateSwipe(Vector2 position, Vector2 frameDelta)
    {
        if (swipeStartPosition == null)
        {
            swipeStartPosition = position;
            return true;
        }

        if (swipeConsumed)
        {
            return true;
        }

        Vector2 totalDelta = position - swipeStartPosition.Value;

        // รอให้นิ้วพ้น dead zone เล็กน้อยก่อน เพื่อกัน noise จาก Touchpad
        if (totalDelta.magnitude < SwipeDeadZoneRaw)
        {
            currentSwipe = SwipeState.None;
            return true;
        }

        bool horizontalSwipe = Mathf.Abs(totalDelta.x)
            > Mathf.Abs(totalDelta.y) * HorizontalSwipeBias;

        // แนวตั้งคือการเดิน จึงไม่บล็อก MoveAlongRoad
        if (!horizontalSwipe)
        {
            currentSwipe = totalDelta.y < 0f ? SwipeState.Up : SwipeState.Down;
            return false;
        }

        currentSwipe = totalDelta.x < 0f ? SwipeState.Left : SwipeState.Right;

        // ใช้ความเร็วต่อ FixedUpdate แทนการบังคับลากสะสมให้ถึง 16 raw
        if (Mathf.Abs(frameDelta.x) < FastSwipeDeltaRaw)
        {
            return true;
        }

        if (IsAtJunction())
        {
            swipeConsumed = true;

            if (StepLaneBySwipe(currentSwipe))
            {
                UpdateRoutePreview(force: true);
            }
        }

        return true;
    }

    private SwipeState DetectSwipe(Vector2 delta)
    {
        if (delta.magnitude < SwipeDeadZoneRaw)
        {
            return SwipeState.None;
        }

        //เพิ่มใหม่--- ยอมให้ Swipe ที่เฉียงเล็กน้อยยังนับเป็นซ้าย/ขวา
        if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * HorizontalSwipeBias)
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
        routeNetwork.MoveCarLoop(carState, distance, moveMode);

        if (carState.roadNo != previousRoadNo)
        {
            // ห้ามเรียก SelectDefaultLane ตรงนี้ — MoveCarLoop sync เลนกับ history ให้แล้ว
            Debug.Log($"Spline changed: Road {previousRoadNo} -> Road {carState.roadNo}");
        }
    }

    /// ลาก 2 นิ้วแนวนอน หมุนเฉพาะกล้องรอบแกน Y โดยไม่เปลี่ยนทิศของ Player
    private void RotateWorld(Vector2 dragDelta)
    {
        if (cameraRotateTarget == null || cameraRotateTarget == player.transform
            || player.transform.IsChildOf(cameraRotateTarget))
        {
            return;
        }

        // Apply the dead zone to the gesture, not every hardware packet.
        // Small deltas in a fast build must still allow a slow camera pan.
        if (!rotatingCamera)
        {
            twoFingerDrag += dragDelta;
            if (Mathf.Abs(twoFingerDrag.x) <= RotateDeadZoneRaw
                || Mathf.Abs(twoFingerDrag.x) <= Mathf.Abs(twoFingerDrag.y))
            {
                return;
            }

            dragDelta.x = twoFingerDrag.x - Mathf.Sign(twoFingerDrag.x) * RotateDeadZoneRaw;
            rotatingCamera = true;
        }

        float rotationDegrees = -dragDelta.x * (TwoFingerRotateDegrees / RawVerticalDistance);
        float previousYaw = twoFingerYawOffset;
        twoFingerYawOffset = Mathf.Clamp(
            twoFingerYawOffset + rotationDegrees,
            -maxTwoFingerYaw,
            maxTwoFingerYaw);

        float appliedRotation = twoFingerYawOffset - previousYaw;
        if (Mathf.Abs(appliedRotation) <= Mathf.Epsilon)
        {
            return;
        }

        cameraRotateTarget.Rotate(Vector3.up, appliedRotation, Space.World);
    }

    private void SnapToRoad()
    {
        if (carState == null || routeNetwork == null || player == null)
        {
            return;
        }

        Vector3 worldPosition = routeNetwork.EvaluateRoadPosition(carState);
        //เอาออก เพื่อให้ไปตาม snap to road ของ spline movement
        //worldPosition.y = player.transform.position.y;
        rb.MovePosition(worldPosition);
        AlignToRoadForward();
    }

    private void AlignToRoadForward()
    {
        if (!alignToRoadForward)
        {
            return;
        }

        Vector3 forward = routeNetwork.EvaluateRoadForward(carState);
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.001f)
        {
            return;
        }
        // The spline already rounds every corner. Face its tangent at the
        // current position; Rigidbody interpolation smooths the rendered pose.
        // Camera look remains a separate rotation on the camera transform.
        Quaternion targetRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        if (player == gameObject)
        {
            rb.MoveRotation(targetRotation);
        }
        else
        {
            player.transform.rotation = targetRotation;
        }
    }

    /// อยู่ในช่วงท้ายถนน (5%) และมีทางให้เลือกจริง (มากกว่า 1 ทาง) ไหม
    private bool IsAtJunction()
    {
        // การเลือกทางเกิดบนเส้นก่อนเข้าแยกเท่านั้น เมื่อรถกำลังข้าม spline
        // ภายในแยกให้ต่อ default ทางตรงไปเลยและไม่แสดง highlight ซ้ำ
        if (routeNetwork == null || carState == null || routeNetwork.IsTraversingConnection(carState)
            || routeNetwork.IsJunctionTraversalRoad(carState.roadNo))
        {
            return false;
        }

        RoadNetworkSplineCreator.RoadData road = routeNetwork.GetRoadData(carState.roadNo);
        if (road == null || road.length <= Mathf.Epsilon)
        {
            return false;
        }

        float normalizedPos = carState.currentPos / road.length;
        float threshold = Mathf.Clamp01(junctionHighlightStart);
        bool nearEnd = carState.dir == 0
            ? normalizedPos >= threshold
            : normalizedPos <= 1f - threshold;

        return nearEnd && GetLaneOptions().Count > 1;
    }

    private void SetupJunctionHighlightSlider()
    {
        if (junctionHighlightSlider == null)
        {
            junctionHighlightSlider = FindJunctionHighlightSlider();
        }

        if (junctionHighlightSlider == null)
        {
            junctionHighlightSlider = CreateJunctionHighlightSlider();
        }

        if (junctionHighlightSlider == null)
        {
            return;
        }

        junctionHighlightSlider.minValue = 0f;
        junctionHighlightSlider.maxValue = 1f;
        junctionHighlightSlider.wholeNumbers = false;

        if (junctionHighlightOffsetText == null)
        {
            junctionHighlightOffsetText = FindJunctionHighlightOffsetText();
        }

        junctionHighlightSlider.SetValueWithoutNotify(junctionHighlightStart);
        junctionHighlightSlider.onValueChanged.RemoveListener(SetJunctionHighlightStart);
        junctionHighlightSlider.onValueChanged.AddListener(SetJunctionHighlightStart);
        UpdateJunctionHighlightLabel();
    }

    private Slider FindJunctionHighlightSlider()
    {
        Slider[] sliders = FindObjectsByType<Slider>(FindObjectsInactive.Include);
        for (int i = 0; i < sliders.Length; i++)
        {
            if (sliders[i].name == JunctionHighlightSliderName)
            {
                return sliders[i];
            }
        }

        return null;
    }

    private TMP_Text FindJunctionHighlightOffsetText()
    {
        if (junctionHighlightSlider == null)
        {
            return null;
        }

        TMP_Text[] textComponents =
            junctionHighlightSlider.GetComponentsInChildren<TMP_Text>(true);

        for (int i = 0; i < textComponents.Length; i++)
        {
            if (textComponents[i].name == JunctionHighlightOffsetTextName)
            {
                return textComponents[i];
            }
        }

        return null;
    }

    private Slider CreateJunctionHighlightSlider()
    {
        Toggle roadColliderToggle = null;
        Toggle[] toggles = FindObjectsByType<Toggle>(FindObjectsInactive.Include);
        for (int i = 0; i < toggles.Length; i++)
        {
            Text label = toggles[i].GetComponentInChildren<Text>(true);
            if (label != null && label.text == "Road Collider")
            {
                roadColliderToggle = toggles[i];
                break;
            }
        }

        if (roadColliderToggle == null || roadColliderToggle.transform.parent == null)
        {
            return null;
        }

        Transform parent = roadColliderToggle.transform.parent;
        GameObject row = new GameObject(
            "Junction Highlight Setting",
            typeof(RectTransform),
            typeof(LayoutElement));
        row.layer = roadColliderToggle.gameObject.layer;
        row.transform.SetParent(parent, false);
        row.transform.SetSiblingIndex(roadColliderToggle.transform.GetSiblingIndex() + 1);
        row.GetComponent<RectTransform>().sizeDelta = new Vector2(220f, 46f);
        row.GetComponent<LayoutElement>().preferredHeight = 46f;

        Text valueLabel = CreateSliderText(row.transform);
        valueLabel.name = "Value";

        GameObject sliderObject = new GameObject(JunctionHighlightSliderName, typeof(RectTransform), typeof(Slider));
        sliderObject.layer = row.layer;
        sliderObject.transform.SetParent(row.transform, false);
        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 0f);
        sliderRect.anchorMax = new Vector2(1f, 0f);
        sliderRect.pivot = new Vector2(0.5f, 0f);
        sliderRect.anchoredPosition = new Vector2(0f, 2f);
        sliderRect.sizeDelta = new Vector2(0f, 20f);

        RectTransform background = CreateSliderImage(
            sliderObject.transform,
            "Background",
            new Color(1f, 1f, 1f, 0.35f));
        background.anchorMin = new Vector2(0f, 0.4f);
        background.anchorMax = new Vector2(1f, 0.6f);
        background.sizeDelta = Vector2.zero;

        RectTransform fillArea = CreateSliderRect(sliderObject.transform, "Fill Area");
        fillArea.anchorMin = new Vector2(0f, 0f);
        fillArea.anchorMax = new Vector2(1f, 1f);
        fillArea.offsetMin = new Vector2(5f, 0f);
        fillArea.offsetMax = new Vector2(-5f, 0f);
        RectTransform fill = CreateSliderImage(fillArea, "Fill", highlightColor);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.sizeDelta = Vector2.zero;

        RectTransform handleArea = CreateSliderRect(sliderObject.transform, "Handle Slide Area");
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(5f, 0f);
        handleArea.offsetMax = new Vector2(-5f, 0f);
        RectTransform handle = CreateSliderImage(handleArea, "Handle", Color.white);
        handle.sizeDelta = new Vector2(16f, 16f);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private static Text CreateSliderText(Transform parent)
    {
        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textObject.layer = parent.gameObject.layer;
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 22f);

        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateSliderRect(Transform parent, string objectName)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        return child.GetComponent<RectTransform>();
    }

    private static RectTransform CreateSliderImage(Transform parent, string objectName, Color color)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        Image image = child.GetComponent<Image>();
        image.color = color;
        return child.GetComponent<RectTransform>();
    }

    public void SetJunctionHighlightStart(float value)
    {
        junctionHighlightStart = Mathf.Clamp01(value);
        UpdateJunctionHighlightLabel();
        UpdateRoutePreview(force: true);
    }

    private void UpdateJunctionHighlightLabel()
    {
        if (junctionHighlightSlider == null)
        {
            return;
        }

        Text label = junctionHighlightSlider.transform.parent.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = $"Junction Highlight  {junctionHighlightStart:0.00}";
        }

        if (junctionHighlightOffsetText != null)
        {
            float minimum = junctionHighlightSlider.minValue;
            float maximum = junctionHighlightSlider.maxValue;
            junctionHighlightOffsetText.text =
                $"Min {minimum:0.00}   Value {junctionHighlightStart:0.00}   Max {maximum:0.00}";
        }
    }

    /// ค่าเริ่มต้นเลือกทางที่ตรงที่สุด (มุมเลี้ยวน้อยสุด)
    /// ใช้เฉพาะเมื่อไม่มี history ด้านหน้า — ห้าม default ทับเส้นทางที่เคยเลือก
    private void SelectDefaultLane()
    {
        if (routeNetwork == null || carState == null)
        {
            return;
        }

        routeNetwork.SyncLaneWithForwardHistory(carState);
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
        SetPendingSelection(selected);
        Debug.Log($"Junction swipe {swipe}: lane={selected.LaneIndex}, nextRoad={selected.NextRoadNo}, "
                  + $"angle={selected.SignedAngle:0.0}, changed={carState.routeChoiceChanged}");
        return true;
    }

    // Swipe = Preview เท่านั้น ยังไม่ commit ลง history จนกว่าจะข้ามเข้าถนนมหม่
    private void SetPendingSelection(LaneOption selected)
    {
        carState.pendingNextRoad = selected.NextRoadNo;
        carState.pendingEnterNode = selected.EnterNode;
        carState.hasPendingSelection = true;

        // เปลี่ยนเส้นทางจริงหรือไม่ = pending ต่างจาก history ด้านหน้าที่เคยเลือกไว้
        RoadNetworkSplineCreator.RouteHistoryEntry forward = routeNetwork.GetForwardHistory(carState);
        carState.routeChoiceChanged = forward != null
            && (forward.roadNo != selected.NextRoadNo || forward.enterNode != selected.EnterNode);
    }

    //ทางเลือกทั้งหมดที่ปลายถนนปัจจุบัน พร้อมมุมเลี้ยวเทียบทิศรถ
    private List<LaneOption> GetLaneOptions()
    {
        Vector3 currentForward = routeNetwork.EvaluateRoadForward(carState);
        return BuildLaneOptions(carState.roadNo, carState.dir, currentForward);
    }

    //ทางเลือกที่ปลายถนน roadNo (ทิศ dir) โดยไม่ต้องอิงกับ carState ปัจจุบัน ใช้ดูล่วงหน้า
    private List<LaneOption> GetLaneOptionsFor(int roadNo, int dir)
    {
        RoadNetworkSplineCreator.RoadData road = routeNetwork.GetRoadData(roadNo);
        if (road == null || road.length <= Mathf.Epsilon)
        {
            return new List<LaneOption>();
        }

        RoadNetworkSplineCreator.CarState midState = new RoadNetworkSplineCreator.CarState
        {
            roadNo = roadNo,
            dir = dir,
            currentLane = 0,
            currentPos = Mathf.Max(road.length * 0.5f, 0.01f)
        };

        Vector3 currentForward = routeNetwork.EvaluateRoadForward(midState);
        return BuildLaneOptions(roadNo, dir, currentForward);
    }

    // ทางที่ตรงที่สุด (มุมเลี้ยวน้อยสุด) ในบรรดาทางเลือกที่ปลายถนน roadNo (ทิศ dir)
    private LaneOption? GetDefaultLaneOptionFor(int roadNo, int dir)
    {
        List<LaneOption> options = GetLaneOptionsFor(roadNo, dir);
        if (options.Count == 0)
        {
            return null;
        }

        RoadNetworkSplineCreator.RoadData road = routeNetwork.GetRoadData(roadNo);
        int defaultLane = dir == 0 ? road.defaultLaneE : road.defaultLaneS;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].LaneIndex == defaultLane)
            {
                return options[i];
            }
        }

        return options[0];
    }

    private List<LaneOption> BuildLaneOptions(int roadNo, int dir, Vector3 currentForward)
    {
        List<LaneOption> options = new List<LaneOption>();
        RoadNetworkSplineCreator.RoadData road = routeNetwork.GetRoadData(roadNo);
        if (road == null)
        {
            return options;
        }

        RoadNetworkSplineCreator.RoadConnection[] lanes = dir == 0 ? road.laneE : road.laneS;
        if (lanes == null)
        {
            return options;
        }

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
        RoadNetworkSplineCreator.RoadData nextRoad = routeNetwork.GetRoadData(connection.roadNo);
        if (nextRoad == null)
        {
            return Vector3.zero;
        }

        RoadNetworkSplineCreator.CarState nextState = new RoadNetworkSplineCreator.CarState
        {
            roadNo = connection.roadNo,
            dir = connection.enterNode == 0 ? 0 : 1,
            currentLane = 0,
            // อ่านทิศทางที่กลางถนนถัดไป
            currentPos = Mathf.Max(nextRoad.length * 0.5f, 0.01f)
        };

        return routeNetwork.EvaluateRoadForward(nextState);
    }

    // Route highlight: ระบายเส้นทางที่จะไป
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

        Shader lineShader = FindLineShader();

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

        EnsureAlternativeMaterial(lineShader);
    }

    private static Shader FindLineShader()
    {
        Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineShader == null) lineShader = Shader.Find("Unlit/Color");
        if (lineShader == null) lineShader = Shader.Find("Sprites/Default");
        if (lineShader == null) lineShader = Shader.Find("Standard");
        return lineShader;
    }

    // Mat โปร่งแสงสำหรับเส้นทางเลือกสีขาวจางๆ
    private void EnsureAlternativeMaterial(Shader lineShader)
    {
        if (alternativeMaterial != null)
        {
            return;
        }

        alternativeMaterial = lineShader != null
            ? new Material(lineShader)
            : new Material(routeMaterial);

        alternativeMaterial.color = alternativeColor;
        MakeMaterialTransparent(alternativeMaterial);
    }

    // ตั้งค่า blend ให้ alpha ทำงาน 
    private static void MakeMaterialTransparent(Material material)
    {
        if (material.HasProperty("_Surface"))
        {
            //Surface Type = Transparent
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    // LineRenderer สำหรับเส้นทางเลือกลำดับที่ index
    private LineRenderer GetAlternativeLine(int index)
    {
        while (alternativeLines.Count <= index)
        {
            GameObject lineObject = new GameObject($"AlternativeRoute_{alternativeLines.Count}");
            lineObject.transform.SetParent(transform, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = alternativeMaterial;
            line.startColor = alternativeColor;
            line.endColor = alternativeColor;
            line.startWidth = alternativeLineWidth;
            line.endWidth = alternativeLineWidth;
            line.widthMultiplier = 1f;
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            alternativeLines.Add(line);
        }

        return alternativeLines[index];
    }

    private void HideAlternativeLines(int fromIndex)
    {
        for (int i = fromIndex; i < alternativeLines.Count; i++)
        {
            alternativeLines[i].positionCount = 0;
        }
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
            && cachedTraversingConnection == routeNetwork.IsTraversingConnection(carState)
            && Mathf.Abs(cachedPos - carState.currentPos) < 0.05f)
        {
            return;
        }

        if (!IsAtJunction())
        {
            routeLine.positionCount = 0;
            HideAlternativeLines(0);
            CachePreviewState();
            return;
        }

        List<Vector3> points = BuildPreviewPoints();
        routeLine.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
        {
            routeLine.SetPosition(i, points[i]);
        }

        UpdateAlternativePreviews();

        CachePreviewState();
    }

    private void CachePreviewState()
    {
        cachedRoadNo = carState.roadNo;
        cachedLane = carState.currentLane;
        cachedDir = carState.dir;
        cachedPos = carState.currentPos;
        cachedTraversingConnection = routeNetwork.IsTraversingConnection(carState);
    }

    private List<Vector3> BuildPreviewPoints()
    {
        List<Vector3> points = new List<Vector3>();

        // Current road
        AppendRoadSegment(
            points,
            carState.roadNo,
            GetCurrentRoadStartT(),
            carState.dir == 0 ? 1f : 0f);

        // Draw exactly one selected road after the junction. 
        LaneOption? selectedLane = GetSelectedLaneOption();
        if (selectedLane != null)
        {
            float startT = selectedLane.Value.EnterNode == 0 ? 0f : 1f;
            routeNetwork.AppendConnectionPreview(points, carState.roadNo, carState.dir,
                selectedLane.Value.NextRoadNo, selectedLane.Value.EnterNode, samplesPerRoad, lineHeightOffset);
            AppendRoadSegment(
                points,
                selectedLane.Value.NextRoadNo,
                startT,
                1f - startT);
        }

        if (points.Count == 0)
        {
            Vector3 fallback = routeNetwork.EvaluateRoadPosition(carState);
            fallback.y += lineHeightOffset;
            points.Add(fallback);
        }

        return points;
    }
    // the forward-only.
    private List<Vector3> BuildPreviewPointsWithHistory()
    {
        List<Vector3> points = new List<Vector3>();

        AppendRoadSegment(points, carState.roadNo, GetCurrentRoadStartT(), carState.dir == 0 ? 1f : 0f);
        int endRoadNo = carState.roadNo;
        int endDir = carState.dir;

        LaneOption? selectedLane = GetSelectedLaneOption();
        int historyStart = carState.historyIndex + 1;
        if (selectedLane != null)
        {
            float startT = selectedLane.Value.EnterNode == 0 ? 0f : 1f;
            routeNetwork.AppendConnectionPreview(points, carState.roadNo, carState.dir,
                selectedLane.Value.NextRoadNo, selectedLane.Value.EnterNode, samplesPerRoad, lineHeightOffset);
            AppendRoadSegment(points, selectedLane.Value.NextRoadNo, startT, 1f - startT);
            endRoadNo = selectedLane.Value.NextRoadNo;
            endDir = selectedLane.Value.EnterNode == 0 ? 0 : 1;
            historyStart++;
        }

        // ปิดเลนไว้ก่อน 
        if (!carState.routeChoiceChanged && carState.history != null)
        {
            for (int i = historyStart; i < carState.history.Count; i++)
            {
                RoadNetworkSplineCreator.RouteHistoryEntry entry = carState.history[i];
                float startT = entry.enterNode == 0 ? 0f : 1f;
                AppendRoadSegment(points, entry.roadNo, startT, 1f - startT);
                endRoadNo = entry.roadNo;
                endDir = entry.dirOnEnter;
            }

            // ต่อเส้นทางที่ตรงที่สุดถัดจากปลายที่รู้จัก ให้เส้นฟ้ายาวต่อกันไปเลยแทนที่จะหยุด
            LaneOption? lookahead = GetDefaultLaneOptionFor(endRoadNo, endDir);
            if (lookahead != null)
            {
                float startT = lookahead.Value.EnterNode == 0 ? 0f : 1f;
                AppendRoadSegment(points, lookahead.Value.NextRoadNo, startT, 1f - startT);
            }
        }

        if (points.Count == 0)
        {
            Vector3 fallback = routeNetwork.EvaluateRoadPosition(carState);
            fallback.y += lineHeightOffset;
            points.Add(fallback);
        }

        return points;
    }

    // วาดทางเลือกที่ไม่ได้เลือกเป็นเส้นขาวจางๆ ตอนอยู่ทางแยก
    private void UpdateAlternativePreviews()
    {
        if (!IsAtJunction())
        {
            HideAlternativeLines(0);
            return;
        }

        List<LaneOption> options = GetLaneOptions();
        int usedLines = 0;

        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].LaneIndex == carState.currentLane)
            {
                continue; // ทางที่เลือกอยู่แล้ว วาดด้วยเส้นฟ้าหลัก
            }

            List<Vector3> points = new List<Vector3>();
            routeNetwork.AppendConnectionPreview(points, carState.roadNo, carState.dir,
                options[i].NextRoadNo, options[i].EnterNode, samplesPerRoad, lineHeightOffset * 0.5f);
            float startT = options[i].EnterNode == 0 ? 0f : 1f;
            // ยกต่ำกว่าเส้นฟ้าเล็กน้อย กัน z-fighting ตรงจุดที่เส้นตัดกัน
            AppendRoadSegment(points, options[i].NextRoadNo, startT, 1f - startT, lineHeightOffset * 0.5f);

            if (points.Count < 2)
            {
                continue;
            }

            LineRenderer line = GetAlternativeLine(usedLines);
            line.positionCount = points.Count;
            for (int p = 0; p < points.Count; p++)
            {
                line.SetPosition(p, points[p]);
            }

            usedLines++;
        }

        HideAlternativeLines(usedLines);
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
        RoadNetworkSplineCreator.RoadData road = routeNetwork.GetRoadData(carState.roadNo);
        if (road == null || road.length <= Mathf.Epsilon)
        {
            return 0f;
        }

        return Mathf.Clamp01(carState.currentPos / road.length);
    }

    private void AppendRoadSegment(List<Vector3> points, int roadNo, float startT, float endT, float? heightOffset = null)
    {
        if (routeNetwork == null || routeNetwork.GetRoadData(roadNo) == null) return;
        int steps = Mathf.Max(2, samplesPerRoad);
        float yOffset = heightOffset ?? lineHeightOffset;

        for (int i = 0; i < steps; i++)
        {
            float t = Mathf.Lerp(startT, endT, i / (float)(steps - 1));
            Vector3 worldPoint = routeNetwork.EvaluateRoadPoint(roadNo, t);
            worldPoint.y += yOffset;

            if (points.Count > 0 && Vector3.Distance(points[points.Count - 1], worldPoint) < 0.01f)
            {
                continue;
            }

            points.Add(worldPoint);
        }
    }
}
