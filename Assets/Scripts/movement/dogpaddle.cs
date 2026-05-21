using UnityEngine;

public class dogpaddle : MonoBehaviour
{
    private const float scaleResearch = 65f / 40f;
    private const float RawVerticalDistance = 1784f;
    private const float TouchPadVerticalCmDistance = 6f;
    private const float VerticalCmPerRaw = TouchPadVerticalCmDistance / RawVerticalDistance;
    private const float RotateDeadZoneRaw = 8f;

    [SerializeField] private TouchpadManager touchManager;
    [SerializeField] private GameObject Player;
    [SerializeField] private Transform worldRotateTarget;

    private int TouchCount = 0;
    private int lastTouchManagerCount = 0;
    private Vector2? lastPosition = null;

    private void Awake()
    {
        if (touchManager == null)
        {
            touchManager = TouchpadManager.Instance;
        }
    }

    private void FixedUpdate()
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
            TouchCount = 0;
            lastTouchManagerCount = 0;
            lastPosition = null;
            return;
        }

        TouchCount += 1;
        int currentTouchManagerCount = touchManager.TouchCount;
        Vector2 CurrentPosition = touchManager.GetCurrentTouch();

        if (lastTouchManagerCount != currentTouchManagerCount)
        {
            TouchCount = 1;
            lastTouchManagerCount = currentTouchManagerCount;
            lastPosition = CurrentPosition;
            return;
        }

        if (TouchCount == 1)
        {
            // จำตำแหน่งแรก
            lastPosition = CurrentPosition;
            return;
        }

        if (lastPosition == null)
        {
            lastPosition = CurrentPosition;
            return;
        }

        Vector2 dragDelta = CurrentPosition - lastPosition.Value;
        lastPosition = CurrentPosition;

        if (touchManager.TouchCount >= 2 && IsHorizontalRotateDrag(dragDelta))
        {
            RotateByTwoFingerDrag(dragDelta);
            return;
        }

        MoveByOneFingerDrag(dragDelta);
    }

    private void MoveByOneFingerDrag(Vector2 dragDelta)
    {
        if (Mathf.Approximately(dragDelta.y, 0f))
        {
            return;
        }

        MoveForwardBackward(dragDelta.y);
    }

    private void MoveForwardBackward(float dragY)
    {
        float dragDeltaY = VerticalCmPerRaw * dragY;
        float distance = scaleResearch * dragDeltaY;

        Transform moveTarget = worldRotateTarget != null ? worldRotateTarget : Player.transform;
        // เคลื่อนที่ตามแกนลาก Y
        Player.transform.position += moveTarget.forward * distance;
    }

    private void RotateByTwoFingerDrag(Vector2 dragDelta)
    {
        Transform rotateTarget = worldRotateTarget != null ? worldRotateTarget : Player.transform;
        // ซ้ายหมุนขวา ขวาหมุนซ้าย
        rotateTarget.Rotate(Vector3.up, -dragDelta.x * (90f / RawVerticalDistance), Space.World);
    }

    private bool IsHorizontalRotateDrag(Vector2 dragDelta)
    {
        // กันเดินเร็วแล้วเข้า rotate
        return Mathf.Abs(dragDelta.x) > RotateDeadZoneRaw && Mathf.Abs(dragDelta.x) > Mathf.Abs(dragDelta.y);
    }
}
