using UnityEngine;
using Unity.Netcode;

public class NetworkLobbyManager : MonoBehaviour
{
    // IP a la que se conecta el cliente (en LAN: la IP local del host, no 127.0.0.1)
    [SerializeField] private string ipDelHost = "127.0.0.1";

    // Debe coincidir exacto con el nombre de la escena en File > Build Settings > Scenes In Build
    [SerializeField] private string escenaDeJuego = "SceneSample";

    // Puntos de spawn (coordenadas del mapa). Si se cambian aqui, tambien hay que
    // actualizarlos en el Inspector: Unity usa la copia serializada del componente en escena.
    [Header("Puntos de spawn (coordenadas del mapa)")]
    [SerializeField] private Vector3[] puntosDeSpawn = new Vector3[]
    {
        new Vector3(-134.085f, 1f, 106.480f),
        new Vector3(-134.085f, 1f, 146.480f),
        new Vector3(-134.085f, 1f, 174.480f),
        new Vector3(-58.085f, 1f, 106.480f),
        new Vector3(-58.085f, 1f, 110.480f),
        new Vector3(-58.085f, 1f, 174.480f),
        new Vector3(-94.085f, 1f, 102.480f),
        new Vector3(-94.085f, 1f, 178.480f),
    };

    // Caja que abraza todo el piso transitable del mapa: descarta puntos de spawn fuera de el
    [Header("Validacion (bounds del piso del mapa)")]
    [SerializeField] private Vector3 minPiso = new Vector3(-136f, -1f, 100f);
    [SerializeField] private Vector3 maxPiso = new Vector3(-56f, 5f, 180f);

    int siguienteSpawn = 0;
    Vector3[] ordenSpawnBarajado;

    // true cuando la escena de juego termino de cargar en esta maquina; mientras,
    // PlayerAstra no se mueve para no caer antes de que exista el piso
    public static bool MapaListo { get; private set; } = false;
    public static void ForzarMapaListoSiTarda() { MapaListo = true; }

    // true si la escena activa ya es la de juego: cubre al cliente que entra tarde,
    // cuya escena llega por sincronizacion (que no dispara OnLoadEventCompleted)
    public static bool EscenaDeJuegoActiva
    {
        get
        {
            var i = instancia;
            return i != null && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == i.escenaDeJuego;
        }
    }

    // gate de movimiento: aviso por red O escena de juego ya activa
    public static bool PuedeMoverse => MapaListo || EscenaDeJuegoActiva;

    static NetworkLobbyManager instancia;

    void Awake()
    {
        // Singleton persistente: sobrevive al cambio de escena Menu -> escena de juego
        if (instancia == null)
        {
            instancia = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instancia != this)
        {
            Destroy(gameObject);   // un segundo NetworkManager rompe la conexion
        }
    }

    void Start()
    {
        // en Start (no Awake) para que NetworkManager.Singleton ya este inicializado
        if (instancia != this) return;

        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback += AprobarConexion;
        // SceneManager es null hasta que arranca la red; la suscripcion se hace al iniciar host/cliente
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
        if (nombreEscena == escenaDeJuego) MapaListo = true;
    }

    // Baraja los puntos de spawn (Fisher-Yates) para que el orden cambie entre partidas.
    // Los puntos fuera de los bounds del piso ya fueron descartados en ValidadPuntos().
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
        Vector3 pos = PuntoCentralDelPiso();   // fallback: nunca (0,0,0)

        if (ordenSpawnBarajado != null && ordenSpawnBarajado.Length > 0)
            pos = ordenSpawnBarajado[siguienteSpawn % ordenSpawnBarajado.Length];

        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Position = pos;
        response.Rotation = Quaternion.identity;
        response.Pending = false;
    }

    // Fallback si el array llega vacio (o todo invalido): el centro aproximado del piso.
    Vector3 PuntoCentralDelPiso()
    {
        return new Vector3((minPiso.x + maxPiso.x) * 0.5f, 1f, (minPiso.z + maxPiso.z) * 0.5f);
    }

    // Dibuja los puntos de spawn y los bounds del piso SOLO en edicion (no en play)
    void OnDrawGizmos()
    {
        if (Application.isPlaying) return;
        Gizmos.color = new Color(1f, 0.35f, 0f, 0.9f);
        Gizmos.DrawWireCube(new Vector3((minPiso.x + maxPiso.x) * 0.5f, 2f, (minPiso.z + maxPiso.z) * 0.5f),
                            new Vector3(maxPiso.x - minPiso.x, 6f, maxPiso.z - minPiso.z));

        if (puntosDeSpawn == null) return;
        for (int i = 0; i < puntosDeSpawn.Length; i++)
        {
            bool valido = DentroDelPiso(puntosDeSpawn[i]);
            Gizmos.color = !valido ? Color.red : (i == siguienteSpawn % Mathf.Max(1, puntosDeSpawn.Length) ? Color.yellow : Color.green);
            Gizmos.DrawSphere(puntosDeSpawn[i], 0.4f);
            Gizmos.DrawLine(puntosDeSpawn[i], puntosDeSpawn[i] + Vector3.up * 2f);
        }
    }

    bool DentroDelPiso(Vector3 p)
    {
        return p.x >= minPiso.x && p.x <= maxPiso.x && p.z >= minPiso.z && p.z <= maxPiso.z;
    }

    // Descarta (solo en play) los puntos fuera del piso y deja un warning para corregirlos
    void ValidadPuntos()
    {
        if (puntosDeSpawn == null || puntosDeSpawn.Length == 0) return;

        int validos = 0;
        for (int i = 0; i < puntosDeSpawn.Length; i++)
        {
            if (DentroDelPiso(puntosDeSpawn[i]))
            {
                if (validos != i) puntosDeSpawn[validos] = puntosDeSpawn[i];
                validos++;
            }
            else
            {
                Debug.LogWarning($"[Lobby] Punto de spawn {i} ({puntosDeSpawn[i]}) queda FUERA de los bounds del piso y fue descartado. Corregilo en el Inspector.");
            }
        }

        if (validos == 0)
        {
            Debug.LogWarning("[Lobby] Ningun punto de spawn es valido, se usa el centro del piso.");
            puntosDeSpawn = new Vector3[] { PuntoCentralDelPiso() };
        }
        else if (validos < puntosDeSpawn.Length)
        {
            System.Array.Resize(ref puntosDeSpawn, validos);
        }
    }

    public void IniciarHost()
    {
        ValidadPuntos();
        BarajarPuntosDeSpawn();   // por si el array cambio despues del Start
        NetworkManager.Singleton.OnServerStarted += CargarEscenaDeJuego;
        if (NetworkManager.Singleton.StartHost())
        {
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
        ValidadPuntos();
        ConfigurarTransporte();
        if (NetworkManager.Singleton.StartClient())
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += AlTerminarDeCargarEscena;
        }
        else
        {
            Debug.LogError("[Lobby] ERROR: no se pudo conectar a " + ipDelHost);
        }
    }

    // La escena de juego se carga una vez en el host via NetworkSceneManager y se
    // replica sola a cada cliente que se conecta.
    void CargarEscenaDeJuego()
    {
        NetworkManager.Singleton.OnServerStarted -= CargarEscenaDeJuego;
        NetworkManager.Singleton.SceneManager.LoadScene(escenaDeJuego, UnityEngine.SceneManagement.LoadSceneMode.Single);
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
}
