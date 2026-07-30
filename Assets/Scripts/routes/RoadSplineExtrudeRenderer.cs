using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(SplineContainer), typeof(SplineExtrude))]
public class RoadSplineExtrudeRenderer : MonoBehaviour
{
    [SerializeField] private SplineContainer movementSource;
    [SerializeField, Min(0.01f)] private float innerScale = 1f;
    [SerializeField, Min(0.01f)] private float outerScale = 2f;
    [SerializeField] private bool rebuildOnStart = true;
    [SerializeField] private bool rebuildInEditor = true;

    private SplineContainer renderContainer;
    private SplineExtrude splineExtrude;

    private void Awake()
    {
        CacheComponents();
    }

    private void Start()
    {
        if (Application.isPlaying && rebuildOnStart)
        {
            RebuildRenderSplines();
        }
    }

    private void OnValidate()
    {
        if (!rebuildInEditor || Application.isPlaying)
        {
            return;
        }

#if UNITY_EDITOR
        EditorApplication.delayCall -= RebuildInEditor;
        EditorApplication.delayCall += RebuildInEditor;
#endif
    }

    [ContextMenu("Rebuild Render Splines")]
    public void RebuildRenderSplines()
    {
        CacheComponents();
        if (movementSource == null || movementSource == renderContainer)
        {
            return;
        }

        List<Spline> renderSplines = new List<Spline>(movementSource.Splines.Count * 2);
        AppendScaledSplines(renderSplines, innerScale);
        AppendScaledSplines(renderSplines, outerScale);
        renderContainer.Splines = renderSplines;

        splineExtrude.enabled = true;
        splineExtrude.Container = renderContainer;
        splineExtrude.Rebuild();
    }

    private void AppendScaledSplines(List<Spline> destination, float scale)
    {
        for (int splineIndex = 0; splineIndex < movementSource.Splines.Count; splineIndex++)
        {
            Spline source = movementSource.Splines[splineIndex];
            Spline copy = new Spline(source.Count, source.Closed);

            for (int knotIndex = 0; knotIndex < source.Count; knotIndex++)
            {
                float3 scaledPosition = source[knotIndex].Position * scale;
                copy.Add(scaledPosition, TangentMode.Linear);
            }

            destination.Add(copy);
        }
    }

    private void CacheComponents()
    {
        renderContainer = GetComponent<SplineContainer>();
        splineExtrude = GetComponent<SplineExtrude>();
    }

#if UNITY_EDITOR
    private void RebuildInEditor()
    {
        if (this == null || Application.isPlaying)
        {
            return;
        }

        RebuildRenderSplines();
    }
#endif
}
