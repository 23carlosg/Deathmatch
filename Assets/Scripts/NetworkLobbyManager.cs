using UnityEngine;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using System.Threading.Tasks;

public class NetworkLobbyManager : MonoBehaviour
{
    [SerializeField] private string escenaDeJuego = "Arena";

    [Header("Puntos de spawn (coordenadas del mapa)")]
    [SerializeField]
    private Vector3[] puntosDeSpawn = new Vector3[]
    {
        new Vector3(-134.085f, 1f, 106.480f), //Coordenadas de los puntos de spawn en el mapa
        new Vector3(-134.085f, 1f, 140f),
        new Vector3(-134.085f, 1f, 174.480f),
        new Vector3(-58.085f, 1f, 106.480f),
        new Vector3(-58.085f, 1f, 140f),
        new Vector3(-58.085f, 1f, 174.480f),
        new Vector3(-100f, 1f, 102.480f),
        new Vector3(-100f, 1f, 178.480f),
    };

    [Header("Validacion (bounds del piso del mapa)")]
    [SerializeField] private Vector3 minPiso = new Vector3(-136f, -1f, 100f); // Coordenadas del piso del mapa
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
            {
                nmDuplicado = FindAnyObjectByType<NetworkManager>();
            }
            // Si hay un NetworkManager duplicado, lo destruye para evitar conflictos
            if (nmDuplicado != null && nmDuplicado != NetworkManager.Singleton)
                Destroy(nmDuplicado.gameObject);
            Destroy(gameObject);
        }
    }

    void Start()
    {
        if (instancia != this) return;

        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true; // Habilita la aprobación de conexión para que el host pueda asignar spawn a cada jugador
        NetworkManager.Singleton.ConnectionApprovalCallback += AprobarConexion; // Callback que se llama cuando un cliente intenta conectarse al host
        BarajarPuntosDeSpawn(); // Baraja los puntos de spawn para que los jugadores no aparezcan siempre en el mismo orden
    }

    void OnDestroy()
    {
        if (instancia != this) return;
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= AprobarConexion; // Quita el callback de aprobación de conexión
            if (NetworkManager.Singleton.SceneManager != null) 
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= AlTerminarDeCargarEscena; // Quita el callback de carga de escena
        }
    }

    void AlTerminarDeCargarEscena(string nombreEscena, UnityEngine.SceneManagement.LoadSceneMode modo,
        System.Collections.Generic.List<ulong> clientesCompletados, System.Collections.Generic.List<ulong> clientesConTimeout)
    {
        if (nombreEscena == escenaDeJuego) MapaListo = true; // Cuando la escena de juego termina de cargar, se habilita el movimiento de los jugadores
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
        Vector3 pos = PuntoCentralDelPiso();

        if (ordenSpawnBarajado != null && ordenSpawnBarajado.Length > 0) 
            pos = ordenSpawnBarajado[siguienteSpawn % ordenSpawnBarajado.Length]; 

        siguienteSpawn++;

        response.Approved = true; 
        response.CreatePlayerObject = true; 
        response.Position = pos; 
        response.Rotation = Quaternion.identity; 
        response.Pending = false; 
    }

    Vector3 PuntoCentralDelPiso() 
    {
        return new Vector3((minPiso.x + maxPiso.x) * 0.5f, 1f, (minPiso.z + maxPiso.z) * 0.5f); 
    }

    // --------------------------------------------------------------------------------------------------------------------------//
    // Muestra los bounds del piso del mapa y los puntos de spawn en la escena, para que se puedan ver y corregir en el Inspector
    // --------------------------------------------------------------------------------------------------------------------------//
    /* void OnDrawGizmos()
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
    */
    bool DentroDelPiso(Vector3 p) // Comprueba si un punto está dentro de los bounds del piso del mapa, para validar los puntos de spawn
    {
        return p.x >= minPiso.x && p.x <= maxPiso.x && p.z >= minPiso.z && p.z <= maxPiso.z; // Comprueba si el punto está dentro de los bounds del piso del mapa, para validar los puntos de spawn
    }

    void ValidarPuntos() // Valida los puntos de spawn, eliminando los que están fuera de los bounds del piso del mapa y dejando solo los válidos
    {
        if (puntosDeSpawn == null || puntosDeSpawn.Length == 0) return; // Si no hay puntos de spawn, no hace nada

        int validos = 0;
        for (int i = 0; i < puntosDeSpawn.Length; i++) // Recorre todos los puntos de spawn y comprueba si están dentro de los bounds del piso del mapa
        {
            if (DentroDelPiso(puntosDeSpawn[i])) // Si el punto de spawn está dentro de los bounds del piso del mapa, lo deja en el array de puntos de spawn válidos
            {
                if (validos != i) puntosDeSpawn[validos] = puntosDeSpawn[i]; // Si el punto de spawn válido no está en la posición correcta del array, lo mueve a la posición correcta
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
            puntosDeSpawn = new Vector3[] { PuntoCentralDelPiso() }; // Si no hay puntos de spawn válidos, se usa el centro del piso como punto de spawn por defecto
        }
        else if (validos < puntosDeSpawn.Length)
        {
            System.Array.Resize(ref puntosDeSpawn, validos);
        }
    }

    // ---------------------------------------------------------------
    // HOST: crea asignacion en Relay y arranca el host
    // ---------------------------------------------------------------
    public async Task<string> IniciarHost() 
    {
        ValidarPuntos();
        BarajarPuntosDeSpawn();
        JoinCodeActual = "";

        try
        {
            await InicializarServicios();

            // Crear asignacion: 7 clientes + 1 host = 8 jugadores max
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(7);

            // Obtener Join Code que compartis con los demas jugadores
            JoinCodeActual = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[Lobby] JOIN CODE: {JoinCodeActual}");

            // Configurar UnityTransport con los datos de Relay
            var transporte = ObtenerTransporte();
            if (transporte == null) return null;

            var relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls"); 
            transporte.SetRelayServerData(relayServerData); 

            // Arrancar host NGO
            NetworkManager.Singleton.OnServerStarted += CargarEscenaDeJuego;
            if (NetworkManager.Singleton.StartHost())
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += AlTerminarDeCargarEscena;
                return JoinCodeActual;
            }
            else
            {
                NetworkManager.Singleton.OnServerStarted -= CargarEscenaDeJuego;
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

    // ---------------------------------------------------------------
    // CLIENTE: se conecta con el Join Code
    // ---------------------------------------------------------------
    public async Task<bool> IniciarCliente(string joinCode) 
    {
        JoinCodeActual = "";
        ValidarPuntos();

        try
        {
            // Si quedó una conexión previa a medias, se limpia
            if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress)
            {
                if (!NetworkManager.Singleton.ShutdownInProgress) NetworkManager.Singleton.Shutdown();

                float espera = 0f;
                while ((NetworkManager.Singleton.ShutdownInProgress || NetworkManager.Singleton.IsListening) && espera < 3f)
                {
                    await Task.Yield();
                    espera += Time.unscaledDeltaTime;
                }
            }

            await InicializarServicios();

            var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var transporte = ObtenerTransporte();
            if (transporte == null) return false;

            var relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            transporte.SetRelayServerData(relayServerData);

            if (NetworkManager.Singleton.StartClient())
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= AlTerminarDeCargarEscena;
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += AlTerminarDeCargarEscena;
                return true;
            }

            Debug.LogError("[Lobby] ERROR: no se pudo conectar");
            return false;
        }
        catch (RelayServiceException e)
        {
            // Código inexistente, vencido o mal escrito
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

    // Inicializa UGS y autentica de forma anonima (necesario antes de Relay)
    async Task InicializarServicios()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    Unity.Netcode.Transports.UTP.UnityTransport ObtenerTransporte() // Devuelve el componente UnityTransport del NetworkManager, o lo busca en la escena si no está en el mismo GameObject
    {
        var transporte = GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transporte == null)
            transporte = FindAnyObjectByType<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transporte == null)
            Debug.LogError("[Lobby] ERROR: no hay UnityTransport en la escena");
        return transporte;
    }

    void CargarEscenaDeJuego()
    {
        NetworkManager.Singleton.OnServerStarted -= CargarEscenaDeJuego;
        NetworkManager.Singleton.SceneManager.LoadScene(escenaDeJuego, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
    public static Vector3 ObtenerPuntoDeRespawn()
    {
        if (instancia == null || instancia.ordenSpawnBarajado == null || instancia.ordenSpawnBarajado.Length == 0)
            return instancia != null ? instancia.PuntoCentralDelPiso() : Vector3.zero;

        int indice = Random.Range(0, instancia.ordenSpawnBarajado.Length);
        return instancia.ordenSpawnBarajado[indice];
    }



}

