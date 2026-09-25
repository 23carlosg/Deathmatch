using Unity.Netcode;
using UnityEngine;

public class AgarrarItem : Interaccion
{   
    public ItemData itemData;

    public override void Interactuar()
    {
        base.Interactuar();

        if (!NetworkObject.IsSpawned)
        {
            Debug.Log("Este item ya fue agarrado, no está spawneado");
            return;
        }

        RequestPickupRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void RequestPickupRpc(RpcParams rpcParams = default)
    {
        ulong clienteId = rpcParams.Receive.SenderClientId;
        int itemId = ItemDataBase.Instance.GetId(itemData);

        AddItemToPickerRpc(itemId, RpcTarget.Single(clienteId, RpcTargetUse.Temp));

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
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        gameObject.SetActive(false);
    }
}