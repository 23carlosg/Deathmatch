using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class AgarrarItem : Interaccion
{
    public ItemData itemData;
    public float tiempoRespawn = 20f;

    readonly NetworkVariable<bool> disponible = new NetworkVariable<bool>(true);

    Renderer[] renderers;
    Collider[] colliders;
    Rigidbody rb;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        rb = GetComponent<Rigidbody>();

        disponible.OnValueChanged += AlCambiarDisponible;
        Aplicar(disponible.Value);
    }

    public override void OnNetworkDespawn()
    {
        disponible.OnValueChanged -= AlCambiarDisponible;
        base.OnNetworkDespawn();
    }

    void AlCambiarDisponible(bool anterior, bool nuevo) => Aplicar(nuevo);

    void Aplicar(bool visible)
    {
        foreach (var r in renderers) r.enabled = visible;
        foreach (var c in colliders) c.enabled = visible;
        // con el collider apagado, un Rigidbody caería atravesando el suelo
        if (rb != null) rb.isKinematic = !visible;
    }

    public override void Interactuar()
    {
        if (!disponible.Value) return;
        base.Interactuar();
        RequestPickupRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void RequestPickupRpc(RpcParams rpcParams = default)
    {
        // evita que dos jugadores lo agarren a la vez
        if (!disponible.Value) return;

        ulong clienteId = rpcParams.Receive.SenderClientId;
        int itemId = ItemDataBase.Instance.GetId(itemData);

        AddItemToPickerRpc(itemId, RpcTarget.Single(clienteId, RpcTargetUse.Temp));

        disponible.Value = false;
        StartCoroutine(Reaparecer());
    }

    IEnumerator Reaparecer()
    {
        yield return new WaitForSeconds(tiempoRespawn);
        disponible.Value = true;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void AddItemToPickerRpc(int itemId, RpcParams rpcParams = default)
    {
        ItemData data = ItemDataBase.Instance.GetItemById(itemId);
        if (data == null) return;

        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayerObject == null) return;

        Hotbar hotbar = localPlayerObject.GetComponent<Hotbar>();
        if (hotbar != null) hotbar.AddItem(data);

        PlayerNetworkMovement jugador = localPlayerObject.GetComponent<PlayerNetworkMovement>();
        if (jugador != null)
        {
            if (data.tipoArma == ItemData.TipoArma.Rifle)
            {
                jugador.tieneRifle = true;
                jugador.CambiarArma(1);
            }
            else if (data.tipoArma == ItemData.TipoArma.Pistola)
            {
                jugador.tienePistola = true;
                jugador.CambiarArma(2);
            }
        }
    }
}




