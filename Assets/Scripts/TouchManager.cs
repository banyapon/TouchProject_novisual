using UnityEngine;
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
    public Vector2[] ContactRawPositions => contactidFrame;

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

    private readonly int[] contactIdsFrame = new int[6];
    private readonly Vector2[] contactidFrame = new Vector2[6];

    void FixedUpdate()
    {
        bool isTouching = false;
        int touchCount = 0;

        //ล้างข้อมูลนิ้วในเฟรมนี้
        for (int i = 0; i < contactidFrame.Length; i++)
        {
            contactIdsFrame[i] = -1;
            contactidFrame[i] = Vector2.zero;
        }

        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
            isTouching = true;
            Debug.Log(contact);

            int contactId = contact.ContactId;
            int contactIndex = -1;

            //หา contact เดิม
            for (int i = 0; i < touchCount; i++)
            {
                if (contactIdsFrame[i] == contactId)
                {
                    contactIndex = i;
                    break;
                }
            }

            //เพิ่ม contact ใหม่
            if (contactIndex == -1 && touchCount < contactidFrame.Length)
            {
                contactIndex = touchCount;
                contactIdsFrame[contactIndex] = contactId;
                touchCount++;
            }

            //อัปเดตตำแหน่งล่าสุด
            if (contactIndex != -1)
            {
                contactidFrame[contactIndex] = new Vector2(contact.X, contact.Y);
            }
        }



        // Set public state outside while loop
        IsTouching = isTouching;
        TouchCount = touchCount;
        if (IsTouching)
        {
            PrimaryRawPosition = contactidFrame[0];
        }
        else
        {
            PrimaryRawPosition = Vector2.zero;
        }

        //PrimaryRawPosition = IsTouching ? contactidFrame[0] : Vector2.zero;
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
