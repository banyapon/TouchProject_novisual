using System.Collections.Generic;
using System.Reflection;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.Splines.ExtrusionShapes;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(SplineContainer), typeof(SplineExtrude))]
public class SplineCreator : MonoBehaviour
{
    [Header("Auto Route")]
    [SerializeField] private bool rebuildOnStart = true;
    [SerializeField] private bool rebuildInEditor = true;
    [SerializeField] private float routeScale = 1f;
    [SerializeField] private bool roundCorners = true;
    [SerializeField] private float cornerRadius = 8f;
    [SerializeField, Range(1, 16)] private int cornerSegments = 8;

    [Header("Road Extrude")]
    [SerializeField] private bool createRoadMesh = true;
    [SerializeField] private float laneWidth = 3f;
    [SerializeField] private int laneCount = 2;
    [SerializeField] private float segmentsPerUnit = 1f;
    [SerializeField] private Material roadMaterial;
    [SerializeField] private string roadLayerName = "Road";
    [SerializeField] private bool addRoadCollider = true;

    private SplineContainer splineContainer;
    private SplineExtrude splineExtrude;

    private const float RoadShapeWidth = 1.2f;
    private static readonly FieldInfo ShapeField = typeof(SplineExtrude).GetField(
        "m_Shape",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly Vector3[][] RouteSegments =
    {
        // Spline 0: main road with rounded bends.
        new[]
        {
            Point(0f, 0f, 0f),
            Point(0f, 0f, 100f),
            Point(-50f, 0f, 100f),
            Point(-50f, 0f, 200f),
            Point(100f, 0f, 200f),
            Point(100f, 0f, 100f)
        },

        // Spline 1: junction branch to the right side.
        new[] { Point(0f, 0f, 100f), Point(100f, 0f, 100f) },
    };

    private void Awake()
    {
        CacheComponents();
    }

    private void Start()
    {
        if (Application.isPlaying && rebuildOnStart)
        {
            RebuildRoute();
        }
    }

    private void Reset()
    {
        RebuildRoute();
    }

    private void OnValidate()
    {
        if (!rebuildInEditor || Application.isPlaying)
        {
            return;
        }

#if UNITY_EDITOR
        EditorApplication.delayCall -= RebuildRouteInEditor;
        EditorApplication.delayCall += RebuildRouteInEditor;
#endif
    }

    [ContextMenu("Rebuild Route")]
    public void RebuildRoute()
    {
        CacheComponents();

        List<Spline> splines = new List<Spline>(RouteSegments.Length);
        for (int i = 0; i < RouteSegments.Length; i++)
        {
            splines.Add(CreateSpline(RouteSegments[i]));
        }

        splineContainer.Splines = splines;
        SetupRoadExtrude();
    }

    private Spline CreateSpline(IReadOnlyList<Vector3> points)
    {
        List<Vector3> renderPoints = BuildRenderPoints(points);
        Spline spline = new Spline(renderPoints.Count, false);

        for (int i = 0; i < renderPoints.Count; i++)
        {
            Vector3 scaledPoint = renderPoints[i] * routeScale;
            spline.Add(ToFloat3(scaledPoint), TangentMode.Linear);
        }

        return spline;
    }

    private List<Vector3> BuildRenderPoints(IReadOnlyList<Vector3> points)
    {
        List<Vector3> renderPoints = new List<Vector3>();
        if (!roundCorners || points.Count < 3 || cornerRadius <= 0f)
        {
            for (int i = 0; i < points.Count; i++)
            {
                renderPoints.Add(points[i]);
            }

            return renderPoints;
        }

        renderPoints.Add(points[0]);

        for (int i = 1; i < points.Count - 1; i++)
        {
            AddRoundedCorner(renderPoints, points[i - 1], points[i], points[i + 1]);
        }

        renderPoints.Add(points[points.Count - 1]);
        return renderPoints;
    }

    private void AddRoundedCorner(List<Vector3> renderPoints, Vector3 previous, Vector3 corner, Vector3 next)
    {
        Vector3 incoming = corner - previous;
        Vector3 outgoing = next - corner;
        incoming.y = 0f;
        outgoing.y = 0f;

        float incomingLength = incoming.magnitude;
        float outgoingLength = outgoing.magnitude;
        if (incomingLength <= Mathf.Epsilon || outgoingLength <= Mathf.Epsilon)
        {
            renderPoints.Add(corner);
            return;
        }

        Vector3 incomingDirection = incoming / incomingLength;
        Vector3 outgoingDirection = outgoing / outgoingLength;
        float turnAngle = Vector3.Angle(incomingDirection, outgoingDirection);
        if (turnAngle <= 1f || turnAngle >= 179f)
        {
            renderPoints.Add(corner);
            return;
        }

        float trimDistance = Mathf.Min(cornerRadius, incomingLength * 0.45f, outgoingLength * 0.45f);
        Vector3 arcStart = corner - incomingDirection * trimDistance;
        Vector3 arcEnd = corner + outgoingDirection * trimDistance;

        if (renderPoints.Count == 0 || Vector3.Distance(renderPoints[renderPoints.Count - 1], arcStart) > 0.001f)
        {
            renderPoints.Add(arcStart);
        }

        int segmentCount = Mathf.Max(1, cornerSegments);
        for (int segment = 1; segment <= segmentCount; segment++)
        {
            float t = segment / (float)segmentCount;
            Vector3 point = QuadraticBezier(arcStart, corner, arcEnd, t);
            renderPoints.Add(point);
        }
    }

    private static Vector3 QuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
    {
        float oneMinusT = 1f - t;
        return oneMinusT * oneMinusT * start +
               2f * oneMinusT * t * control +
               t * t * end;
    }

    private static Vector3 Point(float x, float y, float z)
    {
        return new Vector3(x, y, z);
    }

    private static float3 ToFloat3(Vector3 value)
    {
        return new float3(value.x, value.y, value.z);
    }

    private void CacheComponents()
    {
        splineContainer = GetOrAddComponent<SplineContainer>();
        splineExtrude = GetOrAddComponent<SplineExtrude>();
    }

    private void SetupRoadExtrude()
    {
        if (splineExtrude == null)
        {
            return;
        }

        splineExtrude.enabled = createRoadMesh;
        splineExtrude.Container = splineContainer;
        splineExtrude.RebuildOnSplineChange = true;
        splineExtrude.RebuildFrequency = 30;
        splineExtrude.SegmentsPerUnit = segmentsPerUnit;
        splineExtrude.Capped = true;
        splineExtrude.Range = new Vector2(0f, 1f);

        float roadWidth = Mathf.Max(0.1f, laneWidth * Mathf.Max(1, laneCount));
        splineExtrude.Radius = roadWidth / RoadShapeWidth;
        SetRoadExtrudeShape(splineExtrude);

        if (TryGetComponent(out MeshRenderer meshRenderer) && roadMaterial != null)
        {
            meshRenderer.sharedMaterial = roadMaterial;
        }

        SetRoadLayer();
        SetupRoadCollider();
        splineExtrude.Rebuild();
    }

    private void SetRoadLayer()
    {
        EnsureRoadLayerExists();

        int roadLayer = LayerMask.NameToLayer(roadLayerName);
        if (roadLayer < 0)
        {
            Debug.LogWarning($"Layer '{roadLayerName}' was not found. Create it in Project Settings > Tags and Layers, then rebuild the route.", this);
            return;
        }

        gameObject.layer = roadLayer;
    }

    private void EnsureRoadLayerExists()
    {
#if UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(roadLayerName) || LayerMask.NameToLayer(roadLayerName) >= 0)
        {
            return;
        }

        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(layer.stringValue))
            {
                continue;
            }

            layer.stringValue = roadLayerName;
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            return;
        }

        Debug.LogWarning($"Could not create layer '{roadLayerName}' because all user layers are full.", this);
#endif
    }

    private void SetupRoadCollider()
    {
        if (!addRoadCollider)
        {
            return;
        }

        GetOrAddComponent<MeshCollider>();
    }

    private static void SetRoadExtrudeShape(SplineExtrude extrude)
    {
        if (ShapeField == null)
        {
            return;
        }

        ShapeField.SetValue(extrude, new Road());
    }

    private T GetOrAddComponent<T>() where T : Component
    {
        if (TryGetComponent(out T component))
        {
            return component;
        }

        return gameObject.AddComponent<T>();
    }

#if UNITY_EDITOR
    private void RebuildRouteInEditor()
    {
        if (this == null || Application.isPlaying)
        {
            return;
        }

        RebuildRoute();
    }
#endif
}
