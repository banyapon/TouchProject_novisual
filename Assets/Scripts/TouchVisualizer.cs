using System.Collections;
using UnityEngine;
using System.Collections.Generic;

public class TouchVisualizer : MonoBehaviour
{
    public static TouchVisualizer Instance;

    public GameObject touchPrefab;
    private Dictionary<int, GameObject> visuals = new Dictionary<int, GameObject>();

    void Awake()
    {
        Instance = this;
    }

    public void UpdateTouches(Dictionary<int, Vector2> touches)
    {
        // Remove old
        var toRemove = new List<int>();
        foreach (var id in visuals.Keys)
        {
            if (!touches.ContainsKey(id))
            {
                Destroy(visuals[id]);
                toRemove.Add(id);
            }
        }
        foreach (var id in toRemove) visuals.Remove(id);

        // Update / create
        foreach (var kv in touches)
        {
            if (!visuals.ContainsKey(kv.Key))
            {
                //visuals[kv.Key] = Instantiate(touchPrefab, transform);
                GameObject obj = Instantiate(touchPrefab);
                obj.transform.SetParent(transform as RectTransform, false);
                visuals[kv.Key] = obj;
            }

            //visuals[kv.Key].transform.position = kv.Value;
            RectTransform rt = visuals[kv.Key].GetComponent<RectTransform>();
            rt.anchoredPosition = kv.Value;
        }
    }
}

