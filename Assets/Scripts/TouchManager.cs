using UnityEngine;
using System.Collections.Generic;
using TrackpadDll; // The namespace you defined in Visual Studio
using RawInput.Touchpad;


public class TouchpadManager : MonoBehaviour
{
    public static TouchpadManager Instance { get; private set; }
    private const float ContactTimeoutSeconds = 0.1f; // เกินเวลานี้ให้ถือว่า idle

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

    //วิธีที่ 2 ใช้ Hashset คือนับค่าไม่ซ้ำ private readonly HashSet<int> contactidFrame = new();
    //วิธีที่ 1 ใช้ List แต่นับใหม่เฉพาะค่าที่ไม่ซ้ำใน while
    private readonly List<int> contactidFrame = new();
    void FixedUpdate()
    {
        bool isTouching = false;
        contactidFrame.Clear();

        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
            isTouching = true;
            Debug.Log(contact);
            //วิธีที่ 2 ใช้ Hashset คือนับค่าไม่ซ้ำ contactidFrame.Add(contact.ContactId);
            //วิธีที่ 1 ใช้ List แต่นับใหม่เฉพาะค่าที่ไม่ซ้ำใน while ให้รับค่า contactId แล้วถ้ายังไม่มีนิ้วนี้ใน List ค่อยเพิ่ม
            int contactId = contact.ContactId;
            if (!contactidFrame.Contains(contactId))
            {
                contactidFrame.Add(contactId);
            }
            //จบ วิธีที่ 1
            PrimaryRawPosition = new Vector2(contact.X, contact.Y);
        }

        

        // Set public state outside while loop
        IsTouching = isTouching;
        TouchCount = contactidFrame.Count;
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
