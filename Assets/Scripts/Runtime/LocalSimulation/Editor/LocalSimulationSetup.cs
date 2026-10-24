using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OverTheHorizon.Player.Flight;
using OverTheHorizon.Player.View;

namespace OverTheHorizon.LocalSimulation.Editor
{
    public static class LocalSimulationSetup
    {
        private const string SettingsPath = "Assets/Settings/SimulationTimingSettings.asset";
        [MenuItem("Tools/OverTheHorizon/Configure Local Timing")]
        private static void Configure()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Configure timing outside Play Mode.");
            var flights = UnityEngine.Object.FindObjectsByType<PlayerFlightController>(FindObjectsSortMode.None);
            Array.Sort(flights, (a, b) => string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform)));
            if (flights.Length < 1 || flights.Length > 4) throw new InvalidOperationException("The loaded scene needs one to four players.");
            // Duplicate hierarchy paths would make slot ordering ambiguous.
            for (int i = 1; i < flights.Length; ++i)
                if (HierarchyPath(flights[i - 1].transform) == HierarchyPath(flights[i].transform))
                    throw new InvalidOperationException("Rename duplicate player hierarchy paths before setup.");
            var roots = UnityEngine.Object.FindObjectsByType<LocalSimulationBootstrap>(FindObjectsSortMode.None);
            if (roots.Length > 1) throw new InvalidOperationException("Multiple local timing roots exist.");
            var settings = AssetDatabase.LoadAssetAtPath<SimulationTimingSettings>(SettingsPath);
            if (settings == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
                settings = ScriptableObject.CreateInstance<SimulationTimingSettings>(); AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            LocalSimulationBootstrap root;
            if (roots.Length == 0)
            {
                var obj = new GameObject("LocalSimulationRoot"); Undo.RegisterCreatedObjectUndo(obj, "Create local timing");
                root = Undo.AddComponent<LocalSimulationBootstrap>(obj);
            }
            else root = roots[0];
            Undo.RecordObject(root, "Bind local timing");
            var data = new SerializedObject(root);
            data.FindProperty("timingSettings").objectReferenceValue = settings;
            var playerList = data.FindProperty("players"); playerList.arraySize = flights.Length;
            for (int i = 0; i < flights.Length; ++i)
            {
                playerList.GetArrayElementAtIndex(i).objectReferenceValue = flights[i];
                if (flights[i].GetComponent<PlayerControlSnapshotPublisher>() == null)
                    Undo.AddComponent<PlayerControlSnapshotPublisher>(flights[i].gameObject);
            }
            var cameras = UnityEngine.Object.FindObjectsByType<PlayerCameraController>(FindObjectsSortMode.None);
            var cameraList = data.FindProperty("cameras"); cameraList.arraySize = cameras.Length;
            for (int i = 0; i < cameras.Length; ++i) cameraList.GetArrayElementAtIndex(i).objectReferenceValue = cameras[i];
            data.ApplyModifiedProperties(); EditorUtility.SetDirty(root); EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            AssetDatabase.SaveAssets(); Selection.activeObject = root;
            Debug.Log("Local timing configured. Verify player array order (IDs 1..4), then save the scene.");
        }
        private static string HierarchyPath(Transform transform)
            => transform.parent == null ? transform.gameObject.scene.path + "/" + transform.name : HierarchyPath(transform.parent) + "/" + transform.name;
    }
    [CustomEditor(typeof(LocalSimulationBootstrap))]
    public sealed class LocalSimulationInspector : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!Application.isPlaying) return;
            var root = (LocalSimulationBootstrap)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("상태", root.State.ToString());
            EditorGUILayout.LabelField("세대", root.Generation.ToString());
            EditorGUILayout.LabelField("완료 Tick", root.CompletedTick.ToString());
            EditorGUILayout.LabelField("완료 Ordinal", root.CompletedOrdinal.ToString());
            if (root.LastFault.Code != OverTheHorizon.Timing.SimulationFaultCode.None)
                EditorGUILayout.HelpBox(root.LastFault.Code + ": " + root.LastFault.Message, MessageType.Error);
            if (!string.IsNullOrEmpty(root.LastCleanupError))
                EditorGUILayout.HelpBox("정리 오류: " + root.LastCleanupError, MessageType.Error);
            if (LocalSimulationBootstrap.PendingCleanupCount != 0)
            {
                EditorGUILayout.HelpBox("파괴된 세션 정리 대기: " + LocalSimulationBootstrap.PendingCleanupStage
                    + "\n" + LocalSimulationBootstrap.PendingCleanupError, MessageType.Error);
                if (GUILayout.Button("대기 중인 정리 재시도")) LocalSimulationBootstrap.RetryPendingCleanup();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("시작")) root.RequestStart();
                if (GUILayout.Button("일시정지")) root.RequestPause();
                if (GUILayout.Button("재개")) root.RequestResume();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("재시작")) root.RequestRestart();
                if (GUILayout.Button("중지")) root.RequestStop();
                if (GUILayout.Button("전체 기록 저장")) root.ExportReplayBinary();
            }
        }
    }
}
