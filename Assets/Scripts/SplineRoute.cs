using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(SplineContainer))]
public class SplineRoute : MonoBehaviour
{
    void Start()
    {
        SplineContainer container = GetComponent<SplineContainer>();
        container.Splines = null;

        //A
        Spline spline = container.AddSpline();
        BezierKnot knotStart = new BezierKnot(
            position: new float3(0f, -20f, 0f),
            tangentIn: float3.zero,
            tangentOut: new float3(0f, 0f, 0f)
        );
        BezierKnot knotEnd = new BezierKnot(
            position: new float3(0f, -5f, 0f),
            tangentIn: new float3(0f, 0f, 0f),
            tangentOut: float3.zero
        );
        spline.Add(knotStart, TangentMode.Linear);
        spline.Add(knotEnd, TangentMode.Linear);
        spline.Closed = false;

        //B
        spline = container.AddSpline();
        knotStart = new BezierKnot(
            position: new float3(0f, -5f, 0f),
            tangentIn: float3.zero,
            //tangentOut: new float3(0f, 3.75f, 0f)
            tangentOut: new float3(0f, 5.0f, 0f)
        );
        knotEnd = new BezierKnot(
            position: new float3(-5f, 0f, 0f),
            //tangentIn: new float3(3.75f, 0f, 0f),
            tangentIn: new float3(5.0f, 0f, 0f),
            tangentOut: float3.zero
        );
        spline.Add(knotStart, TangentMode.Broken);
        spline.Add(knotEnd, TangentMode.Broken);
        spline.Closed = false;

        //C
        spline = container.AddSpline();
        knotStart = new BezierKnot(
            position: new float3(0f, -5f, 0f),
            tangentIn: float3.zero,
            //tangentOut: new float3(0f, 3.75f, 0f)
            tangentOut: new float3(0f, 5.0f, 0f)
        );
        knotEnd = new BezierKnot(
            position: new float3(5f, 0f, 0f),
            //tangentIn: new float3(-3.75f, 0f, 0f),
            tangentIn: new float3(-5.0f, 0f, 0f),
            tangentOut: float3.zero
        );
        spline.Add(knotStart, TangentMode.Broken);
        spline.Add(knotEnd, TangentMode.Broken);
        spline.Closed = false;

        //D
        spline = container.AddSpline();
        knotStart = new BezierKnot(
            position: new float3(20f, 0f, 0f),
            tangentIn: float3.zero,
            tangentOut: new float3(0f, 0f, 0f)
        );
        knotEnd = new BezierKnot(
            position: new float3(5f, 0f, 0f),
            tangentIn: new float3(0f, 0f, 0f),
            tangentOut: float3.zero
        );
        spline.Add(knotStart, TangentMode.Linear);
        spline.Add(knotEnd, TangentMode.Linear);
        spline.Closed = false;

        //E
        spline = container.AddSpline();
        knotStart = new BezierKnot(
            position: new float3(-20f, 0f, 0f),
            tangentIn: float3.zero,
            tangentOut: new float3(0f, 0f, 0f)
        );
        knotEnd = new BezierKnot(
            position: new float3(-5f, 0f, 0f),
            tangentIn: new float3(0f, 0f, 0f),
            tangentOut: float3.zero
        );
        spline.Add(knotStart, TangentMode.Linear);
        spline.Add(knotEnd, TangentMode.Linear);
        spline.Closed = false;

        //F
        spline = container.AddSpline();
        knotStart = new BezierKnot(
            position: new float3(-5f, 0f, 0f),
            tangentIn: float3.zero,
            //tangentOut: new float3(3.75f, 0f, 0f)
            tangentOut: new float3(5.0f, 0f, 0f)
        );
        knotEnd = new BezierKnot(
            position: new float3(0f, 5f, 0f),
            //tangentIn: new float3(0f, -3.75f, 0f),
            tangentIn: new float3(0f, -5.0f, 0f),
            tangentOut: float3.zero
        );
        spline.Add(knotStart, TangentMode.Broken);
        spline.Add(knotEnd, TangentMode.Broken);
        spline.Closed = false;

        //G
        spline = container.AddSpline();
        knotStart = new BezierKnot(
            position: new float3(0f, 5f, 0f),
            tangentIn: float3.zero,
            //tangentOut: new float3(0f, -3.75f, 0f)
            tangentOut: new float3(0f, -5.0f, 0f)
        );
        knotEnd = new BezierKnot(
            position: new float3(5f, 0f, 0f),
            //tangentIn: new float3(-3.75f, 0f, 0f),
            tangentIn: new float3(-5.0f, 0f, 0f),
            tangentOut: float3.zero
        );
        spline.Add(knotStart, TangentMode.Broken);
        spline.Add(knotEnd, TangentMode.Broken);
        spline.Closed = false;

        //H
        spline = container.AddSpline();
        knotStart = new BezierKnot(
            position: new float3(0f, 20f, 0f),
            tangentIn: float3.zero,
            tangentOut: new float3(0f, 0f, 0f)
        );
        knotEnd = new BezierKnot(
            position: new float3(0f, 5f, 0f),
            tangentIn: new float3(0f, 0f, 0f),
            tangentOut: float3.zero
        );
        spline.Add(knotStart, TangentMode.Linear);
        spline.Add(knotEnd, TangentMode.Linear);
        spline.Closed = false;

        //I
        spline = container.AddSpline();
        knotStart = new BezierKnot(
            position: new float3(0f, -5f, 0f),
            tangentIn: float3.zero,
            tangentOut: new float3(0f, 0f, 0f)
        );
        knotEnd = new BezierKnot(
            position: new float3(0f, 5f, 0f),
            tangentIn: new float3(0f, 0f, 0f),
            tangentOut: float3.zero
        );
        spline.Add(knotStart, TangentMode.Linear);
        spline.Add(knotEnd, TangentMode.Linear);
        spline.Closed = false;

        //J
        spline = container.AddSpline();
        knotStart = new BezierKnot(
            position: new float3(-5f, 0f, 0f),
            tangentIn: float3.zero,
            tangentOut: new float3(0f, 0f, 0f)
        );
        knotEnd = new BezierKnot(
            position: new float3(5f, 0f, 0f),
            tangentIn: new float3(0f, 0f, 0f),
            tangentOut: float3.zero
        );
        spline.Add(knotStart, TangentMode.Linear);
        spline.Add(knotEnd, TangentMode.Linear);
        spline.Closed = false;
    }
}
