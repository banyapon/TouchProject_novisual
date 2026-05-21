using System.Collections.Generic;
using RawInput.Touchpad;
using TrackpadDll;
using UnityEngine;

public class TouchpadManagerCustom : MonoBehaviour
{
    private const float Scale = 1.625f;
    private const float CalibratedRawDistance = 1784f;
    private const float CalibratedCmDistance = 6f;
    private const float VrFullDistanceMeters = 65f;
    private const float CmPerRaw = CalibratedCmDistance / CalibratedRawDistance;
    private const float ContactTimeoutSeconds = 0.08f;

    [SerializeField] private GameObject planeObject;
    [SerializeField] private GameObject cubeObject;

    private readonly Dictionary<int, TouchSession> sessions = new Dictionary<int, TouchSession>();
    private readonly List<int> _expiredIds = new List<int>();
    private Vector3 cubeStartPosition;

    private struct TouchSession
    {
        public float StartY;
        public float LastY;
        public float LastSeenTime;
        public float AppliedWorldDistance;
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

        if (cubeObject == null)
        {
            cubeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeObject.name = "TouchpadCube";
            cubeObject.transform.position = new Vector3(0f, 0.5f, 0f);
            cubeObject.transform.localScale = Vector3.one;
            cubeStartPosition = cubeObject.transform.position;
            Debug.Log($"Cube created at start position: {cubeStartPosition}");
        }
    }

    private void HandleContact(TouchpadContact contact)
    {
        int contactId = contact.ContactId;
        float now = Time.time;

        if (!sessions.TryGetValue(contactId, out TouchSession session) || now - session.LastSeenTime >= ContactTimeoutSeconds)
        {
            sessions[contactId] = new TouchSession
            {
                StartY = contact.Y,
                LastY = contact.Y,
                LastSeenTime = now,
                AppliedWorldDistance = 0f
            };

            Debug.Log($"Touch start id={contactId}, y={contact.Y}");
            return;
        }

        float totalRawDistance = contact.Y - session.StartY;
        float totalCmDistance = totalRawDistance * CmPerRaw;
        float valueWorldDistance= totalCmDistance * Scale;
        float deltaWorldDistance = valueWorldDistance- session.AppliedWorldDistance;

        session.LastY = contact.Y;
        session.LastSeenTime = now;
        session.AppliedWorldDistance = valueWorldDistance;
        sessions[contactId] = session;

        if (Mathf.Approximately(deltaWorldDistance, 0f))
        {
            return;
        }

        MoveCube(contactId, session.StartY, session.LastY, totalRawDistance, totalCmDistance, valueWorldDistance, deltaWorldDistance);
    }

    private void MoveCube(
        int contactId,
        float startY,
        float lastY,
        float totalRawDistance,
        float totalCmDistance,
        float valueWorldDistance,
        float deltaWorldDistance)
    {
        if (cubeObject == null)
        {
            Debug.LogWarning($"Touch move id={contactId}, cubeObject is missing.");
            return;
        }

        Vector3 previousPosition = cubeObject.transform.position;
        Vector3 movement = Vector3.forward * deltaWorldDistance;
        Vector3 newPosition = previousPosition + movement;
        float distanceFromStart = Vector3.Dot(newPosition - cubeStartPosition, Vector3.forward);

        cubeObject.transform.position = newPosition;

        Debug.Log(
            $"Touch move id={contactId}, startY={startY}, lastY={lastY}, totalRawDistance={totalRawDistance}, " +
            $"totalCmDistance={totalCmDistance}, WorldDistance={valueWorldDistance}, " +
            $"deltaWorldDistance={deltaWorldDistance}, cube from {previousPosition} to {newPosition}, " +
            $"distanceFromStart={distanceFromStart}"
        );
    }

    private void CleanupExpiredSessions()
    {
        float now = Time.time;
        _expiredIds.Clear();

        foreach (KeyValuePair<int, TouchSession> pair in sessions)
        {
            if (now - pair.Value.LastSeenTime >= ContactTimeoutSeconds)
                _expiredIds.Add(pair.Key);
        }

        foreach (int id in _expiredIds)
            sessions.Remove(id);
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
