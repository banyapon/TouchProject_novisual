using UnityEngine;
using UnityEngine.UI;

public class ToggleMenu : MonoBehaviour
{
    public GameObject menuPanel; // Assign this in the Inspector
    bool isMenuOpen = false;
    void Start()
    {
        AddCalibrateButton();
        CloseMenu();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Q))
        {
            Quit();
        }

        if(Input.GetKeyDown(KeyCode.Escape))
        {
            ToggleMenuPanel();
        }

        
    }

    public void ToggleMenuPanel()
    {
        SetMenuOpen(!isMenuOpen);
    }

    public void CloseMenu()
    {
        SetMenuOpen(false);
    }

    private void SetMenuOpen(bool open)
    {
        isMenuOpen = open;
        if (menuPanel != null)
        {
            menuPanel.SetActive(open);
        }
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void AddCalibrateButton()
    {
        if (menuPanel == null || menuPanel.transform.childCount == 0)
        {
            return;
        }

        Transform buttonRoot = menuPanel.transform.GetChild(0);
        if (buttonRoot.Find("Button (Calibrate)") != null)
        {
            return;
        }

        Button template = buttonRoot.GetComponentInChildren<Button>(true);
        SwitchScene switchScene = FindAnyObjectByType<SwitchScene>();
        if (template == null || switchScene == null)
        {
            return;
        }

        GameObject buttonObject = Instantiate(template.gameObject, buttonRoot);
        buttonObject.name = "Button (Calibrate)";
        buttonObject.transform.SetSiblingIndex(0);

        Text label = buttonObject.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = "CALIBRATE";
        }

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => switchScene.SwitchChange("Calibrate"));
    }
}
