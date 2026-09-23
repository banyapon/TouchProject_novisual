using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

[CustomEditor(typeof(SplineJson))]
public sealed class SplineJsonEditor : Editor
{
    private int selectedRoad;
    private bool showGraph;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var source = (SplineJson)target;
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh", GUILayout.Height(30))) Refresh(source);
                if (GUILayout.Button("Extrude", GUILayout.Height(30))) Extrude(source);
            }
        }
        if (!string.IsNullOrEmpty(source.LastMessage))
            EditorGUILayout.HelpBox(source.LastMessage, source.HasError ? MessageType.Error : MessageType.Info);
        EditorGUILayout.HelpBox("JSON [x,y] maps to local X/Z. Yellow = S to E; green = E to S on the same road ID. Refresh reloads JSON; Extrude uses the current splines and saves road/curb meshes.", MessageType.None);

        var options = new List<string> { "All roads" };
        foreach (var road in source.Roads) options.Add($"{road.id}: {road.label}");
        selectedRoad = Mathf.Clamp(selectedRoad, 0, options.Count - 1);
        EditorGUI.BeginChangeCheck();
        selectedRoad = EditorGUILayout.Popup("Inspect Connections", selectedRoad, options.ToArray());
        if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
        showGraph = EditorGUILayout.Foldout(showGraph, "Road connections (S=0 / E=1)", true);
        if (showGraph)
        {
            foreach (var road in source.Roads)
            {
                EditorGUILayout.LabelField($"ID {road.id} / {road.label}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("S", Describe(road.nodeS, road.defaultRoadS));
                EditorGUILayout.LabelField("E", Describe(road.nodeE, road.defaultRoadE));
            }
        }
    }

    private static string Describe(SplineJsonConnection[] connections, int defaultRoad)
    {
        if (connections.Length == 0) return "Open endpoint";
        return string.Join(", ", connections.Select(c => $"{c.roadNo}:{(c.enterNode == 0 ? "S" : "E")}{(c.roadNo == defaultRoad ? " (default)" : "")}"));
    }

    public static bool Refresh(SplineJson source)
    {
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Refresh JSON Splines");
        bool result = source.Refresh();
        MarkChanged(source);
        Undo.CollapseUndoOperations(group);
        return result;
    }

    public static bool Extrude(SplineJson source)
    {
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Extrude JSON Road Mesh");
        Mesh oldSurface = source.RoadMesh, oldCurbs = source.CurbMesh;
        // Duplicated scene objects must not overwrite each other's saved meshes.
        bool shared = Resources.FindObjectsOfTypeAll<SplineJson>().Any(other => other != source &&
            oldSurface != null && other.RoadMesh == oldSurface);
        bool result = source.Extrude();
        if (result)
        {
            string folder = !shared && oldSurface != null && AssetDatabase.Contains(oldSurface)
                ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(oldSurface)).Replace('\\', '/')
                : NewAssetFolder(source.name);
            Mesh temporarySurface = source.RoadMesh, temporaryCurbs = source.CurbMesh;
            Mesh savedSurface = SaveMesh(temporarySurface, shared ? null : oldSurface, folder + "/Road.asset");
            Mesh savedCurbs = SaveMesh(temporaryCurbs, shared ? null : oldCurbs, folder + "/Curbs.asset");
            SaveMaterial(source.RoadMaterial, folder + "/Road Material.mat");
            SaveMaterial(source.CurbMaterial, folder + "/Curb Material.mat");
            source.SetMeshes(savedSurface, savedCurbs);
            if (temporarySurface != savedSurface) DestroyImmediate(temporarySurface);
            if (temporaryCurbs != savedCurbs) DestroyImmediate(temporaryCurbs);
        }
        MarkChanged(source);
        Undo.CollapseUndoOperations(group);
        return result;
    }

    private static string NewAssetFolder(string objectName)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
        if (!AssetDatabase.IsValidFolder("Assets/Generated/SplineJson")) AssetDatabase.CreateFolder("Assets/Generated", "SplineJson");
        string safeName = string.Concat(objectName.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
        if (string.IsNullOrEmpty(safeName)) safeName = "Junction";
        string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/Generated/SplineJson/" + safeName);
        AssetDatabase.CreateFolder("Assets/Generated/SplineJson", Path.GetFileName(folder));
        return folder;
    }

    private static Mesh SaveMesh(Mesh generated, Mesh previous, string path)
    {
        if (previous != null && AssetDatabase.Contains(previous))
        {
            Undo.RegisterCompleteObjectUndo(previous, "Update Road Mesh Asset");
            EditorUtility.CopySerialized(generated, previous);
            EditorUtility.SetDirty(previous);
            AssetDatabase.SaveAssetIfDirty(previous);
            return previous;
        }
        AssetDatabase.CreateAsset(generated, AssetDatabase.GenerateUniqueAssetPath(path));
        return generated;
    }

    private static void SaveMaterial(Material material, string path)
    {
        if (material != null && !AssetDatabase.Contains(material))
            AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(path));
    }

    private static void MarkChanged(SplineJson source)
    {
        EditorUtility.SetDirty(source);
        EditorUtility.SetDirty(source.Container);
        if (source.StraightContainer != null) EditorUtility.SetDirty(source.StraightContainer);
        PrefabUtility.RecordPrefabInstancePropertyModifications(source);
        if (source.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
        SceneView.RepaintAll();
    }

    private void OnSceneGUI()
    {
        var source = (SplineJson)target;
        int focusedId = selectedRoad > 0 && selectedRoad <= source.Roads.Count ? source.Roads[selectedRoad - 1].id : 0;
        Color previous = Handles.color;
        foreach (var road in source.Roads)
        {
            if (road.splineIndex >= source.Container.Splines.Count) continue;
            bool focused = focusedId == 0 || focusedId == road.id;
            Handles.color = focused ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.4f);
            DrawSpline(source.Container, road.splineIndex);
            if (!source.TryGetTravelPose(road.id, 0, 0.5f, out var middle, out var forward)) continue;
            if (source.ShowLabels)
                Handles.Label(middle + source.transform.up * 0.15f, $"{road.label} [ID {road.id}]");
            if (!focused) continue;
            if (source.ShowDirections)
            {
                Arrow(source, road.id, 0, 0.36f, Color.yellow);
                if (road.bidirectional) Arrow(source, road.id, 1, 0.36f, Color.green);
            }
            // Focus one road to see its exact S/E endpoint connections without overlapping labels.
            if (source.ShowEndpoints && focusedId == road.id)
            {
                Endpoint(source, road.id, 0, "S (0)", Color.cyan);
                Endpoint(source, road.id, 1, "E (1)", Color.magenta);
            }
            if (source.ShowConnections && focusedId == road.id)
            {
                DrawConnections(source, road, 0, road.nodeS);
                DrawConnections(source, road, 1, road.nodeE);
            }
        }
        if (source.ShowConnections && source.StraightContainer != null)
        {
            Handles.color = Color.cyan;
            for (int i = 0; i < source.StraightContainer.Splines.Count; i++)
                DrawSpline(source.StraightContainer, i);
        }
        Handles.color = previous;
    }

    private static void DrawSpline(SplineContainer container, int index)
    {
        var points = new Vector3[65];
        for (int i = 0; i < points.Length; i++) points[i] = container.EvaluatePosition(index, i / 64f);
        Handles.DrawAAPolyLine(3f, points);
    }

    private static void Arrow(SplineJson source, int id, int enterNode, float progress, Color color)
    {
        if (!source.TryGetTravelPose(id, enterNode, progress, out var position, out var direction) || direction.sqrMagnitude < 0.001f) return;
        Handles.color = color;
        float size = HandleUtility.GetHandleSize(position) * 0.08f;
        Handles.ArrowHandleCap(0, position, Quaternion.LookRotation(direction, source.transform.up), size, EventType.Repaint);
    }

    private static void Endpoint(SplineJson source, int id, int node, string text, Color color)
    {
        if (!source.TryGetTravelPose(id, 0, node, out var position, out _)) return;
        Handles.color = color;
        Handles.SphereHandleCap(0, position, Quaternion.identity, HandleUtility.GetHandleSize(position) * 0.06f, EventType.Repaint);
        Handles.Label(position + source.transform.up * 0.25f, text);
    }

    private static void DrawConnections(SplineJson source, SplineJson.RoadBinding road, int node, SplineJsonConnection[] links)
    {
        if (!source.TryGetTravelPose(road.id, 0, node, out var origin, out _)) return;
        foreach (var link in links)
        {
            if (!source.TryGetTravelPose(link.roadNo, link.enterNode, 0f, out var target, out _)) continue;
            Handles.color = new Color(1f, 0.6f, 0.1f);
            if ((origin - target).sqrMagnitude > 0.001f) Handles.DrawDottedLine(origin, target, 5f);
            Arrow(source, link.roadNo, link.enterNode, 0.12f, Handles.color);
        }
    }

    [MenuItem("GameObject/Roads/Spline JSON Junction", false, 10)]
    [MenuItem("Tools/KMITL Services/Create Spline JSON Junction")]
    private static void CreateJunction()
    {
        var go = new GameObject("Spline JSON Junction");
        Undo.RegisterCreatedObjectUndo(go, "Create Spline JSON Junction");
        var source = go.AddComponent<SplineJson>();
        Refresh(source);
        Selection.activeGameObject = go;
        SceneView.lastActiveSceneView?.FrameSelected();
    }
}
