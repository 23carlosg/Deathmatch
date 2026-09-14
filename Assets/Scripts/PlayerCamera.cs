using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Camara estilo Fortnite (tercera persona):
/// - El mouse gira la camara y el CUERPO al mismo tiempo, sin retardo (yaw compartido)
/// - La camara es HIJA del cuerpo: sigue su posicion siempre, sin codigo extra
/// - El pitch (arriba/abajo) queda solo en la camara
/// - Esc libera / captura el mouse
/// Todo se aplica en LateUpdate (despues de que Player movio el cuerpo).
/// </summary>
public class PlayerCamera : MonoBehaviour
{
    public Transform jugador;            // cuerpo del jugador (se asigna solo al padre si esta vacio)
    public float sensibilidad = 2f;
    public float limiteVertical = 80f;
    public float rotacionVertical;       // inclinacion actual (pitch)

    void Awake()
    {
        if (jugador == null && transform.parent != null)
            jugador = transform.parent;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        // Esc para liberar o capturar el mouse
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            bool bloqueado = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = bloqueado ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = !bloqueado;
        }

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
