using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Jugador en red para el personaje Astra (tercera persona).
/// - Movimiento WASD/flechas con CharacterController: caminar, correr con Shift, saltar con Espacio
/// - Camara tercera persona: el mouse gira el CUERPO (yaw) y la camara (pitch, es hija del cuerpo)
/// - Apuntar con click derecho: camara sobre el hombro derecho + zoom (FOV) + mira cerrada
/// - Disparar con click izquierdo: raycast desde la camara (a donde apunta el crosshair), danio
///   aplicado por el SERVIDOR, flash en la boca del arma, efecto de impacto y retroceso de camara
/// - Recargar con R (tiempo real), cambiar de arma con 1/2/3 (el modelo viaja por red)
/// - Animaciones por codigo: VelX/VelY alimentan el blend direccional; el int Arma y el bool
///   isGrounded eligen los estados de salto, decididos en el mismo frame del impulso.
/// - Red: posicion via ClientNetworkTransform (autoridad del dueño), estado via NetworkVariables
///   del dueño; la salud la escribe solo el servidor.
/// - Los instantes de disparo viajan por Rpc para verse en el remoto sin retardo.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerNetworkMovement : NetworkBehaviour
{
    [Header("Movimiento")]
    public float velocidadCaminar = 2.5f;
    public float velocidadCorrer = 6f;
    [Range(0f, 1f)] public float controlEnElAire = 0.35f;
    public float aceleracion = 12f;

    [Header("Salto y gravedad")]
    public float alturaSalto = 1.2f;
    [Tooltip("Multiplicador de la altura de salto SOLO con el rifle en la mano (1 = salto normal)")]
    public float multiplicadorSaltoRifle = 1.5f;
    public float gravedad = 22f;
    public float coyoteTime = 0.15f;
    public float bufferSalto = 0.15f;

    [Header("Camara (tercera persona)")]
    public float alturaCamara = 1.6f;
    public float atrasCamara = 2.5f;
    public float ajusteDePies = 0f;
    public float hombroApuntado = 0.45f;
    public float desvioCamaraLibre = 0.6f;
    public float fovApuntado = 30f;
    public float suavizadoCamara = 10f;

    [Header("Disparo - Rifle")]
    public float danioRifle = 20f;
    public float retrocesoRifle = 0.5f;
    public float retrocesoRotacionRifle = 6f;
    public float cadenciaRifle = 6f;

    [Header("Disparo - Pistola")]
    public float danioPistola = 25f;
    public float retrocesoPistola = 0f;
    public float retrocesoRotacionPistola = 6f;
    public float cadenciaPistola = 4f; 

    [Header("Disparo - General")]
    public Transform bocaDelArma;
    public GameObject efectoDisparo;
    public GameObject efectoImpacto;
    public float alcance = 200f;
    public float dispersion = 1.5f;

    [Header("Sonido")]
    public AudioClip sonidoDisparo;
    [Range(0f, 1f)] public float volumenDisparo = 1f;

    [Header("Salud")]
    public float saludMaxima = 100f;
    public float tiempoParaRevivir = 5f;

    [Header("Referencias (se buscan solas si quedan vacias)")]
    public Animator animator;
    public PlayerCamera controlCamara;

    [Header("Armas (modelos en la mano; se buscan solos si quedan vacios)")]
    public GameObject armaRifle;           // modelo del rifle montado en la mano derecha
    public GameObject armaPistola; 
    // Qué armas tiene el jugador disponibles (se activan al agarrarlas con AgarrarItem)
    public bool tieneRifle = false;
    public bool tienePistola = false;        // modelo de la pistola montado en la mano derecha

    readonly NetworkVariable<float> redVelX = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> redVelY = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<int> redArma = new NetworkVariable<int>(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> redSalud = new NetworkVariable<float>(100f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> redEnElPiso = new NetworkVariable<bool>(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool Muerto { get; private set; }
    private bool reviviendoAutomaticamente = false;
    public float Salud => redSalud.Value;

    bool esLocal = true;
    CharacterController controller;
    Camera camara;
    Transform camaraTransform;
    Crosshair mira;
    AudioSource audio;

    Vector3 velocidadHorizontal;
    float velocidadVertical;
    float velXSuave, velYSuave;
    float coyoteTimer;
    float bufferSaltoTimer;
    bool corriendo;
    bool apuntando;
    bool recargando;
    float recargaHasta;
    float proximoDisparoPermitido;
    bool enSaltoReal;
    float tiempoEnElAire;
    float recoilArma;
    Vector3 posReposoRifle, posReposoPistola;
    Quaternion rotReposoRifle, rotReposoPistola;
    int armaActual = 3;      // 1 rifle, 2 pistola, 3 desarmado
    int armaAplicada = -1;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        camara = GetComponentInChildren<Camera>();
        if (camara != null)
        {
            camaraTransform = camara.transform;
            if (controlCamara == null) controlCamara = camara.GetComponent<PlayerCamera>();
        }
        BuscarArmas();
        ConfigurarAudio();
        AplicarArma(armaActual);
        GuardarPoseDeReposoDeLasArmas();
        ConfigurarCuerpo();
    }

    public override void OnNetworkSpawn()
    {
        esLocal = IsLocalPlayer;
        if (!esLocal)
        {
            ApagarLoLocal();
            velXSuave = redVelX.Value;
            velYSuave = redVelY.Value;
            AplicarArma(redArma.Value);
            RevisarEstadoDeSalud();
        }
        else
        {
            mira = Crosshair.Crear();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            PlayerCamera.DueñoMuerto = false;
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

    void Update()
    {
        if (!esLocal)
        {
            if (camara != null) ApagarLoLocal();

            if (animator == null) return;
            float t = 1f - Mathf.Exp(-15f * Time.deltaTime);
            velXSuave = Mathf.Lerp(velXSuave, redVelX.Value, t);
            velYSuave = Mathf.LerpAngle(velYSuave, redVelY.Value, t);
            animator.SetFloat("VelX", velXSuave);
            animator.SetFloat("VelY", velYSuave);
            bool pisoRemoto = redEnElPiso.Value;
            animator.SetBool("isGrounded", pisoRemoto);
            AplicarArma(redArma.Value);
            AplicarRecoilDelArma();
            RevisarEstadoDeSalud();
            return;
        }

        if (Keyboard.current == null) return;

        if (!NetworkLobbyManager.PuedeMoverse) return;

        RevisarEstadoDeSalud();

        if (Muerto) return;

        LeerCambioDeArma();
        LeerApuntar();
        Recargar();

        Mover();
        Animar();
        Disparar();
        AplicarRecoilDelArma();
        MoverCamara();
        ActualizarMira();
    }

    // ------------------------- SEPARACIÓN CON BOTS -------------------------

    [Header("Colisión con bots")]
    [Tooltip("Radio máximo del cuerpo de un wheelbot (para no atravesarlo)")]
    public float radioCuerpoBot = 0.55f;

    // El CharacterController NO colisiona con colliders que se mueven por transform
    // (los bots van cinemáticos por NavMeshAgent, y el NavMeshObstacle solo hace que
    // ELLOS te esquiven a vos). Sin esto, caminando hacia un bot lo atravesás: una vez
    // solapados, el CC nunca más detecta la "pared". LateUpdate corrige la penetracion
    // empujando la capsula fuera de la esfera del bot; controller.Move respeta paredes,
    // asi que el empuje nunca te mete en la geometria del mapa.
    void LateUpdate()
    {
        if (!esLocal || Muerto || controller == null || !controller.enabled) return;

        Vector3 centroMio = transform.position + controller.center;
        float alcance = controller.radius + radioCuerpoBot;
        Collider[] cercanos = Physics.OverlapSphere(centroMio, alcance, ~0, QueryTriggerInteraction.Ignore);

        foreach (Collider c in cercanos)
        {
            if (!(c is SphereCollider)) continue;
            if (c.GetComponentInParent<NetworkWheelBotWander>() == null) continue;

            Vector3 centroBot = c.transform.TransformPoint(((SphereCollider)c).center);
            float escalaBot = Mathf.Max(
                Mathf.Abs(c.transform.lossyScale.x),
                Mathf.Abs(c.transform.lossyScale.y),
                Mathf.Abs(c.transform.lossyScale.z));
            float radioBot = ((SphereCollider)c).radius * escalaBot;

            Vector3 desdeBot = centroMio - centroBot;
            desdeBot.y = 0f;
            float dist = desdeBot.magnitude;
            float minima = radioBot + controller.radius - controller.skinWidth;

            if (dist < minima && dist > 0.0001f)
            {
                controller.Move(desdeBot / dist * (minima - dist));
            }
        }
    }

    // ------------------------- ARMAS EN LA MANO -------------------------

    void LeerCambioDeArma()
    {
        // Tecla 1 = slot 1 de la hotbar = pistola (arma 2 en PlayerAstra)
        if (Keyboard.current.digit1Key.wasPressedThisFrame && tienePistola) CambiarArma(2);

        // Tecla 2 = slot 2 de la hotbar = rifle (arma 1 en PlayerAstra)
        if (Keyboard.current.digit2Key.wasPressedThisFrame && tieneRifle) CambiarArma(1);

        if (Keyboard.current.digit3Key.wasPressedThisFrame) CambiarArma(3);
    }

    // Cambia el arma en la mano (1 rifle, 2 pistola, 3 desarmado) y lo publica por red
    public void CambiarArma(int nueva)
    {
        if (Muerto || armaActual == nueva) return;
        armaActual = nueva;
        redArma.Value = nueva;
        recoilArma = 0f;
        DevolverArmasAReposo();
        AplicarArma(nueva);
    }

    void AplicarArma(int arma)
    {
        if (armaAplicada == arma) return;
        armaAplicada = arma;
        armaActual = arma;
        if (armaRifle != null) armaRifle.SetActive(arma == 1);
        if (armaPistola != null) armaPistola.SetActive(arma == 2);
        if (animator != null)
        {
            animator.SetInteger("Arma", arma == 3 ? 0 : arma);
        }
    }

    void BuscarArmas()
    {
        Transform raiz = animator != null ? animator.transform : transform;
        Transform rifle = null;
        foreach (Transform t in raiz.GetComponentsInChildren<Transform>(true))
        {
            if (rifle != null && t.IsChildOf(rifle)) continue;
            string n = t.name.ToLower();
            if (armaRifle == null && n.Contains("rifle"))
            {
                armaRifle = t.gameObject;
                rifle = t;
            }
            else if (armaPistola == null && n == "base" && t.GetComponentInChildren<Renderer>() != null)
            {
                armaPistola = t.gameObject;
            }
            else if (bocaDelArma == null && n.Contains("muzzle"))
            {
                bocaDelArma = t;
            }
        }
        if (armaRifle == null && armaPistola == null)
            Debug.LogWarning("[PlayerNetworkMovement] No encontre los modelos de las armas bajo la mano: arrastralos en el Inspector del prefab (Arma Rifle / Arma Pistola).");
    }

    void GuardarPoseDeReposoDeLasArmas()
    {
        if (armaRifle != null)
        {
            posReposoRifle = armaRifle.transform.localPosition;
            rotReposoRifle = armaRifle.transform.localRotation;
        }
        if (armaPistola != null)
        {
            posReposoPistola = armaPistola.transform.localPosition;
            rotReposoPistola = armaPistola.transform.localRotation;
        }
    }

    void DevolverArmasAReposo()
    {
        if (armaRifle != null)
        {
            armaRifle.transform.localPosition = posReposoRifle;
            armaRifle.transform.localRotation = rotReposoRifle;
        }
        if (armaPistola != null)
        {
            armaPistola.transform.localPosition = posReposoPistola;
            armaPistola.transform.localRotation = rotReposoPistola;
        }
    }

    void AplicarRecoilDelArma()
    {
        if (recoilArma <= 0f) return;
        recoilArma = Mathf.Max(0f, recoilArma - Time.deltaTime * 3.5f);

        Transform activa;
        Vector3 posReposo;
        Quaternion rotReposo;
        if (armaActual == 2 && armaPistola != null)
        {
            activa = armaPistola.transform; posReposo = posReposoPistola; rotReposo = rotReposoPistola;
        }
        else if (armaRifle != null)
        {
            activa = armaRifle.transform; posReposo = posReposoRifle; rotReposo = rotReposoRifle;
        }
        else return;

        activa.localPosition = posReposo;
        Quaternion reposoMundo = activa.parent != null ? activa.parent.rotation * rotReposo : rotReposo;
        Vector3 ejeDerechaDelArma = reposoMundo * Vector3.right;
        float rotacionRecoilActual = armaActual == 2 ? retrocesoRotacionPistola : retrocesoRotacionRifle;

        Debug.Log($"recoilArma={recoilArma}, armaActual={armaActual}, rotacionRecoilActual={rotacionRecoilActual}, activa={activa.name}"); // TEMPORAL

        Quaternion inclinadaMundo = Quaternion.AngleAxis(-recoilArma * rotacionRecoilActual, ejeDerechaDelArma) * reposoMundo;
        activa.localRotation = activa.parent != null
            ? Quaternion.Inverse(activa.parent.rotation) * inclinadaMundo
            : inclinadaMundo;
    }

    // ------------------------- MOVIMIENTO -------------------------

    void Mover()
    {
        bool enSuelo = controller.isGrounded;

        coyoteTimer = enSuelo ? coyoteTime : coyoteTimer - Time.deltaTime;
        if (Keyboard.current.spaceKey.wasPressedThisFrame) bufferSaltoTimer = bufferSalto;
        else bufferSaltoTimer -= Time.deltaTime;

        float x = 0f, z = 0f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) x -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) x += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) z -= 1f;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) z += 1f;
        Vector2 entrada = Vector2.ClampMagnitude(new Vector2(x, z), 1f);

        corriendo = Keyboard.current.leftShiftKey.isPressed;
        float velocidadObjetivo = corriendo ? velocidadCorrer : velocidadCaminar;
        Vector3 deseada = transform.TransformDirection(new Vector3(entrada.x, 0f, entrada.y)) * velocidadObjetivo;

        // salto
        if (bufferSaltoTimer > 0f && coyoteTimer > 0f)
        {
            float altura = alturaSalto * (armaActual == 1 ? multiplicadorSaltoRifle : 1f);
            velocidadVertical = Mathf.Sqrt(2f * gravedad * altura);
            coyoteTimer = 0f;
            bufferSaltoTimer = 0f;
            enSaltoReal = true;

            if (animator != null) animator.SetBool("isGrounded", false);
        }

        // gravedad
        if (enSuelo && velocidadVertical < 0f) velocidadVertical = -2f;
        velocidadVertical -= gravedad * Time.deltaTime;

        Vector3 objetivo = enSuelo ? deseada : Vector3.Lerp(velocidadHorizontal, deseada, controlEnElAire);
        float t = 1f - Mathf.Exp(-aceleracion * Time.deltaTime);
        velocidadHorizontal = Vector3.Lerp(velocidadHorizontal, objetivo, t);

        controller.Move((velocidadHorizontal + Vector3.up * velocidadVertical) * Time.deltaTime);
    }

    void Animar()
    {
        Vector3 local = transform.InverseTransformDirection(velocidadHorizontal);
        Vector2 dir = new Vector2(local.x, local.z);
        float rapidez = dir.magnitude;

        if (rapidez > 0.05f)
        {
            dir /= rapidez;
            dir *= corriendo ? 2f : 1f;
        }
        float velX = dir.x;
        float velY = dir.y;

        if (controller.isGrounded && velocidadVertical <= 0.01f) enSaltoReal = false;
        if (controller.isGrounded) tiempoEnElAire = 0f; else tiempoEnElAire += Time.deltaTime;

        bool enElPiso = !enSaltoReal && tiempoEnElAire < 0.1f;

        float t = 1f - Mathf.Exp(-14f * Time.deltaTime);
        velXSuave = Mathf.Lerp(velXSuave, velX, t);
        velYSuave = Mathf.Lerp(velYSuave, velY, t);

        if (animator != null)
        {
            animator.SetFloat("VelX", velXSuave);
            animator.SetFloat("VelY", velYSuave);
            animator.SetBool("isGrounded", enElPiso);
        }

        redVelX.Value = velX;
        redVelY.Value = velY;
        redEnElPiso.Value = enElPiso;
    }

    // ------------------------- CAMARA -------------------------

    // La camara es hija del cuerpo (el yaw lo aporta el cuerpo via PlayerCamera); aca se ajusta
    // su posicion local: altura al caminar, sobre el hombro y cerca al apuntar, con zoom (FOV)
    void MoverCamara()
    {
        if (camaraTransform == null) return;
        float alturaObjetivo = apuntando ? alturaCamara * 0.85f : alturaCamara;
        float xObjetivo = apuntando ? hombroApuntado : desvioCamaraLibre;
        float zObjetivo = apuntando ? -Mathf.Max(0.5f, atrasCamara * 0.22f) : -atrasCamara;

        float t = 1f - Mathf.Exp(-suavizadoCamara * Time.deltaTime);
        Vector3 actual = camaraTransform.localPosition;
        actual.x = Mathf.Lerp(actual.x, xObjetivo, t);
        actual.y = Mathf.Lerp(actual.y, alturaObjetivo, t);
        actual.z = Mathf.Lerp(actual.z, zObjetivo, t);
        camaraTransform.localPosition = actual;

        if (camara != null)
            camara.fieldOfView = Mathf.Lerp(camara.fieldOfView, apuntando ? fovApuntado : 60f, t);
    }

    // ------------------------- APUNTAR / MIRA -------------------------

    void LeerApuntar()
    {
        if (armaActual == 3) { apuntando = false; return; } // Desarmado: no puede apuntar
        apuntando = Mouse.current != null && Mouse.current.rightButton.isPressed && !recargando;
    }

    void ActualizarMira()
    {
        if (mira == null) return;
        float apertura = apuntando ? 4f : (corriendo ? 15f : 9f);
        if (!controller.isGrounded) apertura += 5f;
        mira.SetApertura(apertura);
    }

    // ------------------------- DISPARO -------------------------

void Disparar()
{
    if (armaActual == 3) return;
    if (Mouse.current == null) return;

    // La pistola dispara solo con click (una vez por apretada); el rifle es automático mientras se mantiene
    bool gatilloApretado = armaActual == 2
        ? Mouse.current.leftButton.wasPressedThisFrame
        : Mouse.current.leftButton.isPressed;

    if (!gatilloApretado) return;
    if (Time.time < proximoDisparoPermitido || recargando) return;

    float danioActual = armaActual == 2 ? danioPistola : danioRifle;
    float retrocesoActual = armaActual == 2 ? retrocesoPistola : retrocesoRifle;
    float cadenciaActual = armaActual == 2 ? cadenciaPistola : cadenciaRifle;
   
    proximoDisparoPermitido = Time.time + 1f / cadenciaActual;

    // el raycast sale desde la camara: dispara a donde apunta la pantalla (el crosshair)
    Vector3 origen = camaraTransform != null
        ? camaraTransform.position + camaraTransform.forward * 0.2f
        : PuntoDeDisparo();
    Vector3 direccion = camaraTransform != null ? camaraTransform.forward : transform.forward;

    // dispersion al disparar desde la cadera; apuntando el disparo es preciso
    if (!apuntando && camaraTransform != null)
    {
        float angulo = Random.Range(-dispersion, dispersion);
        direccion = Quaternion.AngleAxis(angulo, camaraTransform.up) * direccion;
    }

    // el rayo nace detras del cuerpo: se descarta cualquier impacto del propio jugador
    RaycastHit elegido = default;
    bool acerto = false;
    RaycastHit[] impactos = Physics.RaycastAll(origen, direccion, alcance, ~0, QueryTriggerInteraction.Ignore);
    System.Array.Sort(impactos, (a, b) => a.distance.CompareTo(b.distance));
    foreach (RaycastHit h in impactos)
    {
        if (DelPropioCuerpo(h.collider.transform)) continue;
        elegido = h;
        acerto = true;
        break;
    }

    if (acerto)
    {
        NetworkObject objetivo = elegido.collider.transform.root.GetComponent<NetworkObject>();
        if (objetivo != null && objetivo.IsPlayerObject)
        {
            DanioAJugadorServerRpc(danioActual, objetivo.OwnerClientId);
        }

        if (efectoImpacto != null)
            Instantiate(efectoImpacto, elegido.point, Quaternion.LookRotation(elegido.normal));
    }

    // efecto local inmediato en la boca del arma
    Vector3 posicionDeLaBoca = PuntoDeDisparo();
    if (efectoDisparo != null)
        Instantiate(efectoDisparo, posicionDeLaBoca, bocaDelArma != null ? bocaDelArma.rotation : Quaternion.identity);

    ReproducirDisparo();

    // el instante de disparo viaja por red para reproducirse en el remoto sin retardo
    DisparoEfectuadoClientRpc(posicionDeLaBoca);

    // retroceso: el arma se inclina y vuelve; la vista se empuja hacia arriba
    // (rotacionVertical positivo inclina hacia abajo, por eso se resta)
    recoilArma = 1f;
    if (controlCamara != null) controlCamara.rotacionVertical -= apuntando ? retrocesoActual * 0.7f : retrocesoActual;
}

    // true si el transform pertenece al cuerpo/arma de ESTE jugador (para no autoimpactarse)
    bool DelPropioCuerpo(Transform t)
    {
        if (t.IsChildOf(transform)) return true;
        if (controller != null && t.GetComponentInParent<CharacterController>() == controller) return true;
        return false;
    }

    Vector3 PuntoDeDisparo()
    {
        if (bocaDelArma != null) return bocaDelArma.position;
        return PuntoDelCuerpoEnMundo(new Vector3(0.25f, 1.35f, 0.7f));
    }

    Vector3 PuntoDelCuerpoEnMundo(Vector3 puntoLocal)
    {
        GameObject auxiliar = new GameObject("Aux");
        auxiliar.transform.SetParent(transform, false);
        auxiliar.transform.localPosition = puntoLocal;
        Vector3 resultado = auxiliar.transform.position;
        Destroy(auxiliar);
        return resultado;
    }

    // ------------------------- RECARGA -------------------------

    void Recargar()
    {
        if (armaActual == 3) return; // Desarmado: no puede recargar
        if (recargando)
        {
            if (Time.time >= recargaHasta) recargando = false;
            return;
        }
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            recargando = true;
            recargaHasta = Time.time + 1.8f;
        }
    }

    // ------------------------- DISPARO / DAÑO POR RED -------------------------

    // El cliente que dispara le avisa al SERVIDOR a quien golpeo; el servidor baja la salud
    // de la victima (autoridad del servidor: un cliente no puede escribir la salud de nadie)
    [ServerRpc]
    void DanioAJugadorServerRpc(float danio, ulong clientIdVictima)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientIdVictima, out var cliente)
            && cliente.PlayerObject != null)
        {
            PlayerNetworkMovement victima = cliente.PlayerObject.GetComponent<PlayerNetworkMovement>();
            if (victima != null) victima.RecibirDanioEnServidor(danio);
        }
    }

    // Corre SOLO en el servidor: la salud vive en una NetworkVariable de escritura del servidor
    void RecibirDanioEnServidor(float cantidad)
    {
        if (Muerto) return;
        redSalud.Value = Mathf.Max(0f, redSalud.Value - cantidad);

        // Si esto lo mató, arranca el temporizador de revivir automático (solo una vez)
        if (redSalud.Value <= 0f && !reviviendoAutomaticamente)
        {
            reviviendoAutomaticamente = true;
            StartCoroutine(RevivirAutomaticamenteCoroutine());
        }
    }

    IEnumerator RevivirAutomaticamenteCoroutine()
    {
        yield return new WaitForSeconds(tiempoParaRevivir);
        RevivirEnServidor();
        reviviendoAutomaticamente = false;
    }

    // Reproduce en las instancias remotas el instante de disparo del dueño.
    [ClientRpc]
    void DisparoEfectuadoClientRpc(Vector3 posicionDeLaBoca)
    {
        if (esLocal) return;
        recoilArma = 1f;
        if (efectoDisparo != null)
            Instantiate(efectoDisparo, posicionDeLaBoca, Quaternion.identity);
        ReproducirDisparo();
    }

    // Sonido de disparo en audio 3D: cada maquina lo reproduce una sola vez (el dueño en
    // Disparar, las remotas via el Rpc) y se escucha desde la posicion del cuerpo que dispara,
    // con volumen segun distancia. Pitch con leve variacion para no sonar identico cada tiro.
    void ConfigurarAudio()
    {
        audio = gameObject.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;   // totalmente 3D
        audio.minDistance = 4f;
        audio.maxDistance = 60f;
    }

    void ReproducirDisparo()
    {
        if (sonidoDisparo == null || audio == null) return;
        audio.pitch = Random.Range(0.94f, 1.06f);
        audio.PlayOneShot(sonidoDisparo, volumenDisparo);
    }

    // Punto de entrada clasico (misma firma que el Player viejo) para sistemas externos de daño
    public void TakeDamage(float cantidad)
    {
        RecibirDanioServerRpc(cantidad);
    }

    [ServerRpc]
    void RecibirDanioServerRpc(float cantidad)
    {
        RecibirDanioEnServidor(cantidad);
    }

    // ------------------------- SALUD / MUERTE -------------------------

    // Aplica en cada maquina el estado que vino del servidor (vivo/muerto). Se llama cada frame:
    // si el valor no cambio, no hace nada.
    void RevisarEstadoDeSalud()
    {
        bool estaMuerto = redSalud.Value <= 0f;
        if (estaMuerto == Muerto) return;

        Muerto = estaMuerto;

        if (controller != null) controller.enabled = !Muerto;

        if (animator != null)
            animator.SetBool("Dead", Muerto);

        if (esLocal)
        {
            apuntando = false;
            PlayerCamera.DueñoMuerto = Muerto;
            if (mira != null) mira.Mostrar(!Muerto);
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
    }

    // Muerte pidiendo el danio por el camino de red (lo aplica el servidor)
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
        velocidadHorizontal = Vector3.zero;
        velocidadVertical = 0f;
    }

    // ------------------------- CUERPO / MODELO -------------------------

    // Alinea el modelo con el piso y ajusta la capsula a la altura medida (una sola vez por
    // instancia). La suela se apoya en -skinWidth para que las botas rendericen sobre el piso;
    // ajusteDePies permite un retoque fino desde el Inspector.
    bool cuerpoAlineado;

    void ConfigurarCuerpo()
    {
        float tope = 0f;
        float suela = float.MaxValue;
        bool medido = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            if (EsParteDeUnArma(r.transform)) continue;
            float yMax = r.bounds.max.y - transform.position.y;
            float yMin = r.bounds.min.y - transform.position.y;
            if (yMax > tope) tope = yMax;
            if (yMin < suela) suela = yMin;
            medido = true;
        }

        Transform modelo = animator != null ? animator.transform : null;
        if (medido && modelo != null && !cuerpoAlineado)
        {
            cuerpoAlineado = true;
            float objetivoSuela = -controller.skinWidth;
            float subida = objetivoSuela - suela + ajusteDePies;
            float escala = transform.lossyScale.y != 0f ? transform.lossyScale.y : 1f;
            modelo.localPosition += Vector3.up * (subida / escala);
            tope += subida;
        }

        if (tope > 0.2f)
        {
            controller.height = tope;
            controller.center = new Vector3(0f, tope * 0.5f, 0f);
        }

        if (camara != null)
        {
            float altura = tope > 0.2f ? tope * 0.9f : alturaCamara;
            camara.transform.localPosition = new Vector3(0f, altura, -atrasCamara);
        }
    }

    bool EsParteDeUnArma(Transform t)
    {
        if (armaRifle != null && t.IsChildOf(armaRifle.transform)) return true;
        if (armaPistola != null && t.IsChildOf(armaPistola.transform)) return true;
        return false;
    }
}
