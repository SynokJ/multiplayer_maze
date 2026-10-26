// Assets/Scripts/Net/LobbyClient.cs   (UNTESTED; compile in Unity)
// Minimal on-screen lobby for the WebGL client: type a name, Create or Join a room by code.
// It talks to the Node lobby through the same web address the page was loaded from (no CORS).
using System;
using System.Collections;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public class RoomInfo { public string id; public string name; public string state; public int players; public int maxPlayers; }
[Serializable] public class JoinResponse { public RoomInfo room; public string url; public string host; public int port; public bool secure; public string path; public string token; }
[Serializable] class PlayerBody { public string playerName; }

[RequireComponent(typeof(ClientConnect))]
public class LobbyClient : MonoBehaviour
{
    [Tooltip("Leave EMPTY for the WebGL build (it uses the page's own address). " +
             "Only fill in while testing inside the Editor, e.g. https://mymaze.duckdns.org")]
    public string baseUrlOverride = "";

    string playerName = "Player";
    string roomCode = "";
    string status = "";
    string joinedRoom = "";
    bool busy;

    string Base()
    {
        if (!string.IsNullOrEmpty(baseUrlOverride)) return baseUrlOverride.TrimEnd('/');
        return new Uri(Application.absoluteURL).GetLeftPart(UriPartial.Authority);   // e.g. https://mymaze.duckdns.org
    }

    void OnGUI()
    {
        GUI.matrix = Matrix4x4.Scale(new Vector3(1.6f, 1.6f, 1f));
        GUILayout.BeginArea(new Rect(10, 10, 360, 300));
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsConnectedClient)
        {
            GUILayout.Label("Connected to room " + joinedRoom);
            GUILayout.Label("Players in room: " + nm.ConnectedClientsIds.Count);
            if (GUILayout.Button("Leave")) nm.Shutdown();
        }
        else
        {
            GUILayout.Label("Your name");
            playerName = GUILayout.TextField(playerName, 24);
            GUI.enabled = !busy;
            if (GUILayout.Button("Create room")) StartCoroutine(Send("/api/rooms", Body()));
            GUILayout.Label("Room code");
            roomCode = GUILayout.TextField(roomCode, 5).ToUpperInvariant();
            if (GUILayout.Button("Join room") && roomCode.Length == 5)
                StartCoroutine(Send("/api/rooms/" + roomCode + "/join", Body()));
            GUI.enabled = true;
        }
        GUILayout.Label(status);
        GUILayout.EndArea();
    }

    string Body() => JsonUtility.ToJson(new PlayerBody { playerName = playerName });

    IEnumerator Send(string path, string json)
    {
        busy = true;
        status = "Contacting lobby...";
        using var req = new UnityWebRequest(Base() + path, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();
        busy = false;

        if (req.result != UnityWebRequest.Result.Success)
        {
            status = "Lobby said " + req.responseCode + ": " + req.downloadHandler.text;
            yield break;
        }
        var r = JsonUtility.FromJson<JoinResponse>(req.downloadHandler.text);
        joinedRoom = r.room.id;
        status = "Room " + r.room.id + ": connecting...";
        // host / port / path / secure / token all come from the lobby (server/lobby/src/server.mjs, connInfo)
        GetComponent<ClientConnect>().Join(r.host, (ushort)r.port, r.path, r.secure, r.token);
    }
}
