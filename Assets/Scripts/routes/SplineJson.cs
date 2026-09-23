using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(SplineContainer))]
[AddComponentMenu("Roads/Spline Json")]
public class SplineJson : MonoBehaviour
{
    [Serializable]
    public sealed class RoadBinding
    {
        public int id;
        public string label;
        public string kind;
        public int splineIndex;
        public bool bidirectional;
        public SplineJsonConnection[] nodeS;
        public SplineJsonConnection[] nodeE;
        public int defaultRoadS, defaultRoadE;
        public bool hasJunctionControl;
        public Vector3 junctionControl;
    }

    [Serializable]
    public sealed class StraightBinding
    {
        public int fromRoadNo, fromNode, toRoadNo, toNode;
    }

    [Header("JSON Source")]
    [SerializeField] private TextAsset junctionJson;
    [SerializeField] private string resourcePath = "data/junction";
    [Header("JSON coordinates to local X/Z")]
    [SerializeField] private Vector2 jsonOrigin = new Vector2(0f, -200f);
    [SerializeField, Min(0.0001f)] private float unitsPerJsonUnit = 0.1f;
    [SerializeField] private float surfaceHeight = 0.02f;
    [Header("Road and Raised Edges")]
    [SerializeField, Min(0.01f)] private float roadWidth = 2.5f;
    [SerializeField, Min(0f)] private float curbWidth = 0.2f;
    [SerializeField, Min(0f)] private float curbHeight = 0.35f;
    [SerializeField, Min(0.02f)] private float sampleSpacing = 0.25f;
    [SerializeField] private Material roadMaterial;
    [SerializeField] private Material curbMaterial;
    [SerializeField] private bool addMeshColliders = true;
    [Header("Scene View")]
    [SerializeField] private bool showLabels = true;
    [SerializeField] private bool showEndpoints = true;
    [SerializeField] private bool showDirections = true;
    [SerializeField] private bool showConnections = true;

    [SerializeField, HideInInspector] private RoadBinding[] roads = Array.Empty<RoadBinding>();
    [SerializeField, HideInInspector] private StraightBinding[] straights = Array.Empty<StraightBinding>();
    [SerializeField, HideInInspector] private SplineContainer straightContainer;
    [SerializeField, HideInInspector] private GameObject meshRoot;
    [SerializeField, HideInInspector] private MeshFilter surfaceFilter;
    [SerializeField, HideInInspector] private MeshFilter curbFilter;
    [SerializeField, HideInInspector] private string lastMessage;
    [SerializeField, HideInInspector] private bool hasError;

    public TextAsset JsonAsset { get => junctionJson; set => junctionJson = value; }
    public SplineContainer Container => GetComponent<SplineContainer>();
    public SplineContainer StraightContainer => straightContainer;
    public IReadOnlyList<RoadBinding> Roads => roads;
    public IReadOnlyList<StraightBinding> Straights => straights;
    public Mesh RoadMesh => surfaceFilter != null ? surfaceFilter.sharedMesh : null;
    public Mesh CurbMesh => curbFilter != null ? curbFilter.sharedMesh : null;
    public Material RoadMaterial { get => roadMaterial; set => roadMaterial = value; }
    public Material CurbMaterial { get => curbMaterial; set => curbMaterial = value; }
    public string LastMessage => lastMessage;
    public bool HasError => hasError;
    public bool ShowLabels => showLabels;
    public bool ShowEndpoints => showEndpoints;
    public bool ShowDirections => showDirections;
    public bool ShowConnections => showConnections;
    public float RoadWidth => roadWidth;

    private void Reset() { junctionJson = Resources.Load<TextAsset>(resourcePath); }

    private void OnValidate()
    {
        unitsPerJsonUnit = Mathf.Max(0.0001f, unitsPerJsonUnit);
        roadWidth = Mathf.Max(0.01f, roadWidth);
        curbWidth = Mathf.Max(0f, curbWidth);
        curbHeight = Mathf.Max(0f, curbHeight);
        sampleSpacing = Mathf.Max(0.02f, sampleSpacing);
    }

    public Vector3 JsonToLocal(float[] point)
    {
        // JSON y increases down the picture; north is +Z in Unity.
        return new Vector3((point[0] - jsonOrigin.x) * unitsPerJsonUnit, surfaceHeight,
            (jsonOrigin.y - point[1]) * unitsPerJsonUnit);
    }

    public bool Refresh()
    {
        try
        {
            TextAsset asset = junctionJson != null ? junctionJson : Resources.Load<TextAsset>(resourcePath);
            if (asset == null) throw new InvalidOperationException("Assign junction.json or provide Resources/" + resourcePath + ".json.");
            // Parse, validate, and construct everything before changing the existing scene.
            var document = SplineJsonDocument.Parse(asset.text);
            var nextSplines = new List<Spline>();
            var nextRoads = new List<RoadBinding>();
            foreach (var road in document.roads)
            {
                nextRoads.Add(new RoadBinding
                {
                    id = road.id, label = string.IsNullOrEmpty(road.label) ? road.id.ToString() : road.label,
                    kind = road.kind, bidirectional = road.bidirectional, splineIndex = nextSplines.Count,
                    nodeS = road.nodeS, nodeE = road.nodeE, defaultRoadS = road.defaultRoadS, defaultRoadE = road.defaultRoadE,
                    hasJunctionControl = road.kind == "turn" && road.controlPoint != null,
                    junctionControl = road.controlPoint != null ? JsonToLocal(road.controlPoint) : Vector3.zero
                });
                nextSplines.Add(CreateSpline(road.points, road.controlPoint));
            }
            var nextStraightSplines = new List<Spline>();
            var nextStraights = new List<StraightBinding>();
            foreach (var straight in document.straightConnections)
            {
                nextStraightSplines.Add(CreateSpline(straight.points, null));
                nextStraights.Add(new StraightBinding
                {
                    fromRoadNo = straight.fromRoadNo, fromNode = straight.fromNode,
                    toRoadNo = straight.toRoadNo, toNode = straight.toNode
                });
            }
            RecordChange("Refresh JSON Splines");
            if (straightContainer == null)
                straightContainer = AddOwnedComponent<SplineContainer>(CreateChild("Straight Connections (no road IDs)", transform));
            Container.Splines = nextSplines;
            straightContainer.Splines = nextStraightSplines;
            roads = nextRoads.ToArray(); straights = nextStraights.ToArray(); junctionJson = asset;
            if (meshRoot != null) meshRoot.SetActive(false); // Old geometry must not masquerade as the refreshed layout.
            hasError = false;
            lastMessage = $"Refreshed {roads.Length} road splines and {straights.Length} straight connections. Click Extrude to build meshes.";
            return true;
        }
        catch (Exception exception) { return Fail(exception.Message); }
    }

    private Spline CreateSpline(float[][] points, float[] controlPoint)
    {
        var spline = new Spline();
        if (controlPoint != null)
        {
            float3 start = JsonToLocal(points[0]), end = JsonToLocal(points[points.Length - 1]);
            float3 control = JsonToLocal(controlPoint);
            // Exact conversion of the JSON quadratic curve to a cubic Unity spline.
            spline.Add(new BezierKnot(start, float3.zero, (control - start) * (2f / 3f), quaternion.identity), TangentMode.Broken);
            spline.Add(new BezierKnot(end, (control - end) * (2f / 3f), float3.zero, quaternion.identity), TangentMode.Broken);
        }
        else
            foreach (var point in points) spline.Add((float3)JsonToLocal(point), TangentMode.Linear);
        return spline;
    }

    public RoadBinding FindRoad(int id)
    {
        foreach (var road in roads) if (road.id == id) return road;
        return null;
    }

    public bool TryGetTravelPose(int roadId, int enterNode, float progress, out Vector3 position, out Vector3 forward)
    {
        position = default; forward = default;
        var road = FindRoad(roadId);
        if (road == null || (enterNode != 0 && enterNode != 1) || (!road.bidirectional && enterNode == 1) ||
            road.splineIndex >= Container.Splines.Count) return false;
        float t = enterNode == 0 ? Mathf.Clamp01(progress) : 1f - Mathf.Clamp01(progress);
        position = Container.EvaluatePosition(road.splineIndex, t);
        forward = Container.EvaluateTangent(road.splineIndex, t);
        // Linear Unity knots have zero Bezier derivative exactly at their endpoints.
        if (forward.sqrMagnitude < 0.000001f)
            forward = (Vector3)Container.EvaluatePosition(road.splineIndex, Mathf.Min(1f, t + 0.001f)) -
                (Vector3)Container.EvaluatePosition(road.splineIndex, Mathf.Max(0f, t - 0.001f));
        forward = forward.normalized * (enterNode == 0 ? 1f : -1f);
        return true;
    }

    public bool Extrude()
    {
        try
        {
            if (roads.Length == 0 && !Refresh()) return false;
            if (Container.Splines.Count != roads.Length || straightContainer == null || straightContainer.Splines.Count != straights.Length)
                throw new InvalidOperationException("Spline count changed. Click Refresh before Extrude.");
            var paths = new List<Vector3[]>();
            Sample(Container, paths);
            Sample(straightContainer, paths);
            var fillCenters = new Vector3?[paths.Count];
            foreach (var road in roads)
                if (road.hasJunctionControl) fillCenters[road.splineIndex] = road.junctionControl;
            SplineJsonMeshBuilder.Build(paths, roadWidth, curbWidth, curbHeight, out var surface, out var curbs, fillCenters);
            RecordChange("Extrude JSON Roads");
            if (meshRoot == null) meshRoot = CreateChild("Generated Road Mesh", transform);
            if (surfaceFilter == null) surfaceFilter = CreateMeshChild("Road Surface", meshRoot.transform);
            if (curbFilter == null) curbFilter = CreateMeshChild("Raised Curbs", meshRoot.transform);
            if (roadMaterial == null) roadMaterial = MakeMaterial("Road Surface", new Color(0.16f, 0.18f, 0.21f));
            if (curbMaterial == null) curbMaterial = MakeMaterial("Raised Curbs", new Color(0.7f, 0.73f, 0.77f));
            SetMeshes(surface, curbs);
            meshRoot.SetActive(true);
            hasError = false;
            lastMessage = $"Extruded {roads.Length} roads + {straights.Length} connections; curbs {curbHeight:0.##} units high.";
            return true;
        }
        catch (Exception exception) { return Fail(exception.Message); }
    }

    private void Sample(SplineContainer container, List<Vector3[]> destination)
    {
        foreach (var spline in container.Splines)
        {
            float length = spline.GetLength();
            if (float.IsNaN(length) || float.IsInfinity(length) || length < 0.0001f)
                throw new InvalidOperationException("A spline has no usable length. Click Refresh to restore it from JSON.");
            int count = Mathf.Max(1, Mathf.CeilToInt(length / sampleSpacing));
            if (count > 10000) throw new InvalidOperationException("Too many mesh samples. Increase Sample Spacing or reduce coordinate scale.");
            var points = new Vector3[count + 1];
            for (int i = 0; i <= count; i++)
            {
                Vector3 point = spline.EvaluatePosition(i / (float)count);
                points[i] = transform.InverseTransformPoint(container.transform.TransformPoint(point));
            }
            destination.Add(points);
        }
    }

    public void SetMeshes(Mesh surface, Mesh curbs)
    {
        ApplyMesh(surfaceFilter, surface, roadMaterial);
        ApplyMesh(curbFilter, curbs, curbMaterial);
    }

    private void ApplyMesh(MeshFilter filter, Mesh mesh, Material material)
    {
        if (filter == null) return;
        filter.sharedMesh = mesh;
        filter.GetComponent<MeshRenderer>().sharedMaterial = material;
        var collider = filter.GetComponent<MeshCollider>();
        if (addMeshColliders && collider == null) collider = AddOwnedComponent<MeshCollider>(filter.gameObject);
        if (collider != null)
        {
            collider.sharedMesh = null;
            collider.enabled = addMeshColliders && mesh != null && mesh.vertexCount > 0;
            if (collider.enabled) collider.sharedMesh = mesh;
        }
    }

    private MeshFilter CreateMeshChild(string name, Transform parent)
    {
        var child = CreateChild(name, parent);
        AddOwnedComponent<MeshRenderer>(child);
        return AddOwnedComponent<MeshFilter>(child);
    }

    private GameObject CreateChild(string name, Transform parent)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.layer = gameObject.layer;
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(child, "Create JSON Road Object");
#endif
        return child;
    }

    private static T AddOwnedComponent<T>(GameObject target) where T : Component
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return Undo.AddComponent<T>(target);
#endif
        return target.AddComponent<T>();
    }

    private static Material MakeMaterial(string name, Color color)
    {
        var shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
            ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Standard");
        return new Material(shader) { name = name, color = color };
    }

    private void RecordChange(string name)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RegisterFullObjectHierarchyUndo(gameObject, name);
#endif
    }

    private bool Fail(string message)
    {
        hasError = true; lastMessage = message;
        Debug.LogError("SplineJson: " + message, this);
        return false;
    }
}
