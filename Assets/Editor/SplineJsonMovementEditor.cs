using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(splinemovement), true)]
public sealed class SplineJsonMovementEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();
        var movement = (splinemovement)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Current Route (Play Mode)", EditorStyles.boldLabel);
        var state = movement.CurrentState;
        if (state == null)
        {
            EditorGUILayout.HelpBox("Assign Spline Json for JSON routes, or Road Network for legacy routes. Spline Json takes priority when both are assigned.", MessageType.Info);
            return;
        }
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("Current Road ID", state.roadNo);
            EditorGUILayout.TextField("Road", movement.CurrentRoadLabel);
            EditorGUILayout.IntField("Direction", state.dir);
            EditorGUILayout.FloatField("Distance From S", state.currentPos);
            EditorGUILayout.TextField("Facing", state.dir == 0 ? "S → E" : "E → S");
            EditorGUILayout.IntField("Selected Lane", state.currentLane);
            EditorGUILayout.IntField("Pending Road", state.hasPendingSelection ? state.pendingNextRoad : -1);
            EditorGUILayout.TextField("History", $"{state.historyIndex + 1} / {state.history?.Count ?? 0}");
            if (!string.IsNullOrEmpty(movement.CurrentConnection))
                EditorGUILayout.TextField("Straight Connection", movement.CurrentConnection);
        }
        if (Application.isPlaying) Repaint();
    }
}
