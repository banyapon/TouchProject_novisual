using UnityEngine;
using System.Collections.Generic;
using TrackpadDll; // The namespace you defined in Visual Studio
using RawInput.Touchpad;


public class TouchpadManager : MonoBehaviour
{
    public static TouchpadManager Instance { get; private set; }

    private struct TouchSession
    {
        public float LastX;
        public float StartY;
        public float LastY;
        public float LastSeenTime;
    
    }
    private readonly Dictionary<int, TouchSession> sessions = new Dictionary<int, TouchSession>();
    private int primaryContactId = -1;
    
    private const float ContactTimeoutSeconds = 0.1f; // Timeout for lost contacts
    
    public bool IsTouching => sessions.Count > 0;
    public int TouchCount => sessions.Count;
    public Vector2 PrimaryRawPosition { get; private set; }
    public Vector2 AverageRawPosition { get; private set; }
    public Vector2 currentRawPosition => PrimaryRawPosition;

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        //Application.targetFrameRate =120;
        // This starts the hidden window thread we built in the DLL
        TrackpadInterface.Start();
        Debug.Log("Trackpad Listener Started!");
    }

    void FixedUpdate()
    {
        float now = Time.time;
        bool isTouching = false;
        // Try to pull data out of the thread-safe queue
        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
            int contactId = contact.ContactId;
            
            // contact.X and contact.Y are raw values (usually 0 to 4095)
            // contact.Id identifies which finger is which (multi-touch!)
            
            isTouching = true;
            Debug.Log(isTouching);
            UpdateTouchSession(contact, contactId, now);

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
            if (now - kvp.Value.LastSeenTime >= ContactTimeoutSeconds)
            {
                expiredContacts.Add(kvp.Key);
                Debug.Log($"Touch end id={kvp.Key}");
            }
        }
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
        }

        UpdateRawPositions();
    }

    private void UpdateTouchSession(TouchpadContact contact, int contactId, float now)
    {
        if (sessions.TryGetValue(contactId, out TouchSession session))
        {
            session.LastX = contact.X;
            session.LastY = contact.Y;
            session.LastSeenTime = now;
        }
        else
        {
            session = new TouchSession
            {
                LastX = contact.X,
                StartY = contact.Y,
                LastY = contact.Y,
                LastSeenTime = now
            };
        }

        sessions[contactId] = session;

        if (primaryContactId == -1)
        {
            primaryContactId = contactId;
        }

        if (contactId == primaryContactId)
        {
            PrimaryRawPosition = new Vector2(contact.X, contact.Y);
        }
    }

    private void UpdateRawPositions()
    {
        if (sessions.Count == 0)
        {
            PrimaryRawPosition = Vector2.zero;
            AverageRawPosition = Vector2.zero;
            return;
        }

        Vector2 totalRawPosition = Vector2.zero;
        foreach (TouchSession session in sessions.Values)
        {
            totalRawPosition += new Vector2(session.LastX, session.LastY);
        }

        AverageRawPosition = totalRawPosition / sessions.Count;
    }

    private void OnDisable()
    {
        StopThread();
    }
    private void OnApplicationQuit()
    {
        // CRITICAL: If you don't stop the thread, the hidden window 
        // might stay alive after you stop the Unity Editor!
        TrackpadInterface.Stop();
        StopThread();
    }
    private void StopThread()
    {
        Debug.Log("Shutting down Trackpad Thread...");
        TrackpadInterface.Stop();
    }

}
