using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Va en el mismo GameObject "Camera" (hijo del Player), junto a Camera, AudioListener
/// y PlayerCamera. Se encarga de dejar activos Camera + AudioListener + PlayerCamera
/// SOLO en la instancia del dueño; en las instancias remotas los apaga, para que no
/// compitan varias camaras/AudioListeners en la escena (evita el warning de
/// "3 audio listeners" y que un cliente vea por la camara de otro jugador).
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraNetwork : NetworkBehaviour
{
    Camera cam;
    AudioListener listener;
    PlayerCamera playerCameraScript;

    void Awake()
    {
        cam = GetComponent<Camera>();
        listener = GetComponent<AudioListener>();
        playerCameraScript = GetComponent<PlayerCamera>();

        // Apagados por defecto hasta que sepamos si somos el dueño (evita 1 frame de
        // "todas las camaras activas" mientras se resuelve el spawn).
        if (cam != null) cam.enabled = false;
        if (listener != null) listener.enabled = false;
        if (playerCameraScript != null) playerCameraScript.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        bool esDueno = IsOwner;

        if (cam != null) cam.enabled = esDueno;
        if (listener != null) listener.enabled = esDueno;
        if (playerCameraScript != null) playerCameraScript.enabled = esDueno;
    }
}