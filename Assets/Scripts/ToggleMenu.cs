using System;
using UnityEditor;
using UnityEngine;

public class ToggleMenu : MonoBehaviour
{
    public GameObject menuPanel; // Assign this in the Inspector
    bool isMenuOpen = false;
    void Start()
    {
        menuPanel.SetActive(false); // Ensure the menu is hidden at the start
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Q))
        {
            Quit();
        }

        if (isMenuOpen)
        {
             menuPanel.SetActive(true);
        }
        else
        {
             menuPanel.SetActive(false);
        }

        if(Input.GetKeyDown(KeyCode.Escape))
        {
            ToggleMenuPanel();
        }

        
    }

    public void ToggleMenuPanel()
    {
        if (isMenuOpen)
        {
            isMenuOpen = false;
        }
        else
        {
            isMenuOpen = true;
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
}
