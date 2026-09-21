using Unity.Netcode.Components;

namespace Unity.Netcode
{
    /// <summary>
    /// NetworkTransform con autoridad del DUEÑO: cada jugador replica su propio
    /// transform y el servidor lo acepta tal cual. Es lo correcto para un jugador
    /// cuyo movimiento lo calcula su propia máquina (estilo cliente autoritativo).
    /// Reemplaza al NetworkTransform estándar (autoridad del servidor) en el prefab Player:
    /// con el estándar, el servidor sobrescribía la posición que el dueño calculaba.
    /// </summary>
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
