using System.Collections.Generic;
using RawInput.Touchpad;
using TrackpadDll;
using UnityEngine;

public class dragngo_improved : MonoBehaviour
{
    private const float Scale = 1.625f;
    private const float CalibratedRawDistance = 1784f;
    private const float CalibratedCmDistance = 6f;
    private const float CmPerRaw = CalibratedCmDistance / CalibratedRawDistance;
    private const float ContactTimeoutSeconds = 0.08f;
    private const string DefaultNavPointLayerName = "NavPoint";
    private const string DefaultRoadLayerName = "Road";

    [SerializeField] private GameObject Player;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private Transform worldRotateTarget;
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private GameObject targetPrefab;
    [SerializeField] private LayerMask navPointLayerMask;
    [SerializeField] private LayerMask roadLayerMask;
    [SerializeField] private float roadRaycastHeight = 5f;
    [SerializeField] private float roadRaycastDistance = 20f;
    [SerializeField] private float playerGroundOffset = 0f;
    [SerializeField] private bool snapPlayerHeightToRoad = false;
    [SerializeField, Range(0f, 1f)] private float minRoadSurfaceNormalY = 0.75f;
    [SerializeField] private float oneFingerDeadZoneRaw = 8f;
    [SerializeField] private float twoFingerDeadZoneRaw = 8f;
    [SerializeField] private float twoFingerRotateDegrees = 90f;
    [SerializeField] private float twoFingerSmoothingSeconds = 0.06f;
    [SerializeField] private float maxAimLineDistance = 100f;
    [SerializeField] private float laserWidth = 0.025f;
    [SerializeField] private Color aimingColor = Color.white;
    [SerializeField] private Color readyColor = Color.red;

    private readonly Dictionary<int, TouchSession> sessions = new Dictionary<int, TouchSession>();
    private GameObject targetInstance;
    private Vector3 movementStartPosition;
    private Vector3 currentTargetPosition;
    private bool hasTarget;
    private bool isDraggingToTarget;
    private bool isTwoFingerMode;
    private bool hasTwoFingerCenter;
    private bool hasLastOneFingerPosition;
    private bool hasPassedTwoFingerDeadZone;
    private Vector2 lastOneFingerPosition;
    private float lastTwoFingerCenterX;
    private float twoFingerGestureRawDelta;
    private float pendingRotationDegrees;

    private struct TouchSession
    {
        public float StartX;
        public float StartY;
        public float LastX;
        public float LastY;
        public float LastSeenTime;
    }

    private void Awake()
    {
        EnsureSceneObjects();
    }

    private void Start()
    {
        TrackpadInterface.Start();
        Debug.Log(
            $"dragngo_improved Scale={Scale}, RawDistance={CalibratedRawDistance}, " +
            $"CmDistance={CalibratedCmDistance}, CmPerRaw={CmPerRaw}"
        );
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            StopTrackpad();
            SceneEscape.Handle();
            return;
        }

        CleanupExpiredSessions();

        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
            HandleContact(contact);
        }

        CleanupExpiredSessions();
        UpdateTwoFingerRotationInput();
        ApplyPendingTwoFingerRotation();
        UpdateAimVisual();
    }

    private void EnsureSceneObjects()
    {
        if (Player == null)
        {
            Player = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Player.name = "Player";
            Player.transform.position = new Vector3(0f, 0.5f, 0f);
            Player.transform.localScale = Vector3.one;
        }

        movementStartPosition = Player.transform.position;

        if (rayOrigin == null && Camera.main != null)
        {
            rayOrigin = Camera.main.transform;
        }

        if (worldRotateTarget == null)
        {
            worldRotateTarget = rayOrigin != null ? rayOrigin : transform;
        }

        if (lineRenderer == null)
        {
            lineRenderer = gameObject.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
            }
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = laserWidth;
        lineRenderer.endWidth = laserWidth;
        lineRenderer.material = lineRenderer.material != null
            ? lineRenderer.material
            : new Material(Shader.Find("Sprites/Default"));
        SetLaserColor(aimingColor);

        ResolveNavPointLayerMask();
        ResolveRoadLayerMask();
        EnsureTargetInstance();
    }

    private void HandleContact(TouchpadContact contact)
    {
        int contactId = contact.ContactId;
        float now = Time.time;

        if (!sessions.TryGetValue(contactId, out TouchSession session) || now - session.LastSeenTime >= ContactTimeoutSeconds)
        {
            sessions[contactId] = CreateSession(contact, now);

            if (sessions.Count >= 2)
            {
                EnterTwoFingerMode();
            }
            else
            {
                BeginOneFingerTargeting(contact);
            }

            return;
        }

        session.LastX = contact.X;
        session.LastY = contact.Y;
        session.LastSeenTime = now;
        sessions[contactId] = session;

        if (sessions.Count >= 2)
        {
            if (!isTwoFingerMode)
            {
                EnterTwoFingerMode();
                return;
            }

            return;
        }

        if (isTwoFingerMode)
        {
            ExitTwoFingerMode();
            return;
        }

        UpdateOneFingerTarget(contactId, session);
    }

    private TouchSession CreateSession(TouchpadContact contact, float now)
    {
        return new TouchSession
        {
            StartX = contact.X,
            StartY = contact.Y,
            LastX = contact.X,
            LastY = contact.Y,
            LastSeenTime = now
        };
    }

    private void BeginOneFingerTargeting(TouchpadContact contact)
    {
        if (Player == null || !hasTarget)
        {
            isDraggingToTarget = false;
            return;
        }

        isDraggingToTarget = true;
        movementStartPosition = Player.transform.position;
        hasLastOneFingerPosition = true;
        lastOneFingerPosition = new Vector2(contact.X, contact.Y);
        SetLaserColor(readyColor);
    }

    private void UpdateOneFingerTarget(int contactId, TouchSession session)
    {
        if (!isDraggingToTarget || Player == null || !hasTarget)
        {
            return;
        }

        Vector2 currentPosition = new Vector2(session.LastX, session.LastY);
        if (!hasLastOneFingerPosition)
        {
            lastOneFingerPosition = currentPosition;
            hasLastOneFingerPosition = true;
            return;
        }

        Vector2 dragDelta = currentPosition - lastOneFingerPosition;
        lastOneFingerPosition = currentPosition;

        float totalRawDistance = session.LastY - session.StartY;

        if (Mathf.Abs(totalRawDistance) <= oneFingerDeadZoneRaw)
        {
            Player.transform.position = movementStartPosition;
            return;
        }

        Vector3 targetDirection = currentTargetPosition - movementStartPosition;
        float targetDistance = targetDirection.magnitude;
        if (Mathf.Approximately(targetDistance, 0f))
        {
            Player.transform.position = currentTargetPosition;
            return;
        }

        float totalCmDistance = totalRawDistance * CmPerRaw;
        float scaledWorldDistance = totalCmDistance * Scale;
        float moveDistance = Mathf.Clamp(scaledWorldDistance, 0f, targetDistance);
        Vector3 candidatePosition = movementStartPosition + targetDirection.normalized * moveDistance;

        if (!TryProjectToRoad(candidatePosition, out Vector3 roadPosition))
        {
            Debug.Log(
                $"dragngo_improved target blocked id={contactId}, dragDelta={dragDelta}, " +
                $"candidate={candidatePosition}, roadLayerMask={roadLayerMask.value}"
            );
            return;
        }

        Vector3 previousPosition = Player.transform.position;
        Player.transform.position = roadPosition;

        Debug.Log(
            $"dragngo_improved target id={contactId}, current={currentPosition}, dragDelta={dragDelta}, " +
            $"totalRaw={totalRawDistance}, totalCm={totalCmDistance}, scaledWorld={scaledWorldDistance}, " +
            $"moveDistance={moveDistance}, Player from {previousPosition} to {roadPosition}, target={currentTargetPosition}"
        );
    }

    private bool TryProjectToRoad(Vector3 targetPosition, out Vector3 roadPosition)
    {
        roadPosition = targetPosition;
        ResolveRoadLayerMask();

        if (roadLayerMask.value == 0)
        {
            return true;
        }

        Vector3 rayOrigin = targetPosition + Vector3.up * roadRaycastHeight;
        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, roadRaycastDistance, roadLayerMask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (hit.normal.y < minRoadSurfaceNormalY)
        {
            return false;
        }

        if (snapPlayerHeightToRoad)
        {
            roadPosition = hit.point + Vector3.up * playerGroundOffset;
        }

        return true;
    }

    private void UpdateAimVisual()
    {
        if (lineRenderer == null)
        {
            return;
        }

        if (rayOrigin == null)
        {
            lineRenderer.enabled = hasTarget;
            UpdateTargetPreview(hasTarget);
            return;
        }

        lineRenderer.enabled = true;
        lineRenderer.SetPosition(0, rayOrigin.position);

        if (isDraggingToTarget)
        {
            lineRenderer.SetPosition(1, currentTargetPosition);
            SetLaserColor(readyColor);
            UpdateTargetPreview(true);
            return;
        }

        ResolveNavPointLayerMask();

        Vector3 origin = rayOrigin.position;
        Vector3 direction = rayOrigin.forward;
        Vector3 laserEnd = origin + direction * maxAimLineDistance;
        bool hitNavPoint = false;

        if (navPointLayerMask.value != 0 &&
            Physics.Raycast(origin, direction, out RaycastHit hit, maxAimLineDistance, navPointLayerMask, QueryTriggerInteraction.Collide))
        {
            laserEnd = hit.point;
            currentTargetPosition = hit.point + Vector3.up * playerGroundOffset;
            hitNavPoint = true;
        }

        hasTarget = hitNavPoint;

        if (hasTarget)
        {
            lineRenderer.SetPosition(1, laserEnd);
            SetLaserColor(readyColor);
            UpdateTargetPreview(true);
            return;
        }

        lineRenderer.SetPosition(1, laserEnd);
        SetLaserColor(aimingColor);
        UpdateTargetPreview(false);
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
    }

    private void ResolveRoadLayerMask()
    {
        if (roadLayerMask.value != 0)
        {
            return;
        }

        int roadLayer = LayerMask.NameToLayer(DefaultRoadLayerName);
        if (roadLayer >= 0)
        {
            roadLayerMask = 1 << roadLayer;
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

    private bool DeathZone(float rawDistance, float zoneRaw)
    {
        return Mathf.Abs(rawDistance) <= zoneRaw;
    }

    private void EnterTwoFingerMode()
    {
        isTwoFingerMode = true;
        isDraggingToTarget = false;
        hasTwoFingerCenter = false;
        hasLastOneFingerPosition = false;
        hasPassedTwoFingerDeadZone = false;
        twoFingerGestureRawDelta = 0f;
        pendingRotationDegrees = 0f;
        hasTarget = false;
        ResetSessionStarts();
        SetLaserColor(aimingColor);
        Debug.Log("dragngo_improved enter two finger rotate mode.");
    }

    private void UpdateTwoFingerRotationInput()
    {
        if (!isTwoFingerMode || sessions.Count < 2)
        {
            return;
        }

        float centerX = GetAverageActiveX();

        if (!hasTwoFingerCenter)
        {
            lastTwoFingerCenterX = centerX;
            hasTwoFingerCenter = true;
            return;
        }

        float deltaX = centerX - lastTwoFingerCenterX;
        lastTwoFingerCenterX = centerX;

        if (Mathf.Approximately(deltaX, 0f))
        {
            return;
        }

        if (!hasPassedTwoFingerDeadZone)
        {
            twoFingerGestureRawDelta += deltaX;
            if (DeathZone(twoFingerGestureRawDelta, twoFingerDeadZoneRaw))
            {
                return;
            }

            float direction = Mathf.Sign(twoFingerGestureRawDelta);
            deltaX = twoFingerGestureRawDelta - direction * twoFingerDeadZoneRaw;
            hasPassedTwoFingerDeadZone = true;
        }

        float degreesPerRaw = twoFingerRotateDegrees / CalibratedRawDistance;
        pendingRotationDegrees += -deltaX * degreesPerRaw;
    }

    private void ApplyPendingTwoFingerRotation()
    {
        if (Mathf.Approximately(pendingRotationDegrees, 0f))
        {
            return;
        }

        float rotationDegrees = pendingRotationDegrees;
        if (twoFingerSmoothingSeconds > 0f)
        {
            float smoothingFactor = 1f - Mathf.Exp(-Time.deltaTime / twoFingerSmoothingSeconds);
            rotationDegrees *= smoothingFactor;

            if (Mathf.Abs(rotationDegrees) < 0.001f && Mathf.Abs(pendingRotationDegrees) < 0.01f)
            {
                rotationDegrees = pendingRotationDegrees;
            }
        }

        pendingRotationDegrees -= rotationDegrees;

        Transform target = worldRotateTarget != null ? worldRotateTarget : transform;

        target.Rotate(Vector3.up, rotationDegrees, Space.World);
        if (Player != null && Player.transform != target && !Player.transform.IsChildOf(target))
        {
            Player.transform.Rotate(Vector3.up, rotationDegrees, Space.World);
        }

        Debug.Log(
            $"dragngo_improved two finger rotate rotationDegrees={rotationDegrees}, " +
            $"pendingRotationDegrees={pendingRotationDegrees}, target={target.name}"
        );
    }

    private float GetAverageActiveX()
    {
        float totalX = 0f;
        int count = 0;

        foreach (TouchSession session in sessions.Values)
        {
            totalX += session.LastX;
            count++;
        }

        return count > 0 ? totalX / count : 0f;
    }

    private void CleanupExpiredSessions()
    {
        float now = Time.time;
        List<int> expiredIds = null;

        foreach (KeyValuePair<int, TouchSession> pair in sessions)
        {
            if (now - pair.Value.LastSeenTime < ContactTimeoutSeconds)
            {
                continue;
            }

            if (expiredIds == null)
            {
                expiredIds = new List<int>();
            }

            expiredIds.Add(pair.Key);
        }

        if (expiredIds == null)
        {
            return;
        }

        foreach (int id in expiredIds)
        {
            sessions.Remove(id);
        }

        if (sessions.Count == 0)
        {
            isDraggingToTarget = false;
            hasLastOneFingerPosition = false;
            SetLaserColor(aimingColor);
        }

        if (isTwoFingerMode && sessions.Count < 2)
        {
            ExitTwoFingerMode();
        }
    }

    private void ExitTwoFingerMode()
    {
        isTwoFingerMode = false;
        hasTwoFingerCenter = false;
        hasLastOneFingerPosition = false;
        hasPassedTwoFingerDeadZone = false;
        twoFingerGestureRawDelta = 0f;
        pendingRotationDegrees = 0f;
        ResetSessionStarts();
        Debug.Log("dragngo_improved exit two finger rotate mode.");
    }

    private void ResetSessionStarts()
    {
        foreach (int id in new List<int>(sessions.Keys))
        {
            TouchSession session = sessions[id];
            session.StartX = session.LastX;
            session.StartY = session.LastY;
            sessions[id] = session;
        }
    }

    private void OnDisable()
    {
        StopTrackpad();
    }

    private void OnApplicationQuit()
    {
        StopTrackpad();
    }

    private void StopTrackpad()
    {
        TrackpadInterface.Stop();
    }

    private void QuitApplication()
    {
        StopTrackpad();
        SceneEscape.Handle();
    }
}
