using UnityEngine;
using System.Collections.Generic;
using TrackpadDll;
using RawInput.Touchpad;

public class TouchpadManager : MonoBehaviour
{
    public static TouchpadManager Instance { get; private set; }

    private struct TouchSession
    {
        public float LastX;
<<<<<<< Updated upstream
        public float LastY;
        public float RawX;
        public float RawY;
        public float LastSeenTime;
=======
        public float StartY;
        public float LastY;
        public float LastEventTime;
    
>>>>>>> Stashed changes
    }

    private readonly Dictionary<int, TouchSession> sessions = new Dictionary<int, TouchSession>();
<<<<<<< Updated upstream

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
=======
    private int primaryContactId = -1;
    
    private const float ContactTimeoutSeconds = 0.1f; // Timeout for lost contacts
    
    public bool IsTouching => sessions.Count > 0;
    public int TouchCount => sessions.Count;
    public Vector2 currentRawPosition { get; private set; }
>>>>>>> Stashed changes

    private void Awake()
    {
        Instance = this;
    }

<<<<<<< Updated upstream
    private void Start()
=======
    void Start()
>>>>>>> Stashed changes
    {
        TrackpadInterface.Start();
        Debug.Log("Trackpad Listener Started!");
    }

    private void FixedUpdate()
    {
        float now = Time.time;

        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
<<<<<<< Updated upstream
            //float screenX = (contact.X / 4095f) * Screen.width;
            //float screenY = (1f - (contact.Y / 4095f)) * Screen.height;

            sessions[contact.ContactId] = new TouchSession
=======
            int contactId = contact.ContactId;
            
            // contact.X and contact.Y are raw values (usually 0 to 4095)
            // contact.Id identifies which finger is which (multi-touch!)
            
            isTouching = true;
            Debug.Log(isTouching);
            if (sessions.TryGetValue(contactId, out TouchSession session))
            {
                session.LastX = contact.X;
                session.LastY = contact.Y;
                session.LastEventTime = now;
            }
            else
            {
                session = new TouchSession
                {
                    LastX = contact.X,
                    StartY = contact.Y,
                    LastY = contact.Y,
                    LastEventTime = now
                };
            }

            sessions[contactId] = session;
            if (primaryContactId == -1)
            {
                primaryContactId = contactId;
            }

            if (contactId == primaryContactId)
            {
                currentRawPosition = new Vector2(contact.X, contact.Y);
            }

            /*if(isTouching){
                Debug.Log("in while time=" + Time.time);
            }*/

            // Example: Map raw 0-4095 to screen width/height
            float screenX = (contact.X / 4095f) * Screen.width;
            float screenY = (1f - (contact.Y / 4095f)) * Screen.height;

            // Debug.Log($"Finger {contactId} at: {screenX}, {screenY}");
            // You can use these coordinates to move objects or UI cursors here
        }

        if(!isTouching){
            Debug.Log(isTouching);
            //Debug.Log("current time=" + Time.time);
        }
       
        
        // Clean up expired touch sessions
        var expiredContacts = new List<int>();
        foreach (var kvp in sessions)
        {
            if (now - kvp.Value.LastEventTime >= ContactTimeoutSeconds)
>>>>>>> Stashed changes
            {
                //LastX = screenX,
                //LastY = screenY,
                RawX = contact.X,
                RawY = contact.Y,
                LastSeenTime = now
            };
        }
<<<<<<< Updated upstream

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
=======
        bool primaryExpired = false;
        foreach (var contactId in expiredContacts)
        {
            sessions.Remove(contactId);
            if (contactId == primaryContactId)
            {
                primaryExpired = true;
            }
        }

        if (primaryExpired)
        {
            // เปลี่ยนมือให้เริ่ม gesture ใหม่
            sessions.Clear();
            primaryContactId = -1;
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
=======

>>>>>>> Stashed changes
}
