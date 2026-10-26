// Assets/Scripts/Gameplay/PlayerState.cs   (UNTESTED; compile in Unity)
// SYNCED VARIABLES: the server writes them, every client sees the change.
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerState : NetworkBehaviour
{
    public NetworkVariable<int> Score = new NetworkVariable<int>(0);
    public NetworkVariable<Color> Tint = new NetworkVariable<Color>(Color.white);
    public NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>("Player");

    SpriteRenderer sprite;

    public override void OnNetworkSpawn()
    {
        sprite = GetComponent<SpriteRenderer>();
        Score.OnValueChanged += OnScore;
        Tint.OnValueChanged += (_, c) => { if (sprite) sprite.color = c; };
        if (sprite) sprite.color = Tint.Value;
        if (IsServer)                                    // only the server may write (default write permission)
        {
            Tint.Value = Color.HSVToRGB(Random.value, 0.7f, 1f);
            PlayerName.Value = $"P{OwnerClientId}";
        }
    }
    public override void OnNetworkDespawn() { Score.OnValueChanged -= OnScore; }
    void OnScore(int prev, int next) => Debug.Log($"{PlayerName.Value} score {prev}->{next}");
    public void AddScore(int n) { if (IsServer) Score.Value += n; }

    // REMOTE CALL: a client asks the server to change its display name.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void SetNameRpc(FixedString32Bytes n, RpcParams p = default)
    {
        if (p.Receive.SenderClientId == OwnerClientId) PlayerName.Value = n;   // server re-checks who is asking
    }
}
