using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class NetworkLobbyManager : MonoBehaviour
{
    private static NetworkLobbyManager instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject); // <-- esto lo hace persistente
    }

    public void IniciarHost()
    {
        NetworkManager.Singleton.StartHost();
    }

    public void IniciarCliente()
    {
        NetworkManager.Singleton.StartClient();
    }
}