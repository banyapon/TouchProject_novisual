using UnityEngine;
using System.Collections.Generic;
using TrackpadDll; // The namespace you defined in Visual Studio
using RawInput.Touchpad;


public class TouchpadManager : MonoBehaviour
{
    public static TouchpadManager Instance { get; private set; }
    private const float ContactTimeoutSeconds = 0.1f; // Timeout for lost contacts
    
    public bool IsTouching;
    public int TouchCount;
    public Vector2 PrimaryRawPosition { get; private set; }
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

    private readonly HashSet<int> _frameContactIds = new();
    //private readonly List<int> _frameContactIds = new();

    void FixedUpdate()
    {
        bool isTouching = false;
        _frameContactIds.Clear();

        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
            isTouching = true;
            _frameContactIds.Add(contact.ContactId);
            PrimaryRawPosition = new Vector2(contact.X, contact.Y);
        }

        /*
        TouchpadContact last = default;
        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
            isTouching = true;
            last = contact;
            PrimaryRawPosition = new Vector2(contact.X, contact.Y);
        }
        if (isTouching) _frameContactIds.Add(last.ContactId);
        */

        // Set public state outside while loop
        IsTouching = isTouching;
        TouchCount = _frameContactIds.Count;
        if (!IsTouching)
        {
            PrimaryRawPosition = Vector2.zero;
        }
    }

    public Vector2 GetCurrentTouch()
    {
        return PrimaryRawPosition;
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
