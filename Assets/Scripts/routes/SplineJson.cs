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
    [SerializeField, Min(0.02f)] private float sampleSpacing = 0.1f;
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

    private void Reset()
    {
        junctionJson = Resources.Load<TextAsset>(resourcePath);
    }

    private void OnValidate()
    {
        unitsPerJsonUnit = Mathf.Max(0.0001f, unitsPerJsonUnit);
        roadWidth = Mathf.Max(0.01f, roadWidth);
        curbWidth = Mathf.Max(0f, curbWidth);
        curbHeight = Mathf.Max(0f, curbHeight);
        // ห่างเกิน 0.1 โค้งจะเริ่มเห็นเป็นเหลี่ยม
        sampleSpacing = Mathf.Clamp(sampleSpacing, 0.02f, 0.1f);
    }

    public Vector3 JsonToLocal(float[] point)
    {
        // JSON ลงล่างเป็นบวก แต่ในฉากเราใช้ Z ขึ้นบน เลยต้องกลับ - ตรงนี้
        float x = (point[0] - jsonOrigin.x) * unitsPerJsonUnit;
        float z = (jsonOrigin.y - point[1]) * unitsPerJsonUnit;
        return new Vector3(x, surfaceHeight, z);
    }

    public bool Refresh()
    {
        try
        {
            TextAsset asset = junctionJson != null
                ? junctionJson
                : Resources.Load<TextAsset>(resourcePath);

            if (asset == null)
                throw new InvalidOperationException("หาไฟล์ JSON ไม่เจอ: Resources/" + resourcePath + ".json");

            SplineJsonDocument document = SplineJsonDocument.Parse(asset.text);
            var roadSplines = new List<Spline>();
            var roadBindings = new List<RoadBinding>();

            foreach (SplineJsonRoad road in document.roads)
            {
                roadBindings.Add(CreateRoadBinding(road, roadSplines.Count));
                roadSplines.Add(CreateSpline(road.points, road.controlPoint));
            }

            var straightSplines = new List<Spline>();
            var straightBindings = new List<StraightBinding>();

            foreach (SplineJsonStraight straight in document.straightConnections)
            {
                straightSplines.Add(CreateSpline(straight.points, null));
                straightBindings.Add(new StraightBinding
                {
                    fromRoadNo = straight.fromRoadNo,
                    fromNode = straight.fromNode,
                    toRoadNo = straight.toRoadNo,
                    toNode = straight.toNode
                });
            }

            RecordChange("Refresh JSON Splines");
            EnsureStraightContainer();

            Container.Splines = roadSplines;
            straightContainer.Splines = straightSplines;
            roads = roadBindings.ToArray();
            straights = straightBindings.ToArray();
            junctionJson = asset;

            //เปลี่ยนเส้นแล้ว ซ่อน mesh เก่าไว้ก่อน มี Extrude ค่อยสร้างใหม่
            if (meshRoot != null) meshRoot.SetActive(false);

            hasError = false;
            lastMessage = $"โหลดถนน {roads.Length} เส้น และทางตรงกลางแยก {straights.Length} เส้นแล้ว";
            return true;
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    private RoadBinding CreateRoadBinding(SplineJsonRoad road, int splineIndex)
    {
        return new RoadBinding
        {
            id = road.id,
            label = string.IsNullOrEmpty(road.label) ? road.id.ToString() : road.label,
            kind = road.kind,
            splineIndex = splineIndex,
            bidirectional = road.bidirectional,
            nodeS = road.nodeS,
            nodeE = road.nodeE,
            defaultRoadS = road.defaultRoadS,
            defaultRoadE = road.defaultRoadE,
            hasJunctionControl = road.kind == "turn" && road.controlPoint != null,
            junctionControl = road.controlPoint != null ? JsonToLocal(road.controlPoint) : Vector3.zero
        };
    }

    private Spline CreateSpline(float[][] points, float[] controlPoint)
    {
        // มี control หรือมี 3 จุด = ถนนโค้ง ส่วน 2 จุด = ถนนตรง
        if (controlPoint != null || points.Length == 3)
        {
            float[] curveControl = controlPoint ?? points[1];
            return CreateCurve(points[0], curveControl, points[points.Length - 1]);
        }

        var spline = new Spline();
        foreach (float[] point in points)
            spline.Add((float3)JsonToLocal(point), TangentMode.Linear);
        return spline;
    }

    private Spline CreateCurve(float[] startPoint, float[] controlPoint, float[] endPoint)
    {
        float3 start = JsonToLocal(startPoint);
        float3 control = JsonToLocal(controlPoint);
        float3 end = JsonToLocal(endPoint);

        // 0.9 ทำให้เลี้ยวกว้าง ไม่หักเป็นมุมแหลม
        var spline = new Spline();
        spline.Add(new BezierKnot(start, float3.zero, (control - start) * 1.1f, quaternion.identity), TangentMode.Broken);
        spline.Add(new BezierKnot(end, (control - end) * 1.1f, float3.zero, quaternion.identity), TangentMode.Broken);
        return spline;
    }

    public RoadBinding FindRoad(int id)
    {
        foreach (RoadBinding road in roads)
            if (road.id == id) return road;
        return null;
    }

    public bool TryGetTravelPose(int roadId, int enterNode, float progress,
        out Vector3 position, out Vector3 forward)
    {
        position = default;
        forward = default;
        RoadBinding road = FindRoad(roadId);

        if (road == null || (enterNode != 0 && enterNode != 1)) return false;
        if (!road.bidirectional && enterNode == 1) return false;
        if (road.splineIndex >= Container.Splines.Count) return false;

        float t = enterNode == 0 ? Mathf.Clamp01(progress) : 1f - Mathf.Clamp01(progress);
        position = Container.EvaluatePosition(road.splineIndex, t);
        forward = Container.EvaluateTangent(road.splineIndex, t);

        // เส้นตรงบางทีปลายสุดให้ tangent เป็นศูนย์ เลยขยับมาวัดใกล้ๆ แทน
        if (forward.sqrMagnitude < 0.000001f)
        {
            Vector3 after = Container.EvaluatePosition(road.splineIndex, Mathf.Min(1f, t + 0.001f));
            Vector3 before = Container.EvaluatePosition(road.splineIndex, Mathf.Max(0f, t - 0.001f));
            forward = after - before;
        }

        forward = forward.normalized * (enterNode == 0 ? 1f : -1f);
        return true;
    }

    public bool Extrude()
    {
        try
        {
            if (roads.Length == 0 && !Refresh()) return false;
            ValidateSplineCount();

            List<Vector3[]> paths = SampleAllSplines();
            Vector3?[] fillCenters = FindJunctionCenters(paths.Count);

            // แยกชัดๆ ผิวถนนทำส่วนหนึ่ง ขอบถนนทำอีกส่วนหนึ่ง
            Mesh roadMesh = BuildRoadMesh(paths, fillCenters);
            Mesh edgeMesh = ExtrudeRoadEdges(paths, fillCenters);

            RecordChange("Extrude JSON Roads");
            EnsureMeshObjects();
            EnsureMaterials();
            SetMeshes(roadMesh, edgeMesh);
            meshRoot.SetActive(true);

            hasError = false;
            lastMessage = $"สร้าง Road mesh {roads.Length} เส้น และขอบสูง {curbHeight:0.##} เรียบร้อย";
            return true;
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    private Mesh BuildRoadMesh(IReadOnlyList<Vector3[]> paths, IReadOnlyList<Vector3?> fillCenters)
    {
        // ฟังก์ชันนี้ทำเฉพาะพื้นถนน จะได้แก้เรื่องผิวโดยไม่ไปยุ่งกับขอบ
        return SplineJsonMeshBuilder.BuildRoad(paths, roadWidth, fillCenters);
    }

    private Mesh ExtrudeRoadEdges(IReadOnlyList<Vector3[]> paths, IReadOnlyList<Vector3?> fillCenters)
    {
        // ฟังก์ชันนี้ทำเฉพาะขอบ Extrude ถ้า width/height เป็นศูนย์ก็ได้ mesh ว่าง
        return SplineJsonMeshBuilder.BuildEdges(paths, roadWidth, curbWidth, curbHeight, fillCenters);
    }

    private List<Vector3[]> SampleAllSplines()
    {
        var paths = new List<Vector3[]>();
        Sample(Container, paths);
        Sample(straightContainer, paths);
        return paths;
    }

    private void Sample(SplineContainer container, List<Vector3[]> destination)
    {
        foreach (Spline spline in container.Splines)
        {
            float length = spline.GetLength();
            if (float.IsNaN(length) || float.IsInfinity(length) || length < 0.0001f)
                throw new InvalidOperationException("มี spline สั้นเกินไปหรือใช้งานไม่ได้ ลองกด Refresh ใหม่");

            // ระยะ sample ยิ่งน้อย โค้งยิ่งเนียน แต่จำนวน vertex ก็เพิ่มตาม
            int count = Mathf.Max(1, Mathf.CeilToInt(length / sampleSpacing));
            if (count > 10000)
                throw new InvalidOperationException("จุด mesh เยอะเกินไป เพิ่ม Sample Spacing ขึ้นอีกนิด");

            var points = new Vector3[count + 1];
            for (int i = 0; i <= count; i++)
            {
                Vector3 point = spline.EvaluatePosition(i / (float)count);
                points[i] = transform.InverseTransformPoint(container.transform.TransformPoint(point));
            }
            destination.Add(points);
        }
    }

    private Vector3?[] FindJunctionCenters(int pathCount)
    {
        var centers = new Vector3?[pathCount];
        foreach (RoadBinding road in roads)
            if (road.hasJunctionControl) centers[road.splineIndex] = road.junctionControl;
        return centers;
    }

    private void ValidateSplineCount()
    {
        bool roadCountWrong = Container.Splines.Count != roads.Length;
        bool straightCountWrong = straightContainer == null || straightContainer.Splines.Count != straights.Length;
        if (roadCountWrong || straightCountWrong)
            throw new InvalidOperationException("จำนวน spline ไม่ตรงกับ JSON กด Refresh ก่อน Extrude");
    }

    private void EnsureStraightContainer()
    {
        if (straightContainer != null) return;
        GameObject child = CreateChild("Straight Connections (no road IDs)", transform);
        straightContainer = AddOwnedComponent<SplineContainer>(child);
    }

    private void EnsureMeshObjects()
    {
        if (meshRoot == null) meshRoot = CreateChild("Generated Road Mesh", transform);
        if (surfaceFilter == null) surfaceFilter = CreateMeshChild("Road", meshRoot.transform);
        if (curbFilter == null) curbFilter = CreateMeshChild("Road Edges", meshRoot.transform);
    }

    private void EnsureMaterials()
    {
        if (roadMaterial == null)
            roadMaterial = MakeMaterial("Road", new Color(0.16f, 0.18f, 0.21f));
        if (curbMaterial == null)
            curbMaterial = MakeMaterial("Road Edge", new Color(0.7f, 0.73f, 0.77f));
    }

    public void SetMeshes(Mesh roadMesh, Mesh edgeMesh)
    {
        ApplyMesh(surfaceFilter, roadMesh, roadMaterial);
        ApplyMesh(curbFilter, edgeMesh, curbMaterial);
    }

    private void ApplyMesh(MeshFilter filter, Mesh mesh, Material material)
    {
        if (filter == null) return;
        filter.sharedMesh = mesh;
        filter.GetComponent<MeshRenderer>().sharedMaterial = material;

        MeshCollider collider = filter.GetComponent<MeshCollider>();
        if (addMeshColliders && collider == null)
            collider = AddOwnedComponent<MeshCollider>(filter.gameObject);

        if (collider == null) return;
        collider.sharedMesh = null;
        collider.enabled = addMeshColliders && mesh != null && mesh.vertexCount > 0;
        if (collider.enabled) collider.sharedMesh = mesh;
    }

    private MeshFilter CreateMeshChild(string objectName, Transform parent)
    {
        GameObject child = CreateChild(objectName, parent);
        AddOwnedComponent<MeshRenderer>(child);
        return AddOwnedComponent<MeshFilter>(child);
    }

    private GameObject CreateChild(string objectName, Transform parent)
    {
        var child = new GameObject(objectName);
        child.transform.SetParent(parent, false);
        child.layer = gameObject.layer;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(child, "Create JSON Road Object");
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

    private static Material MakeMaterial(string materialName, Color color)
    {
        bool usesRenderPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
        Shader shader = Shader.Find(usesRenderPipeline ? "Universal Render Pipeline/Lit" : "Standard");
        if (shader == null) shader = Shader.Find("Standard");
        return new Material(shader) { name = materialName, color = color };
    }

    private void RecordChange(string actionName)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterFullObjectHierarchyUndo(gameObject, actionName);
#endif
    }

    private bool Fail(string message)
    {
        hasError = true;
        lastMessage = message;
        Debug.LogError("SplineJson: " + message, this);
        return false;
    }
}
