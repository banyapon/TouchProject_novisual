using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(SplineContainer))]
public class SplineDrawing : MonoBehaviour
{
    void Start()
    {
        SplineContainer container = GetComponent<SplineContainer>();
        container.Splines = new Spline[0];

        float roadLength = 20f;
        float curveLength = 5f;
        float handleLength = curveLength * 0.75f;

        float3 bottom = new float3(0f, -curveLength, 0f);
        float3 left = new float3(-curveLength, 0f, 0f);
        float3 top = new float3(0f, curveLength, 0f);
        float3 right = new float3(curveLength, 0f, 0f);

        // 1A
        Spline road1 = container.AddSpline();
        road1.Add(new BezierKnot(new float3(0f, -roadLength, 0f)), TangentMode.Linear);
        road1.Add(new BezierKnot(bottom), TangentMode.Linear);
        road1.Closed = false;

        // 2B
        Spline road2 = container.AddSpline();
        road2.Add(new BezierKnot(bottom, float3.zero, new float3(0f, handleLength, 0f)), TangentMode.Broken);
        road2.Add(new BezierKnot(left, new float3(handleLength, 0f, 0f), float3.zero), TangentMode.Broken);
        road2.Closed = false;

        //3C
        Spline road3 = container.AddSpline();
        road3.Add(new BezierKnot(bottom, float3.zero, new float3(0f, handleLength, 0f)), TangentMode.Broken);
        road3.Add(new BezierKnot(right, new float3(-handleLength, 0f, 0f), float3.zero), TangentMode.Broken);
        road3.Closed = false;

        //4D
        Spline road4 = container.AddSpline();
        road4.Add(new BezierKnot(new float3(roadLength, 0f, 0f)), TangentMode.Linear);
        road4.Add(new BezierKnot(right), TangentMode.Linear);
        road4.Closed = false;

        //5E
        Spline road5 = container.AddSpline();
        road5.Add(new BezierKnot(new float3(-roadLength, 0f, 0f)), TangentMode.Linear);
        road5.Add(new BezierKnot(left), TangentMode.Linear);
        road5.Closed = false;

        //6F
        Spline road6 = container.AddSpline();
        road6.Add(new BezierKnot(left, float3.zero, new float3(handleLength, 0f, 0f)), TangentMode.Broken);
        road6.Add(new BezierKnot(top, new float3(0f, -handleLength, 0f), float3.zero), TangentMode.Broken);
        road6.Closed = false;

        //7G
        Spline road7 = container.AddSpline();
        road7.Add(new BezierKnot(top, float3.zero, new float3(0f, -handleLength, 0f)), TangentMode.Broken);
        road7.Add(new BezierKnot(right, new float3(-handleLength, 0f, 0f), float3.zero), TangentMode.Broken);
        road7.Closed = false;

        //8H
        Spline road8 = container.AddSpline();
        road8.Add(new BezierKnot(new float3(0f, roadLength, 0f)), TangentMode.Linear);
        road8.Add(new BezierKnot(top), TangentMode.Linear);
        road8.Closed = false;

        //9I
        Spline road9 = container.AddSpline();
        road9.Add(new BezierKnot(bottom), TangentMode.Linear);
        road9.Add(new BezierKnot(top), TangentMode.Linear);
        road9.Closed = false;

        //10J
        Spline road10 = container.AddSpline();
        road10.Add(new BezierKnot(left), TangentMode.Linear);
        road10.Add(new BezierKnot(right), TangentMode.Linear);
        road10.Closed = false;
    }
}
