using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class BarraVida : MonoBehaviour
{
    public Slider slider;

    private PlayerNetworkMovement jugadorLocal;

    void Update()
    {
        // Busca al jugador local si todavía no lo tiene (por ejemplo, al iniciar la partida)
        if (jugadorLocal == null)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

            NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayerObject == null) return;

            jugadorLocal = localPlayerObject.GetComponent<PlayerNetworkMovement>();
            if (jugadorLocal == null) return;

            slider.maxValue = jugadorLocal.saludMaxima;
        }

        slider.value = jugadorLocal.Salud;
    }
}
