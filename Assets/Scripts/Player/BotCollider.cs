using UnityEngine;

/// <summary>
/// Evita que el jugador atraviese a los wheelbots.
///
/// El CharacterController NO colisiona con colliders que se mueven por transform (los bots van
/// cinematicos por NavMeshAgent, y el NavMeshObstacle solo hace que ELLOS te esquiven a vos).
/// Sin esto, caminando hacia un bot lo atravesas: una vez solapados, el CC nunca mas detecta la
/// "pared". LateUpdate corrige la penetracion empujando la capsula fuera de la esfera del bot;
/// controller.Move respeta paredes, asi que el empuje nunca te mete en la geometria del mapa.
/// </summary>
public class BotSeparation : MonoBehaviour
{
    [Header("Colisión con bots")]
    [Tooltip("Radio máximo del cuerpo de un wheelbot (para no atravesarlo)")]
    public float radioCuerpoBot = 0.55f;

    PlayerNetworkMovement jugador;

    void Awake()
    {
        jugador = GetComponent<PlayerNetworkMovement>();
    }

    void LateUpdate()
    {
        if (jugador == null) return;

        CharacterController controller = jugador.Controller;
        if (!jugador.IsLocalPlayer || jugador.Muerto || controller == null || !controller.enabled) return;

        Vector3 centroMio = transform.position + controller.center;
        float radioBusqueda = controller.radius + radioCuerpoBot;
        Collider[] cercanos = Physics.OverlapSphere(centroMio, radioBusqueda, ~0, QueryTriggerInteraction.Ignore);

        foreach (Collider c in cercanos)
        {
            if (!(c is SphereCollider)) continue;
            if (c.GetComponentInParent<NetworkWheelBotWander>() == null) continue;

            Vector3 centroBot = c.transform.TransformPoint(((SphereCollider)c).center);
            float escalaBot = Mathf.Max(
                Mathf.Abs(c.transform.lossyScale.x),
                Mathf.Abs(c.transform.lossyScale.y),
                Mathf.Abs(c.transform.lossyScale.z));
            float radioBot = ((SphereCollider)c).radius * escalaBot;

            Vector3 desdeBot = centroMio - centroBot;
            desdeBot.y = 0f;
            float dist = desdeBot.magnitude;
            float minima = radioBot + controller.radius - controller.skinWidth;

            if (dist < minima && dist > 0.0001f)
            {
                controller.Move(desdeBot / dist * (minima - dist));
            }
        }
    }
}