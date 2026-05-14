using System.Collections.Generic;
using RawInput.Touchpad;
using TrackpadDll;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class dragngo : MonoBehaviour
{
    private const float Scale = 1.625f;
    private const float CalibratedRawDistance = 1784f;
    private const float CalibratedCmDistance = 6f;
    private const float CmPerRaw = CalibratedCmDistance / CalibratedRawDistance;
    private const float ContactTimeoutSeconds = 0.08f;
    private const string DefaultNavPointLayerName = "NavPoint";
    private const string DefaultRoadLayerName = "Road";

    [FormerlySerializedAs("cubeObject")]
    [SerializeField] private GameObject Player;
    [FormerlySerializedAs("rayOrigin")]
    [SerializeField] private Transform fireOrigin;
    [SerializeField] private Transform worldRotateTarget;
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private GameObject targetPrefab;
    [SerializeField] private Button settingsButton;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Toggle constrainToRoadToggle;
    [SerializeField] private LayerMask navPointLayerMask;
    [SerializeField] private float maxRaycastDistance = 100f;
    [SerializeField] private float playerGroundOffset = 0f;
    [SerializeField] private float oneFingerDeadZoneRaw = 8f;
    [SerializeField] private float twoFingerDeadZoneRaw = 8f;
    [SerializeField] private float twoFingerRotateDegrees = 90f;
    [SerializeField] private float twoFingerSmoothingSeconds = 0.06f;
    [SerializeField] private Color aimingColor = Color.white;
    [SerializeField] private Color readyColor = Color.red;
    [SerializeField] private float laserWidth = 0.025f;
    [SerializeField] private bool constrainToRoad = true;
    [SerializeField] private LayerMask roadLayerMask;

    private readonly Dictionary<int, TouchSession> sessions = new Dictionary<int, TouchSession>();
    private GameObject targetInstance;
    private Vector3 playerStartPosition;
    private Vector3 movementStartPosition;
    private Vector3 currentTargetPosition;
    private Vector3 lastValidRoadPosition;
    private bool hasTarget;
    private bool isDraggingToTarget;
    private bool isTwoFingerMode;
    private bool hasTwoFingerCenter;
    private bool hasPassedTwoFingerDeadZone;
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
            $"dragngo Scale={Scale}, RawDistance={CalibratedRawDistance}, " +
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
        UpdateAim();
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

        playerStartPosition = Player.transform.position;
        movementStartPosition = playerStartPosition;

        if (fireOrigin == null && Camera.main != null)
        {
            fireOrigin = Camera.main.transform;
        }

        if (fireOrigin == null)
        {
            fireOrigin = transform;
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

        ResolveNavPointLayerMask();
        EnsureTargetInstance();
        SetupSettingsUi();
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
            else if (hasTarget)
            {
                BeginDragToTarget();
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

        DragToTarget(contactId, session);
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

    private void BeginDragToTarget()
    {
        if (Player == null || !hasTarget)
        {
            isDraggingToTarget = false;
            return;
        }

        isDraggingToTarget = true;
        movementStartPosition = Player.transform.position;
        lastValidRoadPosition = movementStartPosition;
        SetLaserColor(readyColor);
    }

    private void DragToTarget(int contactId, TouchSession session)
    {
        if (!isDraggingToTarget || Player == null || !hasTarget)
        {
            return;
        }

        float totalRawDistance = session.LastY - session.StartY;
        if (Mathf.Abs(totalRawDistance) <= oneFingerDeadZoneRaw)
        {
            Player.transform.position = movementStartPosition;
            return;
        }

        float targetDistance = Vector3.Distance(movementStartPosition, currentTargetPosition);
        if (Mathf.Approximately(targetDistance, 0f))
        {
            Player.transform.position = currentTargetPosition;
            return;
        }

        float totalCmDistance = totalRawDistance * CmPerRaw;
        float availableRaw = totalRawDistance >= 0f
            ? Mathf.Max(CalibratedRawDistance - session.StartY, 1f)
            : Mathf.Max(session.StartY, 1f);
        float availableCm = availableRaw * CmPerRaw;
        float progress = Mathf.Clamp01(totalCmDistance / availableCm);
        Vector3 newPosition = Vector3.Lerp(movementStartPosition, currentTargetPosition, progress);

        if (constrainToRoad)
        {
            if (IsOnRoad(newPosition))
                lastValidRoadPosition = newPosition;
            else
                newPosition = lastValidRoadPosition;
        }

        Player.transform.position = newPosition;

        Debug.Log(
            $"dragngo move id={contactId}, totalRawDistance={totalRawDistance}, " +
            $"totalCmDistance={totalCmDistance}, " +
            $"progress={progress}, Player={newPosition}, target={currentTargetPosition}"
        );
    }

    private void UpdateAim()
    {
        if (lineRenderer == null)
        {
            return;
        }

        if (fireOrigin == null)
        {
            fireOrigin = transform;
        }

        if (isDraggingToTarget)
        {
            lineRenderer.SetPosition(0, fireOrigin.position);
            lineRenderer.SetPosition(1, currentTargetPosition);
            SetLaserColor(readyColor);
            UpdateTargetPreview(true);
            return;
        }

        ResolveNavPointLayerMask();

        Vector3 origin = fireOrigin.position;
        Vector3 direction = fireOrigin.forward;
        Vector3 laserEnd = origin + direction * maxRaycastDistance;
        bool hitNavPoint = false;

        if (navPointLayerMask.value != 0 &&
            Physics.Raycast(origin, direction, out RaycastHit hit, maxRaycastDistance, navPointLayerMask, QueryTriggerInteraction.Collide))
        {
            laserEnd = hit.point;
            currentTargetPosition = hit.point + Vector3.up * playerGroundOffset;
            hitNavPoint = true;
        }

        hasTarget = hitNavPoint;
        lineRenderer.SetPosition(0, origin);
        lineRenderer.SetPosition(1, laserEnd);
        SetLaserColor(hitNavPoint ? readyColor : aimingColor);
        UpdateTargetPreview(hitNavPoint);
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

    private bool IsOnRoad(Vector3 position)
    {
        ResolveRoadLayerMask();
        if (roadLayerMask.value == 0) return true;
        return Physics.Raycast(position + Vector3.up * 5f, Vector3.down, 10f, roadLayerMask);
    }

    private void ResolveRoadLayerMask()
    {
        if (roadLayerMask.value != 0) return;
        int layer = LayerMask.NameToLayer(DefaultRoadLayerName);
        if (layer >= 0)
            roadLayerMask = 1 << layer;
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
        hasPassedTwoFingerDeadZone = false;
        twoFingerGestureRawDelta = 0f;
        pendingRotationDegrees = 0f;
        ResetSessionStarts();
        SetLaserColor(aimingColor);
        Debug.Log("dragngo enter two finger rotate mode.");
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
            $"dragngo two finger rotate rotationDegrees={rotationDegrees}, " +
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
        hasPassedTwoFingerDeadZone = false;
        twoFingerGestureRawDelta = 0f;
        pendingRotationDegrees = 0f;
        ResetSessionStarts();
        Debug.Log("dragngo exit two finger rotate mode.");
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

    public void SetupSettingsUi()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.AddListener(ToggleSettingsPanel);
        }

        if (constrainToRoadToggle != null)
        {
            constrainToRoadToggle.isOn = constrainToRoad;
            constrainToRoadToggle.onValueChanged.AddListener(SetConstrainToRoad);
        }
    }

    private void ToggleSettingsPanel()
    {
        if (settingsPanel == null)
        {
            return;
        }

        settingsPanel.SetActive(!settingsPanel.activeSelf);
    }

    private void SetConstrainToRoad(bool value)
    {
        constrainToRoad = value;
    }
}
