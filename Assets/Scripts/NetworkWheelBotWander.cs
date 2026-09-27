using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

/// <summary>
/// Bot rueda que deambula por el NavMesh (wander) con autoridad del SERVIDOR.
///
/// Rotación: el modelo FBX (de Blender) se importa con el mesh "acostado": queda
/// derecho gracias a una inclinación de -90° en X en la raiz del modelo. Como el
/// NavMeshAgent (updateRotation) reescribe la rotación de SU transform con yaw puro
/// en cada frame, esa inclinación no puede vivir en el mismo objeto que el agent
/// (se pisa al entrar en Play y el bot queda mirando al piso).
/// Por eso el prefab tiene un WRAPPER: el objeto raiz (identidad) lleva TODOS los
/// componentes (NavMeshAgent, red, física) y el modelo FBX cuelga como hijo con su
/// -90°X intacto. El agent gira el wrapper; el modelo de abajo nunca se entera.
///
/// Red: el servidor elige los destinos; NetworkTransform replica posición y rotación
/// de la raiz a los clientes. La rapidez del bot viaja en una NetworkVariable para que
/// la rueda gire también en las máquinas remotas (los hijos no se sincronizan).
/// El Rigidbody va en kinemático para que la física no pelee con el NavMeshAgent.
///
/// Apoyo: al spawnear se miden los bounds reales del modelo y se corre el hijo para
/// que la parte mas baja (la rueda) toque exactamente el plano donde el agent camina
/// (baseOffset = 0). Así no importa la escala ni el pivote del FBX: nunca flota ni
/// se hunde.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Rigidbody))]
public class NetworkWheelBotWander : NetworkBehaviour
{
    [Header("Referencias")]
    [Tooltip("Arrastra aca la rueda para animarla")]
    [SerializeField] private Transform wheelTransform;

    [Header("Configuración de Movimiento")]
    [SerializeField] private float wanderRadius = 15f;
    [SerializeField] private float waitTime = 3f;
    [SerializeField] private float movementSpeed = 3.5f;

    [Header("Configuración de la Rueda")]
    [Tooltip("Cuánto gira la rueda visualmente por cada metro recorrido")]
    [SerializeField] private float wheelRotationMultiplier = 50f;

    [Header("Anti-traba")]
    [Tooltip("Cada cuántos segundos se revisa si el bot avanza")]
    [SerializeField] private float stuckCheckTime = 1.5f;
    [Tooltip("Si en una ventana avanzó menos que esto teniendo camino, está trabado")]
    [SerializeField] private float stuckMinDistance = 0.2f;

    private NavMeshAgent agent;
    private Rigidbody rb;
    private float timer;
    private Vector3 ultimaPosicion;
    private float ultimoChequeoTraba;

    readonly NetworkVariable<float> redVelocidad = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;

        agent.updatePosition = true;
        agent.updateRotation = true;
        agent.speed = movementSpeed;

        timer = waitTime;
        ultimaPosicion = transform.position;
        ultimoChequeoTraba = Time.time;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            agent.enabled = false;
        }

        ApoyarModeloAlPiso();
    }

    void Start()
    {
        ApoyarModeloAlPiso();
    }

    void ApoyarModeloAlPiso()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        float fondo = bounds.min.y - transform.position.y;
        float correccion = -fondo;

        if (Mathf.Abs(correccion) > 0.001f)
        {
            foreach (Transform hijo in transform)
            {
                hijo.localPosition += Vector3.up * correccion;
            }
        }

        SphereCollider col = GetComponent<SphereCollider>();
        if (col != null)
        {
            float escala = transform.lossyScale.y != 0f ? transform.lossyScale.y : 1f;
            col.radius = (bounds.size.y * 0.5f) / escala;
            col.center = new Vector3(0f, (bounds.center.y - transform.position.y) / escala, 0f);
        }
    }

    void Update()
    {
        if (IsServer)
        {
            redVelocidad.Value = agent.velocity.magnitude;

            timer += Time.deltaTime;
            bool llego = !agent.hasPath || agent.remainingDistance <= agent.stoppingDistance;
            if (timer >= waitTime && llego)
            {
                agent.SetDestination(ElegirDestinoDespejado());
                timer = 0f;
            }

            RevisarSiEstaTrabado();
        }

        if (wheelTransform != null)
        {
            float velocidad = redVelocidad.Value;
            if (velocidad > 0.1f)
            {
                wheelTransform.Rotate(Vector3.right * (velocidad * wheelRotationMultiplier * Time.deltaTime), Space.Self);
            }
        }
    }

    void RevisarSiEstaTrabado()
    {
        if (Time.time - ultimoChequeoTraba < stuckCheckTime) return;
        ultimoChequeoTraba = Time.time;

        if (agent.isOnNavMesh && agent.hasPath
            && Physics.Raycast(transform.position + Vector3.up * 0.3f, transform.forward, 0.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            agent.SetDestination(ElegirDestinoDespejado());
            timer = 0f;
            ultimaPosicion = transform.position;
            return;
        }

        float avanzado = Vector3.Distance(transform.position, ultimaPosicion);
        ultimaPosicion = transform.position;

        bool queriendoMoverse = agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 0.1f;
        if (!queriendoMoverse || avanzado >= stuckMinDistance) return;

        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
            return;
        }

        agent.velocity = Vector3.zero;
        agent.SetDestination(ElegirDestinoDespejado());
        timer = 0f;
    }

    private Vector3 ElegirDestinoDespejado()
    {
        Vector3 elegido = transform.position;
        for (int i = 0; i < 6; i++)
        {
            Vector3 candidato = RandomNavMeshLocation(wanderRadius);
            if (candidato == transform.position) continue;
            elegido = candidato;

            if (!ZonaDespejada(candidato)) continue;

            NavMeshPath camino = new NavMeshPath();
            if (agent.CalculatePath(candidato, camino) && camino.status == NavMeshPathStatus.PathComplete)
            {
                break;
            }
        }
        return elegido;
    }
    private bool ZonaDespejada(Vector3 punto)
    {
        Vector3 origen = punto + Vector3.up * 0.3f;
        foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
        {
            if (Physics.Raycast(origen, dir, 0.6f, ~0, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
        }
        return true;
    }

    private Vector3 RandomNavMeshLocation(float radius)
    {
        Vector3 randomDirection = Random.insideUnitSphere * radius + transform.position;

        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, radius, NavMesh.AllAreas))
        {
            return hit.position;
        }
        return transform.position;
    }
}
