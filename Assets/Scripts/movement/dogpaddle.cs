using UnityEngine;

public class dogpaddle : MonoBehaviour
{
    private const float scaleResearch = 65f / 40f;
    private const float RawVerticalDistance = 912f;
    private const float TouchPadVerticalCmDistance = 8f;
    private const float RawHorizontalDistance = 1452f;
    private const float TouchPadHorizontalCmDistance = 12.5f;
    private const float RotateDeadZoneRaw = 8f;

    [SerializeField] private TouchpadManager touchManager;
    [SerializeField] private GameObject Player;
    [SerializeField] private Transform worldRotateTarget;

    private int TouchCount = 0;
    private Vector2? lastPosition = null;
    private bool suppressNextDragFrame;
    private TouchCalibrationSettings.Values calibration;

    private void Awake()
    {
        calibration = TouchCalibrationSettings.Load();

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
            lastPosition = null;
            suppressNextDragFrame = false;
            return;
        }

        TouchCount += 1;
        TouchpadManager.TouchMode mode = touchManager.CurrentMode;
        TouchpadManager.TouchStatus status = touchManager.Status;
        Vector2 currentPosition = touchManager.GetCurrentTouch();

        if (status == TouchpadManager.TouchStatus.OnTouch)
        {
            TouchCount = 1;
            lastPosition = currentPosition;
            suppressNextDragFrame = true;
            return;
        }

        if (mode == TouchpadManager.TouchMode.Change)
        {
            TouchCount = 1;
            lastPosition = currentPosition;
            suppressNextDragFrame = true;
            return;
        }

        if (status != TouchpadManager.TouchStatus.OnDrag)
        {
            lastPosition = currentPosition;
            return;
        }

        if (TouchCount <= 1)
        {
            lastPosition = currentPosition;
            return;
        }

        if (lastPosition == null)
        {
            lastPosition = currentPosition;
            return;
        }

        if (suppressNextDragFrame)
        {
            lastPosition = currentPosition;
            suppressNextDragFrame = false;
            return;
        }

        Vector2 dragDelta = currentPosition - lastPosition.Value;
        lastPosition = currentPosition;

        if (mode == TouchpadManager.TouchMode.Rotate)
        {
            if (IsHorizontalRotateDrag(dragDelta))
            {
                RotateByTwoFingerDrag(dragDelta);
            }

            return;
        }

        if (mode == TouchpadManager.TouchMode.Translate)
        {
            MoveByOneFingerDrag(dragDelta);
        }
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
        float dragDeltaY = calibration.VerticalCmPerRaw * dragY;
        float distance = calibration.ScaleResearch * dragDeltaY;

        Transform moveTarget = worldRotateTarget != null ? worldRotateTarget : Player.transform;
        Player.transform.position += moveTarget.forward * distance;
    }

    private void RotateByTwoFingerDrag(Vector2 dragDelta)
    {
        Transform rotateTarget = worldRotateTarget != null ? worldRotateTarget : Player.transform;
        rotateTarget.Rotate(Vector3.up, -dragDelta.x * (90f / calibration.RawVerticalDistance), Space.World);
    }

    private bool IsHorizontalRotateDrag(Vector2 dragDelta)
    {
        return Mathf.Abs(dragDelta.x) > RotateDeadZoneRaw && Mathf.Abs(dragDelta.x) > Mathf.Abs(dragDelta.y);
    }
}
