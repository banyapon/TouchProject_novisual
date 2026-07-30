using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UI;

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
    private const string JunctionHighlightSliderName = "Junction Highlight Slider";

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

    [Header("Start")]
    [SerializeField] private Vector3 startPosition = new Vector3(0f, 0f, -25f);
    [SerializeField] private Vector3 startEulerAngles = Vector3.zero;
    [SerializeField, Min(1)] private int startRoadNo = 4;
    [SerializeField, Min(0f)] private float startRoadPosition = 13f;
    [SerializeField, Range(0, 1)] private int startDirection = 1;

    [Header("Rotation")]
    [SerializeField] private bool alignToRoadForward = true;
    [SerializeField] private float headingTurnSpeed = 240f;
    [SerializeField, Range(0f, 180f)] private float maxTwoFingerYaw = 180f;

    [Header("Highlight")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.1f, 1f);
    [Tooltip("Normalized progress where route highlights become visible near a junction.")]
    [SerializeField, Range(0f, 1f)] private float junctionHighlightStart = 0.8f;
    [SerializeField] private Slider junctionHighlightSlider;
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
    private RoadNetworkSplineCreator.CarState carState;

    private TouchpadManager.TouchMode lastMode = TouchpadManager.TouchMode.None;
    private Vector2? lastDragPosition;
    private Vector2? swipeStartPosition;
    private SwipeState currentSwipe = SwipeState.None;
    private float twoFingerYawOffset;

    private LineRenderer routeLine;
    private Material routeMaterial;
    private Material alternativeMaterial;
    private readonly List<LineRenderer> alternativeLines = new List<LineRenderer>();
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
        ApplyStartTransform();
        EnsureRouteRenderer();
        EnsureCarState();
    }

    private void Start()
    {
        ResolveReferences();
        EnsureCarState();
        SetupJunctionHighlightSlider();
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

        string text = $"Swipe: {currentSwipe}  |  Rail {carState.roadNo}  Pos {carState.currentPos:0.0}"
                      + $"  Dir {carState.dir}  Lane {carState.currentLane}"
                      + (atJunction ? "  [JUNCTION]" : "");
        GUI.Label(new Rect(16f, Screen.height - 84f, 900f, 40f), text, debugStyle);

        // บรรทัด 2สถานะ Route History
        List<RoadNetworkSplineCreator.RouteHistoryEntry> history = carState.history;
        int historyCount = history != null ? history.Count : 0;
        string prevRail = carState.historyIndex > 0 && history != null
            ? history[carState.historyIndex - 1].roadNo.ToString()
            : "-";
        RoadNetworkSplineCreator.RouteHistoryEntry forward = roadNetwork.GetForwardHistory(carState);
        string storedNext = forward != null ? forward.roadNo.ToString() : "-";
        string pendingNext = carState.hasPendingSelection ? carState.pendingNextRoad.ToString() : "-";

        string historyText = $"History {carState.historyIndex}/{historyCount}"
                             + $"  Prev {prevRail}  StoredNext {storedNext}  Pending {pendingNext}"
                             + $"  Changed {(carState.routeChoiceChanged ? "YES" : "no")}";
        GUI.Label(new Rect(16f, Screen.height - 48f, 900f, 40f), historyText, debugStyle);
    }

    private void ResolveReferences()
    {
        if (touchManager == null)
        {
            touchManager = TouchpadManager.Instance;
        }

        if (roadNetwork == null)
        {
            roadNetwork = FindAnyObjectByType<RoadNetworkSplineCreator>();
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
            roadNo = startRoadNo,
            currentPos = startRoadPosition,
            dir = startDirection,
            currentLane = 0
        };

        if (roadNetwork != null)
        {
            roadNetwork.EnsureHistory(carState);
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
            // ห้ามเรียก SelectDefaultLane ตรงนี้ — MoveCarLoop sync เลนกับ history ให้แล้ว
            Debug.Log($"Spline changed: Road {previousRoadNo} -> Road {carState.roadNo}");
        }
    }

    /// ลาก 2 นิ้วแนวนอน หมุนโลกรอบแกน Y
    private void RotateWorld(Vector2 dragDelta)
    {
        if (Mathf.Abs(dragDelta.x) <= RotateDeadZoneRaw || Mathf.Abs(dragDelta.x) <= Mathf.Abs(dragDelta.y))
        {
            return;
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

        bool rotatePlayer = worldRotateTarget == null || worldRotateTarget == player.transform;
        if (!rotatePlayer || !alignToRoadForward)
        {
            Transform rotateTarget = worldRotateTarget != null ? worldRotateTarget : player.transform;
            rotateTarget.Rotate(Vector3.up, appliedRotation, Space.World);
        }
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
        bool rotatePlayer = worldRotateTarget == null || worldRotateTarget == player.transform;
        if (rotatePlayer)
        {
            targetRotation *= Quaternion.Euler(0f, twoFingerYawOffset, 0f);
        }
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

    /// อยู่ในช่วงท้ายถนน (5%) และมีทางให้เลือกจริง (มากกว่า 1 ทาง) ไหม
    private bool IsAtJunction()
    {
        // การเลือกทางเกิดบนเส้นก่อนเข้าแยกเท่านั้น เมื่อรถกำลังข้าม spline
        // ภายในแยกให้ต่อ default ทางตรงไปเลยและไม่แสดง highlight ซ้ำ
        if (roadNetwork.IsJunctionTraversalRoad(carState.roadNo))
        {
            return false;
        }

        RoadNetworkSplineCreator.RoadData road = roadNetwork.GetRoadData(carState.roadNo);
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
    }

    /// ค่าเริ่มต้นเลือกทางที่ตรงที่สุด (มุมเลี้ยวน้อยสุด)
    /// ใช้เฉพาะเมื่อไม่มี history ด้านหน้า — ห้าม default ทับเส้นทางที่เคยเลือก
    private void SelectDefaultLane()
    {
        if (roadNetwork == null || carState == null)
        {
            return;
        }

        roadNetwork.SyncLaneWithForwardHistory(carState);
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

    /// Swipe = Preview เท่านั้น ยังไม่ commit ลง history จนกว่าจะข้ามเข้ารางใหม่จริง
    private void SetPendingSelection(LaneOption selected)
    {
        carState.pendingNextRoad = selected.NextRoadNo;
        carState.pendingEnterNode = selected.EnterNode;
        carState.hasPendingSelection = true;

        // เปลี่ยนเส้นทางจริงหรือไม่ = pending ต่างจาก history ด้านหน้าที่เคยเลือกไว้
        RoadNetworkSplineCreator.RouteHistoryEntry forward = roadNetwork.GetForwardHistory(carState);
        carState.routeChoiceChanged = forward != null
            && (forward.roadNo != selected.NextRoadNo || forward.enterNode != selected.EnterNode);
    }

    /// ทางเลือกทั้งหมดที่ปลายถนนปัจจุบัน พร้อมมุมเลี้ยวเทียบทิศรถ
    private List<LaneOption> GetLaneOptions()
    {
        Vector3 currentForward = roadNetwork.EvaluateRoadForward(carState);
        return BuildLaneOptions(carState.roadNo, carState.dir, currentForward);
    }

    /// ทางเลือกที่ปลายถนน roadNo (ทิศ dir) โดยไม่ต้องอิงกับ carState ปัจจุบัน ใช้ดูล่วงหน้า
    private List<LaneOption> GetLaneOptionsFor(int roadNo, int dir)
    {
        RoadNetworkSplineCreator.RoadData road = roadNetwork.GetRoadData(roadNo);
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

        Vector3 currentForward = roadNetwork.EvaluateRoadForward(midState);
        return BuildLaneOptions(roadNo, dir, currentForward);
    }

    /// ทางที่ตรงที่สุด (มุมเลี้ยวน้อยสุด) ในบรรดาทางเลือกที่ปลายถนน roadNo (ทิศ dir)
    private LaneOption? GetDefaultLaneOptionFor(int roadNo, int dir)
    {
        List<LaneOption> options = GetLaneOptionsFor(roadNo, dir);
        if (options.Count == 0)
        {
            return null;
        }

        RoadNetworkSplineCreator.RoadData road = roadNetwork.GetRoadData(roadNo);
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
        RoadNetworkSplineCreator.RoadData road = roadNetwork.GetRoadData(roadNo);
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

    /// วัสดุโปร่งแสงสำหรับเส้นทางเลือกสีขาวจางๆ
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

    /// ตั้งค่า blend ให้ alpha ทำงาน (รองรับทั้ง URP Unlit และ shader legacy)
    private static void MakeMaterialTransparent(Material material)
    {
        if (material.HasProperty("_Surface"))
        {
            // URP Unlit: Surface Type = Transparent
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

    /// LineRenderer สำหรับเส้นทางเลือกลำดับที่ index (สร้างเพิ่มเมื่อไม่พอ)
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
    }

    private List<Vector3> BuildPreviewPoints()
    {
        List<Vector3> points = new List<Vector3>();

        // Current road: draw only from the car toward the node ahead.
        AppendRoadSegment(
            points,
            carState.roadNo,
            GetCurrentRoadStartT(),
            carState.dir == 0 ? 1f : 0f);

        // Draw exactly one selected road after the junction. Extending through
        // history/lookahead can make a later road appear to converge from the
        // opposite side of the current junction.
        LaneOption? selectedLane = GetSelectedLaneOption();
        if (selectedLane != null)
        {
            float startT = selectedLane.Value.EnterNode == 0 ? 0f : 1f;
            AppendRoadSegment(
                points,
                selectedLane.Value.NextRoadNo,
                startT,
                1f - startT);
        }

        if (points.Count == 0)
        {
            Vector3 fallback = roadNetwork.EvaluateRoadPosition(carState);
            fallback.y += lineHeightOffset;
            points.Add(fallback);
        }

        return points;
    }

    // Kept as a reference for the history UI, but route highlighting now uses
    // the forward-only method above.
    private List<Vector3> BuildPreviewPointsWithHistory()
    {
        List<Vector3> points = new List<Vector3>();
        // Preview starts at the car and only follows its forward direction.
        AppendRoadSegment(points, carState.roadNo, GetCurrentRoadStartT(), carState.dir == 0 ? 1f : 0f);
        int endRoadNo = carState.roadNo;
        int endDir = carState.dir;

        LaneOption? selectedLane = GetSelectedLaneOption();
        int historyStart = carState.historyIndex + 1;
        if (selectedLane != null)
        {
            float startT = selectedLane.Value.EnterNode == 0 ? 0f : 1f;
            AppendRoadSegment(points, selectedLane.Value.NextRoadNo, startT, 1f - startT);
            endRoadNo = selectedLane.Value.NextRoadNo;
            endDir = selectedLane.Value.EnterNode == 0 ? 0 : 1;
            historyStart++;
        }

        // เลนที่เลือกยัง pending ต่างจาก history เดิม เส้นทางถัดจากนี้ยังไม่แน่นอน จึงไม่วาดต่อ
        // Do not append later history/lookahead here. Those roads can approach
        // the next junction from another side and look like a converging route.
        // The immediate selected road above is the only forward preview needed.
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

            // ต่อเส้นทางที่ตรงที่สุดถัดจากปลายที่รู้จัก ให้เส้นเหลืองยาวต่อกันไปเลยแทนที่จะหยุดรอ
            LaneOption? lookahead = GetDefaultLaneOptionFor(endRoadNo, endDir);
            if (lookahead != null)
            {
                float startT = lookahead.Value.EnterNode == 0 ? 0f : 1f;
                AppendRoadSegment(points, lookahead.Value.NextRoadNo, startT, 1f - startT);
            }
        }

        if (points.Count == 0)
        {
            Vector3 fallback = roadNetwork.EvaluateRoadPosition(carState);
            fallback.y += lineHeightOffset;
            points.Add(fallback);
        }

        return points;
    }

    /// วาดทางเลือกที่ไม่ได้เลือกเป็นเส้นขาวจางๆ (แสดงเฉพาะตอนอยู่ทางแยก)
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
                continue; // ทางที่เลือกอยู่แล้ว วาดด้วยเส้นเหลืองหลัก
            }

            List<Vector3> points = new List<Vector3>();
            float startT = options[i].EnterNode == 0 ? 0f : 1f;
            // ยกต่ำกว่าเส้นเหลืองเล็กน้อย กัน z-fighting ตรงจุดที่เส้นตัดกัน
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
        RoadNetworkSplineCreator.RoadData road = roadNetwork.GetRoadData(carState.roadNo);
        if (road == null || road.length <= Mathf.Epsilon)
        {
            return 0f;
        }

        return Mathf.Clamp01(carState.currentPos / road.length);
    }

    private void AppendRoadSegment(List<Vector3> points, int roadNo, float startT, float endT, float? heightOffset = null)
    {
        int splineIndex = roadNo - 1;
        if (splineIndex < 0 || splineIndex >= splineContainer.Splines.Count)
        {
            return;
        }

        Spline spline = splineContainer.Splines[splineIndex];
        int steps = Mathf.Max(2, samplesPerRoad);
        float yOffset = heightOffset ?? lineHeightOffset;

        for (int i = 0; i < steps; i++)
        {
            float t = Mathf.Lerp(startT, endT, i / (float)(steps - 1));
            float3 localPoint = spline.EvaluatePosition(t);
            Vector3 worldPoint = splineContainer.transform.TransformPoint((Vector3)localPoint);
            worldPoint.y += yOffset;

            if (points.Count > 0 && Vector3.Distance(points[points.Count - 1], worldPoint) < 0.01f)
            {
                continue;
            }

            points.Add(worldPoint);
        }
    }
}
