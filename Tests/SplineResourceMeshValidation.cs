using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CarState = RoadNetworkSplineCreator.CarState;
using MoveMode = RoadNetworkSplineCreator.MoveMode;

public static class SplineResourceMeshValidation
{
    private static int checks, phase;
    private static double nextCheck;
    private static splinemovement movement;
    private static SplineResourceMesh renderer;
    private static SplineJsonMovementRoute route;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    private static void Close(Vector3 a, Vector3 b, string message)
        => Check(Vector3.Distance(a, b) < .003f, message + $" ({a} vs {b})");

    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            renderer = new GameObject("Resource Junction").AddComponent<SplineResourceMesh>();
            Check(renderer.Refresh(), "Default data/junction loads through Resources.");
            Check(renderer.GeneratedRoot.transform.childCount == 5, "Default graph has one intersection and four straight tiles.");
            renderer.JsonAsset = Resources.Load<TextAsset>("data/junction_test");
            Check(renderer.JsonAsset != null && SplineResourceMeshEditor.Refresh(renderer), "Test JSON refresh.");
            Check(renderer.GeneratedRoot.transform.childCount == 9, "One intersection, six straights, two exterior curves; no overlapping internal models.");
            var filters = renderer.GeneratedRoot.GetComponentsInChildren<MeshFilter>();
            Check(filters.Count(x => x.sharedMesh.name.Contains("intersection")) == 1, "Uses intersectionRoad resource exactly once.");
            Check(filters.Count(x => x.sharedMesh.name.Contains("curved")) == 2, "Uses curvedRoad for both external bends.");
            Check(renderer.Source.Roads.Count == 12, "Movement splines include all twelve roads.");
            VerifyPorts(renderer);
            Physics.SyncTransforms();
            // Every movement centreline has real model geometry underneath, including inside the intersection.
            foreach (var road in renderer.Source.Roads)
                for (int i = 1; i < 20; i++)
                {
                    renderer.Source.TryGetTravelPose(road.id, 0, i / 20f, out var p, out _);
                    Check(Physics.Raycast(p + Vector3.up * 5, Vector3.down, out var hit, 6), "Road model supports movement spline " + road.id + " at " + i);
                    Check(hit.point.y < .08f, "Movement does not climb a sidewalk on road " + road.id + " at " + i);
                }
            route = new SplineJsonMovementRoute(renderer.Source);
            Check(route.EnsureReady(), "Route cache from resource renderer source.");
            foreach (int arm in new[] { 3, 4 })
            {
                int dir = arm == 3 ? 1 : 0;
                var data = route.GetRoadData(arm);
                var car = new CarState { roadNo = arm, dir = dir, currentPos = dir == 1 ? 1f : data.length - 1f };
                route.EnsureHistory(car); route.SyncLaneWithForwardHistory(car);
                float distance = 1f + route.GetRoadData(arm + 6).length + 2f;
                route.MoveCarLoop(car, distance, MoveMode.Forward);
                Check(car.roadNo == arm + 8 && Math.Abs(car.currentPos - 2f) < .005f, "Travel arm -> curve -> short straight: " + arm);
                route.MoveCarLoop(car, distance, MoveMode.Backward);
                Check(car.roadNo == arm && car.historyIndex == 0, "Reverse restores full route through curve: " + arm);
            }
            Check(SplineResourceMeshEditor.Refresh(renderer) && renderer.GeneratedRoot.transform.childCount == 9, "Repeated Refresh replaces tiles.");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Check(renderer.GeneratedRoot != null && renderer.GeneratedRoot.activeSelf && renderer.GeneratedRoot.transform.childCount == 9, "Undo restores previous resource models.");
            // Mirroring the graph changes turn handedness, not just the prefab yaw.
            var originalJson = renderer.JsonAsset;
            var mirrored = SplineJsonDocument.Parse(originalJson.text);
            foreach (var road in mirrored.roads)
            {
                foreach (var point in road.points) point[0] = -point[0];
                if (road.controlPoint != null) road.controlPoint[0] = -road.controlPoint[0];
            }
            foreach (var span in mirrored.straightConnections)
                foreach (var point in span.points) point[0] = -point[0];
            using (var jsonStream = new MemoryStream())
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(SplineJsonDocument)).WriteObject(jsonStream, mirrored);
                renderer.JsonAsset = new TextAsset(System.Text.Encoding.UTF8.GetString(jsonStream.ToArray()));
            }
            Check(SplineResourceMeshEditor.Refresh(renderer), "Mirrored turns use the same curvedRoad resource.");
            VerifyPorts(renderer);
            Physics.SyncTransforms();
            foreach (int id in new[] { 9, 10 })
                for (int i = 1; i < 10; i++)
                {
                    renderer.Source.TryGetTravelPose(id, 0, i / 10f, out var p, out _);
                    Check(Physics.Raycast(p + Vector3.up * 5, Vector3.down, out var hit, 6) && hit.point.y < .08f, "Mirrored curved model supports the spline.");
                }
            renderer.JsonAsset = originalJson;
            Check(SplineResourceMeshEditor.Refresh(renderer), "Restore test graph after mirror check.");
            // Parent transforms apply equally to models and movement paths.
            renderer.transform.SetPositionAndRotation(new Vector3(7, 2, -3), Quaternion.Euler(0, 37, 0));
            VerifyPorts(renderer);
            renderer.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            RenderPreview();

            // Create a ready-to-play scene alongside the user's existing scene.
            EditorSceneManager.OpenScene("Assets/Demo/SplineJSON.unity");
            movement = UnityEngine.Object.FindAnyObjectByType<splinemovement>();
            Check(movement != null && movement.SplineSource != null, "Demo retains splinemovement and assigned source.");
            renderer = movement.SplineSource.GetComponent<SplineResourceMesh>();
            if (renderer == null) renderer = movement.SplineSource.gameObject.AddComponent<SplineResourceMesh>();
            renderer.JsonAsset = Resources.Load<TextAsset>("data/junction_test");
            Check(SplineResourceMeshEditor.Refresh(renderer), "Generate resource demo scene.");
            Check(EditorSceneManager.SaveScene(movement.gameObject.scene, "Assets/Demo/SplineResourceMesh.unity"), "Save demo scene and prefab instances.");
            EditorSceneManager.OpenScene("Assets/Demo/SplineResourceMesh.unity");
            movement = UnityEngine.Object.FindAnyObjectByType<splinemovement>();
            renderer = movement.SplineSource.GetComponent<SplineResourceMesh>();
            Check(renderer.GeneratedRoot.transform.childCount == 9, "Resource instances persist on scene reload.");
            var settings = new SerializedObject(movement);
            var manager = (TouchpadManager)settings.FindProperty("touchManager").objectReferenceValue;
            Check(manager != null && manager.ControlsSplineMovement(movement), "Touch manager uses the same movement component.");
            manager.enabled = false; // Only the isolated test copy uses simulated input.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            phase = 0; nextCheck = EditorApplication.timeSinceStartup + 1;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static void VerifyPorts(SplineResourceMesh owner)
    {
        var settings = new SerializedObject(owner);
        Vector3 entry = settings.FindProperty("curveEntry").vector3Value;
        Vector3 exit = settings.FindProperty("curveExit").vector3Value;
        foreach (int id in new[] { 9, 10 })
        {
            Transform tile = owner.GeneratedRoot.transform.Find("Road " + id + " Curve");
            owner.Source.TryGetTravelPose(id, 0, 0, out var start, out _);
            owner.Source.TryGetTravelPose(id, 0, 1, out var end, out _);
            Close(tile.TransformPoint(entry), start, "Curve entry meets spline " + id);
            Close(tile.TransformPoint(exit), end, "Curve exit meets spline " + id);
        }
    }
    private static void RenderPreview()
    {
        var camera = new GameObject("Preview Camera").AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(new Vector3(0, 90, 0), Quaternion.Euler(90, 0, 0));
        camera.orthographic = true; camera.orthographicSize = 31;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f,.16f,.2f);
        var light = new GameObject("Preview Sun").AddComponent<Light>();
        light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(70, 10, 0);
        var target = new RenderTexture(1200, 800, 24);
        camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
        var image = new Texture2D(1200,800,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1200,800),0,0); image.Apply();
        File.WriteAllBytes("resource-road-preview.png", image.EncodeToPNG());
        RenderTexture.active = null; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
    }
    private static void Input(float distance)
    {
        movement.EndTouchFromManager(); movement.BeginTouchFromManager(Vector2.zero);
        float raw = distance / ((65f/40f)*(8f/912f));
        movement.MoveFromTouchManager(new Vector2(0,raw),new Vector2(0,raw),TouchpadManager.TouchMode.Translate,1);
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextCheck) return;
        try
        {
            nextCheck = EditorApplication.timeSinceStartup + .3;
            if (phase == 0)
            {
                Check(movement.CurrentState != null && movement.CurrentState.roadNo == 1, "Play Mode starts at road 1.");
                Check(renderer.Refresh(), "Resources load and instantiate in Play Mode.");
                route = new SplineJsonMovementRoute(renderer.Source); route.EnsureReady();
                var car = movement.CurrentState;
                car.hasPendingSelection = true; car.pendingNextRoad = 5; car.pendingEnterNode = 0;
                Input(route.GetRoadData(1).length + route.GetRoadData(5).length + route.GetRoadData(3).length + route.GetRoadData(9).length + 2f);
                Check(car.roadNo == 11, "Actual splinemovement input follows 1 -> 5 -> 3 -> 9 -> 11.");
            }
            else if (phase == 1)
            {
                Close(movement.GetComponent<Rigidbody>().position, route.EvaluateRoadPosition(movement.CurrentState), "Avatar is on the added straight road.");
                Input(-3f);
                Check(movement.CurrentState.roadNo == 9, "Actual input reverses onto curvedRoad.");
            }
            else
            {
                Close(movement.GetComponent<Rigidbody>().position, route.EvaluateRoadPosition(movement.CurrentState), "Avatar follows the added curve in reverse.");
                File.WriteAllText("resource-validation-result.txt", $"PASS: {checks} checks. Resource models, intersection coverage, curve ports, road support, forward/reverse travel, Undo, repeat Refresh, scene persistence and actual splinemovement in Play Mode.");
                EditorApplication.update -= Tick; EditorApplication.Exit(0);
            }
            phase++;
        }
        catch (Exception ex) { Fail(ex); }
    }
    private static void Fail(Exception ex)
    {
        File.WriteAllText("resource-validation-result.txt", "FAIL: " + ex);
        Debug.LogException(ex); EditorApplication.update -= Tick; EditorApplication.Exit(1);
    }
}
