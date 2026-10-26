// Assets/Scripts/Net/ClientConnect.cs   (UNTESTED)
using System;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class ClientConnect : MonoBehaviour
{
    // Values come from the lobby JSON: host, port (443), path ("/r/7003"), secure (true), token
    public void Join(string host, ushort port, string path, bool secure, string token)
    {
        Application.runInBackground = true;
        var nm  = NetworkManager.Singleton;
        var utp = (UnityTransport)nm.NetworkConfig.NetworkTransport;
        utp.UseWebSockets = true;
        utp.UseEncryption = secure;                       // wss (mapping to scheme UNVERIFIED: the spike proves it)
        if (secure) utp.SetClientSecrets(host);           // common name = host name; null CA = browser trust store
        utp.SetConnectionData(host, port);
        var cd = utp.ConnectionData; cd.WebSocketPath = path; utp.ConnectionData = cd;   // AFTER SetConnectionData

        nm.NetworkConfig.ConnectionApproval = true;
        nm.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(token + "");   // token is the approval payload
        nm.OnClientDisconnectCallback += id =>
        {
            if (id == nm.LocalClientId) Debug.Log("disconnected: " + nm.DisconnectReason);
        };
        if (!nm.StartClient()) Debug.LogError("StartClient failed");
    }
}
