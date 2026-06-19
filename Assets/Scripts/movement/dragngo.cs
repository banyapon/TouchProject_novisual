using UnityEngine;
using UnityEngine.Serialization;

public class dragngo : MonoBehaviour
{
    private const float ScaleResearch = 65f / 40f;
    private const float TwoFingerRotateDegrees = 90f;
    private const float RawVerticalDistance = 912f;
    private const float RawHorizontalDistance = 1452f;
    private const float DragDeadZoneRaw = 2f;
    private const float TouchPadHorizontalCmDistance = 12.5f;
    private const float TouchPadVerticalCmDistance = 8f;
    private const float HorizontalCmPerRaw = TouchPadHorizontalCmDistance / RawHorizontalDistance;
    private const float VerticalCmPerRaw = TouchPadVerticalCmDistance / RawVerticalDistance;
    private const string DefaultNavPointLayerName = "NavPoint";

    [SerializeField] private TouchpadManager touchManager;
    [SerializeField] private GameObject Player;
    [FormerlySerializedAs("fireOrigin")]
    [SerializeField] private Transform RaycastOrigin;
    [SerializeField] private Transform worldRotateTarget;
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private GameObject targetPrefab;
    [SerializeField] private LayerMask navPointLayerMask;
    [SerializeField] private float maxRaycastDistance = 100f;
    [SerializeField] private float playerGroundOffset = 0f;
    [SerializeField] private Color aimingColor = Color.white;
    [SerializeField] private Color readyColor = Color.red;
    [SerializeField] private float laserWidth = 0.025f;
    [SerializeField] private float dragForwardRawYDirection = 1f;

    private GameObject targetInstance;
    private Vector2 dragStartRawPosition;
    private bool hasLastRawPosition;
    private Vector2 lastTwoFingerRawPosition;
    private bool hasLastTwoFingerRawPosition;
    private bool hasTarget;
    private bool isDraggingToTarget;
    private bool suppressNextSingleDragFrame;
    private Vector3 movementStartPosition;
    private Vector3 currentTargetPosition;

    private void Awake()
    {
        EnsureSceneObjects();
    }

    private void Update()
    {
        if (touchManager == null)
        {
            touchManager = TouchpadManager.Instance;
        }

        if (touchManager == null || Player == null)
        {
            UpdateAim();
            return;
        }

        UpdateTouchInput();
        UpdateAim();
    }

    private void EnsureSceneObjects()
    {
        if (touchManager == null)
        {
            touchManager = TouchpadManager.Instance;
        }

        if (Player == null)
        {
            Player = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Player.name = "Player";
            Player.transform.position = new Vector3(0f, 0.5f, 0f);
        }

        if (RaycastOrigin == null && Camera.main != null)
        {
            RaycastOrigin = Camera.main.transform;
        }

        if (RaycastOrigin == null)
        {
            RaycastOrigin = transform;
        }

        if (worldRotateTarget == null)
        {
            worldRotateTarget = RaycastOrigin;
        }

        EnsureLineRenderer();

        ResolveNavPointLayerMask();
        EnsureTargetInstance();
        SetLaserColor(aimingColor);
    }

    private void UpdateTouchInput()
    {
        TouchpadManager.TouchMode mode = touchManager.CurrentMode;
        TouchpadManager.TouchStatus status = touchManager.Status;

        if (mode == TouchpadManager.TouchMode.None)
        {
            ResetTouchState();
            return;
        }

        if (status == TouchpadManager.TouchStatus.OnTouch)
        {
            suppressNextSingleDragFrame = true;
        }

        if (mode == TouchpadManager.TouchMode.Rotate)
        {
            suppressNextSingleDragFrame = true;
            RotateTwoFingerDrag();
            return;
        }

        if (mode == TouchpadManager.TouchMode.Change)
        {
            suppressNextSingleDragFrame = true;
            ResetTouchState();
            return;
        }

        if (mode == TouchpadManager.TouchMode.Translate)
        {
            OneFingerDrag();
        }
    }

    private void OneFingerDrag()
    {
        Vector2 currentRawPosition = touchManager.PrimaryRawPosition;

        if (suppressNextSingleDragFrame)
        {
            dragStartRawPosition = currentRawPosition;
            hasLastRawPosition = true;
            isDraggingToTarget = hasTarget;
            movementStartPosition = Player.transform.position;
            suppressNextSingleDragFrame = false;
            return;
        }

        if (!hasLastRawPosition)
        {
            dragStartRawPosition = currentRawPosition;
            hasLastRawPosition = true;
            isDraggingToTarget = hasTarget;
            movementStartPosition = Player.transform.position;
            return;
        }

        if (!isDraggingToTarget || !hasTarget)
        {
            return;
        }

        Vector3 targetOffset = currentTargetPosition - movementStartPosition;
        float targetDistance = targetOffset.magnitude;
        if (Mathf.Approximately(targetDistance, 0f))
        {
            Player.transform.position = currentTargetPosition;
            return;
        }

        float forwardDirection = dragForwardRawYDirection >= 0f ? 1f : -1f;
        float totalDragRawY = (currentRawPosition.y - dragStartRawPosition.y) * forwardDirection;
        if (Mathf.Abs(totalDragRawY) < DragDeadZoneRaw)
        {
            Player.transform.position = movementStartPosition;
            return;
        }

        float availableRawY = forwardDirection > 0f
            ? Mathf.Max(RawVerticalDistance - dragStartRawPosition.y, 1f)
            : Mathf.Max(dragStartRawPosition.y, 1f);
        float progress = Mathf.Clamp01(totalDragRawY / availableRawY);

        Player.transform.position = Vector3.Lerp(movementStartPosition, currentTargetPosition, progress);
    }

    private void RotateTwoFingerDrag()
    {
        isDraggingToTarget = false;
        hasLastRawPosition = false;

        Vector2 currentRawPosition = touchManager.GetCurrentTouch();
        if (!hasLastTwoFingerRawPosition)
        {
            lastTwoFingerRawPosition = currentRawPosition;
            hasLastTwoFingerRawPosition = true;
            return;
        }

        float dragDeltaX = currentRawPosition.x - lastTwoFingerRawPosition.x;
        lastTwoFingerRawPosition = currentRawPosition;

        if (Mathf.Approximately(dragDeltaX, 0f))
        {
            return;
        }

        Transform rotateTarget = worldRotateTarget != null ? worldRotateTarget : Player.transform;
        float degreesPerRaw = TwoFingerRotateDegrees / RawVerticalDistance;
        float rotationDegrees = -dragDeltaX * degreesPerRaw;

        rotateTarget.Rotate(Vector3.up, rotationDegrees, Space.World);
        if (Player.transform != rotateTarget && !Player.transform.IsChildOf(rotateTarget))
        {
            Player.transform.Rotate(Vector3.up, rotationDegrees, Space.World);
        }
    }

    private void UpdateAim()
    {
        EnsureLineRenderer();
        if (lineRenderer == null)
        {
            return;
        }

        if (RaycastOrigin == null)
        {
            RaycastOrigin = transform;
        }

        ResolveNavPointLayerMask();

        Vector3 origin = RaycastOrigin.position;
        Vector3 direction = RaycastOrigin.forward;
        Vector3 raycastEnd = origin + direction * maxRaycastDistance;
        bool hitNavPoint = false;

        if (!isDraggingToTarget &&
            navPointLayerMask.value != 0 &&
            Physics.Raycast(origin, direction, out RaycastHit hit, maxRaycastDistance, navPointLayerMask, QueryTriggerInteraction.Collide))
        {
            raycastEnd = hit.point;
            currentTargetPosition = hit.point + Vector3.up * playerGroundOffset;
            hitNavPoint = true;
        }
        else if (isDraggingToTarget)
        {
            raycastEnd = currentTargetPosition;
            hitNavPoint = true;
        }

        hasTarget = hitNavPoint;
        lineRenderer.SetPosition(0, origin);
        lineRenderer.SetPosition(1, raycastEnd);
        SetLaserColor(hitNavPoint ? readyColor : aimingColor);
        UpdateTargetPreview(hitNavPoint);
    }

    private void EnsureLineRenderer()
    {
        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
            }
        }

        lineRenderer.enabled = true;
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = laserWidth;
        lineRenderer.endWidth = laserWidth;

        if (lineRenderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                lineRenderer.sharedMaterial = new Material(shader);
            }
        }
    }

    private void UpdateTargetPreview(bool showTarget)
    {
        EnsureTargetInstance();
        if (targetInstance == null)
        {
            return;
        }

        targetInstance.SetActive(showTarget);
        if (showTarget)
        {
            targetInstance.transform.position = currentTargetPosition;
        }
    }

    private void EnsureTargetInstance()
    {
        if (targetInstance != null)
        {
            return;
        }

        if (targetPrefab == null)
        {
            targetPrefab = Resources.Load<GameObject>("Target");
        }

#if UNITY_EDITOR
        if (targetPrefab == null)
        {
            targetPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Target.prefab");
        }
#endif

        targetInstance = targetPrefab != null
            ? Instantiate(targetPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        targetInstance.name = "Target";
        targetInstance.SetActive(false);
    }

    private void SetLaserColor(Color color)
    {
        if (lineRenderer == null)
        {
            return;
        }

        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        if (lineRenderer.material != null)
        {
            lineRenderer.material.color = color;
            lineRenderer.material.EnableKeyword("_EMISSION");
            lineRenderer.material.SetColor("_EmissionColor", color);
        }
    }

    private void ResolveNavPointLayerMask()
    {
        if (navPointLayerMask.value != 0)
        {
            return;
        }

        int navPointLayer = LayerMask.NameToLayer(DefaultNavPointLayerName);
        if (navPointLayer >= 0)
        {
            navPointLayerMask = 1 << navPointLayer;
        }
    }

    private void ResetTouchState()
    {
        hasLastRawPosition = false;
        hasLastTwoFingerRawPosition = false;
        isDraggingToTarget = false;
    }
}
