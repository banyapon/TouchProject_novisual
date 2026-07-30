using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.Splines;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// สร้างพื้นถนนแนบพื้น (flat strip mesh) ด้วย ProBuilder โดยเดินตาม Spline
/// ที่ RoadNetworkSplineCreator สร้างจาก Resources/data/road_data.json
/// ต่างจาก SplineExtrude ตรงที่ mesh ไม่ยกเป็นทรงท่อ แต่เป็นแผ่นแบนแนบกับพื้น (Plane)
[ExecuteAlways]
public class RoadBuilder : MonoBehaviour
{
    [Header("Spline Source")]
    [SerializeField] private RoadNetworkSplineCreator source;

    [Header("Ground Flush")]
    [Tooltip("ชื่อ GameObject ที่ใช้เป็นระดับพื้น (เช่น Plane) mesh ถนนทั้งหมดจะแนบที่ความสูงนี้")]
    [SerializeField] private string groundObjectName = "Plane";
    [SerializeField] private float groundHeightOffset = 0.02f;

    [Header("Road Shape")]
    [SerializeField, Min(0.1f)] private float roadWidth = 6f;
    [SerializeField, Min(0.05f)] private float segmentsPerUnit = 0.5f;

    [Header("Mesh Output")]
    [SerializeField] private Material roadMaterial;
    [SerializeField] private string roadLayerName = "Road";
    [SerializeField] private bool addMeshCollider = true;

    private ProBuilderMesh proBuilderMesh;

    [ContextMenu("Build Road Mesh")]
    public void BuildRoadMesh()
    {
        RoadNetworkSplineCreator splineSource = ResolveSource();
        if (splineSource == null)
        {
            Debug.LogWarning("RoadBuilder: no RoadNetworkSplineCreator found in the scene.", this);
            return;
        }

        splineSource.RebuildRoute();

        if (!splineSource.TryGetComponent(out SplineContainer splineContainer)
            || splineContainer.Splines == null
            || splineContainer.Splines.Count == 0)
        {
            Debug.LogWarning("RoadBuilder: source has no splines to build from.", this);
            return;
        }

        float groundY = ResolveGroundHeight();

        List<Vector3> vertices = new List<Vector3>();
        List<Face> faces = new List<Face>();

        foreach (Spline spline in splineContainer.Splines)
        {
            AppendRoadStrip(spline, splineContainer.transform, groundY, vertices, faces);
        }

        if (vertices.Count == 0 || faces.Count == 0)
        {
            Debug.LogWarning("RoadBuilder: no geometry was generated, splines may be too short.", this);
            return;
        }

        ApplyMesh(vertices, faces);
    }

    private RoadNetworkSplineCreator ResolveSource()
    {
        if (source != null)
        {
            return source;
        }

#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<RoadNetworkSplineCreator>();
#else
        return FindObjectOfType<RoadNetworkSplineCreator>();
#endif
    }

    private float ResolveGroundHeight()
    {
        if (!string.IsNullOrWhiteSpace(groundObjectName))
        {
            GameObject ground = GameObject.Find(groundObjectName);
            if (ground != null)
            {
                return ground.transform.position.y + groundHeightOffset;
            }
        }

        return transform.position.y + groundHeightOffset;
    }

    private void AppendRoadStrip(
        Spline spline,
        Transform splineTransform,
        float groundY,
        List<Vector3> vertices,
        List<Face> faces)
    {
        float length = spline.GetLength();
        if (length <= Mathf.Epsilon)
        {
            return;
        }

        int segmentCount = Mathf.Max(1, Mathf.RoundToInt(length * segmentsPerUnit));
        float halfWidth = roadWidth * 0.5f;

        Vector3 previousLeft = Vector3.zero;
        Vector3 previousRight = Vector3.zero;
        bool hasPrevious = false;

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            Vector3 center = splineTransform.TransformPoint(ToVector3(spline.EvaluatePosition(t)));
            center.y = groundY;

            Vector3 tangent = EvaluateFlatTangent(spline, splineTransform, t);
            if (tangent.sqrMagnitude <= Mathf.Epsilon)
            {
                continue;
            }

            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized * halfWidth;
            Vector3 left = center - right;
            Vector3 rightPoint = center + right;

            if (hasPrevious)
            {
                int baseIndex = vertices.Count;
                vertices.Add(previousLeft);
                vertices.Add(previousRight);
                vertices.Add(left);
                vertices.Add(rightPoint);

                faces.Add(new Face(new[]
                {
                    baseIndex, baseIndex + 1, baseIndex + 2,
                    baseIndex + 2, baseIndex + 1, baseIndex + 3
                }));
            }

            previousLeft = left;
            previousRight = rightPoint;
            hasPrevious = true;
        }
    }

    private static Vector3 EvaluateFlatTangent(Spline spline, Transform splineTransform, float t)
    {
        const float delta = 0.001f;
        float t0 = Mathf.Clamp01(t - delta);
        float t1 = Mathf.Clamp01(t + delta);

        Vector3 a = splineTransform.TransformPoint(ToVector3(spline.EvaluatePosition(t0)));
        Vector3 b = splineTransform.TransformPoint(ToVector3(spline.EvaluatePosition(t1)));

        Vector3 flatTangent = b - a;
        flatTangent.y = 0f;
        return flatTangent;
    }

    private static Vector3 ToVector3(float3 value)
    {
        return new Vector3(value.x, value.y, value.z);
    }

    private void ApplyMesh(List<Vector3> vertices, List<Face> faces)
    {
        if (proBuilderMesh == null)
        {
            TryGetComponent(out proBuilderMesh);
        }

        if (proBuilderMesh == null)
        {
            proBuilderMesh = gameObject.AddComponent<ProBuilderMesh>();
        }

        proBuilderMesh.Clear();
        proBuilderMesh.RebuildWithPositionsAndFaces(vertices, faces);
        proBuilderMesh.ToMesh();
        proBuilderMesh.Refresh();

        if (TryGetComponent(out MeshRenderer meshRenderer) && roadMaterial != null)
        {
            meshRenderer.sharedMaterial = roadMaterial;
        }

        SetRoadLayer();
        SetupMeshCollider();
    }

    private void SetRoadLayer()
    {
        if (string.IsNullOrWhiteSpace(roadLayerName))
        {
            return;
        }

        int roadLayer = LayerMask.NameToLayer(roadLayerName);
        if (roadLayer < 0)
        {
            Debug.LogWarning($"RoadBuilder: layer '{roadLayerName}' was not found. Create it in Project Settings > Tags and Layers.", this);
            return;
        }

        gameObject.layer = roadLayer;
    }

    private void SetupMeshCollider()
    {
        if (!addMeshCollider)
        {
            return;
        }

        if (!TryGetComponent(out MeshCollider meshCollider))
        {
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }

        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = GetComponent<MeshFilter>().sharedMesh;
    }

#if UNITY_EDITOR
    [MenuItem("Tools/KMITL Services/Build Road Mesh (ProBuilder)")]
    private static void BuildRoadMeshFromMenu()
    {
        RoadBuilder builder = FindFirstObjectByType<RoadBuilder>();
        if (builder == null)
        {
            GameObject go = new GameObject("Road Network Mesh");
            Undo.RegisterCreatedObjectUndo(go, "Create Road Network Mesh");
            builder = go.AddComponent<RoadBuilder>();
        }

        Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Build Road Mesh");
        builder.BuildRoadMesh();
        EditorUtility.SetDirty(builder);
        Selection.activeGameObject = builder.gameObject;
    }

    [MenuItem("Tools/KMITL Services/Build Road Mesh (ProBuilder)", true)]
    private static bool ValidateBuildRoadMeshFromMenu()
    {
        return FindFirstObjectByType<RoadNetworkSplineCreator>() != null;
    }
#endif
}
