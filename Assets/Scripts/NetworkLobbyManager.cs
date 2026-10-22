using UnityEngine;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;

public class NetworkLobbyManager : MonoBehaviour
{
    [SerializeField] private string escenaDeJuego = "Arena";
    [SerializeField] private GameObject lobbyRoomStatePrefab;

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

    // ---------------- NOMBRES ----------------
    public static string NombreLocal { get; private set; } = "";
    static readonly Dictionary<ulong, string> nombres = new Dictionary<ulong, string>();

    public static string LimpiarNombre(string n)
    {
        if (string.IsNullOrWhiteSpace(n)) return "";
        n = n.Trim();
        return n.Length > 16 ? n.Substring(0, 16) : n;
    }

    // Lo usan LobbyRoomState y ScoreboardState (corren en el servidor)
    public static string ObtenerNombre(ulong id)
    {
        if (nombres.TryGetValue(id, out var n) && !string.IsNullOrEmpty(n)) return n;
        return "Jugador " + id;
    }

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

    void AlTerminarDeCargarEscena(string nombreEscena, UnityEngine.SceneManagement.LoadSceneMode modo,
        List<ulong> clientesCompletados, List<ulong> clientesConTimeout)
    {
        if (nombreEscena != escenaDeJuego) return;

        MapaListo = true;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        CrearJugadoresFaltantes(nm);
    }

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
        // Leer el nombre que mandó el jugador al conectarse
        string nombreRecibido = (request.Payload != null && request.Payload.Length > 0)
            ? LimpiarNombre(Encoding.UTF8.GetString(request.Payload)) : "";
        if (!string.IsNullOrEmpty(nombreRecibido))
            nombres[request.ClientNetworkId] = nombreRecibido;

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
    // HOST
    // ---------------------------------------------------------------
    public async Task<string> IniciarHost(string nombre)
    {
        NombreLocal = LimpiarNombre(nombre);
        nombres.Clear();

        ValidarPuntos();
        BarajarPuntosDeSpawn();
        siguienteSpawn = 0;
        JoinCodeActual = "";

        try
        {
            if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress)
                await EsperarCierreDeRed();

            await InicializarServicios();

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(7);

            JoinCodeActual = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[Lobby] JOIN CODE: {JoinCodeActual}");

            var transporte = ObtenerTransporte();
            if (transporte == null) return null;

            var relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            transporte.SetRelayServerData(relayServerData);

            // El nombre viaja en los datos de conexión
            NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(NombreLocal);

            if (NetworkManager.Singleton.StartHost())
            {
                nombres[NetworkManager.ServerClientId] = NombreLocal;

                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= AlTerminarDeCargarEscena;
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += AlTerminarDeCargarEscena;

                if (lobbyRoomStatePrefab != null)
                {
                    var sala = Instantiate(lobbyRoomStatePrefab);
                    sala.GetComponent<NetworkObject>().Spawn();
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

    // Lo llama el botón INICIAR PARTIDA (solo el host)
    public void IniciarPartida()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        nm.SceneManager.LoadScene(escenaDeJuego, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    // ---------------------------------------------------------------
    // CLIENTE
    // ---------------------------------------------------------------
    public async Task<bool> IniciarCliente(string joinCode, string nombre)
    {
        NombreLocal = LimpiarNombre(nombre);
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

            // El nombre viaja en los datos de conexión
            NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(NombreLocal);

            if (NetworkManager.Singleton.StartClient())
            {
                JoinCodeActual = joinCode;
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= AlTerminarDeCargarEscena;
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += AlTerminarDeCargarEscena;

                // Si el cliente ya tiene cargada una escena (Menu, ArenaFondo), no la recarga.
                NetworkManager.Singleton.SceneManager.VerifySceneBeforeLoading = (indice, nombreEscena, modo) =>
                {
                    var escena = UnityEngine.SceneManagement.SceneManager.GetSceneByName(nombreEscena);
                    if (escena.isLoaded && nombreEscena != escenaDeJuego) return false;
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