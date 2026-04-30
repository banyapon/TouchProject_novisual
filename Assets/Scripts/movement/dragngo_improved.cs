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
    private const string DefaultRoadLayerName = "Road";

    [SerializeField] private GameObject Player;
    [SerializeField] private Transform fireOrigin;
    [SerializeField] private Transform worldRotateTarget;
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private GameObject targetPrefab;
    [SerializeField] private LayerMask roadLayerMask;
    [SerializeField] private float roadRaycastHeight = 5f;
    [SerializeField] private float roadRaycastDistance = 20f;
    [SerializeField] private float playerGroundOffset = 0f;
    [SerializeField] private bool snapPlayerHeightToRoad = false;
    [SerializeField, Range(0f, 1f)] private float minRoadSurfaceNormalY = 0.75f;
    [SerializeField] private float oneFingerDeadZoneRaw = 8f;
    [SerializeField] private float twoFingerDeadZoneRaw = 8f;
    [SerializeField] private float twoFingerRotateDegrees = 90f;
    [SerializeField] private float maxAimLineDistance = 100f;
    [SerializeField] private float laserWidth = 0.025f;
    [SerializeField] private Color aimingColor = Color.white;
    [SerializeField] private Color readyColor = Color.red;

    private readonly Dictionary<int, TouchSession> sessions = new Dictionary<int, TouchSession>();
    private GameObject targetInstance;
    private Vector3 movementStartPosition;
    private Vector3 currentTargetPosition;
    private bool hasTarget;
    private bool isTwoFingerMode;
    private bool hasTwoFingerCenter;
    private bool hasLastOneFingerPosition;
    private Vector2 lastOneFingerPosition;
    private float lastTwoFingerCenterX;

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

        if (fireOrigin == null && Camera.main != null)
        {
            fireOrigin = Camera.main.transform;
        }

        if (worldRotateTarget == null)
        {
            worldRotateTarget = fireOrigin != null ? fireOrigin : transform;
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

            RotateFromTwoFingerDrag(contactId);
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
        if (Player == null)
        {
            return;
        }

        movementStartPosition = Player.transform.position;
        hasLastOneFingerPosition = true;
        lastOneFingerPosition = new Vector2(contact.X, contact.Y);
    }

    private void UpdateOneFingerTarget(int contactId, TouchSession session)
    {
        if (Player == null)
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

        Vector2 totalRawDistance = new Vector2(
            session.LastX - session.StartX,
            session.LastY - session.StartY);

        if (Mathf.Abs(totalRawDistance.x) <= oneFingerDeadZoneRaw &&
            Mathf.Abs(totalRawDistance.y) <= oneFingerDeadZoneRaw)
        {
            Player.transform.position = movementStartPosition;
            currentTargetPosition = movementStartPosition;
            hasTarget = false;
            return;
        }

        Vector2 totalCmDistance = totalRawDistance * CmPerRaw;
        Vector2 scaledWorldDistance = totalCmDistance * Scale;
        Vector3 candidatePosition =
            movementStartPosition +
            Player.transform.right * scaledWorldDistance.x +
            Player.transform.forward * scaledWorldDistance.y;

        if (!TryProjectToRoad(candidatePosition, out Vector3 roadPosition))
        {
            hasTarget = false;
            Debug.Log(
                $"dragngo_improved target blocked id={contactId}, dragDelta={dragDelta}, " +
                $"candidate={candidatePosition}, roadLayerMask={roadLayerMask.value}"
            );
            return;
        }

        Vector3 previousPosition = Player.transform.position;
        Player.transform.position = roadPosition;
        currentTargetPosition = roadPosition;
        hasTarget = true;

        Debug.Log(
            $"dragngo_improved target id={contactId}, current={currentPosition}, dragDelta={dragDelta}, " +
            $"totalRaw={totalRawDistance}, totalCm={totalCmDistance}, scaledWorld={scaledWorldDistance}, " +
            $"Player from {previousPosition} to {roadPosition}"
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

        if (fireOrigin == null)
        {
            lineRenderer.enabled = hasTarget;
            UpdateTargetPreview(hasTarget);
            return;
        }

        lineRenderer.enabled = true;
        lineRenderer.SetPosition(0, fireOrigin.position);

        if (hasTarget)
        {
            lineRenderer.SetPosition(1, currentTargetPosition);
            SetLaserColor(readyColor);
            UpdateTargetPreview(true);
            return;
        }

        lineRenderer.SetPosition(1, fireOrigin.position + fireOrigin.forward * maxAimLineDistance);
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

    private bool DeathZone(float rawDistance, float zoneRaw)
    {
        return Mathf.Abs(rawDistance) <= zoneRaw;
    }

    private void EnterTwoFingerMode()
    {
        isTwoFingerMode = true;
        hasTwoFingerCenter = false;
        hasLastOneFingerPosition = false;
        hasTarget = false;
        ResetSessionStarts();
        SetLaserColor(aimingColor);
        Debug.Log("dragngo_improved enter two finger rotate mode.");
    }

    private void RotateFromTwoFingerDrag(int contactId)
    {
        float centerX = GetAverageActiveX();

        if (!hasTwoFingerCenter)
        {
            lastTwoFingerCenterX = centerX;
            hasTwoFingerCenter = true;
            return;
        }

        float deltaX = centerX - lastTwoFingerCenterX;
        lastTwoFingerCenterX = centerX;

        if (DeathZone(deltaX, twoFingerDeadZoneRaw))
        {
            return;
        }

        Transform target = worldRotateTarget != null ? worldRotateTarget : transform;
        float degreesPerRaw = twoFingerRotateDegrees / CalibratedRawDistance;
        float rotationDegrees = -deltaX * degreesPerRaw;

        target.Rotate(Vector3.up, rotationDegrees, Space.World);
        if (Player != null && Player.transform != target && !Player.transform.IsChildOf(target))
        {
            Player.transform.Rotate(Vector3.up, rotationDegrees, Space.World);
        }

        Debug.Log(
            $"dragngo_improved two finger rotate id={contactId}, centerX={centerX}, " +
            $"deltaX={deltaX}, rotationDegrees={rotationDegrees}, target={target.name}"
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
            hasLastOneFingerPosition = false;
            hasTarget = false;
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
