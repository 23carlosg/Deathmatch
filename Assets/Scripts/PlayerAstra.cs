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
    public float ajusteDePies = 0f;        // retoque fino de la altura del modelo (desde el prefab)
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
    public float retrocesoRotacionArma = 6f; // grados que sube la punta del arma por disparo (negativo = baja)

    [Header("Sonido")]
    public AudioClip sonidoDisparo;        // clip del disparo (arrastrar en el Inspector del prefab)
    [Range(0f, 1f)] public float volumenDisparo = 1f;

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
    // arma en la mano: 1 = rifle, 2 = pistola, 3 = desarmado (tambien parametro del animator)
    readonly NetworkVariable<int> redArma = new NetworkVariable<int>(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // salud: la escribe SOLO el servidor cuando le llega el danio; 0 = muerto
    readonly NetworkVariable<float> redSalud = new NetworkVariable<float>(100f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // estado de piso estable (con debounce) que consumen los estados de salto del animator
    readonly NetworkVariable<bool> redEnElPiso = new NetworkVariable<bool>(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool Muerto { get; private set; }
    public float Salud => redSalud.Value;   // para el HUD

    bool esLocal = true;   // false en las instancias remotas
    CharacterController controller;
    Camera camara;
    Transform camaraTransform;
    Crosshair mira;
    AudioSource audio;

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
        ConfigurarAudio();
        AplicarArma(armaActual);           // arranca mostrando solo el arma del slot inicial
        GuardarPoseDeReposoDeLasArmas();   // el recoil se aplica alrededor de la pose de reposo
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
            PlayerCamera.DueñoMuerto = false;   // reconectar a una partida arranca vivo
            StartCoroutine(SalvedadPorSiElMapaNoAvisa());
            StartCoroutine(AsegurarMiraTrasCargaDeEscena());
        }
    }

    // El cambio de escena puede dejar la mira sin canvas: la re-crea si no esta visible.
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

    // Red de seguridad: si a los 5 segundos sigue sin haber gate de movimiento, se fuerza.
    IEnumerator SalvedadPorSiElMapaNoAvisa()
    {
        yield return new WaitForSeconds(5f);
        if (!NetworkLobbyManager.PuedeMoverse)
        {
            Debug.LogWarning("[PlayerAstra] El mapa nunca avisó que estaba listo, se fuerza el movimiento igual.");
            NetworkLobbyManager.ForzarMapaListoSiTarda();
        }
    }

    // En una instancia remota se apaga todo lo privativo de la maquina del dueño
    void ApagarLoLocal()
    {
        // las camaras remotas se destruyen por completo: no deben renderizar en esta maquina
        foreach (Camera c in GetComponentsInChildren<Camera>(true))
        {
            c.gameObject.SetActive(false);
            Destroy(c.gameObject);
        }
        foreach (PlayerCamera pc in GetComponentsInChildren<PlayerCamera>(true))
            pc.enabled = false;   // el remoto no mueve camara ni cursor; el estado de muerte lo comparte PlayerCamera.DueñoMuerto
        if (mira != null) mira.Mostrar(false);
    }

    void Update()
    {
        if (!esLocal)
        {
            // seguridad: ninguna camara debe sobrevivir en una instancia remota
            if (camara != null) ApagarLoLocal();

            // instancia remota: reproduce el estado del dueño, suavizado entre paquetes
            if (animator == null) return;
            float t = 1f - Mathf.Exp(-15f * Time.deltaTime);
            velXSuave = Mathf.Lerp(velXSuave, redVelX.Value, t);
            velYSuave = Mathf.LerpAngle(velYSuave, redVelY.Value, t);
            animator.SetFloat("VelX", velXSuave);
            animator.SetFloat("VelY", velYSuave);
            bool pisoRemoto = redEnElPiso.Value;
            animator.SetBool("isGrounded", pisoRemoto);
            AplicarArma(redArma.Value);   // setea el int Arma y muestra el modelo del arma en mano
            AplicarRecoilDelArma();   // el remoto tambien ve el saltito del arma al disparar
            RevisarEstadoDeSalud();
            return;
        }

        if (Keyboard.current == null) return; // no hay teclado conectado

        // no moverse hasta que la escena de juego este lista (evita caer antes de que exista el piso)
        if (!NetworkLobbyManager.PuedeMoverse) return;

        RevisarEstadoDeSalud();   // aplica muerte/resurreccion que vengan del servidor

        if (Muerto) return;   // cuerpo muerto sin control: el CharacterController queda apagado

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
            animator.SetInteger("Arma", arma == 3 ? 0 : arma);   // elige rifle/pistola y sus saltos
        }
    }

    // Busca los modelos de armas por nombre (o se arrastran en el Inspector).
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

    // Guarda la pose de reposo de cada arma: el retroceso se aplica alrededor de ella.
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

    // Retroceso del arma: inclinacion breve que decae sola (remotas via el Rpc de disparo).
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

        // inclinacion en espacio de mundo alrededor de la derecha del arma:
        // la punta sube siempre, montada como este montada en la mano
        activa.localPosition = posReposo;
        Quaternion reposoMundo = activa.parent != null ? activa.parent.rotation * rotReposo : rotReposo;
        Vector3 ejeDerechaDelArma = reposoMundo * Vector3.right;
        Quaternion inclinadaMundo = Quaternion.AngleAxis(-recoilArma * retrocesoRotacionArma, ejeDerechaDelArma) * reposoMundo;
        activa.localRotation = activa.parent != null
            ? Quaternion.Inverse(activa.parent.rotation) * inclinadaMundo
            : inclinadaMundo;
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

            // la pose de salto se decide en el mismo frame del impulso
            if (animator != null) animator.SetBool("isGrounded", false);
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

        // la pose de salto solo se enciende con un salto real (impulso de Mover) y vuelve al aterrizar
        if (controller.isGrounded && velocidadVertical <= 0.01f) enSaltoReal = false;
        if (controller.isGrounded) tiempoEnElAire = 0f; else tiempoEnElAire += Time.deltaTime;

        // piso estable: gracia de 0.1s para los parpadeos de isGrounded
        bool enElPiso = !enSaltoReal && tiempoEnElAire < 0.1f;

        // suavizado local minimo (0.08s): elimina el pop entre walk/run sin sentirse con delay
        float t = 1f - Mathf.Exp(-14f * Time.deltaTime);
        velXSuave = Mathf.Lerp(velXSuave, velX, t);
        velYSuave = Mathf.Lerp(velYSuave, velY, t);

        if (animator != null)
        {
            animator.SetFloat("VelX", velXSuave);
            animator.SetFloat("VelY", velYSuave);
            animator.SetBool("isGrounded", enElPiso);
        }

        // publicar por red para las instancias remotas
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

        ReproducirDisparo();

        // el instante de disparo viaja por red para reproducirse en el remoto sin retardo
        DisparoEfectuadoClientRpc(posicionDeLaBoca);

        // retroceso: el arma se inclina y vuelve; la vista se empuja hacia arriba
        // (rotacionVertical positivo inclina hacia abajo, por eso se resta)
        recoilArma = 1f;
        if (controlCamara != null) controlCamara.rotacionVertical -= apuntando ? retroceso * 0.7f : retroceso;
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

    // Posicion de mundo de un punto local del cuerpo, via un hijo temporal de escala 1 (los huesos heredan escala).
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
    }

    // Reproduce en las instancias remotas el instante de disparo del dueño.
    [ClientRpc]
    void DisparoEfectuadoClientRpc(Vector3 posicionDeLaBoca)
    {
        if (esLocal) return;   // el dueño ya disparo localmente
        recoilArma = 1f;       // el remoto ve el saltito del arma en el mismo instante
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

        // el cuerpo muerto queda sin collider (se puede caminar a traves de el) y el revivido
        // lo recupera; corre en TODAS las maquinas porque RevisarEstadoDeSalud corre en todas
        if (controller != null) controller.enabled = !Muerto;

        if (animator != null)
            animator.SetBool("Dead", Muerto);   // el AnyState del animator entra/sale de la muerte

        if (esLocal)
        {
            apuntando = false;
            PlayerCamera.DueñoMuerto = Muerto;   // la camara deja de girar el cuerpo: el cadaver queda quieto
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
        redSalud.Value = saludMaxima;
        Vector3 posicion = transform.position + Vector3.up * 1.5f;   // futuro: puntos de spawn
        RevivirClientRpc(posicion);
    }

    [ClientRpc]
    void RevivirClientRpc(Vector3 posicion)
    {
        transform.position = posicion;   // la replica la hace el ClientNetworkTransform del dueño
        velocidadHorizontal = Vector3.zero;
        velocidadVertical = 0f;
        // RevisarEstadoDeSalud (en el Update) hace el resto: Muerto=false, cursor, mira, animacion
    }

    // ------------------------- CUERPO / MODELO -------------------------

    // Alinea el modelo con el piso y ajusta la capsula a la altura medida (una sola vez por
    // instancia). La suela se apoya en -skinWidth para que las botas rendericen sobre el piso;
    // ajusteDePies permite un retoque fino desde el Inspector.
    bool cuerpoAlineado;

    void ConfigurarCuerpo()
    {
        // tope y suela del modelo medidos por bounds (sin armas ni particulas)
        float tope = 0f;
        float suela = float.MaxValue;
        bool medido = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            if (EsParteDeUnArma(r.transform)) continue;   // las armas no ensucian la medida del cuerpo
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
            float objetivoSuela = -controller.skinWidth;   // altura a la que las botas quedan sobre el piso
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
