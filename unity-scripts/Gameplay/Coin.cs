// Assets/Scripts/Gameplay/Coin.cs   (UNTESTED; compile in Unity)
// Coin prefab = NetworkObject + CircleCollider2D (Is Trigger) + this script. Add it to the Network Prefabs List.
// The player prefab needs a Rigidbody2D (Body Type: Kinematic): Unity only raises trigger events when one side has a Rigidbody2D.
using Unity.Netcode;
using UnityEngine;

public class Coin : NetworkBehaviour
{
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer || !IsSpawned) return;                    // only the server decides who gets the coin
        if (!other.TryGetComponent(out PlayerState player)) return;
        player.AddScore(1);                                     // writes a NetworkVariable (server only)
        NetworkObject.Despawn();                                // removes the coin on the server and on every client
    }
}
