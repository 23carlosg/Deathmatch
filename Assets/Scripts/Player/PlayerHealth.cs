using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Salud del jugador. La salud vive en una NetworkVariable que SOLO escribe el servidor
/// (un cliente no puede tocar la salud de nadie). Aqui tambien estan morir y revivir.
/// Avisa con el evento CambioDeMuerte cuando el jugador muere o revive, en cualquier maquina.
/// </summary>
public class PlayerHealth : NetworkBehaviour
{
    [Header("Salud")]
    public float saludMaxima = 100f;
    public float tiempoParaRevivir = 5f;

    readonly NetworkVariable<float> redSalud = new NetworkVariable<float>(100f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool Muerto { get; private set; }
    public float Salud => redSalud.Value;

    // true = acaba de morir, false = acaba de revivir
    public event System.Action<bool> CambioDeMuerte;

    PlayerNetworkMovement jugador;
    bool reviviendoAutomaticamente = false;

    public void Iniciar(PlayerNetworkMovement j)
    {
        jugador = j;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsLocalPlayer) Revisar();
    }

    // ------------------------- ESTADO (todas las maquinas) -------------------------

    public void Revisar()
    {
        bool estaMuerto = redSalud.Value <= 0f;
        if (estaMuerto == Muerto) return;

        Muerto = estaMuerto;

        CharacterController controller = jugador.Controller;
        if (controller != null && !Muerto) controller.enabled = true;

        Animator animator = jugador.animator;
        if (animator != null)
        {
            if (Muerto)
            {
                animator.SetBool("isGrounded", true);
                animator.SetFloat("VelX", 0f);
                animator.SetFloat("VelY", 0f);
            }
            animator.SetBool("Dead", Muerto);
        }

        if (IsLocalPlayer)
        {
            PlayerCamera.DuenoMuerto = Muerto;
            if (Muerto)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                if (!PlayerNetworkMovement.PartidaTerminada)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        CambioDeMuerte?.Invoke(Muerto);
    }

    // ------------------------- DAÑO (servidor) -------------------------

    public void TakeDamage(float cantidad)
    {
        RecibirDanioServerRpc(cantidad);
    }

    [ServerRpc]
    void RecibirDanioServerRpc(float cantidad)
    {
        RecibirDanioEnServidor(cantidad, OwnerClientId);
    }

    // Corre SOLO en el servidor. Lo llama PlayerWeapons (disparo) o RecibirDanioServerRpc.
    public void RecibirDanioEnServidor(float cantidad, ulong atacanteId)
    {
        if (Muerto || redSalud.Value <= 0f) return;
        redSalud.Value = Mathf.Max(0f, redSalud.Value - cantidad);

        if (redSalud.Value <= 0f && !reviviendoAutomaticamente)
        {
            reviviendoAutomaticamente = true;

            if (ScoreboardState.Instance != null)
                ScoreboardState.Instance.RegistrarKill(atacanteId, OwnerClientId);

            var propio = GetComponent<PlayerPowerup>();
            if (propio != null) propio.RegistrarMuerte();

            if (atacanteId != OwnerClientId
                && NetworkManager.Singleton.ConnectedClients.TryGetValue(atacanteId, out var cliente)
                && cliente.PlayerObject != null)
            {
                var del_asesino = cliente.PlayerObject.GetComponent<PlayerPowerup>();
                if (del_asesino != null) del_asesino.RegistrarKill();
            }

            StartCoroutine(RevivirAutomaticamenteCoroutine());
        }
    }

    public void Curar(float cantidad)
    {
        if (!IsServer || Muerto || redSalud.Value <= 0f) return;
        if (redSalud.Value >= saludMaxima) return;

        redSalud.Value = Mathf.Min(saludMaxima, redSalud.Value + cantidad);
    }

    // ------------------------- MORIR / REVIVIR -------------------------

    public void Morir()
    {
        if (Muerto) return;
        RecibirDanioServerRpc(9999f);
    }

    public void Revivir()
    {
        if (!Muerto) return;
        RevivirServerRpc();
    }

    [ServerRpc]
    void RevivirServerRpc()
    {
        RevivirEnServidor();
    }

    IEnumerator RevivirAutomaticamenteCoroutine()
    {
        yield return new WaitForSeconds(tiempoParaRevivir);
        RevivirEnServidor();
        reviviendoAutomaticamente = false;
    }

    void RevivirEnServidor()
    {
        redSalud.Value = saludMaxima;
        Vector3 posicion = NetworkLobbyManager.ObtenerPuntoDeRespawn();
        RevivirClientRpc(posicion);
    }

    [ClientRpc]
    void RevivirClientRpc(Vector3 posicion)
    {
        Teletransportar(posicion);
        jugador.Movement.ReiniciarVelocidades();
    }

    // Lo llama el servidor al crear al jugador, para asegurar la posicion inicial
    public void PosicionarEnServidor(Vector3 posicion)
    {
        if (!IsServer) return;
        PosicionarClientRpc(posicion);
    }

    [ClientRpc]
    void PosicionarClientRpc(Vector3 posicion)
    {
        Teletransportar(posicion);
        jugador.Movement.ReiniciarVelocidades();
    }

    // El CharacterController pisa transform.position si esta activo, por eso se apaga un instante.
    // Con NetworkTransform de autoridad del cliente, Teleport evita que el salto se interpole.
    void Teletransportar(Vector3 posicion)
    {
        CharacterController cc = jugador.Controller;
        bool estabaActivo = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;

        transform.position = posicion;

        var nt = GetComponent<NetworkTransform>();
        if (nt != null && nt.CanCommitToTransform)
            nt.Teleport(posicion, transform.rotation, transform.localScale);

        if (cc != null) cc.enabled = estabaActivo;
    }
}