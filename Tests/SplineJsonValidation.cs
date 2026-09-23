// Run in an isolated Unity project with the SplineJson scripts and junction.json copied in.
// Unity -batchmode -projectPath <test-project> -executeMethod SplineJsonValidation.Run
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SplineJsonValidation
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var asset = Resources.Load<TextAsset>("data/junction");
            Check(asset != null, "Default JSON loads from Resources.");
            var document = SplineJsonDocument.Parse(asset.text);
            Check(document.roads.Length == 8 && document.straightConnections.Length == 2, "Parse nested point arrays.");
            bool invalidRejected = false;
            try { SplineJsonDocument.Parse("{\"roads\":[]}"); } catch { invalidRejected = true; }
            Check(invalidRejected, "Reject invalid JSON before generation.");
            var go = new GameObject("Spline JSON Validation");
            var source = go.AddComponent<SplineJson>();
            source.JsonAsset = asset;
            Check(SplineJsonEditor.Refresh(source), "Refresh succeeds.");
            Check(source.Container.Splines.Count == 8, "One spline per road ID.");
            Check(source.StraightContainer.Splines.Count == 2, "Straight spans have no extra road IDs.");
            Check(source.Container.Splines[source.FindRoad(5).splineIndex].Count == 2, "Quadratic converts to two cubic knots.");
            Check(source.TryGetTravelPose(1, 0, 0, out var south, out var northward) && south.z < 0 && northward.z > 0.99f, "Image down maps to Unity south.");
            Check(source.TryGetTravelPose(2, 0, 1, out var north, out _) && north.z > 0, "North endpoint orientation.");
            Check(source.TryGetTravelPose(3, 0, 0, out var west, out _) && west.x < 0, "West endpoint orientation.");
            for (int id = 1; id <= 8; id++)
            {
                Check(source.TryGetTravelPose(id, 0, .35f, out var p, out var f) &&
                    source.TryGetTravelPose(id, 1, .65f, out var reverseP, out var reverseF) &&
                    Vector3.Distance(p, reverseP) < .0001f && (f + reverseF).magnitude < .0001f,
                    "Opposite travel directions share the same spline: " + id);
            }
            Check(!source.TryGetTravelPose(5, 2, 0, out _, out _), "Reject invalid enterNode.");
            int childCount = source.transform.childCount;
            Check(SplineJsonEditor.Refresh(source) && source.transform.childCount == childCount, "Repeated Refresh reuses children.");
            Check(SplineJsonEditor.Extrude(source), "Extrude succeeds.");
            Check(source.RoadMesh.vertexCount > 0 && source.CurbMesh.vertexCount > 0, "Surface and curb geometry exist.");
            Check(AssetDatabase.Contains(source.RoadMesh) && AssetDatabase.Contains(source.CurbMesh), "Meshes saved as assets.");
            Check(AssetDatabase.Contains(source.RoadMaterial) && AssetDatabase.Contains(source.CurbMaterial), "Materials saved as assets.");
            Check(Mathf.Abs(source.CurbMesh.bounds.max.y - .37f) < .001f, "Curbs have requested height.");
            foreach (var n in source.RoadMesh.normals) Check(n.y > .99f, "Road triangle faces upward.");
            CheckOpenLanes(source);
            // The small spaces between the two straight ribbons and turn ribbons must be filled.
            for (float x = -1.6f; x <= 1.6f; x += .2f)
                for (float z = -1.6f; z <= 1.6f; z += .2f)
                    Check(Physics.Raycast(new Vector3(x, 2, z), Vector3.down, 3), "Continuous center surface.");

            string meshPath = AssetDatabase.GetAssetPath(source.RoadMesh);
            childCount = source.transform.childCount;
            Check(SplineJsonEditor.Extrude(source), "Repeated Extrude succeeds.");
            Check(source.transform.childCount == childCount && AssetDatabase.GetAssetPath(source.RoadMesh) == meshPath,
                "Repeated Extrude reuses children and mesh assets.");
            SplineJsonEditor.Refresh(source);
            Check(!source.GetComponentInChildren<MeshRenderer>(true).gameObject.activeInHierarchy, "Refresh hides stale mesh.");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Check(source.GetComponentInChildren<MeshRenderer>(true).gameObject.activeInHierarchy, "Undo restores mesh visibility.");
            Check(source.Container.Splines.Count == 8, "Undo preserves splines.");

            var duplicate = UnityEngine.Object.Instantiate(go);
            duplicate.name = "Duplicate Junction";
            var second = duplicate.GetComponent<SplineJson>();
            Check(second.RoadMesh == source.RoadMesh, "Duplicate initially shares asset.");
            Check(SplineJsonEditor.Extrude(second), "Duplicate can extrude.");
            Check(second.RoadMesh != source.RoadMesh && AssetDatabase.GetAssetPath(source.RoadMesh) == meshPath,
                "Duplicate does not overwrite original asset.");
            UnityEngine.Object.DestroyImmediate(duplicate);

            Check(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/Validation.unity"), "Save scene.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.OpenScene("Assets/Validation.unity");
            source = UnityEngine.Object.FindFirstObjectByType<SplineJson>();
            Check(source != null && source.Container.Splines.Count == 8, "Spline data persists after scene reload.");
            Check(source.StraightContainer.Splines.Count == 2 && source.RoadMesh.vertexCount > 0, "Bridges and mesh persist.");
            Check(AssetDatabase.GetAssetPath(source.RoadMesh) == meshPath, "Scene uses saved mesh asset.");
            CheckOpenLanes(source);
            RenderPreview(source);
            File.WriteAllText("validation-result.txt", "PASS: " + checks + " checks. Refresh, direction, extrusion, open junctions, undo, duplicate isolation, scene reload and rendering.");
            Debug.Log("SPLINE_JSON_VALIDATION_PASS " + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText("validation-result.txt", "FAIL: " + exception);
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void CheckOpenLanes(SplineJson source)
    {
        Physics.SyncTransforms();
        Vector3 center = source.transform.position + Vector3.up * .18f;
        Check(!Physics.Raycast(center - Vector3.forward * 19, Vector3.forward, 38), "Curbs do not obstruct north/south traffic.");
        Check(!Physics.Raycast(center - Vector3.right * 19, Vector3.right, 38), "Curbs do not obstruct east/west traffic.");
        for (int id = 5; id <= 8; id++)
        {
            source.TryGetTravelPose(id, 0, 0, out var previous, out _);
            for (int i = 1; i <= 64; i++)
            {
                source.TryGetTravelPose(id, 0, i / 64f, out var current, out _);
                Vector3 delta = current - previous;
                Check(!Physics.Raycast(previous + Vector3.up * .15f, delta.normalized, delta.magnitude), "Curbs do not obstruct turn " + id);
                previous = current;
            }
        }
    }

    private static void RenderPreview(SplineJson source)
    {
        var cameraObject = new GameObject("Validation Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(30, 42, -38);
        camera.transform.LookAt(source.transform.position);
        camera.orthographic = true; camera.orthographicSize = 27;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.055f,.067f,.08f);
        var light = new GameObject("Validation Light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.2f;
        light.transform.rotation = Quaternion.Euler(45,-30,0);
        RenderSettings.ambientLight = new Color(.55f,.55f,.55f);
        var render = new RenderTexture(1024,768,24);
        camera.targetTexture = render;
        camera.Render();
        RenderTexture.active = render;
        var image = new Texture2D(1024,768,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1024,768),0,0); image.Apply();
        File.WriteAllBytes("mesh-preview.png",image.EncodeToPNG());
        RenderTexture.active = null; camera.targetTexture = null;
        render.Release();
        UnityEngine.Object.DestroyImmediate(render); UnityEngine.Object.DestroyImmediate(image);
    }
}
