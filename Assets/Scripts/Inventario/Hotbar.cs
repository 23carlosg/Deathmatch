using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Hotbar : MonoBehaviour
{
    public Transform hotBarSlotContainer;

    [Header("Debug")]
    public ItemData itemDev; // Item de prueba para testear con F1

    private List<Slot> slots = new List<Slot>();

    void Start()
    {
        // Busca todos los Slot (incluyendo inactivos) dentro de hotBarSlotContainer
        slots.AddRange(hotBarSlotContainer.GetComponentsInChildren<Slot>(true));

        // Le asigna a cada slot su número según el orden en la jerarquía (empezando en 1)
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].SetSlotNumber(i + 1);
        }

        Debug.Log("Hotbar inicializada con " + slots.Count + " slots");
    }

    void Update()
    {
        // Tecla de prueba: agrega el item de debug al primer slot vacío
        if (Keyboard.current.f1Key.wasPressedThisFrame)
        {
            AddItem(itemDev);
        }
    }

    public int AddItem(ItemData itemData)
    {
        // Busca un slot vacío
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].itemData == null)
            {
                slots[i].SetItem(itemData);
                return 0; // El item se añadió correctamente
            }
        }

        // No hay slots vacíos
        return -1;
    }
}
