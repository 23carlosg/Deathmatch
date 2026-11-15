using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Cursor = UnityEngine.Cursor;

public struct PlayerEntry : INetworkSerializable, IEquatable<PlayerEntry>
{
    public ulong ClientId;
    public FixedString64Bytes Name;
    public int Score;   // kills del jugador
    public int Deaths;  // veces que murió
    public bool Listo;  // listo en la sala de espera
    public int Ping;    // ms

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Name);
        serializer.SerializeValue(ref Score);
        serializer.SerializeValue(ref Deaths);
        serializer.SerializeValue(ref Listo);
        serializer.SerializeValue(ref Ping);
    }

    public bool Equals(PlayerEntry other)
    {
        return ClientId == other.ClientId && Name == other.Name
            && Score == other.Score && Deaths == other.Deaths
            && Listo == other.Listo && Ping == other.Ping;
    }
}

public class ScoreboardState : NetworkBehaviour
{
    public static ScoreboardState Instance { get; private set; }

    // Esta lista se sincroniza sola del servidor a todos los clientes
    public NetworkList<PlayerEntry> Players;

    public const int KillsParaGanar = 10;

    public NetworkVariable<bool> Terminada = new NetworkVariable<bool>(false);
    public NetworkVariable<FixedString64Bytes> Ganador = new NetworkVariable<FixedString64Bytes>();

    void Awake()
    {
        Instance = this;
        Players = new NetworkList<PlayerEntry>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        NetworkManager.OnClientConnectedCallback += AddPlayer;
        NetworkManager.OnClientDisconnectCallback += RemovePlayer;

        foreach (ulong id in NetworkManager.ConnectedClientsIds)
            AddPlayer(id);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= AddPlayer;
            NetworkManager.OnClientDisconnectCallback -= RemovePlayer;
        }
    }

    void AddPlayer(ulong clientId)
    {
        for (int i = 0; i < Players.Count; i++)
            if (Players[i].ClientId == clientId) return;

        Players.Add(new PlayerEntry
        {
            ClientId = clientId,
            //Name = "Jugador " + clientId,
            Name = NetworkLobbyManager.ObtenerNombre(clientId),
            Score = 0,
            Deaths = 0
        });
    }

    void RemovePlayer(ulong clientId)
    {
        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i].ClientId == clientId)
            {
                Players.RemoveAt(i);
                break;
            }
        }
    }

    public void AddScore(ulong clientId, int points)
    {
        if (!IsServer) return;

        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i].ClientId != clientId) continue;

            PlayerEntry p = Players[i];
            p.Score += points;
            Players[i] = p;
            return;
        }
    }

    // Llamar solo desde el servidor cuando un jugador mata a otro
    public void RegistrarKill(ulong killerId, ulong victimId)
    {
        if (!IsServer || Terminada.Value) return;

        for (int i = 0; i < Players.Count; i++)
        {
            PlayerEntry p = Players[i];

            if (p.ClientId == killerId && killerId != victimId) // no sumar por suicidio
            {
                p.Score += 1;
                Players[i] = p;

                if (p.Score >= KillsParaGanar && !Terminada.Value)
                    {
                        Ganador.Value = p.Name;
                        Terminada.Value = true;
                    }
            }
            else if (p.ClientId == victimId)
            {
                p.Deaths += 1;
                Players[i] = p;
            }
        }
    }

    // Ping
    float proximoPing;

    void Update()
    {
        if (!IsServer || !IsSpawned) return;
        if (Time.unscaledTime < proximoPing) return;
        proximoPing = Time.unscaledTime + 1f;   // una vez por segundo
        ActualizarPings();
    }

    void ActualizarPings()
    {
        var utp = NetworkManager.NetworkConfig.NetworkTransport
                as Unity.Netcode.Transports.UTP.UnityTransport;
        if (utp == null) return;

        for (int i = 0; i < Players.Count; i++)
        {
            PlayerEntry p = Players[i];

            // El host no tiene latencia contra sí mismo
            int ping = p.ClientId == NetworkManager.ServerClientId
                ? 0
                : (int)utp.GetCurrentRtt(p.ClientId);

            // Solo se escribe si cambió bastante, para no saturar la red
            if (Mathf.Abs(ping - p.Ping) >= 5)
            {
                p.Ping = ping;
                Players[i] = p;
            }
        }
    }
}