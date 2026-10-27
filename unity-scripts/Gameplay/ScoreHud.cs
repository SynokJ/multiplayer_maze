// Assets/Scripts/Gameplay/ScoreHud.cs   (UNTESTED; compile in Unity)
// Shows the local player's name and score. Add it to the "Net" object. It only reads synced values; it never writes them.
using Unity.Netcode;
using UnityEngine;

public class ScoreHud : MonoBehaviour
{
    PlayerState mine;

    void Update()
    {
        if (mine != null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient) return;
        var playerObject = nm.LocalClient.PlayerObject;         // set once your own player object has spawned
        if (playerObject != null) mine = playerObject.GetComponent<PlayerState>();
    }

    void OnGUI()
    {
        if (mine == null) return;
        GUI.Label(new Rect(Screen.width - 260, 10, 250, 30), $"{mine.PlayerName.Value}   score: {mine.Score.Value}");
    }
}
