using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCamera : MonoBehaviour
{

    private readonly List<Renderer> ocultados = new List<Renderer>();
    public Transform jugador;
    public float sensibilidad = 2f;
    public float limiteVertical = 80f;
    public float rotacionVertical;

    [Header("Camara libre")]
    public float velocidadLibre = 10f;
    public float multiplicadorRapido = 3f;   // con Shift

    public static bool DuenoMuerto { get; set; }

    // PlayerNetworkMovement debe consultar esto para ignorar el input del cuerpo
    public static bool CamaraLibre { get; private set; }

    private PauseMenuUI pausa;

    // estado camara libre
    private Vector3 posLocalOriginal;
    private float yawLibre;
    private float pitchLibre;

    void Start()
    {
        pausa = FindAnyObjectByType<PauseMenuUI>();
    }

    void Awake()
    {
        if (jugador == null && transform.parent != null)
            jugador = transform.parent;

        posLocalOriginal = transform.localPosition;
        CamaraLibre = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDestroy()
    {
        CamaraLibre = false;
    }

    void LateUpdate()
    {
        if (DuenoMuerto)
        {
            // si muere en camara libre, volver a la normal
            if (CamaraLibre) SalirCamaraLibre();
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (pausa == null) pausa = FindAnyObjectByType<PauseMenuUI>();
            if (pausa != null) pausa.Alternar();
        }

        if (PauseMenuUI.Abierto) return;

        if (Keyboard.current != null && Keyboard.current.zKey.wasPressedThisFrame)
        {
            if (CamaraLibre) SalirCamaraLibre();
            else EntrarCamaraLibre();
        }

        if (jugador == null || Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
            return;

        Vector2 delta = Mouse.current.delta.ReadValue() * (sensibilidad * 0.1f);

        if (CamaraLibre)
            ActualizarCamaraLibre(delta);
        else
            ActualizarCamaraNormal(delta);
    }

    void ActualizarCamaraNormal(Vector2 delta)
    {
        jugador.rotation = Quaternion.Euler(0f, jugador.eulerAngles.y + delta.x, 0f);

        rotacionVertical = Mathf.Clamp(rotacionVertical - delta.y, -limiteVertical, limiteVertical);
        transform.localRotation = Quaternion.Euler(rotacionVertical, 0f, 0f);
    }

    void ActualizarCamaraLibre(Vector2 delta)
    {
        // rotacion propia de la camara (el cuerpo no se toca)
        yawLibre += delta.x;
        pitchLibre = Mathf.Clamp(pitchLibre - delta.y, -89f, 89f);
        transform.rotation = Quaternion.Euler(pitchLibre, yawLibre, 0f);

        // movimiento
        var k = Keyboard.current;
        Vector3 dir = Vector3.zero;
        if (k.wKey.isPressed) dir += transform.forward;
        if (k.sKey.isPressed) dir -= transform.forward;
        if (k.dKey.isPressed) dir += transform.right;
        if (k.aKey.isPressed) dir -= transform.right;
        if (k.spaceKey.isPressed) dir += Vector3.up;
        if (k.leftCtrlKey.isPressed) dir -= Vector3.up;

        float vel = velocidadLibre * (k.leftShiftKey.isPressed ? multiplicadorRapido : 1f);
        transform.position += dir.normalized * (vel * Time.deltaTime);
    }

    void EntrarCamaraLibre()
    {
        CamaraLibre = true;

        posLocalOriginal = transform.localPosition;

        Vector3 e = transform.eulerAngles;
        yawLibre = e.y;
        pitchLibre = e.x > 180f ? e.x - 360f : e.x;

        // sacar el zoom de apuntar
        Camera cam = GetComponent<Camera>();
        if (cam != null) cam.fieldOfView = 60f;

        transform.SetParent(null, true);

        FindAnyObjectByType<HealthBarUI>(FindObjectsInactive.Include).gameObject.SetActive(false);

        MostrarJugador(false);
    }

    void SalirCamaraLibre()
    {
        CamaraLibre = false;

        // volver a ser hija del cuerpo
        transform.SetParent(jugador, false);
        transform.localPosition = posLocalOriginal;
        transform.localRotation = Quaternion.Euler(rotacionVertical, 0f, 0f);

        FindAnyObjectByType<HealthBarUI>(FindObjectsInactive.Include).gameObject.SetActive(true);

        MostrarJugador(true);
    }

    void MostrarJugador(bool visible)
    {
        if (jugador == null) return;

        if (!visible)
        {
            ocultados.Clear();
            // solo renderers activos (las armas guardadas quedan intactas)
            foreach (var r in jugador.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                r.enabled = false;
                ocultados.Add(r);
            }
        }
        else
        {
            foreach (var r in ocultados)
                if (r != null) r.enabled = true;
            ocultados.Clear();
        }
    }
}