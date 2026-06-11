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
    [SerializeField] private bool showTouchDebug = true;

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
    private readonly string[] contactDebugActions = new string[6];
    private int debugEventCountFrame;

    void FixedUpdate()
    {
        bool isTouching = false;
        int touchCount = 0;
        int eventCount = 0;

        //ล้างข้อมูลนิ้วในเฟรมนี้
        for (int i = 0; i < contactidFrame.Length; i++)
        {
            contactIdsFrame[i] = -1;
            contactidFrame[i] = Vector2.zero;
            contactDebugActions[i] = "Clear";
        }

        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
            isTouching = true;
            eventCount++;
            Debug.Log(contact);

            int contactId = contact.ContactId;
            int contactIndex = -1;

            //[1,1,2,1,3,2]
            //รอบ contactIdsFrame, contactIndex, touchcount, contactId
             //1 [] -1, touchcount 1, 1
             //2 [1] 0, touchcount 1, 1
             //3 [1] -1, touchcount 2, 2
             //4 [1,2] 0, touchcount 2, 1
             //5 [1,2] -1, touchcount 3, 3
             //6 [1,2,3] 1, touchcount 3, 2

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
                contactDebugActions[contactIndex] = "Add";
                touchCount++;
            }

            //อัปเดตตำแหน่งล่าสุด
            if (contactIndex != -1)
            {
                if (contactDebugActions[contactIndex] != "Add")
                {
                    contactDebugActions[contactIndex] = "Update";
                }

                contactidFrame[contactIndex] = new Vector2(contact.X, contact.Y);
            }
        }



        // Set public state outside while loop
        IsTouching = isTouching;
        TouchCount = touchCount;
        if (IsTouching)
        {
            PrimaryRawPosition = contactidFrame[0];
            //เปลี่ยนเป็นแบบ อาจารย์ ปรับใน Excel 
        }
        else
        {
            PrimaryRawPosition = Vector2.zero;
        }

        //PrimaryRawPosition = IsTouching ? contactidFrame[0] : Vector2.zero;
        
        // เขียนเพิ่มมา Debug จำนวน event ในเฟรมนี้
        debugEventCountFrame = eventCount;
    }

    //เอามา Debug
    private void OnGUI()
    {
        if (!showTouchDebug)
        {
            return;
        }

        float debugWidth = 320f;
        float debugHeight = 320f;
        float debugMargin = 10f;
        GUILayout.BeginArea(new Rect(Screen.width - debugWidth - debugMargin, debugMargin, debugWidth, debugHeight), GUI.skin.box);
        GUILayout.Label("Touch Debug");
        GUILayout.Label("IsTouching: " + IsTouching + " | TouchCount: " + TouchCount + " | Events: " + debugEventCountFrame);
        GUILayout.Label("Primary: " + PrimaryRawPosition.x.ToString("0") + " | " + PrimaryRawPosition.y.ToString("0"));
        GUILayout.Space(6f);
        GUILayout.Label("Slot | Action | ContactId | X | Y");

        for (int i = 0; i < contactidFrame.Length; i++)
        {
            Vector2 position = contactidFrame[i];
            GUILayout.Label(
                i + " | " +
                contactDebugActions[i] + " | " +
                contactIdsFrame[i] + " | " +
                position.x.ToString("0") + " | " +
                position.y.ToString("0"));
        }

        GUILayout.EndArea();
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
