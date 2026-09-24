using UnityEngine;

public class AgarrarItem : Interaccion
{   
    public ItemData itemData; // Referencia al ScriptableObject del item que se va a agarrar
    public int cantidad = 1; // Cantidad de items que se van a agarrar

    public override void Interactuar()
    {
        base.Interactuar();

        // Busca la Hotbar del jugador y le agrega el item
        Hotbar hotbar = FindAnyObjectByType<Hotbar>();

        if (hotbar != null)
        {
            int resultado = hotbar.AddItem(itemData);

            if (resultado == 0)
            {
                // Se agregó correctamente, así que el item desaparece del mundo
                Destroy(gameObject);
            }
            else
            {
                Debug.Log("No hay espacio en la hotbar");
            }
        }
    }
}