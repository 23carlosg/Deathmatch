using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerDetection : MonoBehaviour
{
    private Camera playerCamera;

    void Start()
    {
        playerCamera = Camera.main;
    }
    
    void Update()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;

            if (playerCamera == null)
            {
                Debug.Log("No se encontró ninguna cámara con tag MainCamera");
                return;
            }
        }

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            Debug.Log("Se presionó E");
            
            if(Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, 5))
            {
                Debug.Log("Golpeó: " + hit.collider.gameObject.name);

                Interaccion interaccion = hit.collider.GetComponent<Interaccion>();

                if (interaccion != null)
                {
                    interaccion.Interactuar();
                }
                else
                {
                    Debug.Log("El objeto no tiene componente Interaccion");
                }
            }
            else
            {
                Debug.Log("El raycast no golpeó nada");
            }
        }
    }
}