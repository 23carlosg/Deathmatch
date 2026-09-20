using UnityEngine;
using Unity.Netcode;

public class NetworkLobbyManager : MonoBehaviour
{
    // IP a la que se conecta el cliente (configurable desde el Inspector).
    // En LAN/red: la IP local del host (ej. 192.168.1.15), NO 127.0.0.1.
    [SerializeField] private string ipDelHost = "127.0.0.1";

    // Tiene que coincidir EXACTO (mayusculas incluidas) con el nombre de la escena
    // tal cual figura en File > Build Settings > Scenes In Build.
    [SerializeField] private string escenaDeJuego = "SceneSample";

    [Header("Puntos de spawn (coordenadas del mapa)")]
    [SerializeField] private Vector3[] puntosDeSpawn = new Vector3[]
    {
        new Vector3(-58, 0, 178),
        new Vector3(-58, 0, 102),
        new Vector3(-134, 0, 102),
        new Vector3(-134, 0, 178),
    };

    int siguienteSpawn = 0;
    Vector3[] ordenSpawnBarajado;

    // Bandera global: se pone en true recien cuando SceneSample termino de cargar
    // (tanto en el host como en cada cliente). Mientras sea false, PlayerAstra no se mueve,
    // para que no caiga al vacio antes de que exista el piso del mapa real.
    public static bool MapaListo { get; private set; } = false;
    public static void ForzarMapaListoSiTarda() { MapaListo = true; }

    static NetworkLobbyManager instancia;

    void Awake()
    {
        // Singleton persistente: este objeto (y el NetworkManager que vive con el)
        // tiene que sobrevivir al cambio de escena Menu -> SceneSample, sino
        // Unity lo destruye a mitad de la conexion y se rompe todo.
        if (instancia == null)
        {
            instancia = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instancia != this)
        {
            // Ya existe uno (por ejemplo, volviste a cargar el Menu sin querer):
            // este duplicado se destruye para no tener dos NetworkManager al mismo tiempo.
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // Se mueve aca (en vez de Awake) porque Unity no garantiza el orden entre
        // Awake() de objetos distintos: si este corria antes que el Awake() del
        // NetworkManager, Singleton todavia era null y tiraba NullReferenceException.
        // Start() SI se garantiza que corre despues de todos los Awake() de la escena.
        if (instancia != this) return; // este objeto es un duplicado que ya se va a destruir

        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback += AprobarConexion;
        // OJO: NetworkManager.Singleton.SceneManager es null hasta que arranca la red
        // (StartHost/StartClient), asi que esa suscripcion se hace mas abajo,
        // recien cuando el host o el cliente arrancan de verdad.
        BarajarPuntosDeSpawn();
    }

    void OnDestroy()
    {
        if (instancia != this) return; // el duplicado destruido no tenia nada suscripto
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= AprobarConexion;
            if (NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= AlTerminarDeCargarEscena;
        }
    }

    void AlTerminarDeCargarEscena(string nombreEscena, UnityEngine.SceneManagement.LoadSceneMode modo,
        System.Collections.Generic.List<ulong> clientesCompletados, System.Collections.Generic.List<ulong> clientesConTimeout)
    {
        Debug.Log($"[Lobby] Escena cargada por red: '{nombreEscena}' (esperada: '{escenaDeJuego}')");
        if (nombreEscena == escenaDeJuego) MapaListo = true;
    }

    // Baraja los puntos de spawn (Fisher-Yates) para que el orden cambie entre partidas.
    void BarajarPuntosDeSpawn()
    {
        ordenSpawnBarajado = (Vector3[])puntosDeSpawn.Clone();
        for (int i = ordenSpawnBarajado.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (ordenSpawnBarajado[i], ordenSpawnBarajado[j]) = (ordenSpawnBarajado[j], ordenSpawnBarajado[i]);
        }
    }

    // Se llama para CADA jugador que se conecta, incluido el host. Ahi elegimos
    // un punto de spawn distinto y se lo asignamos antes de que Netcode cree su objeto.
    void AprobarConexion(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        Vector3 pos = Vector3.zero;

        if (ordenSpawnBarajado != null && ordenSpawnBarajado.Length > 0)
        {
            pos = ordenSpawnBarajado[siguienteSpawn % ordenSpawnBarajado.Length];
            siguienteSpawn++;
        }
        else
        {
            Debug.LogWarning("[Lobby] No hay puntos de spawn asignados en el Inspector, todos van a (0,0,0)");
        }

        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Position = pos;
        response.Rotation = Quaternion.identity;
        response.Pending = false;
    }

    public void IniciarHost()
    {
        // Inicia el modo Host (Servidor autoritativo + Cliente local)
        NetworkManager.Singleton.OnServerStarted += CargarEscenaDeJuego;
        if (NetworkManager.Singleton.StartHost())
        {
            // Recien ahora SceneManager existe de verdad: nos suscribimos aca.
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += AlTerminarDeCargarEscena;
        }
        else
        {
            NetworkManager.Singleton.OnServerStarted -= CargarEscenaDeJuego;
            Debug.LogError("[Lobby] ERROR: no se pudo iniciar el host");
        }
    }

    public void IniciarCliente()
    {
        // Se conecta al Host remoto como un cliente
        ConfigurarTransporte();
        NetworkManager.Singleton.OnClientConnectedCallback += AlConectarse;
        NetworkManager.Singleton.OnClientDisconnectCallback += AlDesconectarse;
        if (NetworkManager.Singleton.StartClient())
        {
            // Recien ahora SceneManager existe de verdad: nos suscribimos aca.
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += AlTerminarDeCargarEscena;
            Debug.Log("[Lobby] Conectando a " + ipDelHost + "...");
        }
        else
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= AlConectarse;
            NetworkManager.Singleton.OnClientDisconnectCallback -= AlDesconectarse;
            Debug.LogError("[Lobby] ERROR: no se pudo conectar a " + ipDelHost);
        }
    }

    // La escena de juego se carga UNA VEZ en el host, via NetworkSceneManager,
    // para que se replique sola a cada cliente al conectarse.
    void CargarEscenaDeJuego()
    {
        NetworkManager.Singleton.OnServerStarted -= CargarEscenaDeJuego;
        NetworkManager.Singleton.SceneManager.LoadScene(escenaDeJuego, UnityEngine.SceneManagement.LoadSceneMode.Single);
        Debug.Log("[Lobby] HOST iniciado - cargando " + escenaDeJuego + " por la red");
    }

    // Setea la IP destino (y el puerto de escucha) en el UnityTransport antes de conectar.
    void ConfigurarTransporte()
    {
        var transporte = GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transporte == null) transporte = FindAnyObjectByType<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transporte == null)
        {
            Debug.LogError("[Lobby] ERROR: no hay UnityTransport en la escena");
            return;
        }
        if (!string.IsNullOrWhiteSpace(ipDelHost)) transporte.ConnectionData.Address = ipDelHost;
        transporte.ConnectionData.Port = 7777;
    }

    void AlConectarse(ulong clientId)
    {
        Debug.Log("[Lobby] Conectado");
    }

    void AlDesconectarse(ulong clientId)
    {
        Debug.Log("[Lobby] Desconectado del servidor");
    }
}