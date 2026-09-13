using Unity.VisualScripting.Antlr3.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Windows;

public class Player : MonoBehaviour

{
    [SerializeField] private float velocidad = 5f; //velocidad a la que se mueve el jugador

    void Update()

    {
        float horizontal = 0f; //guardda la direccion del movimiento
        float vertical = 0f;

        if (Keyboard.current != null) //verifica si se detecta el teclado
        {
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
                horizontal = -1f; //si presiona A se movera a la izquierda
            else if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
                horizontal = 1f; //si presiona D se movera a la derecha

            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
                vertical = -1f; //si presiona S se movera hacia atras
            else if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
                vertical = 1f; //si presiona W se movera hacia adelante
        }

        // Mover el personaje
        Vector3 direccion = new Vector3(horizontal, 0f, vertical);
        transform.Translate(direccion * velocidad * Time.deltaTime, Space.World); //Mueve el personaje en la direccion calculada 
    }
}