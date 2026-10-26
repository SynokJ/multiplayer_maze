// Assets/Scripts/Gameplay/MoverNet.cs   (UNTESTED; compile in Unity)
// Server-authoritative movement for a 2D player: the client sends only its INPUT, the server moves the player.
// Add a NetworkTransform (server authoritative, the default) to the same prefab so the position reaches everyone.
// Uses the new Input System because this project's Active Input Handling is "Input System Package (New)".
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class MoverNet : NetworkBehaviour
{
    public float speed = 4f;
    Vector2 input;

    void Update()
    {
        if (IsOwner)
        {
            var k = Keyboard.current;
            if (k != null)
            {
                var v = new Vector2(
                    (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0),
                    (k.wKey.isPressed || k.upArrowKey.isPressed ? 1 : 0) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1 : 0));
                if (v != input) { input = v; SendInputRpc(v); }
            }
        }
        if (IsServer)
            transform.position += (Vector3)(input.normalized * speed * Time.deltaTime);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Reliable)]
    void SendInputRpc(Vector2 v) => input = Vector2.ClampMagnitude(v, 1f);   // never trust the client's length
}
