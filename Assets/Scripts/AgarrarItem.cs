using Unity.Netcode;
using UnityEngine;

public class AgarrarItem : Interaccion
{   
    public ItemData itemData;

    public override void Interactuar()
    {
        base.Interactuar();

        // El cliente le pide permiso al servidor para agarrar el item
        RequestPickupRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void RequestPickupRpc(RpcParams rpcParams = default)
    {
        // Esto corre solo en el servidor
        ulong clienteId = rpcParams.Receive.SenderClientId;
        int itemId = ItemDataBase.Instance.GetId(itemData);

        // Le devuelve el aviso solo al cliente que lo pidió
        AddItemToPickerRpc(itemId, RpcTarget.Single(clienteId, RpcTargetUse.Temp));

        // Despawnea el objeto: desaparece para todos los jugadores
        NetworkObject.Despawn(false);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void AddItemToPickerRpc(int itemId, RpcParams rpcParams = default)
    {
        ItemData data = ItemDataBase.Instance.GetItemById(itemId);
        if (data == null) return;

        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayerObject == null) return;

        Hotbar hotbar = localPlayerObject.GetComponent<Hotbar>();
        if (hotbar != null)
        {
            hotbar.AddItem(data);
        }

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

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Al despawnearse, se desactiva visualmente y deja de poder detectarse
        gameObject.SetActive(false);
    }
}




