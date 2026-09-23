using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(SplineResourceMesh))]
public sealed class SplineResourceMeshEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var source = (SplineResourceMesh)target;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Refresh Resource Roads", GUILayout.Height(32))) Refresh(source);
        if (!string.IsNullOrEmpty(source.LastMessage))
            EditorGUILayout.HelpBox(source.LastMessage, source.HasError ? MessageType.Error : MessageType.Info);
        EditorGUILayout.HelpBox("Uses SplineJson for movement. Crossings use intersectionRoad, straight spans use straightRoad, and external bends use curvedRoad. Assign this object's SplineJson to splinemovement.", MessageType.None);
    }
    public static bool Refresh(SplineResourceMesh source)
    {
        if (source == null) return false;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Refresh Resource Roads");
        try
        {
            bool result = source.Refresh();
            EditorUtility.SetDirty(source);
            var splineSource = source.Source;
            if (splineSource != null)
            {
                EditorUtility.SetDirty(splineSource);
                if (splineSource.Container != null) EditorUtility.SetDirty(splineSource.Container);
                if (splineSource.StraightContainer != null) EditorUtility.SetDirty(splineSource.StraightContainer);
            }
            if (source.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
            SceneView.RepaintAll();
            return result;
        }
        finally { Undo.CollapseUndoOperations(group); }
    }
}
