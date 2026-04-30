using UnityEngine;
using TrackpadDll; // The namespace you defined in Visual Studio
using RawInput.Touchpad; // The namespace for the TouchpadContact class


public class TouchpadManager : MonoBehaviour
{
    void Start()
    {
        // This starts the hidden window thread we built in the DLL
        TrackpadInterface.Start();
        Debug.Log("Trackpad Listener Started!");
    }

    void Update()
    {
        // Try to pull data out of the thread-safe queue
        while (TrackpadInterface.EventQueue.TryDequeue(out TouchpadContact contact))
        {
            // contact.X and contact.Y are raw values (usually 0 to 4095)
            // contact.Id identifies which finger is which (multi-touch!)
            
            Debug.Log($"Finger {contact.ContactId} at: {contact.X}, {contact.Y}");
            

            // Example: Map raw 0-4095 to screen width/height
            float screenX = (contact.X / 4095f) * Screen.width;
            float screenY = (1f - (contact.Y / 4095f)) * Screen.height;
            
            // You can use these coordinates to move objects or UI cursors here
        }
    }

    private void OnDisable()
    {
        StopThread();
    }
    private void OnApplicationQuit()
    {
        // CRITICAL: If you don't stop the thread, the hidden window 
        // might stay alive after you stop the Unity Editor!
        TrackpadInterface.Stop();
        StopThread();
    }
    private void StopThread()
    {
        Debug.Log("Shutting down Trackpad Thread...");
        TrackpadInterface.Stop();
}

}