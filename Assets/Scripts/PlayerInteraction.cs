using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    private Camera camara;

    void Start()
    {
        camara = Camera.main;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
           if (Physics.Raycast(camara.transform.position, camara.transform.forward, out RaycastHit hit, 5))
            {
                ObjetoInteractuable objetoInteractuable = hit.collider.GetComponent<ObjetoInteractuable>();
                if (objetoInteractuable != null)
                {
                    objetoInteractuable.Interactuar();
                }
            }
        }
    }
}