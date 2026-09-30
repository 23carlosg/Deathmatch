using UnityEngine;
using Unity.Netcode;

// Va en el mismo GameObject que el componente NetworkManager.
// Evita que se creen NetworkManagers duplicados al recargar la escena del Menú.
public class NetworkManagerSingleton : MonoBehaviour
{
    void Awake()
    {
        // Si ya existe un NetworkManager persistente de antes, este duplicado se destruye
        if (NetworkManager.Singleton != null && NetworkManager.Singleton != GetComponent<NetworkManager>())
        {
            Destroy(gameObject);
        }
    }
}