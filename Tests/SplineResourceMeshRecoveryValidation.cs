using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Exercises the pre-existing component in the user's SplineMesh scene, not a freshly
// added component (RequireComponent already fixes the latter).
public static class SplineResourceMeshRecoveryValidation
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Demo/SplineMesh.unity");
            var renderer = UnityEngine.Object.FindAnyObjectByType<SplineResourceMesh>();
            Check(renderer != null, "Scene contains the legacy resource component.");
            // Unity may repair RequireComponent when reopening the scene. Remove that
            // load-time repair to reproduce the still-open pre-upgrade scene in the screenshot.
            if (renderer.Source != null) UnityEngine.Object.DestroyImmediate(renderer.Source);
            bool wasMissing = renderer.Source == null;
            Check(wasMissing, "Reproduce the missing source in the open scene.");
            // Invalid input must report one error without the Inspector throwing another.
            renderer.JsonAsset = new TextAsset("{\"roads\":[]}");
            Check(!SplineResourceMeshEditor.Refresh(renderer), "Invalid JSON returns false without null SetDirty exceptions.");
            if (wasMissing) Check(renderer.Source == null, "Invalid JSON does not create dependencies.");
            renderer.JsonAsset = Resources.Load<TextAsset>("data/junction_test");
            Check(SplineResourceMeshEditor.Refresh(renderer), "Refresh repairs the missing source.");
            Check(renderer.Source != null && renderer.Source.Container != null, "Both required components exist.");
            Check(renderer.Source.Roads.Count == 12 && renderer.Source.Straights.Count == 2, "Recovered source loads all test roads.");
            Check(renderer.GeneratedRoot != null && renderer.GeneratedRoot.transform.childCount == 9, "Models appear after recovery.");
            var movement = UnityEngine.Object.FindAnyObjectByType<splinemovement>();
            var resolve = typeof(splinemovement).GetMethod("ResolveMovementRoute", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(resolve.Invoke(movement, null) is SplineJsonMovementRoute, "Existing Player discovers the recovered source.");
            Check(movement.SplineSource == renderer.Source, "Movement follows the source owned by this renderer.");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            if (wasMissing) Check(renderer.Source == null, "Undo removes the automatically added source.");
            Check(SplineResourceMeshEditor.Refresh(renderer), "Refresh also works after undoing recovery.");
            Check(SplineResourceMeshEditor.Refresh(renderer) && renderer.GeneratedRoot.transform.childCount == 9, "Repeated Refresh keeps one model set.");
            Check(!SplineResourceMeshEditor.Refresh(null), "Null Inspector target returns false safely.");
            File.WriteAllText("resource-recovery-result.txt", $"PASS: {checks} checks. Missing SplineJson recovery, invalid-input Inspector safety, model generation, Player discovery, Undo and retry. Source initially missing: {wasMissing}.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("resource-recovery-result.txt", "FAIL: " + ex);
            Debug.LogException(ex); EditorApplication.Exit(1);
        }
    }
}
