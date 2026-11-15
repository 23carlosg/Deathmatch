using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Powerup por racha: 3 kills seguidas sin morir dan un uso de invisibilidad (5 s).
/// Estado en NetworkVariables que escribe SOLO el servidor; el dueño solo pide activarlo.
/// </summary>
public class PlayerPowerup : NetworkBehaviour
{
    [SerializeField] int rachaNecesaria = 3;
    [SerializeField] float duracion = 5f;
    [Header("Efecto para el dueño")]
    [SerializeField] Material materialFantasma; //arrastrar material del fantasma
    readonly Dictionary<Renderer, Material[]> originales = new Dictionary<Renderer, Material[]>();

    public float Duracion => duracion;
    public string TeclaTexto => "Q";

    public readonly NetworkVariable<bool> TienePowerup = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<bool> Invisible = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    int racha;              // solo se usa en el servidor
    Coroutine rutina;       // solo servidor

    PlayerNetworkMovement jugador;
    PlayerHealth salud;
    readonly List<Renderer> visibles = new List<Renderer>();

    void Awake()
    {
        jugador = GetComponent<PlayerNetworkMovement>();
        salud = GetComponent<PlayerHealth>();
    }

    public override void OnNetworkSpawn()
    {
        // Guardamos solo los renderers que estan prendidos ahora, para no prender
        // después uno que el prefab tenia apagado a proposito
        visibles.Clear();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r.enabled) visibles.Add(r);

        Invisible.OnValueChanged += AlCambiarInvisible;
        AplicarVisibilidad(Invisible.Value);
    }

    public override void OnNetworkDespawn()
    {
        Invisible.OnValueChanged -= AlCambiarInvisible;
    }

    void AlCambiarInvisible(bool anterior, bool actual) => AplicarVisibilidad(actual);

    // Los demas lo ven invisible y el dueño se sigue viendo
    void AplicarVisibilidad(bool invisible)
    {
        if (IsLocalPlayer)
        {
            AplicarFantasma(invisible);   // dueño se ve como fantasma
            return;
        }
        foreach (Renderer r in visibles)
            if (r != null) r.enabled = !invisible;
    }

    void AplicarFantasma(bool activo)
    {
        if (materialFantasma == null) return;

        // Cuerpo: lo maneja PlayerCameraRig
        if (jugador != null && jugador.CameraRig != null)
            jugador.CameraRig.SetFantasma(activo ? materialFantasma : null);

        // Arma: el rig no la toca, asi que se cambia aca
        foreach (Renderer r in visibles)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (jugador == null || !jugador.Weapons.EsParteDeUnArma(r.transform)) continue;

            if (activo)
            {
                if (originales.ContainsKey(r)) continue;
                Material[] actuales = r.sharedMaterials;
                originales[r] = actuales;

                Material[] fantasmas = new Material[actuales.Length];
                for (int i = 0; i < fantasmas.Length; i++) fantasmas[i] = materialFantasma;
                r.sharedMaterials = fantasmas;
            }
            else if (originales.TryGetValue(r, out Material[] guardados))
            {
                r.sharedMaterials = guardados;
                originales.Remove(r);
            }
        }
    }

    // ------------------------- SERVIDOR -------------------------

    // Lo llama PlayerHealth cuando ESTE jugador mató a otro
    public void RegistrarKill()
    {
        if (!IsServer) return;
        racha++;
        if (racha >= rachaNecesaria)
        {
            racha = 0;
            TienePowerup.Value = true;
        }
    }

    // Lo llama PlayerHealth cuando ESTE jugador murió
    public void RegistrarMuerte()
    {
        if (!IsServer) return;
        racha = 0;
        TienePowerup.Value = false;      // se pierde si muere sin usarlo
        Invisible.Value = false;
        if (rutina != null) { StopCoroutine(rutina); rutina = null; }
    }

    [Rpc(SendTo.Server)]   // por defecto solo lo puede invocar el dueño del objeto
    void ActivarRpc()
    {
        if (!TienePowerup.Value) return;     // el servidor valida
        if (salud != null && salud.Salud <= 0f) return;

        TienePowerup.Value = false;
        if (rutina != null) StopCoroutine(rutina);
        rutina = StartCoroutine(InvisibilidadRutina());
    }

    IEnumerator InvisibilidadRutina()
    {
        Invisible.Value = true;
        yield return new WaitForSeconds(duracion);
        Invisible.Value = false;
        rutina = null;
    }

    // ------------------------- DUEÑO -------------------------

    void Update()
    {
        if (!IsLocalPlayer || Keyboard.current == null) return;
        if (!TienePowerup.Value) return;
        if (jugador != null && (jugador.Muerto || PlayerNetworkMovement.InputBloqueado)) return;
        if (PauseMenuUI.Abierto || !NetworkLobbyManager.PuedeMoverse) return;

        if (Keyboard.current.qKey.wasPressedThisFrame)
            ActivarRpc();
    }
}