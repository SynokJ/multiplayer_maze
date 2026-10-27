// Assets/Scripts/Net/DevHost.cs   (UNTESTED; compile in Unity)
// Editor-only shortcut: press H in Play Mode to start a HOST inside the Editor.
// Use it to test gameplay scripts without the VM. Host = server + client in one process, so IsServer and IsOwner
// are both true for your own player. A WebGL build can never be a host; this works in the Editor only.
// Tip: in host mode [Rpc(SendTo.NotServer)] does not reach the host; use SendTo.ClientsAndHost for broadcasts.
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class DevHost : MonoBehaviour
{
#if UNITY_EDITOR
    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsListening) return;
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.hKey.wasPressedThisFrame)
        {
            nm.NetworkConfig.ConnectionApproval = false;        // no lobby token in the Editor
            nm.StartHost();
        }
    }
#endif
}
