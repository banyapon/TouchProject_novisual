using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneEscape
{
    private const string TitleSceneName = "Title";

    public static void Handle()
    {
        if (SceneManager.GetActiveScene().name == TitleSceneName)
        {
            Application.Quit();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
            return;
        }

        SceneManager.LoadScene(TitleSceneName, LoadSceneMode.Single);
    }
}
