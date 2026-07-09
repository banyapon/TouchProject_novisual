using UnityEngine;

public class QuadraticBezier : MonoBehaviour
{
    [Header("จุดควบคุม 3 จุด")]
    public Transform p0; // Start
    public Transform p1; // Control
    public Transform p2; // End

    [Header("วัตถุที่จะให้วิ่งตามเส้น")]
    public Transform mover;
    public float duration = 3f; // วิ่งครบเส้นใน 3 วินาที

    private float t = 0f;

    // สูตร Quadratic Bezier จาก https://en.wikipedia.org/wiki/B%C3%A9zier_curve#Quadratic_B%C3%A9zier_curves
    // B(t) = (1-t)² * P0  +  2(1-t)t * P1  +  t² * P2
    public static Vector3 GetPoint(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return (u * u * a) + (2f * u * t * b) + (t * t * c);
    }

    void Update()
    {
        if (mover == null) return;

        //t จาก 0 - 1 แล้ววนกลับ
        t += Time.deltaTime / duration;
        if (t > 1f) t = 0f;

        mover.position = GetPoint(p0.position, p1.position, p2.position, t);
    }

    void OnDrawGizmos()
    {
        if (p0 == null || p1 == null || p2 == null) return;

        Gizmos.color = Color.cyan;
        Vector3 prev = p0.position;

        int segments = 30; //โค้ว 30 ช่วง
        for (int i = 1; i <= segments; i++)
        {
            Vector3 next = GetPoint(p0.position, p1.position, p2.position, i / (float)segments);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        //Gizmos Drawline 
        Gizmos.color = Color.gray;
        Gizmos.DrawLine(p0.position, p1.position);
        Gizmos.DrawLine(p1.position, p2.position);
    }
}