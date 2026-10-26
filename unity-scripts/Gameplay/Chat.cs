// Assets/Scripts/Gameplay/Chat.cs   (UNTESTED; compile in Unity)
// REMOTE CALLS: client -> server (SendTo.Server), then server -> all clients (SendTo.ClientsAndHost).
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class Chat : NetworkBehaviour
{
    [Rpc(SendTo.Server)]
    public void SendChatRpc(FixedString128Bytes msg, RpcParams p = default)
    {
        if (msg.Length == 0) return;                          // server validates: length, rate, content
        BroadcastChatRpc(p.Receive.SenderClientId, msg);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void BroadcastChatRpc(ulong from, FixedString128Bytes msg) => Debug.Log($"[{from}] {msg}");
}
