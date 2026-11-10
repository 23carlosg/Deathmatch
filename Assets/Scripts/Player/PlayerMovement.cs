using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Movimiento del jugador: caminar/correr, salto, gravedad y la animacion de locomocion.
/// Publica por red la velocidad y si esta en el piso, para que los demas lo animen.
/// No tiene Update propio: lo llama PlayerNetworkMovement en orden.
/// </summary>
public class PlayerMovement : NetworkBehaviour
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

    readonly NetworkVariable<float> redVelX = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> redVelY = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<bool> redEnElPiso = new NetworkVariable<bool>(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    PlayerNetworkMovement jugador;
    CharacterController controller;

    Vector3 velocidadHorizontal;
    float velocidadVertical;
    float velXSuave, velYSuave;
    float coyoteTimer;
    float bufferSaltoTimer;
    bool corriendo;
    bool enSaltoReal;
    float tiempoEnElAire;
    bool murioCayendo;

    public bool Corriendo => corriendo;

    public void Iniciar(PlayerNetworkMovement j)
    {
        jugador = j;
        controller = j.Controller;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsLocalPlayer)
        {
            velXSuave = redVelX.Value;
            velYSuave = redVelY.Value;
        }
    }

    // ------------------------- LOCAL -------------------------

    public void Mover()
    {
        bool enSuelo = controller.isGrounded;
        bool libre = PlayerNetworkMovement.InputBloqueado;

        coyoteTimer = enSuelo ? coyoteTime : coyoteTimer - Time.deltaTime;
        if (!libre && Keyboard.current.spaceKey.wasPressedThisFrame) bufferSaltoTimer = bufferSalto;
        else bufferSaltoTimer -= Time.deltaTime;

        float x = 0f, z = 0f;
        if (!libre)
        {
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) x += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) z -= 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) z += 1f;
        }
        Vector2 entrada = Vector2.ClampMagnitude(new Vector2(x, z), 1f);

        corriendo = !libre && Keyboard.current.leftShiftKey.isPressed;
        float velocidadObjetivo = corriendo ? velocidadCorrer : velocidadCaminar;
        Vector3 deseada = transform.TransformDirection(new Vector3(entrada.x, 0f, entrada.y)) * velocidadObjetivo;

        // salto
        if (bufferSaltoTimer > 0f && coyoteTimer > 0f)
        {
            float altura = alturaSalto * (jugador.Weapons.ArmaActual == 1 ? multiplicadorSaltoRifle : 1f);
            velocidadVertical = Mathf.Sqrt(2f * gravedad * altura);
            coyoteTimer = 0f;
            bufferSaltoTimer = 0f;
            enSaltoReal = true;

            if (jugador.animator != null) jugador.animator.SetBool("isGrounded", false);
        }

        // gravedad
        if (enSuelo && velocidadVertical < 0f) velocidadVertical = -2f;
        velocidadVertical -= gravedad * Time.deltaTime;

        Vector3 objetivo = enSuelo ? deseada : Vector3.Lerp(velocidadHorizontal, deseada, controlEnElAire);
        float t = 1f - Mathf.Exp(-aceleracion * Time.deltaTime);
        velocidadHorizontal = Vector3.Lerp(velocidadHorizontal, objetivo, t);

        controller.Move((velocidadHorizontal + Vector3.up * velocidadVertical) * Time.deltaTime);
    }

    public void Animar()
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

        Animator animator = jugador.animator;
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

    // ------------------------- REMOTO -------------------------

    // Anima a los demas jugadores con los valores que llegan por red
    public void ActualizarAnimacionRemota()
    {
        Animator animator = jugador.animator;
        if (animator == null) return;

        float t = 1f - Mathf.Exp(-15f * Time.deltaTime);
        velXSuave = Mathf.Lerp(velXSuave, redVelX.Value, t);
        velYSuave = Mathf.Lerp(velYSuave, redVelY.Value, t);

        if (!jugador.Health.Muerto)
        {
            animator.SetFloat("VelX", velXSuave);
            animator.SetFloat("VelY", velYSuave);
            animator.SetBool("isGrounded", redEnElPiso.Value);
        }
    }

    // ------------------------- MUERTE / REVIVIR -------------------------

    // Mientras esta muerto en el aire, aplica gravedad hasta tocar el piso
    public void CaerMuerto()
    {
        if (!murioCayendo || controller == null || !controller.enabled) return;

        if (controller.isGrounded)
        {
            murioCayendo = false;
            velocidadVertical = 0f;
            controller.enabled = false; // ya en el piso: se apaga como antes
            return;
        }

        velocidadVertical -= gravedad * Time.deltaTime;
        controller.Move(Vector3.up * velocidadVertical * Time.deltaTime);
    }

    // PlayerHealth avisa que el jugador murio: frena y empieza a caer si esta en el aire
    public void AlMorir()
    {
        velocidadHorizontal = Vector3.zero;
        murioCayendo = true;
    }

    // Al revivir en otra posicion se limpia la velocidad que traia
    public void ReiniciarVelocidades()
    {
        velocidadHorizontal = Vector3.zero;
        velocidadVertical = 0f;
    }
}