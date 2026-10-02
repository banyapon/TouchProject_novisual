using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(SplineContainer))]
public class SingleSegmentRightAngle : MonoBehaviour
{
    void Start()
    {
        SplineContainer container = GetComponent<SplineContainer>();
        container.Splines = null;
        Spline spline = container.AddSpline();

        // Dimensions of our L-shape
        float sideLength = 5f;
        
        // Tangent multiplier: 
        // 0.55228f gives a perfect circular arc approximation.
        // 0.75f to 0.95f pulls the curve tighter into a sharper, right-angle corner.
        float handleLength = sideLength * 0.75f; 

        // Knot 0: Start Point (Bottom-Left)
        // Leaving this point, the curve needs to shoot straight UP toward the corner.
        BezierKnot knotStart = new BezierKnot(
            position: new float3(0f, 0f, 0f),
            tangentIn: new float3(0f, 3f, 0f), 
            tangentOut: new float3(0f, handleLength, 0f) // Relative direction: UP
        );

        // Knot 1: End Point (Top-Right)
        // Entering this point, the curve needs to arrive from the LEFT (pointing toward the corner).
        // Since TangentIn is a relative vector pointing backwards from the knot, 
        // pointing backwards from the right towards the left means moving in the negative X direction.
        BezierKnot knotEnd = new BezierKnot(
            position: new float3(sideLength, sideLength, 0f),
            tangentIn: new float3(-handleLength, 0f, 0f), // Relative direction: LEFT
            tangentOut: float3.zero
        );

        // Add the knots using Broken mode so the incoming/outgoing handles don't mirror each other
        spline.Add(knotStart, TangentMode.Broken);
        spline.Add(knotEnd, TangentMode.Broken);
        
        spline.Closed = false;
    }
}
