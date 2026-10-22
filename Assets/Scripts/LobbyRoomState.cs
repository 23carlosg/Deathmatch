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

        Players.Add(new PlayerEntry { ClientId = id, Name = NetworkLobbyManager.ObtenerNombre(id), Score = 0, Deaths = 0 });
    }

    void QuitarJugador(ulong id)
    {
        for (int i = 0; i < Players.Count; i++)
            if (Players[i].ClientId == id) { Players.RemoveAt(i); break; }
    }
}