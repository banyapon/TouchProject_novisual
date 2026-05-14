using UnityEngine;
using System.Collections.Generic;
using TrackpadDll;
using RawInput.Touchpad;

public class TouchpadManager : MonoBehaviour
{
    private struct TouchSession
    {
        public float LastX;
        public float LastY;
        public float RawX;
        public float RawY;
        public float LastSeenTime;
    }

    private readonly Dictionary<int, TouchSession> sessions = new Dictionary<int, TouchSession>();

    private const float ContactTimeoutSeconds = 0.1f;

    public static TouchpadManager Instance { get; private set; }

    public bool IsTouching => sessions.Count > 0;
    public bool isTouching => IsTouching;
    public int TouchCount => sessions.Count;
    public Vector2 PrimaryTouchPosition { get; private set; }
    public Vector2 AverageTouchPosition { get; private set; }
    public Vector2 PrimaryRawPosition { get; private set; }
    public Vector2 AverageRawPosition { get; private set; }

    private bool lastDebugIsTouching;
    private bool hasDebugState;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        TrackpadInterface.Start();
        Debug.Log("Trackpad Listener Started!");
    }

    private void FixedUpdate()
    {
        float now = Time.time;

        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
            //float screenX = (contact.X / 4095f) * Screen.width;
            //float screenY = (1f - (contact.Y / 4095f)) * Screen.height;

            sessions[contact.ContactId] = new TouchSession
            {
                //LastX = screenX,
                //LastY = screenY,
                RawX = contact.X,
                RawY = contact.Y,
                LastSeenTime = now
            };
        }

        CleanupExpiredSessions(now);
        UpdateTouchPositions();
        DebugTouching();
    }

    private void CleanupExpiredSessions(float now)
    {
        List<int> expiredContacts = null;
        foreach (KeyValuePair<int, TouchSession> kvp in sessions)
        {
            if (now - kvp.Value.LastSeenTime < ContactTimeoutSeconds)
            {
                continue;
            }

            if (expiredContacts == null)
            {
                expiredContacts = new List<int>();
            }

            expiredContacts.Add(kvp.Key);
        }

        if (expiredContacts == null)
        {
            return;
        }

        foreach (int contactId in expiredContacts)
        {
            sessions.Remove(contactId);
            Debug.Log($"Touch end id={contactId}");
        }
    }

    private void UpdateTouchPositions()
    {
        if (sessions.Count == 0)
        {
            PrimaryTouchPosition = Vector2.zero;
            AverageTouchPosition = Vector2.zero;
            PrimaryRawPosition = Vector2.zero;
            AverageRawPosition = Vector2.zero;
            return;
        }

        Vector2 totalTouch = Vector2.zero;
        Vector2 totalRaw = Vector2.zero;
        bool hasPrimary = false;

        foreach (TouchSession session in sessions.Values)
        {
            Vector2 touchPosition = new Vector2(session.LastX, session.LastY);
            Vector2 rawPosition = new Vector2(session.RawX, session.RawY);

            if (!hasPrimary)
            {
                PrimaryTouchPosition = touchPosition;
                PrimaryRawPosition = rawPosition;
                hasPrimary = true;
            }

            totalTouch += touchPosition;
            totalRaw += rawPosition;
        }

        AverageTouchPosition = totalTouch / sessions.Count;
        AverageRawPosition = totalRaw / sessions.Count;
    }

    private void DebugTouching()
    {
        if (hasDebugState && lastDebugIsTouching == isTouching)
        {
            return;
        }

        Debug.Log($"isTouching={isTouching}, touchCount={TouchCount}");
        lastDebugIsTouching = isTouching;
        hasDebugState = true;
    }

    private void OnDisable()
    {
        StopThread();
    }

    private void OnApplicationQuit()
    {
        StopThread();
    }

    private void StopThread()
    {
        Debug.Log("Shutting down Trackpad Thread...");
        TrackpadInterface.Stop();
    }
}
