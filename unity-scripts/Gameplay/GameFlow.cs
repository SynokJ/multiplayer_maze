// Assets/Scripts/Gameplay/GameFlow.cs   (UNTESTED; compile in Unity)
// Put on an in-scene GameObject that also has a NetworkObject. Shows a synced game phase and a ready-up list.
using Unity.Netcode;
using UnityEngine;

public enum GamePhase : byte { Lobby, Countdown, Playing, Ended }

public class GameFlow : NetworkBehaviour
{
    public NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Lobby);
    public NetworkList<ulong> ReadyIds;
    void Awake() { ReadyIds = new NetworkList<ulong>(); }         // create in Awake, never in OnNetworkSpawn

    public override void OnNetworkSpawn()
    {
        Phase.OnValueChanged += (a, b) => Debug.Log($"phase {a}->{b}");
        if (IsServer) NetworkManager.OnClientDisconnectCallback += id => ReadyIds.Remove(id);
    }

    [Rpc(SendTo.Server)]
    public void SetReadyRpc(bool ready, RpcParams p = default)
    {
        var id = p.Receive.SenderClientId;
        if (Phase.Value != GamePhase.Lobby) return;
        if (ready && !ReadyIds.Contains(id)) ReadyIds.Add(id);
        else if (!ready) ReadyIds.Remove(id);
        if (ReadyIds.Count >= 1 && ReadyIds.Count == NetworkManager.ConnectedClientsIds.Count)
            Phase.Value = GamePhase.Playing;
    }
}
