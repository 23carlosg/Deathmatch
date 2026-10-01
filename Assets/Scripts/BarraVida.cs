using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    public Slider slider;
    public GameObject fill; // Arrastrá acá el objeto "Fill" (dentro de Fill Area)

    private PlayerNetworkMovement jugadorLocal;

    void Update()
    {
        if (jugadorLocal == null)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

            NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayerObject == null) return;

            jugadorLocal = localPlayerObject.GetComponent<PlayerNetworkMovement>();
            if (jugadorLocal == null) return;

            slider.maxValue = jugadorLocal.saludMaxima;
        }


        bool debeVerse = !jugadorLocal.Muerto;

        if (fill != null && fill.activeSelf != debeVerse)
        {
            fill.SetActive(debeVerse);
        }

        if (debeVerse)
        {
            slider.value = jugadorLocal.Salud;
        }
    }
}