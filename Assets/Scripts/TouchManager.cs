using UnityEngine;
using TrackpadDll; 
using RawInput.Touchpad;


public class TouchpadManager : MonoBehaviour
{
    public static TouchpadManager Instance { get; private set; }
    private const float ContactTimeoutSeconds = 0.1f; 
    // เกินเวลานี้ให้ถือว่า idle

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

    // ใช้ ContactId เป็น index ตรง ๆ  ห้อง i = นิ้วที่มี ContactId = i
    private readonly Vector2[] contactidFrame = new Vector2[10];
    private readonly bool[] contactActiveFrame = new bool[10];
    private readonly string[] contactDebugActions = new string[10];
    private int debugEventCountFrame;

    // จำว่า "นิ้วหลักตอนนี้คือห้องไหน" (-1 = ยังไม่มี)
    private int primaryId = -1;

    void FixedUpdate()
    {
        int touchCount = 0;
        int eventCount = 0;

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

        //หานิ้วแรก หรือนิ้วหลัก
        //ถ้านิ้วหลักเดิมยังแตะอยู่ ใช้ค่าเดิม
        //ถ้านิ้วหลักยกไปแล้ว ให้ เดินดู 0,1,2 เจอนิ้วที่แตะอยู่ก่อนเอาอันนั้น
        if (primaryId == -1 || !contactActiveFrame[primaryId])
        {
            primaryId = -1;
            for (int i = 0; i < contactActiveFrame.Length; i++)
            {
                if (contactActiveFrame[i])
                {
                    primaryId = i;
                    break;
                }
            }
        }

        // Set public state outside while loop
        IsTouching = primaryId != -1;
        TouchCount = touchCount;
        PrimaryRawPosition = IsTouching ? contactidFrame[primaryId] : Vector2.zero;

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
        float debugHeight = 380f;
        float debugMargin = 10f;
        GUILayout.BeginArea(new Rect(Screen.width - debugWidth - debugMargin, debugMargin, debugWidth, debugHeight), GUI.skin.box);
        GUILayout.Label("Touch Debug");
        GUILayout.Label("IsTouching: " + IsTouching + " | TouchCount: " + TouchCount + " | Events: " + debugEventCountFrame);
        GUILayout.Label("PrimaryId: " + primaryId + " | Primary: " + PrimaryRawPosition.x.ToString("0") + " | " + PrimaryRawPosition.y.ToString("0"));
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