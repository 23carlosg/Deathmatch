using UnityEngine;
using Unity.Netcode;

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