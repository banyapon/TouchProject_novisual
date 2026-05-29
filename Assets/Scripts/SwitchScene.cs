using UnityEngine;
using UnityEngine.SceneManagement;

public class SwitchScene : MonoBehaviour
{
    private string currentSceneName;
    private Coroutine switchRoutine;

    public void SwitchChange(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("Scene name is empty.");
            return;
        }

        if (switchRoutine != null)
        {
            StopCoroutine(switchRoutine);
        }

        switchRoutine = StartCoroutine(SwitchSceneRoutine(sceneName));
    }

    private System.Collections.IEnumerator SwitchSceneRoutine(string sceneName)
    {
        // ปิดฉากเดิม
        if (!string.IsNullOrEmpty(currentSceneName))
        {
            Scene oldScene = SceneManager.GetSceneByName(currentSceneName);
            if (oldScene.isLoaded)
            {
                AsyncOperation unloadAsync = SceneManager.UnloadSceneAsync(currentSceneName);
                while (unloadAsync != null && !unloadAsync.isDone)
                {
                    yield return null;
                }
            }
        }

        // โหลดฉากใหม่ซ้อน
        AsyncOperation loadAsync = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        while (loadAsync != null && !loadAsync.isDone)
        {
            yield return null;
        }

        Scene newScene = SceneManager.GetSceneByName(sceneName);
        if (newScene.isLoaded)
        {
            SceneManager.SetActiveScene(newScene);
            currentSceneName = sceneName;
        }

        switchRoutine = null;
    }
}
