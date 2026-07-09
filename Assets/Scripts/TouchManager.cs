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

    public enum TouchMode
    {
        None,
        Translate,
        Rotate,
        Change
    }
    public TouchMode CurrentMode { get; private set; }

    public enum TouchStatus
    {
        None,
        OnTouch,
        OnDrag
    }
    public TouchStatus Status { get; private set; }

    private int numTouch;
    private int oldNumTouch;
    private bool oldTouch;
    private bool newTouch;
    private TouchMode oldMode;
    private TouchMode newMode;

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

    // ใช้ ContactId เป็น index ตรง ๆ ห้อง i = นิ้วที่มี ContactId = i
    private readonly Vector2[] contactidFrame = new Vector2[6];
    private readonly bool[] contactActiveFrame = new bool[6];
    private readonly string[] contactDebugActions = new string[6];
    private int debugEventCountFrame;

    //ตัวแปรไว้จำค่านิ้วหลักตอนนี้คือ index ห้แงไหน "-1 = ยังไม่มีการเตะ
    void FixedUpdate()
    {
        oldTouch = newTouch;
        oldMode = CurrentMode;
        oldNumTouch = numTouch;

        int touchCount = 0;
        int eventCount = 0;
        Vector2 totalRawPosition = Vector2.zero;

        //ล้างข้อมูลนิ้วในเฟรมนี้
        for (int i = 0; i < contactidFrame.Length; i++)
        {
            contactActiveFrame[i] = false;
            contactidFrame[i] = Vector2.zero;
            contactDebugActions[i] = "Clear";
        }

        //รับ event แบบ simple: ContactId คือ index ตรง ๆ
        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
            eventCount++;
            Debug.Log(contact);

            int id = contact.ContactId;

            //กัน id เกินขนาด array (บาง driver อาจส่ง id แปลก ๆ มา)
            if (id < 0 || id >= contactidFrame.Length)
            {
                continue;
            }

            //เจอห้องนี้ครั้งแรกในเฟรม = นิ้วใหม่ของเฟรมนี้
            if (!contactActiveFrame[id])
            {
                contactActiveFrame[id] = true;
                contactDebugActions[id] = "Add";
                touchCount++;
            }
            else
            {
                contactDebugActions[id] = "Update";
            }

            //อัปเดตตำแหน่งล่าสุด
            contactidFrame[id] = new Vector2(contact.X, contact.Y);
        }

        //ถ้านิ้วหลักเดิมยังแตะอยู่ ใช้ค่าเดิมต่อ (นิ้วไม่สลับไปมา)
        //ถ้านิ้วหลักยกไปแล้ว ให้เช็คห้อง[index] 0,1,2,เจอใครก่อนเอาคนนั้น
        for (int i = 0; i < contactActiveFrame.Length; i++)
        {
            if (contactActiveFrame[i])
            {
                totalRawPosition += contactidFrame[i];
            }
        }

        TouchCount = touchCount;
        if (touchCount > 0)
        {
            PrimaryRawPosition = totalRawPosition / touchCount;
        }
        else
        {
            PrimaryRawPosition = Vector2.zero;
        }

        debugEventCountFrame = eventCount;

        if (touchCount <= 0)
        {
            numTouch = 0;
        }
        else if (touchCount == 1)
        {
            numTouch = 1;
        }
        else
        {
            numTouch = 2;
        }

        newTouch = numTouch > 0;
        IsTouching = newTouch;

        if (!oldTouch && newTouch)
        {
            Status = TouchStatus.OnTouch;
        }
        else if (oldTouch && newTouch)
        {
            Status = TouchStatus.OnDrag;
        }
        else
        {
            Status = TouchStatus.None;
        }

        if (!newTouch)
        {
            newMode = TouchMode.None;
        }
        else if (!oldTouch)
        {
            newMode = TouchMode.Change;
        }
        else if (oldNumTouch != numTouch)
        {
            newMode = TouchMode.Change;
        }
        else if (numTouch == 2)
        {
            newMode = TouchMode.Rotate;
        }
        else
        {
            newMode = TouchMode.Translate;
        }

        CurrentMode = newMode;

        if(Input.GetKeyDown(KeyCode.Tab))
        {
            if(showTouchDebug)
            {
                showTouchDebug = false;
            }
            else
            {
                showTouchDebug = true;
            }
        }
    }

    //เอามา Debug
    private void OnGUI()
    {
        if (!showTouchDebug)
        {
            return;
        }

        float debugWidth = 320f;
        float debugHeight = 380f;
        float debugMargin = 10f;
        GUILayout.BeginArea(new Rect(Screen.width - debugWidth - debugMargin, debugMargin, debugWidth, debugHeight), GUI.skin.box);
        GUILayout.Label("Touch Debug");
        GUILayout.Label("IsTouching: " + IsTouching + " | TouchCount: " + TouchCount + " | Events: " + debugEventCountFrame);
        GUILayout.Label("oldTouch: " + oldTouch + " | newTouch: " + newTouch + " | numTouch: " + numTouch);
        GUILayout.Label("oldMode: " + oldMode + " | newMode: " + newMode + " | status: " + Status);
        GUILayout.Label("Mode: " + CurrentMode);
        GUILayout.Label("Current: " + PrimaryRawPosition.x.ToString("0") + " | " + PrimaryRawPosition.y.ToString("0"));
        GUILayout.Space(6f);
        GUILayout.Label("Id | Action | Active | X | Y");

        for (int i = 0; i < contactidFrame.Length; i++)
        {
            Vector2 position = contactidFrame[i];
            GUILayout.Label(
                i + " | " +
                contactDebugActions[i] + " | " +
                contactActiveFrame[i] + " | " +
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
