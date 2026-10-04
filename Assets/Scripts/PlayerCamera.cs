using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Camara estilo Fortnite (tercera persona):
/// - El mouse gira la camara y el CUERPO al mismo tiempo, sin retardo (yaw compartido)
/// - La camara es HIJA del cuerpo: sigue su posicion siempre, sin codigo extra
/// - El pitch (arriba/abajo) queda solo en la camara
/// - Esc libera / captura el mouse
/// Todo se aplica en LateUpdate (despues de que PlayerNetworkMovement movio el cuerpo).
/// Si el jugador esta muerto no gira nada y no captura el cursor.
/// </summary>
public class PlayerCamera : MonoBehaviour
{
    public Transform jugador;            // cuerpo del jugador (se asigna solo al padre si esta vacio)
    public float sensibilidad = 2f;
    public float limiteVertical = 80f;
    public float rotacionVertical;       // inclinacion actual (pitch)

    // Estado de muerte del dueño, escrito por PlayerNetworkMovement y consultado por la camara
    public static bool DuenoMuerto { get; set; }

    // Menu de pausa
    private PauseMenuUI pausa;

void Start()
{
    pausa = FindAnyObjectByType<PauseMenuUI>();
}

    void Awake()
    {
        if (jugador == null && transform.parent != null)
            jugador = transform.parent;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        // si el dueño esta muerto, el cuerpo queda quieto
        if (DuenoMuerto) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (pausa == null) pausa = FindAnyObjectByType<PauseMenuUI>();
            if (pausa != null) pausa.Alternar();
        }

        // con el menú abierto la cámara no gira
        if (PauseMenuUI.Abierto) return;

        if (jugador == null || Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
            return;

        Vector2 delta = Mouse.current.delta.ReadValue() * (sensibilidad * 0.1f);

        // yaw: gira el CUERPO (la camara, como hija, lo acompana instantaneamente)
        jugador.rotation = Quaternion.Euler(0f, jugador.eulerAngles.y + delta.x, 0f);

        // pitch: solo la camara
        rotacionVertical = Mathf.Clamp(rotacionVertical - delta.y, -limiteVertical, limiteVertical);
        transform.localRotation = Quaternion.Euler(rotacionVertical, 0f, 0f);
    }
}
