using UnityEngine;
using UnityEngine.UI;

public class ProveAlgorithm : MonoBehaviour
{
    private const float ScaleResearch = 65f / 40f;
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
            hasLastRawPosition = false;
            return;
        }

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

    
}
