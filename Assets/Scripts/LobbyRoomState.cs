using Unity.Netcode;
using UnityEngine;

public class LobbyRoomState : NetworkBehaviour
{
    public static LobbyRoomState Instance { get; private set; }
    public NetworkList<PlayerEntry> Players;

    void Awake()
    {
        Instance = this;
        Players = new NetworkList<PlayerEntry>();
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (!IsServer) return;

        Players.Clear();
        NetworkManager.OnClientConnectedCallback += AgregarJugador;
        NetworkManager.OnClientDisconnectCallback += QuitarJugador;
        foreach (ulong id in NetworkManager.ConnectedClientsIds) AgregarJugador(id);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= AgregarJugador;
            NetworkManager.OnClientDisconnectCallback -= QuitarJugador;
        }
    }

    void AgregarJugador(ulong id)
    {
        for (int i = 0; i < Players.Count; i++)
            if (Players[i].ClientId == id) return;

        Players.Add(new PlayerEntry
        {
            ClientId = id,
            Name = NetworkLobbyManager.ObtenerNombre(id),
            Score = 0,
            Deaths = 0,
            Listo = false
        });
    }

    void QuitarJugador(ulong id)
    {
        for (int i = 0; i < Players.Count; i++)
            if (Players[i].ClientId == id) { Players.RemoveAt(i); break; }
    }

    // Lo llama el cliente al apretar el botón LISTO
    public void AlternarListo()
    {
        if (IsSpawned) AlternarListoServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    void AlternarListoServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        if (id == NetworkManager.ServerClientId) return; // el host no usa "listo"

        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i].ClientId != id) continue;
            PlayerEntry p = Players[i];
            p.Listo = !p.Listo;
            Players[i] = p;
            return;
        }
    }

    // true si todos los clientes (sin contar al host) están listos
    public bool TodosListos()
    {
        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i].ClientId == NetworkManager.ServerClientId) continue;
            if (!Players[i].Listo) return false;
        }
        return true;
    }

    public int CantidadListos()
    {
        int n = 0;
        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i].ClientId == NetworkManager.ServerClientId) continue;
            if (Players[i].Listo) n++;
        }
        return n;
    }

    public int CantidadClientes()
    {
        int n = 0;
        for (int i = 0; i < Players.Count; i++)
            if (Players[i].ClientId != NetworkManager.ServerClientId) n++;
        return n;
    }
}