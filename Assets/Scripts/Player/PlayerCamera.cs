using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCamera : MonoBehaviour
{
    public Transform jugador;
    public float sensibilidad = 2f;
    public float limiteVertical = 80f;
    public float rotacionVertical;

    public static bool DuenoMuerto { get; set; }

    public static bool CamaraLibre => false;

    private PauseMenuUI pausa;

    void Start()
    {
        pausa = FindAnyObjectByType<PauseMenuUI>();
    }

    void Awake()
    {
        if (jugador == null && transform.parent != null)
            jugador = transform.parent;

        if (!PlayerNetworkMovement.PartidaTerminada)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void LateUpdate()
    {
        if (DuenoMuerto || PlayerNetworkMovement.PartidaTerminada) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (pausa == null) pausa = FindAnyObjectByType<PauseMenuUI>();
            if (pausa != null) pausa.Alternar();
        }

        if (PauseMenuUI.Abierto) return;

        if (jugador == null || Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
            return;

        Vector2 delta = Mouse.current.delta.ReadValue() * (sensibilidad * 0.1f);

        jugador.rotation = Quaternion.Euler(0f, jugador.eulerAngles.y + delta.x, 0f);

        rotacionVertical = Mathf.Clamp(rotacionVertical - delta.y, -limiteVertical, limiteVertical);
        transform.localRotation = Quaternion.Euler(rotacionVertical, 0f, 0f);
    }
}