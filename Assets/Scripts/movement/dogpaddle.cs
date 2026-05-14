using System.Collections.Generic;
using RawInput.Touchpad;
using TrackpadDll;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class dogpaddle : MonoBehaviour
{
    private const float Scale = 1.625f;
    private const float CalibratedRawDistance = 1784f;
    private const float CalibratedCmDistance = 6f;
    private const float CalibratedHorizontalRawDistance = 4095f;
    private const float CalibratedHorizontalCmDistance = 11f;
    private const float VrFullDistanceMeters = 65f;
    private const float CmPerRaw = CalibratedCmDistance / CalibratedRawDistance;
    private const float HorizontalCmPerRaw = CalibratedHorizontalCmDistance / CalibratedHorizontalRawDistance;
    private const float ContactTimeoutSeconds = 0.08f;
    private const string DefaultRoadLayerName = "Road";

    [SerializeField] private GameObject planeObject;
    [FormerlySerializedAs("cubeObject")]
    [SerializeField] private GameObject Player;
    [SerializeField] private Transform worldRotateTarget;
    [SerializeField] private Button settingsButton;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Slider speedSlider;
    [SerializeField] private Toggle horizontalToggle;
    [SerializeField] private float speed = 5f;
    [SerializeField] private bool isHorizontal = true;
    [SerializeField] private float oneFingerDeadZoneRaw = 8f;
    [SerializeField] private float twoFingerDeadZoneRaw = 8f;
    [SerializeField] private float twoFingerRotateDegrees = 90f;
    [SerializeField] private LayerMask roadLayerMask;
    [SerializeField] private float roadRaycastHeight = 5f;
    [SerializeField] private float roadRaycastDistance = 20f;
    [SerializeField] private float playerGroundOffset = 0.5f;
    [SerializeField] private bool snapPlayerHeightToRoad = false;
    [SerializeField, Range(0f, 1f)] private float minRoadSurfaceNormalY = 0.75f;

    private readonly Dictionary<int, TouchSession> sessions = new Dictionary<int, TouchSession>();
    private Vector3 playerStartPosition;
    private bool isTwoFingerMode;
    private bool hasTwoFingerCenter;
    private float lastTwoFingerCenterX;

    private struct TouchSession
    {
        public float StartX;
        public float StartY;
        public float LastX;
        public float LastY;
        public float LastSeenTime;
        public Vector2 AppliedWorldDistance;
    }

    private void Awake()
    {
        EnsureSceneObjects();
    }

    private void Start()
    {
        TrackpadInterface.Start();
        Debug.Log(
            $"Scale={Scale}, CalibratedRawDistance={CalibratedRawDistance}, " +
            $"CalibratedCmDistance={CalibratedCmDistance}, CmPerRaw={CmPerRaw}, " +
            $"VrFullDistanceMeters={VrFullDistanceMeters}"
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
    }

    private void EnsureSceneObjects()
    {
        if (planeObject == null)
        {
            planeObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
            planeObject.name = "TouchpadPlane";
            planeObject.transform.position = Vector3.zero;
            planeObject.transform.localScale = new Vector3(100f, 100f, 100f);
        }

        if (Player == null)
        {
            Player = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Player.name = "Player";
            Player.transform.position = new Vector3(0f, 0.5f, 0f);
            Player.transform.localScale = Vector3.one;
            playerStartPosition = Player.transform.position;
            Debug.Log($"Player created at start position: {playerStartPosition}");
        }
        else
        {
            playerStartPosition = Player.transform.position;
        }

        if (worldRotateTarget == null && Camera.main != null)
        {
            worldRotateTarget = Camera.main.transform;
        }

        ResolveRoadLayerMask();
        SetupSettingsUi();
    }

    private void HandleContact(TouchpadContact contact)
    {
        int contactId = contact.ContactId;
        float now = Time.time;

        if (!sessions.TryGetValue(contactId, out TouchSession session) || now - session.LastSeenTime >= ContactTimeoutSeconds)
        {
            sessions[contactId] = CreateSession(contact, now);
            Debug.Log($"Touch start id={contactId}, x={contact.X}, y={contact.Y}");

            if (sessions.Count >= 2)
            {
                EnterTwoFingerMode();
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

        MoveFromOneFingerDrag(contactId, session);
    }

    private TouchSession CreateSession(TouchpadContact contact, float now)
    {
        return new TouchSession
        {
            StartX = contact.X,
            StartY = contact.Y,
            LastX = contact.X,
            LastY = contact.Y,
            LastSeenTime = now,
            AppliedWorldDistance = Vector2.zero
        };
    }

    private bool deathZone(float rawDistance, float zoneRaw)
    {
        return Mathf.Abs(rawDistance) <= zoneRaw;
    }

    private void MoveFromOneFingerDrag(int contactId, TouchSession session)
    {
        Vector2 totalRawDistance = new Vector2(
            session.LastX - session.StartX,
            session.LastY - session.StartY);

        bool horizontalInDeadZone = deathZone(totalRawDistance.x, oneFingerDeadZoneRaw);
        bool verticalInDeadZone = deathZone(totalRawDistance.y, oneFingerDeadZoneRaw);

        bool inactiveDrag = verticalInDeadZone && (!isHorizontal || horizontalInDeadZone);
        if (inactiveDrag && session.AppliedWorldDistance == Vector2.zero)
        {
            session.AppliedWorldDistance = Vector2.zero;
            sessions[contactId] = session;
            return;
        }

        Vector2 totalCmDistance = new Vector2(
            totalRawDistance.x * HorizontalCmPerRaw,
            totalRawDistance.y * CmPerRaw);
        Vector2 valueWorldDistance = totalCmDistance * Scale * speed;
        Vector2 deltaWorldDistance = valueWorldDistance - session.AppliedWorldDistance;

        if (Mathf.Approximately(deltaWorldDistance.x, 0f) && Mathf.Approximately(deltaWorldDistance.y, 0f))
        {
            return;
        }

        if (MovePlayer(contactId, session.StartX, session.StartY, session.LastX, session.LastY, totalRawDistance, totalCmDistance, valueWorldDistance, deltaWorldDistance))
        {
            session.AppliedWorldDistance = valueWorldDistance;
            sessions[contactId] = session;
        }
    }

    private bool MovePlayer(
        int contactId,
        float startX,
        float startY,
        float lastX,
        float lastY,
        Vector2 totalRawDistance,
        Vector2 totalCmDistance,
        Vector2 valueWorldDistance,
        Vector2 deltaWorldDistance)
    {
        if (Player == null)
        {
            Debug.LogWarning($"Touch move id={contactId}, Player is missing.");
            return false;
        }

        Vector3 previousPosition = Player.transform.position;
        Vector3 right = Player.transform.right;
        Vector3 forward = Player.transform.forward;
        Vector3 movement = forward * deltaWorldDistance.y;
        if (isHorizontal)
        {
            movement += right * deltaWorldDistance.x;
        }
        Vector3 newPosition = previousPosition + movement;

        if (!TryProjectToRoad(newPosition, out Vector3 roadPosition))
        {
            Debug.Log(
                $"One finger horizontal move blocked id={contactId}, candidate={newPosition}, roadLayerMask={roadLayerMask.value}"
            );
            return false;
        }

        float distanceFromStart = Vector3.Distance(playerStartPosition, roadPosition);
        Player.transform.position = roadPosition;

        Debug.Log(
            $"One finger move id={contactId}, start=({startX},{startY}), last=({lastX},{lastY}), " +
            $"totalRawDistance={totalRawDistance}, totalCmDistance={totalCmDistance}, " +
            $"WorldDistance={valueWorldDistance}, deltaWorldDistance={deltaWorldDistance}, isHorizontal={isHorizontal}, " +
            $"right={right}, forward={forward}, " +
            $"Player from {previousPosition} to {roadPosition}, distanceFromStart={distanceFromStart}"
        );

        return true;
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
        else
        {
            roadPosition = targetPosition;
        }

        return true;
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

    private void EnterTwoFingerMode()
    {
        isTwoFingerMode = true;
        hasTwoFingerCenter = false;

        foreach (int id in new List<int>(sessions.Keys))
        {
            TouchSession session = sessions[id];
            session.StartX = session.LastX;
            session.StartY = session.LastY;
            session.AppliedWorldDistance = Vector2.zero;
            sessions[id] = session;
        }

        Debug.Log("Enter two finger rotate mode. One finger movement stopped.");
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

        if (deathZone(deltaX, twoFingerDeadZoneRaw))
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
            $"Two finger world rotate id={contactId}, centerX={centerX}, deltaX={deltaX}, " +
            $"rotationDegrees={rotationDegrees}, target={target.name}"
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

        if (isTwoFingerMode && sessions.Count < 2)
        {
            ExitTwoFingerMode();
        }
    }

    private void ExitTwoFingerMode()
    {
        isTwoFingerMode = false;
        hasTwoFingerCenter = false;

        foreach (int id in new List<int>(sessions.Keys))
        {
            TouchSession session = sessions[id];
            session.StartX = session.LastX;
            session.StartY = session.LastY;
            session.AppliedWorldDistance = Vector2.zero;
            sessions[id] = session;
        }

        Debug.Log("Exit two finger rotate mode.");
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
        AutoFindSettingsUi();

        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveListener(ToggleSettingsPanel);
            settingsButton.onClick.AddListener(ToggleSettingsPanel);
        }

        if (speedSlider != null)
        {
            if (speedSlider.maxValue < speed)
            {
                speedSlider.maxValue = speed;
            }

            speedSlider.value = speed;
            speedSlider.onValueChanged.AddListener(SetSpeed);
        }

        if (horizontalToggle != null)
        {
            horizontalToggle.isOn = isHorizontal;
            horizontalToggle.onValueChanged.AddListener(SetHorizontal);
        }
    }

    public void ToggleSettingsPanel()
    {
        if (settingsPanel == null)
        {
            AutoFindSettingsUi();
        }

        if (settingsPanel == null)
        {
            Debug.LogWarning("Settings panel is missing.", this);
            return;
        }

        settingsPanel.SetActive(!settingsPanel.activeSelf);
    }

    private void SetSpeed(float value)
    {
        speed = value;
    }

    private void SetHorizontal(bool value)
    {
        isHorizontal = value;
        foreach (int id in new List<int>(sessions.Keys))
        {
            TouchSession session = sessions[id];
            session.StartX = session.LastX;
            session.StartY = session.LastY;
            session.AppliedWorldDistance = Vector2.zero;
            sessions[id] = session;
        }
    }

    private void AutoFindSettingsUi()
    {
        if (settingsButton == null)
        {
            settingsButton = FindUiByName<Button>("setting");
        }

        if (settingsPanel == null)
        {
            Transform panelTransform = FindTransformByName("Panel");
            settingsPanel = panelTransform != null ? panelTransform.gameObject : null;
        }

        if (speedSlider == null)
        {
            speedSlider = FindUiByName<Slider>("speed");
        }

        if (horizontalToggle == null)
        {
            horizontalToggle = FindUiByName<Toggle>("horizontal");
            if (horizontalToggle == null)
            {
                horizontalToggle = FindUiByName<Toggle>("toggle");
            }
        }
    }

    private static T FindUiByName<T>(string namePart) where T : Component
    {
        foreach (T component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (!component.gameObject.scene.IsValid())
            {
                continue;
            }

            if (component.name.ToLowerInvariant().Contains(namePart))
            {
                return component;
            }
        }

        return null;
    }

    private static Transform FindTransformByName(string namePart)
    {
        foreach (Transform transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (!transform.gameObject.scene.IsValid())
            {
                continue;
            }

            if (transform.name.ToLowerInvariant().Contains(namePart))
            {
                return transform;
            }
        }

        return null;
    }
}
