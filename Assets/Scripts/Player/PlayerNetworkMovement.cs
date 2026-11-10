using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Coordinador del jugador. Ya no contiene la logica: la reparte en componentes que viven
/// en el MISMO GameObject y los llama en orden desde Update.
///
///   PlayerMovement      movimiento, salto, gravedad, animacion de caminar
///   PlayerWeapons    armas, disparo, recarga, laser, destello, sonido
///   PlayerHealth     salud, muerte, revivir, kills
///   PlayerCameraRig  camara tercera persona, colision con paredes, fundido del cuerpo
///   PlayerBodySetup  alinea el modelo con el piso y ajusta la capsula
///   BotSeparation    evita atravesar a los wheelbots
///
/// Mantiene los nombres publicos de antes (Muerto, Salud, tieneRifle, tienePistola,
/// CambiarArma, TakeDamage, Morir, Revivir) para que AgarrarItem, el scoreboard y los bots
/// sigan funcionando sin tocarlos.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(PlayerWeapons))]
[RequireComponent(typeof(PlayerCameraRig))]
[RequireComponent(typeof(PlayerBodySetup))]
[RequireComponent(typeof(BotSeparation))]
public class PlayerNetworkMovement : NetworkBehaviour
{
    [Header("Referencias (se buscan solas si quedan vacias)")]
    public Animator animator;
    public PlayerCamera controlCamara;

    CharacterController controller;
    Camera camara;
    Transform camaraTransform;

    PlayerMovement movement;
    PlayerHealth health;
    PlayerWeapons weapons;
    PlayerCameraRig cameraRig;
    PlayerBodySetup body;

    Crosshair mira;
    bool esLocal = true;
    bool camaraLibreAnterior;

    public static bool PartidaTerminada => 
        ScoreboardState.Instance != null && ScoreboardState.Instance.IsSpawned 
        && ScoreboardState.Instance.Terminada.Value;

    public static bool InputBloqueado => PlayerCamera.CamaraLibre || PartidaTerminada;

    // ------------------------- ACCESO PARA LOS COMPONENTES -------------------------

    public CharacterController Controller => controller;
    public Camera Camara => camara;
    public Transform CamaraTransform => camaraTransform;
    public PlayerMovement Movement => movement;
    public PlayerHealth Health => health;
    public PlayerWeapons Weapons => weapons;
    public PlayerCameraRig CameraRig => cameraRig;

    // ------------------------- API PUBLICA DE SIEMPRE (la usan otros scripts) -------------------------

    public bool Muerto => health != null && health.Muerto;
    public float Salud => health != null ? health.Salud : 0f;

    // AgarrarItem hace jugador.tieneRifle = true; se reenvia a PlayerWeapons
    public bool tieneRifle
    {
        get => weapons != null && weapons.tieneRifle;
        set { if (weapons != null) weapons.tieneRifle = value; }
    }

    public bool tienePistola
    {
        get => weapons != null && weapons.tienePistola;
        set { if (weapons != null) weapons.tienePistola = value; }
    }

    public void CambiarArma(int nueva) { if (weapons != null) weapons.CambiarArma(nueva); }
    public void TakeDamage(float cantidad) { if (health != null) health.TakeDamage(cantidad); }
    public void Morir() { if (health != null) health.Morir(); }
    public void Revivir() { if (health != null) health.Revivir(); }

    // true si el transform pertenece al cuerpo/arma de ESTE jugador (para no autoimpactarse)
    public bool DelPropioCuerpo(Transform t)
    {
        if (t.IsChildOf(transform)) return true;
        if (controller != null && t.GetComponentInParent<CharacterController>() == controller) return true;
        return false;
    }

    // ------------------------- INICIO -------------------------

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();
        weapons = GetComponent<PlayerWeapons>();
        cameraRig = GetComponent<PlayerCameraRig>();
        body = GetComponent<PlayerBodySetup>();

        if (movement == null || health == null || weapons == null || cameraRig == null
            || body == null || GetComponent<BotSeparation>() == null)
        {
            Debug.LogError("[PlayerNetworkMovement] Faltan componentes en el prefab del jugador. " +
                           "Agregar: PlayerMovement, PlayerHealth, PlayerWeapons, PlayerCameraRig, PlayerBodySetup y BotSeparation.");
            enabled = false;
            return;
        }

        if (animator == null) animator = GetComponentInChildren<Animator>();
        camara = GetComponentInChildren<Camera>();
        if (camara != null)
        {
            camaraTransform = camara.transform;
            if (controlCamara == null) controlCamara = camara.GetComponent<PlayerCamera>();
        }

        // El orden importa: las armas primero (el cuerpo y la camara necesitan saber cuales son),
        // despues el cuerpo, y la camara al final (mide los renderers ya alineados)
        weapons.Iniciar(this);
        movement.Iniciar(this);
        health.Iniciar(this);
        body.Configurar(this);
        cameraRig.Iniciar(this);

        health.CambioDeMuerte += AlCambiarEstadoDeMuerte;
    }

    public override void OnDestroy()
    {
        if (health != null) health.CambioDeMuerte -= AlCambiarEstadoDeMuerte;
        base.OnDestroy();   // obligatorio: Netcode limpia el objeto acá
    }

    public override void OnNetworkSpawn()
    {
        esLocal = IsLocalPlayer;
        if (!esLocal)
        {
            ApagarLoLocal();
        }
        else
        {
            mira = Crosshair.Crear();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            PlayerCamera.DuenoMuerto = false;
            StartCoroutine(SalvedadPorSiElMapaNoAvisa());
            StartCoroutine(AsegurarMiraTrasCargaDeEscena());
        }
    }

    IEnumerator AsegurarMiraTrasCargaDeEscena()
    {
        yield return new WaitForSeconds(1f);
        if (mira == null || !mira.EstaVisible)
        {
            if (mira != null) Destroy(mira.gameObject);
            mira = Crosshair.Crear();
            if (Muerto) mira.Mostrar(false);
        }
    }

    IEnumerator SalvedadPorSiElMapaNoAvisa()
    {
        yield return new WaitForSeconds(5f);
        if (!NetworkLobbyManager.PuedeMoverse)
        {
            Debug.LogWarning("[PlayerNetworkMovement] El mapa nunca avisó que estaba listo, se fuerza el movimiento igual.");
            NetworkLobbyManager.ForzarMapaListoSiTarda();
        }
    }

    void ApagarLoLocal()
    {
        foreach (Camera c in GetComponentsInChildren<Camera>(true))
        {
            c.gameObject.SetActive(false);
            Destroy(c.gameObject);
        }
        foreach (PlayerCamera pc in GetComponentsInChildren<PlayerCamera>(true))
            pc.enabled = false;
        if (mira != null) mira.Mostrar(false);
    }

    // ------------------------- UPDATE -------------------------

    void Update()
    {
        // Jugadores remotos: solo reproducen lo que llega por red
        if (!esLocal)
        {
            if (camara != null) ApagarLoLocal();

            movement.ActualizarAnimacionRemota();
            weapons.ActualizarRemoto();
            health.Revisar();
            if (health.Muerto) movement.CaerMuerto();
            return;
        }

        if (Keyboard.current == null) return;
        if (!NetworkLobbyManager.PuedeMoverse) return;

        health.Revisar();

        if (health.Muerto)
        {
            movement.CaerMuerto();
            return;
        }

        bool libre = InputBloqueado;

        if (!libre)
        {
            weapons.LeerCambioDeArma();
            weapons.LeerApuntar();
            weapons.Recargar();
        }
        else weapons.ForzarNoApuntar();

        movement.Mover();      // dentro ignora el input si la camara es libre, pero sigue aplicando gravedad
        movement.Animar();     // sigue corriendo para que el personaje frene y caiga con animacion normal

        if (!libre)
        {
            weapons.Disparar();                       // si no, el rayo saldria desde la camara libre
            cameraRig.MoverCamara(weapons.Apuntando); // si no, pelearia con la camara libre (ya desparentada)
        }

        weapons.AplicarRecoilDelArma();
        ActualizarMira();
        ActualizarMiraSegunCamaraLibre();
    }

    void ActualizarMira()
    {
        if (mira == null) return;
        float apertura = weapons.Apuntando ? 4f : (movement.Corriendo ? 15f : 9f);
        if (!controller.isGrounded) apertura += 5f;
        mira.SetApertura(apertura);
    }

    void ActualizarMiraSegunCamaraLibre()
    {
        bool libre = InputBloqueado;
        if (libre == camaraLibreAnterior) return;
        camaraLibreAnterior = libre;
        if (mira != null && !Muerto) mira.Mostrar(!libre);
    }

    // PlayerHealth avisa cuando el jugador muere o revive (en cualquier maquina)
    void AlCambiarEstadoDeMuerte(bool muerto)
    {
        if (muerto) movement.AlMorir();

        if (esLocal)
        {
            weapons.ForzarNoApuntar();
            if (mira != null) mira.Mostrar(!muerto);
        }
    }
}