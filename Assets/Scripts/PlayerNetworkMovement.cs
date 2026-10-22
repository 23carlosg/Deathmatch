using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

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

    [Header("Camara - Colision con paredes")]
    [Tooltip("Capas que acercan la camara (marca solo Walls y el suelo; NO la capa del jugador ni bots)")]
    public LayerMask capasColisionCamara = ~0;
    public float radioColisionCamara = 0.25f;
    public float margenPared = 0.1f;
    public float distanciaMinimaCamara = 0.3f;

    [Header("Camara pegada a la pared")]
    [Tooltip("Por debajo de esta distancia al pivote se oculta el cuerpo (el arma sigue visible)")]
    public float distanciaOcultarCuerpo = 1.0f;
    [Tooltip("Evita parpadeo en el borde del umbral")]
    public float histeresisOcultar = 0.25f;
    [Tooltip("Opacidad del cuerpo cuando la camara queda pegada a una pared (0 = invisible, 1 = opaco)")]
    [Range(0f, 1f)] public float alphaCuerpoCerca = 0.35f;
    [Tooltip("Velocidad del fundido (mas alto = mas rapido)")]
    public float velocidadFundido = 8f;

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
    [Tooltip("Punta del cañon de la pistola (objeto vacio hijo del arma). Si queda vacio se usa Boca Del Arma")]
    public Transform bocaPistola;
    [Tooltip("Punta del cañon del rifle (objeto vacio hijo del arma). Si queda vacio se usa Boca Del Arma")]
    public Transform bocaRifle;
    public GameObject efectoDisparo;
    public GameObject efectoImpacto;

    [Header("Disparo - Rayo laser")]
    [Tooltip("Material con el shader Custom/LaserBeam. Si queda vacio no se dibuja rayo")]
    public Material materialLaser;
    public Color colorLaser = new Color(0.2f, 0.9f, 1f, 1f);
    public float anchoLaser = 0.06f;
    [Tooltip("Cuanto tarda en desvanecerse el rayo (segundos)")]
    public float duracionLaser = 0.08f;

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
    public GameObject armaRifle;
    public GameObject armaPistola;
    public bool tieneRifle = false;
    public bool tienePistola = false;

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
    int armaActual = 3;      // 1 Rifle, 2 Pistola, 3 desarmado
    int armaAplicada = -1;

    // Camara: buffer para el SphereCast sin generar basura, y ocultado del cuerpo
    static readonly RaycastHit[] golpesCamara = new RaycastHit[8];
    Renderer[] renderersCuerpo;
    bool cuerpoOculto;          // true = la camara esta pegada a una pared, el cuerpo se funde

    // Fundido: copias transparentes de los materiales (se crean la primera vez que hacen falta)
    Material[][] matsOriginales;
    Material[][] matsTransparentes;
    Color[][] coloresBase;
    float alphaCuerpo = 1f;
    bool usandoTransparentes;
    static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int IdColor = Shader.PropertyToID("_Color");

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

        // Renderers del cuerpo (sin armas ni particulas) para ocultarlos si la camara queda pegada a una pared
        var lista = new System.Collections.Generic.List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (EsParteDeUnArma(r.transform)) continue;   // el arma se sigue viendo
            lista.Add(r);
        }
        renderersCuerpo = lista.ToArray();
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

    // La camara es hija del cuerpo (el yaw lo aporta el cuerpo via PlayerCamera). Aca se ajusta
    // su posicion local: orbita alrededor del pecho segun el pitch, se acerca al mirar abajo,
    // se acerca si hay una pared detras (SphereCast) y oculta el cuerpo si queda muy pegada.
    void MoverCamara()
    {
        if (camaraTransform == null) return;

        // Todo se calcula en el espacio del PADRE de la camara, que es donde vive su localPosition
        Transform padre = camaraTransform.parent != null ? camaraTransform.parent : transform;

        float alturaObjetivo = apuntando ? alturaCamara * 0.85f : alturaCamara;
        float xObjetivo = apuntando ? hombroApuntado : desvioCamaraLibre;
        float zObjetivo = apuntando ? -Mathf.Max(0.5f, atrasCamara * 0.22f) : -atrasCamara;

        // Pitch positivo = mirando hacia abajo
        float pitch = controlCamara != null ? controlCamara.rotacionVertical : 0f;

        // Al mirar abajo, la camara se acerca (hasta el 55% de la distancia a 60° o mas)
        float abajo = Mathf.InverseLerp(0f, 60f, Mathf.Max(0f, pitch));
        float factorDistancia = Mathf.Lerp(1f, 0.55f, abajo);

        // Orbitar alrededor del pecho/hombros, no de la cabeza
        float alturaPivote = alturaObjetivo * 0.8f;
        Vector3 pivoteLocal = new Vector3(0f, alturaPivote, 0f);
        Vector3 desfase = new Vector3(xObjetivo, alturaObjetivo - alturaPivote, zObjetivo * factorDistancia);

        Vector3 objetivoLocal = pivoteLocal + Quaternion.Euler(pitch, 0f, 0f) * desfase;
        Vector3 idealLocal = objetivoLocal;

        // Colision con paredes: rayo grueso desde el pivote hacia la posicion ideal
        Vector3 idealMundo = padre.TransformPoint(objetivoLocal);
        Vector3 pivote = padre.TransformPoint(pivoteLocal);
        Vector3 haciaCamara = idealMundo - pivote;
        float distancia = haciaCamara.magnitude;
        bool choco = false;

        if (distancia > 0.001f)
        {
            Vector3 dir = haciaCamara / distancia;
            int n = Physics.SphereCastNonAlloc(pivote, radioColisionCamara, dir, golpesCamara,
                distancia, capasColisionCamara, QueryTriggerInteraction.Ignore);

            float masCerca = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = golpesCamara[i];
                if (h.distance <= 0f) continue;                       // ya solapaba al empezar
                if (DelPropioCuerpo(h.collider.transform)) continue;  // ignorar al propio jugador
                if (h.distance < masCerca) masCerca = h.distance;
            }

            if (masCerca < float.MaxValue)
            {
                choco = true;
                float nueva = Mathf.Max(distanciaMinimaCamara, masCerca - margenPared);
                idealLocal = padre.InverseTransformPoint(pivote + dir * nueva);
            }
        }

        // Si hay obstaculo, acercar rapido; si no, volver suave
        bool acercando = (idealLocal - objetivoLocal).sqrMagnitude > 0.0001f;
        float velocidad = acercando ? suavizadoCamara * 3f : suavizadoCamara;
        float t = 1f - Mathf.Exp(-velocidad * Time.deltaTime);
        camaraTransform.localPosition = Vector3.Lerp(camaraTransform.localPosition, idealLocal, t);

        // Ocultar el cuerpo solo si una pared empujo la camara hacia adentro (no al apuntar)
        float distReal = Vector3.Distance(camaraTransform.position, pivote);
        if (!cuerpoOculto && choco && distReal < distanciaOcultarCuerpo)
            OcultarCuerpo(true);
        else if (cuerpoOculto && (!choco || distReal > distanciaOcultarCuerpo + histeresisOcultar))
            OcultarCuerpo(false);
        ActualizarFundidoCuerpo();

        // Zoom (FOV) al apuntar
        if (camara != null)
        {
            float tFov = 1f - Mathf.Exp(-suavizadoCamara * Time.deltaTime);
            camara.fieldOfView = Mathf.Lerp(camara.fieldOfView, apuntando ? fovApuntado : 60f, tFov);
        }
    }

    // Solo marca el estado; el fundido real lo hace ActualizarFundidoCuerpo cada frame
    void OcultarCuerpo(bool ocultar)
    {
        cuerpoOculto = ocultar;
    }

    // Crea una copia transparente de cada material del cuerpo (URP/Lit)
    void PrepararMaterialesTransparentes()
    {
        int n = renderersCuerpo.Length;
        matsOriginales = new Material[n][];
        matsTransparentes = new Material[n][];
        coloresBase = new Color[n][];

        for (int i = 0; i < n; i++)
        {
            Renderer r = renderersCuerpo[i];
            if (r == null) { matsOriginales[i] = new Material[0]; matsTransparentes[i] = new Material[0]; coloresBase[i] = new Color[0]; continue; }

            Material[] orig = r.sharedMaterials;
            var trans = new Material[orig.Length];
            var cols = new Color[orig.Length];

            for (int j = 0; j < orig.Length; j++)
            {
                if (orig[j] == null) continue;
                Material m = new Material(orig[j]);
                HacerTransparente(m);
                trans[j] = m;
                cols[j] = m.HasProperty(IdBaseColor) ? m.GetColor(IdBaseColor)
                        : m.HasProperty(IdColor) ? m.GetColor(IdColor)
                        : Color.white;
            }

            matsOriginales[i] = orig;
            matsTransparentes[i] = trans;
            coloresBase[i] = cols;
        }
    }

    // Equivale a poner Surface Type = Transparent / Blend = Alpha en el material URP/Lit
    void HacerTransparente(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 1f);   // mejor orden de dibujado entre las partes del cuerpo
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    // Lleva la opacidad del cuerpo hacia alphaCuerpoCerca (pegado a pared) o 1 (normal)
    void ActualizarFundidoCuerpo()
    {
        if (renderersCuerpo == null) return;

        float objetivo = cuerpoOculto ? alphaCuerpoCerca : 1f;

        if (matsTransparentes == null)
        {
            if (objetivo >= 1f) return;           // todavia no hace falta
            PrepararMaterialesTransparentes();
        }

        alphaCuerpo = Mathf.MoveTowards(alphaCuerpo, objetivo, velocidadFundido * Time.deltaTime);

        // Totalmente opaco: volver a los materiales originales
        if (alphaCuerpo >= 0.999f)
        {
            if (usandoTransparentes)
            {
                for (int i = 0; i < renderersCuerpo.Length; i++)
                    if (renderersCuerpo[i] != null) renderersCuerpo[i].sharedMaterials = matsOriginales[i];
                usandoTransparentes = false;
            }
            return;
        }

        // Fundido: usar las copias transparentes y aplicar el alpha
        if (!usandoTransparentes)
        {
            for (int i = 0; i < renderersCuerpo.Length; i++)
                if (renderersCuerpo[i] != null) renderersCuerpo[i].sharedMaterials = matsTransparentes[i];
            usandoTransparentes = true;
        }

        for (int i = 0; i < matsTransparentes.Length; i++)
        {
            for (int j = 0; j < matsTransparentes[i].Length; j++)
            {
                Material m = matsTransparentes[i][j];
                if (m == null) continue;
                Color c = coloresBase[i][j];
                c.a = coloresBase[i][j].a * alphaCuerpo;
                if (m.HasProperty(IdBaseColor)) m.SetColor(IdBaseColor, c);
                else if (m.HasProperty(IdColor)) m.SetColor(IdColor, c);
            }
        }
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
        if (PauseMenuUI.Abierto) return;

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
        CrearEfectoDisparo(posicionDeLaBoca, BocaActual());

        // rayo laser: de la boca del arma al punto de impacto (o al maximo alcance)
        Vector3 puntoFinal = acerto ? elegido.point : origen + direccion * alcance;
        CrearLaser(posicionDeLaBoca, puntoFinal);

        ReproducirDisparo();

        // el instante de disparo viaja por red para reproducirse en el remoto sin retardo
        DisparoEfectuadoClientRpc(posicionDeLaBoca, puntoFinal);

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

    // Boca del arma que tenes en la mano (cada arma puede tener la suya)
    Transform BocaActual()
    {
        if (armaActual == 2 && bocaPistola != null) return bocaPistola;
        if (armaActual == 1 && bocaRifle != null) return bocaRifle;
        return bocaDelArma;
    }

    Vector3 PuntoDeDisparo()
    {
        Transform boca = BocaActual();
        if (boca != null) return boca.position;
        return PuntoDelCuerpoEnMundo(new Vector3(0.25f, 1.35f, 0.7f));
    }

    // Crea el destello y lo destruye solo, aunque el prefab no tenga Stop Action en Destroy
    // Si hay boca activa, el destello nace como hijo de ella y sigue al arma mientras te movés
    void CrearEfectoDisparo(Vector3 posicion, Transform boca)
    {
        if (efectoDisparo == null) return;

        GameObject fx = (boca != null && boca.gameObject.activeInHierarchy)
            ? Instantiate(efectoDisparo, boca.position, boca.rotation, boca)
            : Instantiate(efectoDisparo, posicion, Quaternion.identity);

        Destroy(fx, 0.3f);
    }

    Vector3 PuntoDelCuerpoEnMundo(Vector3 puntoLocal)
    {
        return transform.TransformPoint(puntoLocal);
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
    [Rpc(SendTo.ClientsAndHost)]
    void DisparoEfectuadoClientRpc(Vector3 posicionDeLaBoca, Vector3 puntoFinal)
    {
        if (esLocal) return;
        recoilArma = 1f;
        CrearEfectoDisparo(posicionDeLaBoca, BocaActual());
        CrearLaser(posicionDeLaBoca, puntoFinal);
        ReproducirDisparo();
    }

    // Dibuja el rayo como una linea que se afina y desvanece rapido
    void CrearLaser(Vector3 desde, Vector3 hasta)
    {
        if (materialLaser == null) return;

        GameObject go = new GameObject("Laser");
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.material = materialLaser;
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, desde);
        lr.SetPosition(1, hasta);
        lr.widthMultiplier = anchoLaser;
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.startColor = colorLaser;
        lr.endColor = colorLaser;

        Destroy(go, duracionLaser + 0.2f);   // respaldo por si la corrutina se corta
        StartCoroutine(DesvanecerLaser(lr));
    }

    IEnumerator DesvanecerLaser(LineRenderer lr)
    {
        float t = 0f;
        while (t < duracionLaser && lr != null)
        {
            float k = 1f - t / duracionLaser;
            Color c = colorLaser;
            c.a = colorLaser.a * k;
            lr.startColor = c;
            lr.endColor = c;
            lr.widthMultiplier = anchoLaser * k;
            t += Time.deltaTime;
            yield return null;
        }
        if (lr != null) Destroy(lr.gameObject);
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
            PlayerCamera.DuenoMuerto = Muerto;
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