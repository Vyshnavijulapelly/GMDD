using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor convenience: make sure the Flappy Bird scene remains available in Build Settings.
/// </summary>
[InitializeOnLoad]
public static class FlappyEditorSetup
{
    const string ScenePath = "Assets/Scenes/FlappyBird.unity";
    const string DoneKey = "FlappyBird.FirstOpenDone";

    static FlappyEditorSetup()
    {
        EditorApplication.delayCall += FirstOpen;
    }

    static void FirstOpen()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool(DoneKey, false)) return;
        SessionState.SetBool(DoneKey, true);

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) return;
        AddToBuildSettings();

    }

    static void AddToBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        for (int i = 0; i < scenes.Count; i++)
            if (scenes[i].path == ScenePath) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    [MenuItem("Flappy Bird/Open Game Scene")]
    static void OpenScene()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(ScenePath);
    }

    [MenuItem("Flappy Bird/Reset Best Score")]
    static void ResetBest()
    {
        PlayerPrefs.DeleteKey("FlappyBest");
        PlayerPrefs.Save();
        Debug.Log("Flappy Bird: best score reset.");
    }
}
