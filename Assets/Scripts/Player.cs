using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controlador en primera persona para el personaje Sci-Fi_Soldier.
/// - Movimiento WASD/flechas: caminar (por defecto), correr con Shift, agacharse con Ctrl o C
/// - Salto con Espacio (coyote time + buffer de salto) y gravedad con CharacterController
/// - Apuntar con click derecho, disparar con click izquierdo (raycast con danio)
/// - Recargar con R, cambiar de arma con 1 y 2
/// - Sincroniza las animaciones del asset: Speed, Aiming, Squat, Jump, Attack, Damage, Death
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class Player : MonoBehaviour
{
    [Header("Movimiento")]
    public float velocidadCaminar = 4f;
    public float velocidadCorrer = 8f;
    public float velocidadAgachado = 2f;
    [Range(0f, 1f)] public float controlEnElAire = 0.35f;
    public float aceleracion = 12f;

    [Header("Salto y gravedad")]
    public float alturaSalto = 1.2f;
    public float gravedad = 22f;
    public float coyoteTime = 0.15f;
    public float bufferSalto = 0.15f;

    [Header("Agacharse")]
    public float alturaDePie = 2f;       // se recalcula sola midiendo el modelo
    public float alturaAgachado = 1.3f;  // si es >= alturaDePie se recalcula sola
    [Range(0.5f, 1f)] public float fraccionOjos = 0.65f; // ojos = pies + altura real x esta fraccion (0.65 = pecho)
    [Min(0f)] public float atrasCamara = 0.3f; // metros de atraso de la camara detras de la cabeza (0 = dentro del cuerpo)
    public float hombroApuntado = 0.45f;       // desvio lateral de la camara al apuntar (sobre el hombro derecho)
    public float fovApuntado = 45f;            // zoom de camara al apuntar (fov normal = 60)

    [Header("Camara sin apuntar")]
    [Range(0.5f, 1.2f)] public float fraccionOjosLibre = 0.95f; // altura de camara al caminar sin apuntar (fraccion de la altura del modelo; fraccionOjos se usa al apuntar)
    [Min(0f)] public float giroModeloSinApuntar = 12f; // rapidez con que el MODELO gira hacia donde camina sin apuntar (0 = desactivado)
    public LayerMask capasTecho = ~0;
    public LayerMask capasPiso = ~0;

    [Header("Disparo")]
    public Transform bocaDelArma;        // opcional: punto de la boca del arma (para efectos)
    public GameObject efectoDisparo;     // opcional: prefab de flash/chispas al disparar
    public GameObject efectoImpacto;     // opcional: prefab de impacto en la superficie
    public float danio = 25f;
    public float alcance = 200f;
    public float cadencia = 9f;          // disparos por segundo
    public float dispersion = 1.5f;      // grados de dispersion al disparar sin apuntar
    public int tamanoCargador = 30;
    public int municionReserva = 120;
    public float tiempoRecarga = 1.8f;

    [Header("Salud")]
    public float saludMaxima = 100f;

    [Header("Referencias (se buscan solas si quedan vacias)")]
    public Animator animator;                 // Animator del modelo hijo (Sci-Fi_Soldier)
    public PlayerController controlDeArmas;   // script de armas del asset
    public Crosshair mira;                    // UI de punteria (se crea sola)

    // --- estado interno ---
    CharacterController controller;
    Camera camara;
    Transform camaraTransform;
    PlayerCamera playerCamera;

    Vector3 velocidadHorizontal;
    float velocidadVertical;
    float velocidadHorizontalActual;
    float coyoteTimer;
    float bufferSaltoTimer;
    float proximoDisparoPermitido;

    bool corriendo;
    bool agachado;
    bool apuntando;
    bool recargando;
    bool muerto;
    int municionEnCargador;
    Transform modelo;          // hijo visual (Sci-Fi_Soldier): se gira hacia el movimiento sin apuntar
    float anguloModelo;        // yaw actual del modelo relativo al cuerpo
    float alturaOjosDePie;
    float alturaOjosAgachado;
    float alturaVisible = 1f;   // altura del mesh visible (tope - pies)
    float piesRelativos = 0f;   // y local donde quedan los pies del mesh (puede ser negativa)
    int framesDeAjuste = 2;     // frames iniciales en que se re-ancla el mesh al piso

    // Propiedades para leer desde HUD u otros sistemas
    public float Salud { get; private set; }
    public bool EstaMuerto => muerto;
    public bool EstaAgachado => agachado;
    public bool EstaApuntando => apuntando;
    public bool EstaRecargando => recargando;
    public int MunicionEnCargador => municionEnCargador;
    public int MunicionReserva => municionReserva;
    public float VelocidadActual => velocidadHorizontalActual;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        camara = GetComponentInChildren<Camera>();
        if (camara != null)
        {
            camaraTransform = camara.transform;
            playerCamera = camara.GetComponent<PlayerCamera>();
        }

        // seguridad: un Animator en el ROOT pelea con el del modelo (le escribe curvas de root motion
        // al hijo y lo desplaza). Si hay duplicado, el del root se elimina: manda el del modelo.
        Animator animatorDelRoot = GetComponent<Animator>();
        Animator animatorDelHijo = GetComponentInChildren<Animator>();
        if (animatorDelRoot != null && animatorDelHijo != null && animatorDelRoot != animatorDelHijo)
            Destroy(animatorDelRoot);

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (controlDeArmas == null) controlDeArmas = GetComponentInChildren<PlayerController>();
        if (animator != null) modelo = animator.transform;

        if (mira == null) mira = Crosshair.Crear();

        MedirModelo();
        AplicarAltura(alturaDePie);
        AjustarAlPiso();
        MedirModelo();        // re-medir tras apoyar el piso: la pose animada cambia el bounds del mesh
        AplicarAltura(alturaDePie);
        if (camaraTransform != null)
            camaraTransform.localPosition = new Vector3(0f, alturaOjosDePie, -atrasCamara);

        municionEnCargador = tamanoCargador;
        Salud = saludMaxima;
    }

    // Start (no Awake) para correr DESPUES del Awake de PlayerController: el arsenal arranca en "Empty"
    // y nos cambia el controller sin el rifle. Forzamos Rifle para que haya arma y animaciones de disparo.
    void Start()
    {
        if (controlDeArmas != null) controlDeArmas.SetArsenal("Rifle");
    }

    void Update()
    {
        if (framesDeAjuste > 0)
        {
            framesDeAjuste--;
            MedidaYCorrige(); // las primeras frames la animacion mueve el mesh: re-anclar los pies
            AplicarAltura(agachado ? alturaAgachado : alturaDePie);
        }

        if (Keyboard.current == null) return; // no hay teclado conectado

        LeerCambioDeArma();

        if (muerto)
        {
            SoloGravedad();
            return;
        }

        LeerAgacharse();
        LeerApuntar();
        LeerRecarga();

        Mover();
        Animar();
        Disparar();
        BajarCamara();
        ActualizarMira();
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

        // entrada WASD / flechas
        float x = 0f, z = 0f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) x -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) x += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) z -= 1f;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) z += 1f;
        Vector2 entrada = Vector2.ClampMagnitude(new Vector2(x, z), 1f);

        corriendo = Keyboard.current.leftShiftKey.isPressed && !agachado && z > 0.1f; // se puede correr apuntando
        float velocidadObjetivo = agachado ? velocidadAgachado : (corriendo ? velocidadCorrer : velocidadCaminar);

        // Estilo Fortnite: la camara mira siempre hacia donde mira el cuerpo (mismo yaw).
        // W/S/A/D son relativos a esa direccion.
        Vector3 deseada = transform.TransformDirection(new Vector3(entrada.x, 0f, entrada.y)) * velocidadObjetivo;

        // salto
        if (bufferSaltoTimer > 0f && coyoteTimer > 0f && !agachado)
        {
            velocidadVertical = Mathf.Sqrt(2f * gravedad * alturaSalto);
            coyoteTimer = 0f;
            bufferSaltoTimer = 0f;
            if (animator != null) animator.SetTrigger("Jump");
        }

        // gravedad
        if (enSuelo && velocidadVertical < 0f) velocidadVertical = -2f;
        velocidadVertical -= gravedad * Time.deltaTime;

        // en el aire se conserva la inercia y solo se corrige un poco la direccion
        Vector3 objetivo = enSuelo ? deseada : Vector3.Lerp(velocidadHorizontal, deseada, controlEnElAire);
        float t = 1f - Mathf.Exp(-aceleracion * Time.deltaTime);
        velocidadHorizontal = Vector3.Lerp(velocidadHorizontal, objetivo, t);

        velocidadHorizontalActual = new Vector2(velocidadHorizontal.x, velocidadHorizontal.z).magnitude;

        controller.Move((velocidadHorizontal + Vector3.up * velocidadVertical) * Time.deltaTime);
    }

    void SoloGravedad()
    {
        if (controller.isGrounded && velocidadVertical < 0f) velocidadVertical = -2f;
        velocidadVertical -= gravedad * Time.deltaTime;
        controller.Move(Vector3.up * velocidadVertical * Time.deltaTime);
    }

    // ------------------------- AUTOAJUSTE AL MODELO -------------------------

    // Mide el cuerpo usando SOLO el SkinnedMeshRenderer (el mesh animado del soldado);
    // otros renderers (capsula vieja, armas, etc.) pueden colgar bajo el piso y ensuciar la medida
    void MedirModelo()
    {
        float tope = 0f;
        float base_ = 0f;
        bool medido = false;
        string detalle = "";
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            float yMax = r.bounds.max.y - transform.position.y;
            float yMin = r.bounds.min.y - transform.position.y;
            detalle += $"\n[Player]   renderer '{r.gameObject.name}' ({r.GetType().Name}): y {yMin:0.00}..{yMax:0.00}";
            if (!(r is SkinnedMeshRenderer)) continue; // solo el cuerpo manda
            tope = Mathf.Max(tope, yMax);
            base_ = Mathf.Min(base_, yMin);
            medido = true;
        }
        if (medido && tope - base_ > 0.2f)
        {
            alturaVisible = tope - base_;
            piesRelativos = base_;      // si los pies cuelgan bajo el origen queda negativo
            alturaDePie = alturaVisible;
            if (alturaAgachado >= alturaDePie) alturaAgachado = alturaDePie * 0.65f;
        }

        alturaOjosDePie = alturaDePie * fraccionOjos + piesRelativos;  // +pies: desde el piso real, no desde el origen
        alturaOjosAgachado = alturaAgachado * fraccionOjos + piesRelativos;

        // diagnostico: si algo queda por debajo de los pies, mostramos que es
        if (piesRelativos < -0.05f)
            Debug.Log("[Player] Bounds por debajo de los pies:" + detalle);
    }

    // Apoya los PIES VISUALES del mesh (no el origen del transform) justo sobre el piso real de la escena
    void AjustarAlPiso()
    {
        float radio = controller.radius * 0.9f;
        Vector3 origen = transform.position + Vector3.up * (controller.height * 0.5f + 1f);
        float distancia = controller.height * 0.5f + 5f;

        RaycastHit elegido = default;
        float mejor = float.MaxValue;
        foreach (RaycastHit g in Physics.SphereCastAll(origen, radio, Vector3.down, distancia, capasPiso, QueryTriggerInteraction.Ignore))
        {
            if (g.collider.transform.IsChildOf(transform)) continue; // no contarse a si mismo
            if (g.distance < mejor) { mejor = g.distance; elegido = g; }
        }

        if (mejor < float.MaxValue)
        {
            transform.position += Vector3.up * ((elegido.point.y + 0.02f) - transform.position.y);
            MedidaYCorrige();
            Debug.Log($"[Player] Piso: '{elegido.collider.name}' y={elegido.point.y:0.000} | " +
                      $"pies del mesh: y={(transform.position.y + piesRelativos):0.000} | " +
                      $"altura visible: {alturaVisible:0.00} (piesRelativos {piesRelativos:0.00}) | ojos: {alturaOjosDePie:0.00}");
        }
        else
        {
            Debug.LogWarning($"[Player] No encontre piso debajo (pos y={transform.position.y:0.000}). Revisar capasPiso o la posicion inicial.");
        }
    }

    // Cuanto hay que subir el transform para que los pies visuales queden en el origen
    float CorreccionDePies()
    {
        Renderer visible = null;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is SkinnedMeshRenderer && r.enabled) { visible = r; break; } // el cuerpo animado manda
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            visible = r;
            break;
        }
        if (visible == null) return 0f;
        return -(visible.bounds.min.y - transform.position.y);
    }

    // Si el mesh cuelga bajo el origen del transform, lo sube hasta apoyar los pies visuales
    void MedidaYCorrige()
    {
        float correccion = CorreccionDePies();
        if (correccion > 0f)
        {
            float escala = transform.lossyScale.y != 0f ? transform.lossyScale.y : 1f;
            transform.position += Vector3.up * (correccion / escala);
        }
    }

    // Apertura de la mira segun el estado (apuntando = cerrada, corriendo = abierta)
    void ActualizarMira()
    {
        if (mira == null) return;
        float apertura = apuntando ? 4f : (corriendo ? 15f : (agachado ? 7f : 9f));
        if (!controller.isGrounded) apertura += 5f;
        mira.SetApertura(apertura);
    }

    // ------------------------- AGACHARSE -------------------------

    void LeerAgacharse()
    {
        bool quiere = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.cKey.isPressed;

        if (quiere && !agachado)
        {
            agachado = true;
            AplicarAltura(alturaAgachado);
        }
        else if (!quiere && agachado && TieneEspacioParaPararse())
        {
            agachado = false;
            AplicarAltura(alturaDePie);
        }

        if (animator != null) animator.SetBool("Squat", agachado);
    }

    void AplicarAltura(float altura)
    {
        controller.height = altura;
        // apoyado en los PIES VISUALES del mesh, no en la base del transform
        controller.center = new Vector3(0f, piesRelativos + altura * 0.5f, 0f);
    }

    bool TieneEspacioParaPararse()
    {
        float radio = controller.radius * 0.95f;
        Vector3 origen = transform.position + Vector3.up * (piesRelativos + alturaAgachado - radio);
        float distancia = alturaDePie - alturaAgachado + 0.1f;
        return !Physics.SphereCast(origen, radio, Vector3.up, out _, distancia, capasTecho, QueryTriggerInteraction.Ignore);
    }

    // La camara baja y sube suavemente al agacharse; al apuntar pasa sobre el hombro derecho y hace zoom
    void BajarCamara()
    {
        if (camaraTransform == null) return;
        // apuntando usa fraccionOjos (tu valor afinado); sin apuntar usa fraccionOjosLibre (mas alta)
        float fraccion = apuntando ? fraccionOjos : fraccionOjosLibre;
        float alturaBase = agachado ? alturaAgachado : alturaDePie;
        float alturaObjetivo = alturaBase * fraccion + piesRelativos;

        // apuntando: sobre el hombro derecho y mas cerca del cuerpo
        float xObjetivo = apuntando ? hombroApuntado : 0f;
        float zObjetivo = apuntando ? Mathf.Max(0.05f, atrasCamara * 0.35f) : -atrasCamara;

        float t = 1f - Mathf.Exp(-10f * Time.deltaTime);
        Vector3 actual = camaraTransform.localPosition;
        actual.x = Mathf.Lerp(actual.x, xObjetivo, t);
        actual.y = Mathf.Lerp(actual.y, alturaObjetivo, t);
        actual.z = Mathf.Lerp(actual.z, zObjetivo, t);
        camaraTransform.localPosition = actual;

        // zoom al apuntar
        if (camara != null)
            camara.fieldOfView = Mathf.Lerp(camara.fieldOfView, apuntando ? fovApuntado : 60f, t);
    }

    // ------------------------- APUNTAR Y DISPARAR -------------------------

    void LeerApuntar()
    {
        apuntando = Mouse.current != null && Mouse.current.rightButton.isPressed && !recargando;
        if (animator != null) animator.SetBool("Aiming", apuntando);
    }

    void Disparar()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.isPressed) return;
        if (Time.time < proximoDisparoPermitido || recargando) return;

        if (municionEnCargador <= 0)
        {
            EmpezarRecarga();
            return;
        }

        proximoDisparoPermitido = Time.time + 1f / cadencia;
        municionEnCargador--;

        // el raycast sale desde la camara para que apunte a donde mira el jugador
        Vector3 origen = camaraTransform != null
            ? camaraTransform.position + camaraTransform.forward * 0.2f
            : transform.position + Vector3.up * alturaOjosDePie;
        Vector3 direccion = camaraTransform != null ? camaraTransform.forward : transform.forward;

        // dispersion al disparar desde la cadera; apuntando el disparo es preciso
        if (!apuntando)
        {
            float angulo = Random.Range(-dispersion, dispersion);
            direccion = Quaternion.AngleAxis(angulo, camaraTransform != null ? camaraTransform.up : transform.up) * direccion;
        }

        // el raycast nace detras del cuerpo (camara en 3ra persona): elegimos el PRIMER impacto
        // que no sea parte del propio jugador, para nunca autodispararnos
        RaycastHit elegido = default;
        bool acerto = false;
        RaycastHit[] impactos = Physics.RaycastAll(origen, direccion, alcance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(impactos, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in impactos)
        {
            if (h.collider.transform.IsChildOf(transform)) continue; // saltar el propio cuerpo/arma
            elegido = h;
            acerto = true;
            break;
        }

        if (acerto)
        {
            // los enemigos implementan TakeDamage(float); si no lo tienen, no pasa nada
            elegido.collider.SendMessageUpwards("TakeDamage", danio, SendMessageOptions.DontRequireReceiver);
            if (efectoImpacto != null)
                Instantiate(efectoImpacto, elegido.point, Quaternion.LookRotation(elegido.normal));
        }

        if (efectoDisparo != null && bocaDelArma != null)
            Instantiate(efectoDisparo, bocaDelArma.position, bocaDelArma.rotation);

        if (animator != null) animator.SetTrigger("Attack");

        // retroceso leve de la camara
        if (playerCamera != null) playerCamera.rotacionVertical += apuntando ? 0.35f : 0.6f;
    }

    // ------------------------- RECARGA -------------------------

    void LeerRecarga()
    {
        if (Keyboard.current.rKey.wasPressedThisFrame) EmpezarRecarga();
    }

    void EmpezarRecarga()
    {
        if (recargando || municionEnCargador >= tamanoCargador || municionReserva <= 0) return;
        StartCoroutine(RecargarRutina());
    }

    IEnumerator RecargarRutina()
    {
        recargando = true;
        apuntando = false;
        if (animator != null) animator.SetBool("Aiming", false);

        yield return new WaitForSeconds(tiempoRecarga);

        int faltante = tamanoCargador - municionEnCargador;
        int tomada = Mathf.Min(faltante, municionReserva);
        municionEnCargador += tomada;
        municionReserva -= tomada;
        recargando = false;
    }

    // ------------------------- ARMAS -------------------------

    void LeerCambioDeArma()
    {
        if (controlDeArmas == null) return;
        if (Keyboard.current.digit1Key.wasPressedThisFrame) controlDeArmas.SetArsenal("Rifle");
        if (Keyboard.current.digit2Key.wasPressedThisFrame) controlDeArmas.SetArsenal("Empty");
    }

    // ------------------------- ANIMACIONES -------------------------

    void Animar()
    {
        if (animator == null) return;
        // El blend tree del asset usa: 0 = quieto, 0.5 = caminar, 1 = correr
        float normalizado = Mathf.Clamp01(velocidadHorizontalActual / velocidadCorrer);
        float valor = normalizado <= 0.05f ? 0f : (corriendo && !agachado ? 1f : 0.5f);
        animator.SetFloat("Speed", valor);

        // El asset NO tiene animaciones de strafe (caminar de costado): sin apuntar, el MODELO
        // gira hacia donde camina (estilo RE4/Zelda) para nunca verse caminando de costado.
        // Apuntando (o quieto) vuelve a mirar al frente, alineado con la camara.
        if (modelo == null) modelo = animator.transform;
        float objetivo = 0f;
        if (!apuntando && giroModeloSinApuntar > 0f && velocidadHorizontalActual > 0.3f)
        {
            Vector3 local = transform.InverseTransformDirection(velocidadHorizontal);
            if (local.sqrMagnitude > 0.0001f)
                objetivo = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }
        float tm = 1f - Mathf.Exp(-giroModeloSinApuntar * Time.deltaTime);
        anguloModelo = Mathf.LerpAngle(anguloModelo, objetivo, tm);
        modelo.localRotation = Quaternion.Euler(0f, anguloModelo, 0f);
    }

    // ------------------------- SALUD -------------------------

    public void TakeDamage(float cantidad)
    {
        if (muerto) return;
        Salud = Mathf.Max(0f, Salud - cantidad);

        if (animator != null)
        {
            animator.SetInteger("DamageID", Random.Range(0, 3));
            animator.SetTrigger("Damage");
        }

        if (Salud <= 0f) Morir();
    }

    void Morir()
    {
        muerto = true;
        apuntando = false;
        if (mira != null) mira.Mostrar(false);
        if (animator != null) animator.SetTrigger("Death");
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Revivir()
    {
        muerto = false;
        Salud = saludMaxima;
        municionEnCargador = tamanoCargador;
        velocidadHorizontal = Vector3.zero;
        velocidadVertical = 0f;
        if (animator != null) animator.Play("Idle", 0);
        if (mira != null) mira.Mostrar(true);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
