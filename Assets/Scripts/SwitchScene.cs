using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SwitchScene : MonoBehaviour
{
    private const string DogPaddleScene = "Dogpaddle";
    private const string DragNGoScene = "DragnGo";
    private const string SplineMovementScene = "Spline";

    private string currentSceneName;
    private string pendingSceneName;
    private Coroutine switchRoutine;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            SwitchChange(DogPaddleScene);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            SwitchChange(DragNGoScene);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
        {
            SwitchChange(SplineMovementScene);
        }
    }

    public void SwitchChange(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("Scene name is empty.");
            return;
        }

        ToggleMenu toggleMenu = FindAnyObjectByType<ToggleMenu>(FindObjectsInactive.Include);
        if (toggleMenu != null)
        {
            toggleMenu.CloseMenu();
        }

        if (sceneName == currentSceneName
            && SceneManager.GetSceneByName(sceneName).isLoaded)
        {
            return;
        }

        pendingSceneName = sceneName;
        if (switchRoutine == null)
        {
            switchRoutine = StartCoroutine(SwitchSceneRoutine());
        }
    }

    private IEnumerator SwitchSceneRoutine()
    {
        while (!string.IsNullOrEmpty(pendingSceneName))
        {
            string sceneName = pendingSceneName;
            pendingSceneName = null;

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
        }

        switchRoutine = null;
    }
}
