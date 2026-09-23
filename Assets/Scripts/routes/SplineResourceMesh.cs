using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Resource road models over the same JSON splines used by splinemovement.</summary>
[ExecuteAlways, DefaultExecutionOrder(-100), DisallowMultipleComponent, RequireComponent(typeof(UnityEngine.Splines.SplineContainer))]
public sealed class SplineResourceMesh : MonoBehaviour
{
    [Header("JSON")]
    [SerializeField] private TextAsset junctionJson;
    [SerializeField] private string resourcePath = "data/";
    [Header("Resource models ")]
    [SerializeField] private string intersectionResource = "roads/intersectionRoad";
    [SerializeField] private string straightResource = "roads/straightRoad";
    [SerializeField] private string curvedResource = "roads/curvedRoad";
    [Tooltip("Used when no intersection defines the model scale.")]
    [SerializeField, Min(.001f)] private float modelScale = .6529172f;
    [Tooltip("Port centres in curvedRoad coordinates. Entry faces -Z; exit faces -X.")]
    [SerializeField] private Vector3 curveEntry = new Vector3(0, 0, 14.09059f);
    [SerializeField] private Vector3 curveExit = new Vector3(-21.7139f, 0, -12.51552f);
    [SerializeField] private bool addMeshColliders = true;
    [SerializeField, HideInInspector] private GameObject generatedRoot;
    [SerializeField, HideInInspector] private string lastMessage;
    [SerializeField, HideInInspector] private bool hasError;
    public TextAsset JsonAsset { get => junctionJson; set => junctionJson = value; }
    public SplineJson Source => GetComponent<SplineJson>();
    public GameObject GeneratedRoot => generatedRoot;
    public string LastMessage => lastMessage;
    public bool HasError => hasError;

    private sealed class Model { public GameObject prefab; public Bounds bounds; }
    private sealed class Tile
    {
        public Model model;
        public string name;
        public Vector3 position, scale;
        public Quaternion rotation;
    }
    private sealed class Intersection
    {
        public Vector3 center, forward;
        public float halfX, halfZ;
        public bool Contains(Vector3 point)
        {
            Vector3 delta = Quaternion.Inverse(Quaternion.LookRotation(forward, Vector3.up)) * (point - center);
            return Mathf.Abs(delta.x) <= halfX + .001f && Mathf.Abs(delta.z) <= halfZ + .001f;
        }
    }
    private void Awake() { if (Application.isPlaying && (generatedRoot == null || Source == null)) Refresh(); }

    [ContextMenu("Refresh Resource Roads")]
    public bool Refresh()
    {
        GameObject nextRoot = null;
        try
        {
            TextAsset asset = junctionJson != null ? junctionJson : Resources.Load<TextAsset>(resourcePath);
            if (asset == null) throw new InvalidOperationException("Missing Resources/" + resourcePath + ".json");
            var document = SplineJsonDocument.Parse(asset.text);
            EnsureSource();
            var straight = LoadModel(straightResource);
            var junctions = FindIntersections(document);
            Model intersectionModel = junctions.Count > 0 ? LoadModel(intersectionResource) : null;
            var tiles = new List<Tile>();
            float scale = modelScale;
            foreach (var junction in junctions)
            {
                float sx = junction.halfX * 2 / intersectionModel.bounds.size.x;
                float sz = junction.halfZ * 2 / intersectionModel.bounds.size.z;
                if (Mathf.Abs(sx - sz) > .005f || (tiles.Count > 0 && Mathf.Abs(scale - sx) > .005f))
                    throw new InvalidOperationException("Intersection models require equally sized perpendicular mainRoad spans.");
                scale = (sx + sz) * .5f;
                AddCentered(tiles, intersectionModel, "Intersection " + (tiles.Count + 1), junction.center,
                    Quaternion.LookRotation(junction.forward, Vector3.up), Vector3.one * scale);
            }
            Model curved = null;
            foreach (var road in document.roads)
            {
                Vector3 start = Source.JsonToLocal(road.points[0]);
                Vector3 end = Source.JsonToLocal(road.points[road.points.Length - 1]);
                if (road.controlPoint != null)
                {
                    Vector3 control = Source.JsonToLocal(road.controlPoint);
                    // Internal turning lanes are already covered by the intersection model.
                    if (junctions.Exists(j => j.Contains(start) && j.Contains(control) && j.Contains(end))) continue;
                    if (curved == null) curved = LoadModel(curvedResource);
                    AddCurve(tiles, curved, road.id, start, control, end, scale);
                }
                else AddPolyline(tiles, straight, "Road " + road.id, road.points, junctions, scale);
            }
            for (int i = 0; i < document.straightConnections.Length; i++)
                AddPolyline(tiles, straight, "Main Road " + i, document.straightConnections[i].points, junctions, scale);

            // Plan and validate before replacing the visible hierarchy.
            nextRoot = new GameObject("Resource Road Models");
            nextRoot.SetActive(false);
            foreach (var tile in tiles) CreateTile(nextRoot.transform, tile);
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RegisterFullObjectHierarchyUndo(gameObject, "Refresh Resource Roads");
#endif
            Source.JsonAsset = asset;
            if (!Source.Refresh()) throw new InvalidOperationException(Source.LastMessage);
            if (generatedRoot != null) DestroyOwned(generatedRoot);
            nextRoot.transform.SetParent(transform, false);
            generatedRoot = nextRoot;
            nextRoot = null;
            generatedRoot.SetActive(true);
            junctionJson = asset;
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(generatedRoot, "Create Resource Roads");
#endif
            hasError = false;
            lastMessage = $"Built {tiles.Count} resource models, {junctions.Count} intersections, {document.roads.Length} movement roads.";
            return true;
        }
        catch (Exception exception)
        {
            if (nextRoot != null) DestroyOwned(nextRoot);
            hasError = true; lastMessage = exception.Message;
            Debug.LogError("SplineResourceMesh: " + exception, this);
            return false;
        }
    }

    private void EnsureSource()
    {
        // RequireComponent only runs when adding a component. Older scenes may already
        // contain the former stub without its newly declared dependencies.
        if (GetComponent<UnityEngine.Splines.SplineContainer>() == null)
            AddRequiredComponent<UnityEngine.Splines.SplineContainer>();
        if (Source == null) AddRequiredComponent<SplineJson>();
    }

    private T AddRequiredComponent<T>() where T : Component
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return Undo.AddComponent<T>(gameObject);
#endif
        return gameObject.AddComponent<T>();
    }

    private Model LoadModel(string path)
    {
        var prefab = Resources.Load<GameObject>(path);
        // Existing project models live in Resources/road; prefer the requested roads path.
        if (prefab == null && path.StartsWith("roads/", StringComparison.Ordinal))
            prefab = Resources.Load<GameObject>("road/" + path.Substring(6));
        if (prefab == null) throw new InvalidOperationException("Missing road model: Resources/" + path);
        bool found = false;
        Bounds bounds = default;
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            var b = filter.sharedMesh.bounds;
            Matrix4x4 matrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z)));
                        if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                        else bounds.Encapsulate(point);
                    }
        }
        if (!found || bounds.size.x < .001f || bounds.size.z < .001f)
            throw new InvalidOperationException("Road model has no usable X/Z mesh bounds: " + path);
        return new Model { prefab = prefab, bounds = bounds };
    }

    private List<Intersection> FindIntersections(SplineJsonDocument document)
    {
        var result = new List<Intersection>();
        var spans = document.straightConnections;
        for (int a = 0; a < spans.Length; a++)
            for (int b = a + 1; b < spans.Length; b++)
                for (int i = 1; i < spans[a].points.Length; i++)
                    for (int k = 1; k < spans[b].points.Length; k++)
                    {
                        Vector3 p = Source.JsonToLocal(spans[a].points[i - 1]);
                        Vector3 q = Source.JsonToLocal(spans[a].points[i]);
                        Vector3 r = Source.JsonToLocal(spans[b].points[k - 1]);
                        Vector3 s = Source.JsonToLocal(spans[b].points[k]);
                        Vector3 d = q - p, e = s - r;
                        float cross = Cross(d, e);
                        if (Mathf.Abs(cross) < .0001f) continue;
                        float t = Cross(r - p, e) / cross, u = Cross(r - p, d) / cross;
                        if (t <= .0001f || t >= .9999f || u <= .0001f || u >= .9999f) continue;
                        if (Mathf.Abs(Vector3.Dot(d.normalized, e.normalized)) > .01f)
                            throw new InvalidOperationException("intersectionRoad supports perpendicular crossings only.");
                        Vector3 center = p + d * t;
                        if (result.Exists(j => (j.center - center).sqrMagnitude < .0001f)) continue;
                        result.Add(new Intersection
                        {
                            center = center, forward = d.normalized,
                            halfZ = Mathf.Min(t, 1 - t) * d.magnitude,
                            halfX = Mathf.Min(u, 1 - u) * e.magnitude
                        });
                    }
        return result;
    }
    private static float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;

    private void AddPolyline(List<Tile> tiles, Model model, string name, float[][] points, List<Intersection> junctions, float scale)
    {
        for (int i = 1; i < points.Length; i++)
        {
            Vector3 start = Source.JsonToLocal(points[i - 1]), end = Source.JsonToLocal(points[i]);
            var intervals = new List<Vector2> { new Vector2(0, 1) };
            foreach (var junction in junctions)
            {
                if (!Clip(junction, start, end, out float lo, out float hi)) continue;
                var next = new List<Vector2>();
                foreach (var range in intervals)
                {
                    if (hi <= range.x || lo >= range.y) { next.Add(range); continue; }
                    if (lo > range.x) next.Add(new Vector2(range.x, lo));
                    if (hi < range.y) next.Add(new Vector2(hi, range.y));
                }
                intervals = next;
            }
            foreach (var interval in intervals)
            {
                Vector3 a = Vector3.Lerp(start, end, interval.x), b = Vector3.Lerp(start, end, interval.y);
                float length = Vector3.Distance(a, b);
                if (length < .001f) continue;
                AddCentered(tiles, model, name + " Straight", (a + b) * .5f,
                    Quaternion.LookRotation(b - a, Vector3.up), new Vector3(scale, scale, length / model.bounds.size.z));
            }
        }
    }
    private static bool Clip(Intersection junction, Vector3 start, Vector3 end, out float lo, out float hi)
    {
        var inverse = Quaternion.Inverse(Quaternion.LookRotation(junction.forward, Vector3.up));
        Vector3 p = inverse * (start - junction.center), d = inverse * (end - start);
        lo = 0; hi = 1;
        return ClipAxis(p.x, d.x, junction.halfX, ref lo, ref hi) && ClipAxis(p.z, d.z, junction.halfZ, ref lo, ref hi) && hi - lo > .00001f;
    }
    private static bool ClipAxis(float p, float d, float extent, ref float lo, ref float hi)
    {
        if (Mathf.Abs(d) < .00001f) return Mathf.Abs(p) <= extent;
        float a = (-extent - p) / d, b = (extent - p) / d;
        lo = Mathf.Max(lo, Mathf.Min(a, b)); hi = Mathf.Min(hi, Mathf.Max(a, b));
        return hi >= lo;
    }
    private static void AddCentered(List<Tile> tiles, Model model, string name, Vector3 center, Quaternion rotation, Vector3 scale)
    {
        var anchor = new Vector3(model.bounds.center.x, model.bounds.min.y, model.bounds.center.z);
        tiles.Add(new Tile { model = model, name = name, rotation = rotation, scale = scale,
            position = center - rotation * Vector3.Scale(anchor, scale) });
    }
    private void AddCurve(List<Tile> tiles, Model model, int id, Vector3 start, Vector3 control, Vector3 end, float scale)
    {
        Vector3 incoming = control - start, outgoing = end - control;
        if (incoming.sqrMagnitude < .001f || outgoing.sqrMagnitude < .001f ||
            Mathf.Abs(Vector3.Dot(incoming.normalized, outgoing.normalized)) > .01f)
            throw new InvalidOperationException("Road " + id + " requires a right-angle controlPoint for curvedRoad.");
        if (Mathf.Abs(curveEntry.x - curveExit.x) < .001f || Mathf.Abs(curveEntry.z - curveExit.z) < .001f)
            throw new InvalidOperationException("Configure two distinct curvedRoad ports.");
        Quaternion rotation = Quaternion.LookRotation(-incoming, Vector3.up);
        float sx = Vector3.Dot(outgoing, rotation * Vector3.right) / (curveExit.x - curveEntry.x);
        float sz = incoming.magnitude / (curveEntry.z - curveExit.z);
        var size = new Vector3(sx, scale, sz);
        tiles.Add(new Tile { model = model, name = "Road " + id + " Curve", rotation = rotation, scale = size,
            position = start - rotation * Vector3.Scale(curveEntry, size) });
    }
    private void CreateTile(Transform parent, Tile tile)
    {
        var holder = new GameObject(tile.name);
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = tile.position;
        holder.transform.localRotation = tile.rotation;
        holder.transform.localScale = tile.scale;
        GameObject model;
#if UNITY_EDITOR
        if (!Application.isPlaying) model = (GameObject)PrefabUtility.InstantiatePrefab(tile.model.prefab, holder.transform);
        else
#endif
            model = Instantiate(tile.model.prefab, holder.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        if (addMeshColliders)
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && filter.GetComponent<Collider>() == null)
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
    }
    private static void DestroyOwned(GameObject obj)
    {
        obj.SetActive(false);
#if UNITY_EDITOR
        if (!Application.isPlaying) { Undo.DestroyObjectImmediate(obj); return; }
#endif
        Destroy(obj);
    }
}
