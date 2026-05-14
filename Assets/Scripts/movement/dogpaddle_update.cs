using UnityEngine;

public class dogpaddle_update : MonoBehaviour
{
    private const float ScaleResearch = 65f / 40f;
    private const float TwoFingerRotateDegrees = 90f;
    private const float RawVerticalDistance = 1784f;
    private const float RawHorizontalDistance = 4095f;
    private const float TouchPadHorizontalCmDistance = 11f;
    private const float TouchPadVerticalCmDistance = 6f;
    private const float HorizontalCmPerRaw = TouchPadHorizontalCmDistance / RawHorizontalDistance;
    private const float VerticalCmPerRaw = TouchPadVerticalCmDistance / RawVerticalDistance;

    [SerializeField] private TouchpadManager touchManager;
    [SerializeField] private GameObject Player;
    [SerializeField] private Transform worldRotateTarget;

    private Vector2 lastRawPosition;
    private bool hasLastRawPosition;
    private Vector2 lastTwoFingerRawPosition;
    private bool hasLastTwoFingerRawPosition;
    private bool isTwoFingerMode;

    private void Awake()
    {
        if (touchManager == null)
        {
            touchManager = TouchpadManager.Instance;
        }
    }

    private void Update()
    {
        if (touchManager == null)
        {
            touchManager = TouchpadManager.Instance;
        }

        if (touchManager == null || Player == null)
        {
            return;
        }

        if (!touchManager.IsTouching)
        {
            ResetTouchState();
            return;
        }

        if (touchManager.TouchCount >= 2)
        {
            RotateTwoFingerDrag();
            return;
        }

        if (isTwoFingerMode)
        {
            // รีเซ็ตก่อนกลับมาเดิน
            ResetTouchState();
            return;
        }

        OneFingerDrag();
    }

    private void OneFingerDrag()
    {
        Vector2 currentRawPosition = touchManager.PrimaryRawPosition;
        if (!hasLastRawPosition)
        {
            // เริ่มจำตำแหน่งนิ้ว
            lastRawPosition = currentRawPosition;
            hasLastRawPosition = true;
            return;
        }

        Vector2 dragDeltaRaw = currentRawPosition - lastRawPosition;
        lastRawPosition = currentRawPosition;

        float dragYcm = dragDeltaRaw.y * VerticalCmPerRaw;
        float moveMeters = dragYcm * ScaleResearch;
        if (Mathf.Approximately(moveMeters, 0f))
        {
            return;
        }

        Transform moveTarget = worldRotateTarget != null ? worldRotateTarget : Player.transform;
        // ลากแกน Y เพื่อเดินหน้า/ถอยหลัง
        Player.transform.position += moveTarget.forward * moveMeters;
    }

    private void RotateTwoFingerDrag()
    {
        isTwoFingerMode = true;
        hasLastRawPosition = false;

        Vector2 currentRawPosition = touchManager.AverageRawPosition;
        if (!hasLastTwoFingerRawPosition)
        {
            // เริ่มจำตำแหน่งสองนิ้ว
            lastTwoFingerRawPosition = currentRawPosition;
            hasLastTwoFingerRawPosition = true;
            return;
        }

        float dragDeltaX = currentRawPosition.x - lastTwoFingerRawPosition.x;
        lastTwoFingerRawPosition = currentRawPosition;

        if (Mathf.Approximately(dragDeltaX, 0f))
        {
            return;
        }

        Transform rotateTarget = worldRotateTarget != null ? worldRotateTarget : Player.transform;
        float degreesPerRaw = TwoFingerRotateDegrees / RawVerticalDistance;
        float rotationDegrees = -dragDeltaX * degreesPerRaw;

        // สองนิ้วซ้ายหันขวา ขวาหันซ้าย
        rotateTarget.Rotate(Vector3.up, rotationDegrees, Space.World);
        if (Player.transform != rotateTarget && !Player.transform.IsChildOf(rotateTarget))
        {
            Player.transform.Rotate(Vector3.up, rotationDegrees, Space.World);
        }
    }

    private void ResetTouchState()
    {
        hasLastRawPosition = false;
        hasLastTwoFingerRawPosition = false;
        isTwoFingerMode = false;
    }
}
