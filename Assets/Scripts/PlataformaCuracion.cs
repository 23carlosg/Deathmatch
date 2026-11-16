using Unity.Netcode;
using UnityEngine;

public class PlataformaCuracion : NetworkBehaviour
{
    [Header("Movimiento")]
    public float alturaMaxima = 29.9f;
    public float velocidad = 3f;

    [Header("Detección del jugador")]
    public string tagJugador = "Player";
    public Vector3 tamanoZona = new Vector3(3.6f, 1.5f, 3.6f);
    public float alturaZona = 1.8f;

    [Header("Curación")]
    public float curacionPorSegundo = 10f;

    // El servidor escribe, todos leen
    private NetworkVariable<float> alturaActual = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float alturaInicial;
    private readonly Collider[] resultados = new Collider[16];

    void Awake()
    {
        alturaInicial = transform.position.y;
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"Plataforma spawneada. IsServer={IsServer}");
        if (IsServer) alturaActual.Value = alturaInicial;
    }

    void Update()
    {
        if (!IsSpawned) return;

        if (IsServer)
        {
            int cantidad = ContarJugadores();
            bool hayJugador = cantidad > 0;

            float destino = hayJugador ? alturaMaxima : alturaInicial;
            alturaActual.Value = Mathf.MoveTowards(alturaActual.Value, destino,
                                                   velocidad * Time.deltaTime);

            bool llegoArriba = Mathf.Approximately(alturaActual.Value, alturaMaxima);
            if (hayJugador && llegoArriba)
            {
                for (int i = 0; i < cantidad; i++)
                    Curar(resultados[i], curacionPorSegundo * Time.deltaTime);
            }
        }

        // Servidor y clientes aplican la misma altura
        Vector3 pos = transform.position;
        pos.y = alturaActual.Value;
        transform.position = pos;
    }

    int ContarJugadores()
    {
        // Usa la altura sincronizada para que la zona siga a la plataforma
        Vector3 centro = new Vector3(transform.position.x,
                                     alturaActual.Value + alturaZona,
                                     transform.position.z);
        int total = Physics.OverlapBoxNonAlloc(centro, tamanoZona * 0.5f, resultados,
                                               Quaternion.identity, ~0,
                                               QueryTriggerInteraction.Ignore);
        int jugadores = 0;
        for (int i = 0; i < total; i++)
        {
            if (resultados[i].CompareTag(tagJugador))
                resultados[jugadores++] = resultados[i];
        }
        return jugadores;
    }

    void Curar(Collider jugador, float cantidad)
    {
        var vida = jugador.GetComponentInParent<PlayerHealth>();
        if (vida != null) vida.Curar(cantidad);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position + Vector3.up * alturaZona, tamanoZona);
    }
}