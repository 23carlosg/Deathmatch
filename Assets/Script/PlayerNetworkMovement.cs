using UnityEngine;
using Unity.Netcode;
public class PlayerNetworkMovement : NetworkBehaviour
{
    [SerializeField] private float speed = 5f;
    public override void OnNetworkSpawn()
    {
    if (IsOwner)
        {
            Debug.Log("¡Soy el dueño de esta instancia de jugador!");
        }
    }

    void Update()
    {
        // Prevenir que un cliente controle el avatar de otro jugador remoto
        if (!IsOwner) return;
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 dir = new Vector3(h, 0, v);
        transform.Translate(dir * speed * Time.deltaTime);
    }
}