// Assets/Scripts/Gameplay/CoinSpawner.cs   (UNTESTED; compile in Unity)
// Put it on the same in-scene object as GameFlow (that object needs a NetworkObject).
// When the synced game phase becomes Playing, the SERVER spawns the coins; clients receive them automatically.
using Unity.Netcode;
using UnityEngine;

public class CoinSpawner : NetworkBehaviour
{
    [SerializeField] GameObject coinPrefab;     // must be registered in the Network Prefabs List
    [SerializeField] int count = 10;
    [SerializeField] float area = 8f;

    GameFlow flow;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        flow = GetComponent<GameFlow>();
        flow.Phase.OnValueChanged += OnPhase;
    }

    public override void OnNetworkDespawn()
    {
        if (flow != null) flow.Phase.OnValueChanged -= OnPhase;
    }

    void OnPhase(GamePhase previous, GamePhase next)
    {
        if (next != GamePhase.Playing) return;
        for (int i = 0; i < count; i++)
        {
            var pos = new Vector3(Random.Range(-area, area), Random.Range(-area, area), 0f);
            // Pass Quaternion.identity: the default value of the rotation parameter is an all-zero quaternion.
            NetworkObject.InstantiateAndSpawn(coinPrefab, NetworkManager, position: pos, rotation: Quaternion.identity);
        }
    }
}
