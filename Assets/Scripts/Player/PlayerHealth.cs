using System.Collections;
using Unity.Netcode;
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

    // Aplica en cada maquina el estado que vino del servidor (vivo/muerto). Se llama cada frame:
    // si el valor no cambio, no hace nada.
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
                // Sacar al Animator del salto/aterrizaje para que pueda entrar a la animación de muerte
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
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        CambioDeMuerte?.Invoke(Muerto);
    }

    // ------------------------- DAÑO (servidor) -------------------------

    // Punto de entrada clasico (misma firma que el Player viejo) para sistemas externos de daño
    public void TakeDamage(float cantidad)
    {
        RecibirDanioServerRpc(cantidad);
    }

    [ServerRpc]
    void RecibirDanioServerRpc(float cantidad)
    {
        // Sin atacante conocido; se usa el propio id (RegistrarKill ignora suicidios)
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

            StartCoroutine(RevivirAutomaticamenteCoroutine());
        }
    }

    // ------------------------- MORIR / REVIVIR -------------------------

    // Muerte pidiendo el daño por el camino de red (lo aplica el servidor)
    public void Morir()
    {
        if (Muerto) return;
        RecibirDanioServerRpc(9999f);
    }

    // Revive por red: el servidor restaura la salud y reposiciona
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

    // Lógica real de revivir; la puede llamar el RPC (revivir manual) o la corrutina (revivir automático)
    void RevivirEnServidor()
    {
        redSalud.Value = saludMaxima;
        Vector3 posicion = NetworkLobbyManager.ObtenerPuntoDeRespawn();
        RevivirClientRpc(posicion);
    }

    [ClientRpc]
    void RevivirClientRpc(Vector3 posicion)
    {
        transform.position = posicion;
        jugador.Movement.ReiniciarVelocidades();
    }
}