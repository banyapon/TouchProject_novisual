using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Splines;

[RequireComponent(typeof(Rigidbody))]
public class splinemovement : MonoBehaviour
{
    [Header("References")]
    public RoadNetworkSplineCreator roadNetwork;

    [Header("Movement")]
    public float baseSpeed = 6f;
    public float maxSpeed = 18f;
    public float fallbackDpi = 160f;

    [Header("Drag (cm)")]
    public float dragDeadZoneCm = 0.1f;
    public float cmToSpeedScale = 3f;

    [Header("Hold Detection")]
    public float holdPixelThreshold = 5f;
    public float holdTimeThreshold = 0.1f;

    [Header("Swipe (cm/s)")]
    public float swipeVelocityThresholdCm = 12f;
    public float swipeMaxDuration = 0.2f;
    public float swipeMinDistanceCm = 2.0f;
    public float swipeMinPixels = 50f;

    [Header("Two-Finger Rotation")]
    public bool enableTwoFingerRotation = true;
    public float twoFingerRotateSpeed = 0.15f;

    [Header("Junction Zone")]
    [Range(0f, 0.3f)] public float junctionNormalizedRadius = 0.15f;

    [Header("Rotation Animation")]
    public float tangentRotateSpeed = 120f;

    [Header("Road Highlight")]
    public Color currentRoadColor = new Color(1f, 0.9f, 0.1f, 1f);
    public Color nextRoadColor = new Color(0.2f, 1f, 0.4f, 1f);
    public Color idleRoadColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
    [Min(4)] public int lineResolution = 48;
    public float lineWidth = 0.2f;

    private Rigidbody rb;
    private SplineContainer splineContainer;
    private RoadNetworkSplineCreator.CarState carState;
    private LineRenderer[] lineRenderers;
    private Material[] lineMaterials;

    private float yRotationOffset;
    private bool isAnimatingRotation;
    private Quaternion targetRotation;

    private Vector2 touchStartPos;
    private float touchStartTime;
    private bool isTouching;
    private bool touchStartedOnUI;
    private bool isTwoFingerActive;
    private float holdTimer;
    private bool gestureLocked;
    private Vector2 dragDistanceCm;
    private float totalDragDistanceCm;

    private int prevRoadNo = -1;
    private bool prevAtJunction;

    private enum GestureType { None, Hold, Drag, Swipe }
    private GestureType currentGesture;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.isKinematic = true;

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetworkSplineCreator>();

        if (roadNetwork == null)
        {
            Debug.LogError("splinemovement: RoadNetworkSplineCreator not found.");
            enabled = false;
            return;
        }

        splineContainer = roadNetwork.GetComponent<SplineContainer>();

        carState = new RoadNetworkSplineCreator.CarState
        {
            roadNo = 1,
            currentPos = 0f,
            dir = 0,
            currentLane = 0
        };

        ApplyDefaultLane();
        BuildLineRenderers();
        PlaceOnRoad(true);
        UpdateHighlight();
    }

    void Update()
    {
        if (roadNetwork == null) return;

        HandleTouchInput();

        if (isAnimatingRotation)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRotation,
                tangentRotateSpeed * Time.deltaTime);

            if (Quaternion.Angle(transform.rotation, targetRotation) < 0.5f)
            {
                transform.rotation = targetRotation;
                isAnimatingRotation = false;
            }
        }

        PlaceOnRoad(false);

        bool atJunction = IsAtJunction();
        if (carState.roadNo != prevRoadNo || atJunction != prevAtJunction)
        {
            UpdateHighlight();
            prevRoadNo = carState.roadNo;
            prevAtJunction = atJunction;
        }
    }

    void PlaceOnRoad(bool forceRotation)
    {
        Vector3 worldPos = roadNetwork.EvaluateRoadPosition(carState);
        worldPos.y = transform.position.y;
        rb.MovePosition(worldPos);

        if (!isAnimatingRotation || forceRotation)
        {
            Vector3 fwd = roadNetwork.EvaluateRoadForward(carState);
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f)
            {
                fwd.Normalize();
                Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
                transform.rotation = Quaternion.AngleAxis(yRotationOffset, Vector3.up) * rot;
            }
        }
    }

    void MoveOnRoad(float frameDeltaCm_y)
    {
        float speedMul = baseSpeed / 5f;
        float dist = Mathf.Clamp(
            Mathf.Abs(frameDeltaCm_y) * cmToSpeedScale * speedMul,
            0f, maxSpeed) * Time.deltaTime;

        if (dist < 0.001f) return;

        // drag down (negative y delta) = move forward, like dog-paddle stroke
        var mode = frameDeltaCm_y < 0f
            ? RoadNetworkSplineCreator.MoveMode.Forward
            : RoadNetworkSplineCreator.MoveMode.Backward;

        int prevRoad = carState.roadNo;
        roadNetwork.MoveCarLoop(carState, dist, mode);

        if (carState.roadNo != prevRoad)
        {
            ApplyDefaultLane();
            StartRotateToCurrentRoad();
        }
    }

    // -----------------------------------------------------------------------
    // Junction & Lane
    // -----------------------------------------------------------------------

    bool IsAtJunction()
    {
        var road = roadNetwork.GetRoadData(carState.roadNo);
        if (road == null || road.length <= 0f) return false;

        float t = carState.currentPos / road.length;

        if (carState.dir == 0)
            return t >= 1f - junctionNormalizedRadius && (road.laneE?.Length ?? 0) > 0;

        return t <= junctionNormalizedRadius && (road.laneS?.Length ?? 0) > 0;
    }

    int GetNextSplineIndex()
    {
        var road = roadNetwork.GetRoadData(carState.roadNo);
        if (road == null) return -1;

        var lanes = carState.dir == 0 ? road.laneE : road.laneS;
        if (lanes == null || lanes.Length == 0) return -1;

        int idx = Mathf.Clamp(carState.currentLane, 0, lanes.Length - 1);
        var conn = lanes[idx];
        return conn.IsValid ? conn.roadNo - 1 : -1;
    }

    void ApplyDefaultLane()
    {
        var road = roadNetwork.GetRoadData(carState.roadNo);
        if (road == null) return;

        var lanes = carState.dir == 0 ? road.laneE : road.laneS;
        int count = lanes?.Length ?? 0;

        // 2 branches → left (0), 3 branches → middle (1), more → center
        if (count <= 2)
            carState.currentLane = 0;
        else if (count == 3)
            carState.currentLane = 1;
        else
            carState.currentLane = count / 2;
    }

    // -----------------------------------------------------------------------
    // Rotation
    // -----------------------------------------------------------------------

    void StartRotateToCurrentRoad()
    {
        Vector3 fwd = roadNetwork.EvaluateRoadForward(carState);
        fwd.y = 0f;
        if (fwd.sqrMagnitude <= 0.001f) return;

        fwd.Normalize();
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        targetRotation = Quaternion.AngleAxis(yRotationOffset, Vector3.up) * rot;
        isAnimatingRotation = true;
    }

    // -----------------------------------------------------------------------
    // Road Highlight (LineRenderers)
    // -----------------------------------------------------------------------

    void BuildLineRenderers()
    {
        if (splineContainer == null) return;

        int count = splineContainer.Splines.Count;
        lineRenderers = new LineRenderer[count];
        lineMaterials = new Material[count];

        Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineShader == null) lineShader = Shader.Find("Unlit/Color");
        if (lineShader == null) lineShader = Shader.Find("Sprites/Default");
        if (lineShader == null) lineShader = Shader.Find("Standard");

        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject($"RoadLine_{i}");
            go.transform.SetParent(splineContainer.transform);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            Material mat = lineShader != null
                ? new Material(lineShader)
                : new Material(lr.material);
            mat.color = idleRoadColor;
            lr.material = mat;
            lr.startColor = idleRoadColor;
            lr.endColor = idleRoadColor;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.useWorldSpace = true;
            lr.positionCount = lineResolution;

            var spline = splineContainer.Splines[i];
            for (int j = 0; j < lineResolution; j++)
            {
                float t = (float)j / (lineResolution - 1);
                float3 lp = spline.EvaluatePosition(t);
                lr.SetPosition(j, splineContainer.transform.TransformPoint((Vector3)lp));
            }

            lineRenderers[i] = lr;
            lineMaterials[i] = mat;
        }
    }

    void UpdateHighlight()
    {
        if (lineRenderers == null) return;

        int curSpline = carState.roadNo - 1;
        int nextSpline = IsAtJunction() ? GetNextSplineIndex() : -1;

        for (int i = 0; i < lineRenderers.Length; i++)
        {
            if (lineRenderers[i] == null) continue;

            Color c = i == curSpline ? currentRoadColor
                    : i == nextSpline ? nextRoadColor
                    : idleRoadColor;

            lineMaterials[i].color = c;
            lineRenderers[i].startColor = c;
            lineRenderers[i].endColor = c;
        }
    }

    // -----------------------------------------------------------------------
    // Touch Input
    // -----------------------------------------------------------------------

    void HandleTouchInput()
    {
        if (Input.touchCount == 2)
        {
            if (isTouching) ResetState();
            isTwoFingerActive = true;
            HandleTwoFingerRotation();
            return;
        }

        if (Input.touchCount != 1)
        {
            if (isTouching) ResetState();
            isTwoFingerActive = false;
            return;
        }

        if (isTwoFingerActive)
        {
            isTwoFingerActive = false;
            return;
        }

        Touch touch = Input.GetTouch(0);

        if (touch.phase == TouchPhase.Began)
            touchStartedOnUI = IsTouchOverUI(touch.fingerId);

        if (touchStartedOnUI)
        {
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                touchStartedOnUI = false;
            return;
        }

        switch (touch.phase)
        {
            case TouchPhase.Began:      OnTouchBegan(touch);    break;
            case TouchPhase.Moved:      OnTouchMoved(touch);    break;
            case TouchPhase.Stationary: OnTouchStationary();    break;
            case TouchPhase.Ended:
            case TouchPhase.Canceled:   OnTouchEnded(touch);    break;
        }
    }

    void OnTouchBegan(Touch touch)
    {
        touchStartPos = touch.position;
        touchStartTime = Time.time;
        isTouching = true;
        holdTimer = 0f;
        gestureLocked = false;
        currentGesture = GestureType.None;
        dragDistanceCm = Vector2.zero;
        totalDragDistanceCm = 0f;
    }

    void OnTouchMoved(Touch touch)
    {
        dragDistanceCm = PixelsToCm(touch.position - touchStartPos);
        totalDragDistanceCm = dragDistanceCm.magnitude;

        if (gestureLocked && currentGesture == GestureType.Hold)
        {
            if (totalDragDistanceCm > dragDeadZoneCm * 2f)
                gestureLocked = false;
            else
                return;
        }

        if (touch.deltaPosition.magnitude < holdPixelThreshold)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= holdTimeThreshold && totalDragDistanceCm < dragDeadZoneCm)
            {
                currentGesture = GestureType.Hold;
                gestureLocked = true;
                return;
            }
        }
        else
        {
            holdTimer = 0f;
        }

        if (totalDragDistanceCm < dragDeadZoneCm) return;

        currentGesture = GestureType.Drag;

        Vector2 frameCm = PixelsToCm(touch.deltaPosition);
        if (Mathf.Abs(frameCm.y) > 0.001f)
            MoveOnRoad(frameCm.y);
    }

    void OnTouchStationary()
    {
        holdTimer += Time.deltaTime;
        currentGesture = GestureType.Hold;
        gestureLocked = true;
    }

    void OnTouchEnded(Touch touch)
    {
        float elapsed = Time.time - touchStartTime;
        Vector2 finalPx = touch.position - touchStartPos;
        Vector2 finalCm = PixelsToCm(finalPx);
        float finalDist = finalCm.magnitude;
        float velocity = elapsed > 0f ? finalDist / elapsed : 0f;

        bool isSwipe = velocity >= swipeVelocityThresholdCm
            && elapsed <= swipeMaxDuration
            && finalDist >= swipeMinDistanceCm
            && Mathf.Abs(finalPx.x) >= swipeMinPixels
            && Mathf.Abs(finalPx.x) > Mathf.Abs(finalPx.y);

        if (isSwipe && IsAtJunction())
        {
            currentGesture = GestureType.Swipe;
            int swipeDir = finalPx.x > 0f ? 1 : -1;
            int prevLane = carState.currentLane;
            roadNetwork.ChangeLane(carState, swipeDir);
            if (carState.currentLane != prevLane)
                UpdateHighlight();
        }

        ResetState();
    }

    void HandleTwoFingerRotation()
    {
        if (!enableTwoFingerRotation || Input.touchCount != 2) return;

        Touch t0 = Input.GetTouch(0);
        Touch t1 = Input.GetTouch(1);
        yRotationOffset -= (t0.deltaPosition.x + t1.deltaPosition.x) * 0.5f * twoFingerRotateSpeed;
    }

    // -----------------------------------------------------------------------
    // Utilities
    // -----------------------------------------------------------------------

    void ResetState()
    {
        isTouching = false;
        holdTimer = 0f;
        gestureLocked = false;
        currentGesture = GestureType.None;
    }

    bool IsTouchOverUI(int fingerId)
    {
        return EventSystem.current != null
            && EventSystem.current.IsPointerOverGameObject(fingerId);
    }

    Vector2 PixelsToCm(Vector2 pixels)
    {
        float dpi = Screen.dpi > 0f ? Screen.dpi : fallbackDpi;
        return pixels * (2.54f / dpi);
    }

    void OnDestroy()
    {
        if (lineRenderers == null) return;
        foreach (var lr in lineRenderers)
        {
            if (lr != null) Destroy(lr.gameObject);
        }
    }

    // -----------------------------------------------------------------------
    // Public accessors
    // -----------------------------------------------------------------------

    public int GetCurrentRoadNo() => carState.roadNo;
    public float GetCurrentPos() => carState.currentPos;
    public int GetCurrentLane() => carState.currentLane;
    public bool IsAtJunctionPublic() => IsAtJunction();
    public string GetGestureState() => currentGesture.ToString();
}
