using UnityEngine;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using System.Threading.Tasks;

public class NetworkLobbyManager : MonoBehaviour
{
    [SerializeField] private GameObject lobbyRoomStatePrefab;
    [SerializeField] private string escenaDeJuego = "Arena";

    [Header("Puntos de spawn (coordenadas del mapa)")]
    [SerializeField]
    private Vector3[] puntosDeSpawn = new Vector3[]
    {
        new Vector3(-134.085f, 1f, 106.480f),
        new Vector3(-134.085f, 1f, 140f),
        new Vector3(-134.085f, 1f, 174.480f),
        new Vector3(-58.085f, 1f, 106.480f),
        new Vector3(-58.085f, 1f, 140f),
        new Vector3(-58.085f, 1f, 174.480f),
        new Vector3(-100f, 1f, 102.480f),
        new Vector3(-100f, 1f, 178.480f),
    };

    [Header("Validacion (bounds del piso del mapa)")]
    [SerializeField] private Vector3 minPiso = new Vector3(-136f, -1f, 100f);
    [SerializeField] private Vector3 maxPiso = new Vector3(-56f, 5f, 180f);

    int siguienteSpawn = 0;
    Vector3[] ordenSpawnBarajado;

    public static string JoinCodeActual { get; private set; } = "";

    // Player no se mueve para no caer antes de que exista el piso
    public static bool MapaListo { get; private set; } = false;
    public static void ForzarMapaListoSiTarda() { MapaListo = true; }
    public static void ResetearMapaListo() { MapaListo = false; }

    public static bool EscenaDeJuegoActiva
    {
        get
        {
            var i = instancia;
            return i != null && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == i.escenaDeJuego;
        }
    }

    public static bool PuedeMoverse => MapaListo || EscenaDeJuegoActiva;

    static NetworkLobbyManager instancia;
    public static NetworkLobbyManager Instancia => instancia;

    void Awake()
    {
        if (instancia == null)
        {
            instancia = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instancia != this)
        {
            var nmDuplicado = GetComponent<NetworkManager>();
            if (nmDuplicado == null)
                nmDuplicado = FindAnyObjectByType<NetworkManager>();
            if (nmDuplicado != null && nmDuplicado != NetworkManager.Singleton)
                Destroy(nmDuplicado.gameObject);
            Destroy(gameObject);
        }
    }

    void Start()
    {
        if (instancia != this) return;

        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback += AprobarConexion;
        BarajarPuntosDeSpawn();
    }

    void OnDestroy()
    {
        if (instancia != this) return;
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= AprobarConexion;
            if (NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= AlTerminarDeCargarEscena;
        }
    }

    // Se llama cuando termina de cargar una escena (en el host y en los clientes)
    void AlTerminarDeCargarEscena(string nombreEscena, UnityEngine.SceneManagement.LoadSceneMode modo,
        System.Collections.Generic.List<ulong> clientesCompletados, System.Collections.Generic.List<ulong> clientesConTimeout)
    {
        if (nombreEscena != escenaDeJuego) return;

        MapaListo = true;

        // Solo el host/servidor crea los jugadores
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        CrearJugadoresFaltantes(nm);
    }

    // Crea el jugador de cada cliente conectado que todavía no tenga uno
    void CrearJugadoresFaltantes(NetworkManager nm)
    {
        GameObject prefab = nm.NetworkConfig.PlayerPrefab;
        if (prefab == null)
        {
            Debug.LogError("[Lobby] El NetworkManager no tiene Player Prefab asignado.");
            return;
        }

        foreach (ulong id in nm.ConnectedClientsIds)
        {
            if (nm.ConnectedClients.TryGetValue(id, out var cliente) && cliente.PlayerObject != null)
                continue;

            GameObject jugador = Instantiate(prefab, SiguientePosicionDeSpawn(), Quaternion.identity);
            jugador.GetComponent<NetworkObject>().SpawnAsPlayerObject(id, true);
        }
    }

    Vector3 SiguientePosicionDeSpawn()
    {
        Vector3 pos = PuntoCentralDelPiso();
        if (ordenSpawnBarajado != null && ordenSpawnBarajado.Length > 0)
            pos = ordenSpawnBarajado[siguienteSpawn % ordenSpawnBarajado.Length];
        siguienteSpawn++;
        return pos;
    }

    void BarajarPuntosDeSpawn()
    {
        ordenSpawnBarajado = (Vector3[])puntosDeSpawn.Clone();
        for (int i = ordenSpawnBarajado.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (ordenSpawnBarajado[i], ordenSpawnBarajado[j]) = (ordenSpawnBarajado[j], ordenSpawnBarajado[i]);
        }
    }

    void AprobarConexion(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        // En la sala de espera (escena Menu) NO se crea el jugador.
        // Si alguien entra con la partida ya empezada, sí se crea.
        response.Approved = true;
        response.CreatePlayerObject = EscenaDeJuegoActiva;
        response.Position = SiguientePosicionDeSpawn();
        response.Rotation = Quaternion.identity;
        response.Pending = false;
    }

    Vector3 PuntoCentralDelPiso()
    {
        return new Vector3((minPiso.x + maxPiso.x) * 0.5f, 1f, (minPiso.z + maxPiso.z) * 0.5f);
    }

    bool DentroDelPiso(Vector3 p)
    {
        return p.x >= minPiso.x && p.x <= maxPiso.x && p.z >= minPiso.z && p.z <= maxPiso.z;
    }

    void ValidarPuntos()
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
                Debug.LogWarning($"[Lobby] Punto de spawn {i} ({puntosDeSpawn[i]}) queda FUERA de los bounds del piso y fue descartado.");
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

    // Espera a que se termine de cerrar una conexión anterior
    async Task EsperarCierreDeRed()
    {
        var nm = NetworkManager.Singleton;
        if (nm.IsListening && !nm.ShutdownInProgress) nm.Shutdown();

        float espera = 0f;
        while ((nm.ShutdownInProgress || nm.IsListening) && espera < 3f)
        {
            await Task.Yield();
            espera += Time.unscaledDeltaTime;
        }
    }

    // ---------------------------------------------------------------
    // HOST: crea asignacion en Relay y arranca el host (NO carga Arena)
    // ---------------------------------------------------------------
    public async Task<string> IniciarHost()
    {
        ValidarPuntos();
        BarajarPuntosDeSpawn();
        siguienteSpawn = 0;
        JoinCodeActual = "";

        try
        {
            if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress)
                await EsperarCierreDeRed();

            await InicializarServicios();

            // 7 clientes + 1 host = 8 jugadores max
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(7);

            JoinCodeActual = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[Lobby] JOIN CODE: {JoinCodeActual}");

            var transporte = ObtenerTransporte();
            if (transporte == null) return null;

            var relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            transporte.SetRelayServerData(relayServerData);

            if (NetworkManager.Singleton.StartHost())
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= AlTerminarDeCargarEscena;
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += AlTerminarDeCargarEscena;

                Debug.Log("[Lobby] Host iniciado. Prefab sala asignado: " + (lobbyRoomStatePrefab != null));

                // Crear el estado de la sala por código
                if (lobbyRoomStatePrefab != null)
                {
                    var sala = Instantiate(lobbyRoomStatePrefab);
                    sala.GetComponent<NetworkObject>().Spawn();
                    Debug.Log("[Lobby] LobbyRoomState creado y spawneado.");
                }
                else
                {
                    Debug.LogError("[Lobby] Falta asignar Lobby Room State Prefab en el Inspector de NetworkLobby.");
                }

                return JoinCodeActual;
            }
            else
            {
                Debug.LogError("[Lobby] ERROR: no se pudo iniciar el host");
                return null;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Lobby] ERROR al iniciar host: {e.Message}");
            return null;
        }
    }

    // Lo llama el botón INICIAR PARTIDA de la sala de espera (solo el host)
    public void IniciarPartida()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        nm.SceneManager.LoadScene(escenaDeJuego, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    // ---------------------------------------------------------------
    // CLIENTE: se conecta con el Join Code
    // ---------------------------------------------------------------
    public async Task<bool> IniciarCliente(string joinCode)
    {
        JoinCodeActual = "";
        ValidarPuntos();

        try
        {
            if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress)
                await EsperarCierreDeRed();

            await InicializarServicios();

            var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var transporte = ObtenerTransporte();
            if (transporte == null) return false;

            var relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            transporte.SetRelayServerData(relayServerData);

            if (NetworkManager.Singleton.StartClient())
            {
                JoinCodeActual = joinCode;
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= AlTerminarDeCargarEscena;
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += AlTerminarDeCargarEscena;

                // Si el cliente ya tiene cargada una escena (Menu, ArenaFondo), no la recarga.
                // La escena de juego (Arena) sí se carga siempre.
                NetworkManager.Singleton.SceneManager.VerifySceneBeforeLoading = (indice, nombre, modo) =>
                {
                    var escena = UnityEngine.SceneManagement.SceneManager.GetSceneByName(nombre);
                    if (escena.isLoaded && nombre != escenaDeJuego) return false;
                    return true;
                };
                return true;
            }

            Debug.LogError("[Lobby] ERROR: no se pudo conectar");
            return false;
        }
        catch (RelayServiceException e)
        {
            Debug.LogWarning($"[Lobby] Join Code inválido o sala no disponible: {e.Message}");
            return false;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Lobby] ERROR al conectar cliente: {e.Message}");
            if (NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            return false;
        }
    }

    async Task InicializarServicios()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    Unity.Netcode.Transports.UTP.UnityTransport ObtenerTransporte()
    {
        var transporte = GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transporte == null)
            transporte = FindAnyObjectByType<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transporte == null)
            Debug.LogError("[Lobby] ERROR: no hay UnityTransport en la escena");
        return transporte;
    }

    public static Vector3 ObtenerPuntoDeRespawn()
    {
        if (instancia == null || instancia.ordenSpawnBarajado == null || instancia.ordenSpawnBarajado.Length == 0)
            return instancia != null ? instancia.PuntoCentralDelPiso() : Vector3.zero;

        int indice = Random.Range(0, instancia.ordenSpawnBarajado.Length);
        return instancia.ordenSpawnBarajado[indice];
    }
}