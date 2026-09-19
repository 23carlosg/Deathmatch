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
/// - Animaciones por CODIGO, sin triggers: VelX/VelY alimentan el blend direccional y "Pose" el salto.
///   El salto se decide EN EL MISMO FRAME que se toca Espacio (antes se publicaba un frame despues:
///   el remoto veia un frame caminando y recien ahi el salto = el "saltito")
/// - Red: la posicion la replica el ClientNetworkTransform (autoridad del dueño) y el resto viaja
///   por NetworkVariables escritas por el dueño. La salud la escribe SOLO el servidor.
/// - Los INSTANTES de disparo viajan por Rpc: el remoto ve el flash y la animacion en el momento
///   exacto, no un estado suavizado adestrozado por la interpolacion.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerAstra : NetworkBehaviour
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
    public float alturaCamara = 1.6f;      // altura de la camara al caminar
    public float atrasCamara = 2.5f;       // metros detras del cuerpo
    public float ajusteDePies = 0f;        // ajuste manual extra si el modelo queda hundido/flotando
    public float hombroApuntado = 0.45f;   // desvio lateral al apuntar (camara sobre el hombro derecho)
    public float desvioCamaraLibre = 0.6f; // desvio lateral al caminar SIN apuntar: la camara se corre
                                           // a la derecha y el personaje queda a la IZQUIERDA del centro,
                                           // para que la mira no caiga encima de su cuerpo
    public float fovApuntado = 30f;        // zoom al apuntar (FOV normal = 60; MENOR = MAS zoom)
    public float suavizadoCamara = 10f;    // rapidez con que la camara se acomoda

    [Header("Disparo")]
    public Transform bocaDelArma;          // punto en la punta del cañon (para el flash; opcional)
    public GameObject efectoDisparo;       // prefab de flash al disparar (opcional)
    public GameObject efectoImpacto;       // prefab de impacto en la superficie (opcional)
    public float danio = 20f;
    public float alcance = 200f;
    public float cadencia = 6f;            // disparos por segundo
    public float dispersion = 1.5f;        // grados al disparar desde la cadera (apuntando es preciso)
    public float retroceso = 0.5f;         // empujon vertical de la camara por disparo
    public float retrocesoRotacionArma = 6f; // cuantos grados sube la punta del arma por disparo
                                             // (si la punta baja en vez de subir, pone el valor negativo)

    [Header("Salud")]
    public float saludMaxima = 100f;

    [Header("Referencias (se buscan solas si quedan vacias)")]
    public Animator animator;
    public PlayerCamera controlCamara;     // rotacion con el mouse (vive en la camara)

    [Header("Armas (modelos en la mano; se buscan solos si quedan vacios)")]
    public GameObject armaRifle;           // modelo del rifle montado en la mano derecha
    public GameObject armaPistola;         // modelo de la pistola montado en la mano derecha

    // --- red: el dueño escribe, los demas leen ---
    readonly NetworkVariable<float> redVelX = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> redVelY = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // 0 = en el piso, 1 = salto con rifle, 2 = salto con pistola, 3 = salto desarmado
    readonly NetworkVariable<int> redPose = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // arma en la mano: 1 = rifle, 2 = pistola, 3 = desarmado
    readonly NetworkVariable<int> redArma = new NetworkVariable<int>(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // salud: la escribe SOLO el servidor cuando le llega el danio; 0 = muerto
    readonly NetworkVariable<float> redSalud = new NetworkVariable<float>(100f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // piso ESTABLE (con debounce del parpadeo de isGrounded): viaja por red porque los estados de
    // salto del animator entran/salen con isGrounded. Sin esto el remoto lo tenia en false fijo
    // y quedaba ciclando la animacion de salto = los "saltitos" del personaje lejano
    readonly NetworkVariable<bool> redEnElPiso = new NetworkVariable<bool>(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool Muerto { get; private set; }
    public float Salud => redSalud.Value;   // para el HUD

    bool esLocal = true;                    // false en las instancias remotas
    CharacterController controller;
    Camera camara;
    Transform camaraTransform;
    Crosshair mira;

    Vector3 velocidadHorizontal;
    float velocidadVertical;
    float velXSuave, velYSuave;             // valores que alimentan el animator (suavizados)
    float coyoteTimer;
    float bufferSaltoTimer;
    bool corriendo;
    bool apuntando;
    bool recargando;
    float recargaHasta;
    float proximoDisparoPermitido;
    bool enSaltoReal;        // true SOLO cuando se aplico el impulso de salto (nunca por isGrounded)
    float tiempoEnElAire;    // segundos consecutivos sin piso (para el debounce del parpadeo)
    float recoilArma;        // 0..1: cuan corrida esta el arma por el retroceso (decae solo)
    Vector3 posReposoRifle, posReposoPistola;
    Quaternion rotReposoRifle, rotReposoPistola;
    int armaActual = 1;      // 1 rifle, 2 pistola, 3 desarmado
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
        AplicarArma(armaActual);           // arranca mostrando solo el arma del slot inicial
        GuardarPoseDeReposoDeLasArmas();   // el recoil se aplica alrededor de la pose que ajustaste a mano
        ConfigurarCuerpo();
    }

    public override void OnNetworkSpawn()
    {
        esLocal = IsLocalPlayer;
        if (!esLocal)
        {
            ApagarLoLocal();
            // entrar a una partida en curso: partir de lo que el dueño ya sincronizo
            velXSuave = redVelX.Value;
            velYSuave = redVelY.Value;
            AplicarArma(redArma.Value);
            RevisarEstadoDeSalud();
        }
        else
        {
            mira = Crosshair.Crear();      // la mira (crosshair) solo existe en la maquina del dueño
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            StartCoroutine(SalvedadPorSiElMapaNoAvisa());
            StartCoroutine(AsegurarMiraTrasCargaDeEscena());
        }
    }

    // La escena del menu se descarga al entrar a la partida y se lleva puesta la mira del HOST
    // (en el host los jugadores ya existen cuando se cambia de escena; en el cliente se spawnean
    // despues, por eso el cliente nunca tuvo este problema). Al re-crearla quedaba el canvas
    // apagado para siempre. Espera a que termine la carga y re-crea la mira si no esta visible.
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

    // Si a los 5 segundos el mapa todavia no avisa que esta listo, se fuerza igual:
    // mejor que el jugador se mueva un poco antes de tiempo a que quede congelado para siempre.
    IEnumerator SalvedadPorSiElMapaNoAvisa()
    {
        yield return new WaitForSeconds(5f);
        if (!NetworkLobbyManager.MapaListo)
        {
            Debug.LogWarning("[PlayerAstra] El mapa nunca avisó que estaba listo, se fuerza el movimiento igual.");
            NetworkLobbyManager.ForzarMapaListoSiTarda();
        }
    }

    // En una instancia remota se apaga todo lo privativo de la maquina del dueño
    void ApagarLoLocal()
    {
        foreach (Camera c in GetComponentsInChildren<Camera>())
        {
            c.enabled = false;
            AudioListener listener = c.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
        }
        foreach (PlayerCamera pc in GetComponentsInChildren<PlayerCamera>())
            pc.enabled = false;
        if (mira != null) mira.Mostrar(false);
    }

    void Update()
    {
        if (!esLocal)
        {
            // instancia remota: reproducir lo que manda el dueño, suavizado para disimular
            // los saltos entre paquetes de red (local NO se suaviza: responde al toque de tecla)
            if (animator == null) return;
            float t = 1f - Mathf.Exp(-15f * Time.deltaTime);
            velXSuave = Mathf.Lerp(velXSuave, redVelX.Value, t);
            velYSuave = Mathf.LerpAngle(velYSuave, redVelY.Value, t);
            animator.SetFloat("VelX", velXSuave);
            animator.SetFloat("VelY", velYSuave);
            bool pisoRemoto = redEnElPiso.Value;
            animator.SetBool("isGrounded", pisoRemoto);
            animator.SetInteger("Pose", redPose.Value);
            AplicarArma(redArma.Value);
            AplicarRecoilDelArma();   // el remoto tambien ve el saltito del arma al disparar
            RevisarEstadoDeSalud();
            return;
        }

        if (Keyboard.current == null) return; // no hay teclado conectado

        // No mover el CharacterController hasta que el mapa real haya terminado de cargar,
        // para no caer al vacio antes de que exista el piso (ver NetworkLobbyManager.MapaListo)
        if (!NetworkLobbyManager.MapaListo) return;

        // TEST de danio: K = quitarse vida (viaja por el servidor), L = revivir.
        // Cuando existan balas reales estas teclas sobran: el disparo llama a lo mismo.
        if (Keyboard.current.kKey.wasPressedThisFrame && !Muerto) Morir();
        if (Keyboard.current.lKey.wasPressedThisFrame) Revivir();

        // TEST de sincronizacion: P suelta una marca visible para TODOS en el mismo instante
        if (Keyboard.current.pKey.wasPressedThisFrame) MarcarPuntoServerRpc();

        RevisarEstadoDeSalud();   // aplica muerte/resurreccion que vengan del servidor

        if (Muerto)
        {
            // el cuerpo muerto solo cae: gravedad sin input; el animator queda en el ultimo frame
            if (controller.isGrounded && velocidadVertical < 0f) velocidadVertical = -2f;
            velocidadVertical -= gravedad * Time.deltaTime;
            controller.Move(Vector3.up * velocidadVertical * Time.deltaTime);
            return;
        }

        LeerCambioDeArma();
        LeerApuntar();
        Recargar();

        Mover();                  // salta DENTRO, en el mismo frame que Espacio
        Animar();
        Disparar();
        AplicarRecoilDelArma();
        MoverCamara();
        ActualizarMira();
    }

    // ------------------------- ARMAS EN LA MANO -------------------------

    void LeerCambioDeArma()
    {
        if (Keyboard.current.digit1Key.wasPressedThisFrame) CambiarArma(1);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) CambiarArma(2);
        if (Keyboard.current.digit3Key.wasPressedThisFrame) CambiarArma(3);
    }

    // Cambia el arma en la mano (1 rifle, 2 pistola, 3 desarmado) y lo publica por red
    void CambiarArma(int nueva)
    {
        if (Muerto || armaActual == nueva) return;
        armaActual = nueva;
        redArma.Value = nueva;   // las instancias remotas la reciben y aplican en su Update
        recoilArma = 0f;
        DevolverArmasAReposo();  // por si estaba a mitad del recoil del disparo anterior
        AplicarArma(nueva);
    }

    // Muestra/oculta los modelos segun el slot y sincroniza el parametro del animator.
    // Corre en el dueño y en las remotas; armaAplicada evita re-aplicar lo mismo cada frame.
    void AplicarArma(int arma)
    {
        if (armaAplicada == arma) return;
        armaAplicada = arma;
        armaActual = arma;   // las remotas tambien lo saben (el recoil elige el arma por este valor)
        if (armaRifle != null) armaRifle.SetActive(arma == 1);
        if (armaPistola != null) armaPistola.SetActive(arma == 2);
        if (animator != null)
        {
            animator.SetInteger("Arma", arma == 3 ? 0 : arma);
            animator.SetBool("Pistol", arma == 2);   // por si el animator usa el bool en vez del int
        }
    }

    // Encuentra los modelos de armas colgados de la mano por nombre. Si preferis, tambien podes
    // arrastrarlos a mano en el Inspector: si los campos estan llenos, no se busca nada.
    void BuscarArmas()
    {
        Transform raiz = animator != null ? animator.transform : transform;
        Transform rifle = null;
        foreach (Transform t in raiz.GetComponentsInChildren<Transform>(true))
        {
            if (rifle != null && t.IsChildOf(rifle)) continue;   // ignorar partes internas del rifle
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
            Debug.LogWarning("[PlayerAstra] No encontre los modelos de las armas bajo la mano: arrastralos en el Inspector del prefab (Arma Rifle / Arma Pistola).");
    }

    // Guarda la pose que ajustaste a mano de cada arma en la mano: el retroceso se aplica como un
    // desvio temporario alrededor de esa pose y al terminar vuelve EXACTO a ella.
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

    // Retroceso del arma: un empujon corto hacia atras con el cañon subiendo un poco, y vuelve
    // solo. Corre en el dueño y en las remotas (a la remota le llega via el Rpc de disparo).
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

        // SOLO rotacion, y PRE-multiplicada: el eje de giro es el del HUESO de la mano (no el del
        // arma, que esta apuntando para cualquier lado dentro de la mano). Asi el pivote queda en
        // la mano: la culata no se despega y solo la punta sube.
        activa.localPosition = posReposo;
        activa.localRotation = Quaternion.Euler(-recoilArma * retrocesoRotacionArma, 0f, 0f) * rotReposo;
    }

    // ------------------------- MOVIMIENTO -------------------------

    void Mover()
    {
        bool enSuelo = controller.isGrounded;

        // coyote time: permite saltar un instante despues de dejar el borde
        coyoteTimer = enSuelo ? coyoteTime : coyoteTimer - Time.deltaTime;
        // buffer de salto: el salto sale aunque se presione Espacio apenas antes de tocar el piso
        if (Keyboard.current.spaceKey.wasPressedThisFrame) bufferSaltoTimer = bufferSalto;
        else bufferSaltoTimer -= Time.deltaTime;

        // entrada WASD / flechas, relativa a donde mira el cuerpo (el cuerpo gira con el mouse)
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
            // con rifle se salta mas alto (multiplicador ajustable en el Inspector)
            float altura = alturaSalto * (armaActual == 1 ? multiplicadorSaltoRifle : 1f);
            velocidadVertical = Mathf.Sqrt(2f * gravedad * altura);
            coyoteTimer = 0f;
            bufferSaltoTimer = 0f;
            enSaltoReal = true;   // la pose de salto se mantiene hasta aterrizar de verdad

            // SIN DELAY: la animacion de salto se decide ACA, en el frame del toque. Antes "Pose"
            // se publicaba en Animar() con un frame de retraso: el remoto veia un frame caminando
            // y recien el siguiente el salto -> el "saltito" que se veia entre jugadores.
            int poseAlSaltar = armaActual;
            if (animator != null)
            {
                animator.SetInteger("Pose", poseAlSaltar);
                animator.SetBool("isGrounded", false);
            }
            redPose.Value = poseAlSaltar;
        }

        // gravedad
        if (enSuelo && velocidadVertical < 0f) velocidadVertical = -2f;
        velocidadVertical -= gravedad * Time.deltaTime;

        // en el aire se conserva la inercia y solo se corrige un poco la direccion
        Vector3 objetivo = enSuelo ? deseada : Vector3.Lerp(velocidadHorizontal, deseada, controlEnElAire);
        float t = 1f - Mathf.Exp(-aceleracion * Time.deltaTime);
        velocidadHorizontal = Vector3.Lerp(velocidadHorizontal, objetivo, t);

        controller.Move((velocidadHorizontal + Vector3.up * velocidadVertical) * Time.deltaTime);
    }

    void Animar()
    {
        // direccion de movimiento relativa al cuerpo: VelX (derecha+) y VelY (adelante+)
        Vector3 local = transform.InverseTransformDirection(velocidadHorizontal);
        Vector2 dir = new Vector2(local.x, local.z);
        float rapidez = dir.magnitude;

        // el blend espera unidades de su grilla (caminar = radio 1, correr = radio 2),
        // NO la velocidad real en m/s: se normaliza la direccion y se escala a mano
        if (rapidez > 0.05f)
        {
            dir /= rapidez;                 // solo la direccion, vector unitario
            dir *= corriendo ? 2f : 1f;     // caminar cae en el anillo de walk, correr en el de run
        }
        float velX = dir.x;
        float velY = dir.y;

        // La pose de salto SOLO se enciende con un salto REAL (impulso al tocar Espacio, marcado
        // en Mover) y vuelve a 0 al aterrizar. Nunca por heuristica de isGrounded: ese bool
        // "parpadea" mientras se camina y cada parpadeo convertia la pose en salto = los
        // "saltitos" del personaje que veia el otro jugador. Caminar/saltar de una plataforma
        // ahora cae con la animacion de locomocion, sin pose de salto inventada.
        if (controller.isGrounded && velocidadVertical <= 0.01f) enSaltoReal = false;
        if (controller.isGrounded) tiempoEnElAire = 0f; else tiempoEnElAire += Time.deltaTime;

        // piso ESTABLE: los parpadeos de isGrounded no cuentan (gracia de 0.1s); el salto real
        // saca los pies del piso en el mismo frame del impulso (enSaltoReal)
        bool enElPiso = !enSaltoReal && tiempoEnElAire < 0.1f;
        int pose = enSaltoReal ? armaActual : 0;

        // suavizado local minimo (0.08s): elimina el pop entre walk/run sin sentirse con delay
        float t = 1f - Mathf.Exp(-14f * Time.deltaTime);
        velXSuave = Mathf.Lerp(velXSuave, velX, t);
        velYSuave = Mathf.Lerp(velYSuave, velY, t);

        if (animator != null)
        {
            animator.SetFloat("VelX", velXSuave);
            animator.SetFloat("VelY", velYSuave);
            animator.SetInteger("Pose", pose);
            animator.SetBool("isGrounded", enElPiso);
        }

        // publicar por red para las instancias remotas
        redVelX.Value = velX;
        redVelY.Value = velY;
        redPose.Value = pose;
        redEnElPiso.Value = enElPiso;
    }

    // ------------------------- CAMARA -------------------------

    // La camara es hija del cuerpo (el yaw lo aporta el cuerpo via PlayerCamera); aca se ajusta
    // su posicion local: altura al caminar, sobre el hombro y cerca al apuntar, con zoom (FOV)
    void MoverCamara()
    {
        if (camaraTransform == null) return;
        float alturaObjetivo = apuntando ? alturaCamara * 0.85f : alturaCamara;
        // sin apuntar la camara se corre al otro lado: el cuerpo queda a la izquierda del centro
        // de la pantalla y el crosshair apunta limpio, no encima del jugador
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
        apuntando = Mouse.current != null && Mouse.current.rightButton.isPressed && !recargando;
    }

    // Apertura de la mira segun el estado (apuntando = cerrada, corriendo = abierta)
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
        if (Mouse.current == null || !Mouse.current.leftButton.isPressed) return;
        if (Time.time < proximoDisparoPermitido || recargando) return;

        proximoDisparoPermitido = Time.time + 1f / cadencia;

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

        // el rayo nace detras del cuerpo (camara en 3ra persona): el primer impacto que NO sea
        // parte del propio jugador es el valido, para nunca autodispararnos
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
                // el DANIO lo aplica el SERVIDOR (un cliente nunca escribe la salud de otro)
                DanioAJugadorServerRpc(danio, objetivo.OwnerClientId);
            }

            if (efectoImpacto != null)
                Instantiate(efectoImpacto, elegido.point, Quaternion.LookRotation(elegido.normal));
        }

        // efecto local inmediato en la boca del arma
        Vector3 posicionDeLaBoca = PuntoDeDisparo();
        if (efectoDisparo != null)
            Instantiate(efectoDisparo, posicionDeLaBoca, bocaDelArma != null ? bocaDelArma.rotation : Quaternion.identity);

        // el INSTANTE de disparo viaja por red: el remoto reproduce el flash y la animacion
        // en el momento exacto (los Rpc no se suavizan, llegan como eventos)
        DisparoEfectuadoClientRpc(posicionDeLaBoca);

        if (animator != null) animator.SetTrigger("Attack");

        // retroceso: la camara empuja hacia arriba y el arma salta hacia atras y vuelve
        recoilArma = 1f;
        if (controlCamara != null) controlCamara.rotacionVertical += apuntando ? retroceso * 0.7f : retroceso;
    }

    // true si el transform pertenece al cuerpo/arma de ESTE jugador (para no autoimpactarse)
    bool DelPropioCuerpo(Transform t)
    {
        if (t.IsChildOf(transform)) return true;
        if (controller != null && t.GetComponentInParent<CharacterController>() == controller) return true;
        return false;
    }

    // Punto del mundo donde sale el disparo (boca del arma). Sin boca configurada, un punto
    // delante del pecho equivalente a la punta del arma (ver PuntoDelCuerpoEnMundo).
    Vector3 PuntoDeDisparo()
    {
        if (bocaDelArma != null) return bocaDelArma.position;
        return PuntoDelCuerpoEnMundo(new Vector3(0.25f, 1.35f, 0.7f));
    }

    // Posicion de mundo de un punto expresado en espacio LOCAL del cuerpo, usando un hijo
    // temporal de escala 1. (Unity no deja crear un hijo con escala 0: la pistola colgada del
    // hueso usa una escala heredada de ~0.07 y sus transforms no sirven como puntos de efecto.)
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
            PlayerAstra victima = cliente.PlayerObject.GetComponent<PlayerAstra>();
            if (victima != null) victima.RecibirDanioEnServidor(danio);
        }
    }

    // Corre SOLO en el servidor: la salud vive en una NetworkVariable de escritura del servidor
    void RecibirDanioEnServidor(float cantidad)
    {
        if (Muerto) return;
        redSalud.Value = Mathf.Max(0f, redSalud.Value - cantidad);
        Debug.Log($"[PlayerAstra] Servidor: {cantidad} de danio al jugador {OwnerClientId} (salud: {redSalud.Value})");
    }

    // El INSTANTE de disparo viaja por red: el remoto ve el flash y la animacion de disparo
    // en el momento exacto, sin depender de la interpolacion de la animacion locomotora
    [ClientRpc]
    void DisparoEfectuadoClientRpc(Vector3 posicionDeLaBoca)
    {
        if (esLocal) return;   // el dueño ya disparo localmente
        recoilArma = 1f;       // el remoto ve el saltito del arma en el mismo instante
        if (efectoDisparo != null)
            Instantiate(efectoDisparo, posicionDeLaBoca, Quaternion.identity);
        if (animator != null) animator.SetTrigger("Attack");
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
        if (animator != null)
        {
            // "Dead" es el nombre REAL del bool del animator AstraAnimatorFull (verificado en el
            // .controller). "Death" queda por si alguna transicion usa el trigger, como el soldado viejo.
            animator.SetBool("Dead", Muerto);
            if (Muerto) animator.SetTrigger("Death");
        }

        if (esLocal)
        {
            apuntando = false;
            if (mira != null) mira.Mostrar(!Muerto);
            if (Muerto)
            {
                Cursor.lockState = CursorLockMode.None;   // futuro: menu de respawn
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    // TEST (tecla K): muerte via el camino real de red (cliente pide danio -> servidor aplica)
    public void Morir()
    {
        if (Muerto) return;
        RecibirDanioServerRpc(9999f);
    }

    // TEST (tecla L): el servidor restaura la salud y reposiciona a todos lados igual
    public void Revivir()
    {
        if (!Muerto) return;
        RevivirServerRpc();
    }

    [ServerRpc]
    void RevivirServerRpc()
    {
        redSalud.Value = saludMaxima;
        Vector3 posicion = transform.position + Vector3.up * 1.5f;   // futuro: puntos de spawn
        RevivirClientRpc(posicion);
    }

    [ClientRpc]
    void RevivirClientRpc(Vector3 posicion)
    {
        if (!esLocal) transform.position = posicion;
        else transform.position = posicion;   // el dueño tambien se mueve (su NetworkTransform es autoridad del dueño)
        velocidadHorizontal = Vector3.zero;
        velocidadVertical = 0f;
        // RevisarEstadoDeSalud (en el Update) hace el resto: Muerto=false, cursor, mira, animacion
    }

    // ------------------------- PING DE PRUEBA DE SYNC -------------------------

    // Tecla P: marca amarilla visible para TODOS los jugadores en el mismo punto y mismo
    // instante. Si todos ven la marca en el mismo lugar a la vez, la sincronizacion anda.
    [ServerRpc]
    void MarcarPuntoServerRpc()
    {
        MarcarPuntoClientRpc();
    }

    [ClientRpc]
    void MarcarPuntoClientRpc()
    {
        if (esLocal) return;   // el que la lanzo ya la ve en su maquina
        GameObject marca = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marca.transform.position = PuntoDeDisparo();
        marca.transform.localScale = Vector3.one * 0.25f;
        Destroy(marca.GetComponent<Collider>());
        Renderer r = marca.GetComponent<Renderer>();
        if (r != null && Shader.Find("Sprites/Default") != null)
            r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.9f, 0.1f) };
        Destroy(marca, 1.5f);
    }

    // ------------------------- CUERPO / MODELO -------------------------

    // Mide el modelo una vez al arrancar: apoya los pies del mesh en el origen del cuerpo,
    // ajusta la capsula del CharacterController a la altura real y ubica la camara detras.
    void ConfigurarCuerpo()
    {
        Transform modelo = animator != null ? animator.transform : null;

        float tope = 0f;
        float pies = 0f;
        bool medido = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            if (EsParteDeUnArma(r.transform)) continue;   // las armas no ensucian la medida del cuerpo
            float yMax = r.bounds.max.y - transform.position.y;
            float yMin = r.bounds.min.y - transform.position.y;
            if (yMax > tope) tope = yMax;
            pies = medido ? Mathf.Min(pies, yMin) : yMin;
            medido = true;
        }

        if (medido && modelo != null)
        {
            // subir el modelo hasta que los pies queden en el origen (si cuelgan por debajo)
            float subida = -pies + ajusteDePies;
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
