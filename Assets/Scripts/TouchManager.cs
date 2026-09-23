using System;
using System.Collections.Generic;
using UnityEngine;
using TrackpadDll; // The namespace you defined in Visual Studio
using RawInput.Touchpad;


[DefaultExecutionOrder(-200)]
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

    //เพิ่มเป็นตัวควบคุม spline และจำตำแหน่งนิ้วครั้งก่อน
    [Header("Spline Movement")]
    [SerializeField] private bool controlSplineMovement = true;
    [SerializeField] private splinemovement splineMovement;
    private Vector2? previousSplineTouchPosition;
    private TouchMode previousSplineTouchMode = TouchMode.None;

    private int numTouch;
    private int oldNumTouch;
    private bool oldTouch;
    private bool newTouch;
    private TouchMode oldMode;
    private TouchMode newMode;
    private static bool listenerStarted;
    private string listenerStatus = "Stopped";

    private struct ContactState
    {
        public Vector2 position;
        public float lastSeen;
    }

    private readonly Dictionary<int, ContactState> contacts = new Dictionary<int, ContactState>();
    private readonly List<int> expiredContacts = new List<int>();
    private readonly HashSet<int> previousContactIds = new HashSet<int>();
    private bool contactsChanged;

    private void OnEnable()
    {
        if (Instance != null && Instance != this && Instance.isActiveAndEnabled)
        {
            Debug.LogWarning("Only one TouchpadManager may consume the touchpad queue.", this);
            enabled = false;
            return;
        }

        Instance = this;
        ResetContacts();
        StartListener();
    }

    private void StartListener()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try
        {
            // The native listener belongs to the application, not a scene.
            // An outgoing scene must not stop the next scene's shared thread.
            if (!listenerStarted)
            {
                TrackpadInterface.Start();
                listenerStarted = true;
                Application.quitting -= StopListener;
                Application.quitting += StopListener;
#if UNITY_EDITOR
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopListener;
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopListener;
#endif
            }

            // Discard packets accumulated while this scene was not consuming.
            while (TrackpadInterface.EventQueue.TryDequeue(out _)) { }
            listenerStatus = "Listening (Windows raw touchpad)";
        }
        catch (Exception exception)
        {
            listenerStatus = "Start failed: " + exception.Message;
            Debug.LogError("Touchpad listener could not start: " + exception, this);
        }
#else
        listenerStatus = "Raw touchpad requires Windows";
#endif
    }

    // Compact display slots only; actual contact IDs are stored in contacts.
    private readonly Vector2[] contactidFrame = new Vector2[6];
    private readonly bool[] contactActiveFrame = new bool[6];
    private readonly string[] contactDebugActions = new string[6];
    private readonly int[] debugContactIds = new int[6];
    private int debugEventCountFrame;

    void FixedUpdate()
    {
        oldTouch = newTouch;
        oldMode = CurrentMode;
        oldNumTouch = numTouch;

        int eventCount = 0;
        float now = Time.realtimeSinceStartup;

        // Contact IDs are driver IDs, not array indices. Retain the latest
        // sample until timeout: a physics tick without a packet is not a lift.
        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
            eventCount++;
            RecordContact(contact.ContactId, new Vector2(contact.X, contact.Y), now);
        }

        RefreshContacts(now);
        int touchCount = TouchCount;

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
        else if (oldNumTouch != numTouch || contactsChanged)
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

        //เพิ่มใหม่ย้านมาอ่าน Touch และสั่งให้ Avatar เคลื่อนที่บน spline ใน FixedUpdate นี้ไม่แยกกัน
        UpdateSplineMovement();

    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            showTouchDebug = !showTouchDebug;
        }
    }

    private void RecordContact(int id, Vector2 position, float now)
    {
        if (id < 0) return;
        contacts[id] = new ContactState { position = position, lastSeen = now };
    }

    private void RefreshContacts(float now)
    {
        expiredContacts.Clear();
        foreach (var pair in contacts)
        {
            if (now - pair.Value.lastSeen >= ContactTimeoutSeconds)
                expiredContacts.Add(pair.Key);
        }
        foreach (int id in expiredContacts) contacts.Remove(id);

        contactsChanged = !previousContactIds.SetEquals(contacts.Keys);
        previousContactIds.Clear();
        Vector2 total = Vector2.zero;
        int slot = 0;
        foreach (var pair in contacts)
        {
            previousContactIds.Add(pair.Key);
            total += pair.Value.position;
            if (slot >= contactidFrame.Length) continue;
            debugContactIds[slot] = pair.Key;
            contactidFrame[slot] = pair.Value.position;
            contactActiveFrame[slot] = true;
            contactDebugActions[slot] = pair.Value.lastSeen == now ? "Update" : "Held";
            slot++;
        }
        for (; slot < contactidFrame.Length; slot++)
        {
            debugContactIds[slot] = -1;
            contactidFrame[slot] = Vector2.zero;
            contactActiveFrame[slot] = false;
            contactDebugActions[slot] = "Idle";
        }
        TouchCount = contacts.Count;
        PrimaryRawPosition = TouchCount > 0 ? total / TouchCount : Vector2.zero;
    }

    private void ResetContacts()
    {
        contacts.Clear();
        previousContactIds.Clear();
        RefreshContacts(Time.realtimeSinceStartup);
        IsTouching = oldTouch = newTouch = false;
        numTouch = oldNumTouch = 0;
        CurrentMode = oldMode = newMode = TouchMode.None;
        Status = TouchStatus.None;
        previousSplineTouchPosition = null;
        previousSplineTouchMode = TouchMode.None;
        if (splineMovement != null) splineMovement.EndTouchFromManager();
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
        GUILayout.Label(listenerStatus);
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
                debugContactIds[i] + " | " +
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

    //เพิ่มใหม่ splinemovement ว่า TouchManager ตัวนี้เป็นผู้ควบคุม input อยู่
    public bool ControlsSplineMovement(splinemovement target)
    {
        return controlSplineMovement
            && isActiveAndEnabled
            && splineMovement != null
            && splineMovement == target;
    }

    //Algorithm:
    // 1. อ่าน Touch ปัจจุบัน
    // 2. ลบด้วย Touch ครั้งก่อนเพื่อหา dragDelta
    // 3. อัปเดต Touch ครั้งก่อน
    // 4. ส่ง dragDelta ให้ spline คำนวณระยะและย้าย Avatar
    private void UpdateSplineMovement()
    {
        if (!controlSplineMovement)
        {
            return;
        }

        if (splineMovement == null)
        {
            splineMovement = FindAnyObjectByType<splinemovement>();
        }

        if (splineMovement == null)
        {
            return;
        }

        if (!IsTouching)
        {
            previousSplineTouchPosition = null;
            previousSplineTouchMode = TouchMode.None;
            splineMovement.EndTouchFromManager();
            return;
        }

        Vector2 currentTouchPosition = GetCurrentTouch();

        //เริ่มแตะหรือเปลี่ยนจากหนึ่งนิ้วเป็นสองนิ้ว เก็บตำแหน่งตั้งต้นก่อนไม่ให้ Avatar กระโดดจาก delta ก่น
        bool startedNewTouch = Status == TouchStatus.OnTouch
            || previousSplineTouchPosition == null;
        bool changedFingerCount = CurrentMode != previousSplineTouchMode
            && previousSplineTouchMode != TouchMode.Change;

        if (startedNewTouch || changedFingerCount)
        {
            previousSplineTouchPosition = currentTouchPosition;
            previousSplineTouchMode = CurrentMode;
            splineMovement.BeginTouchFromManager(currentTouchPosition);
            return;
        }

        //เพิ่มใหม่--- Change -> Translate เป็นการเปลี่ยนสถานะหลังเริ่มแตะตามปกติ
        // เก็บจุดเริ่มเดิมไว้ เพื่อไม่ให้ระยะช่วงต้นของ Swipe หายไป
        previousSplineTouchMode = CurrentMode;

        if (Status != TouchStatus.OnDrag)
        {
            previousSplineTouchPosition = currentTouchPosition;
            return;
        }

        Vector2 dragDelta = currentTouchPosition - previousSplineTouchPosition.Value;

        //อัปเดต Touch ครั้งก่อน เพื่อใช้ใน FixedUpdate ถัดไป
        previousSplineTouchPosition = currentTouchPosition;

        splineMovement.MoveFromTouchManager(
            currentTouchPosition,
            dragDelta,
            CurrentMode,
            TouchCount);
    }

    private void OnDisable()
    {
        if (Instance != this) return;
        ResetContacts();
        Instance = null;
    }

    private static void StopListener()
    {
        if (!listenerStarted) return;
        listenerStarted = false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        TrackpadInterface.Stop();
#endif
        Application.quitting -= StopListener;
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopListener;
#endif
    }

}
