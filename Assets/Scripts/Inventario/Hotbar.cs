using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Hotbar : MonoBehaviour
{
    public Transform hotBarSlotContainer;

    [Header("Debug")]
    public ItemData itemDev;
    public ItemData itemDev2;

    private List<Slot> slots = new List<Slot>();

    void Start()
    {
        slots.AddRange(hotBarSlotContainer.GetComponentsInChildren<Slot>(true));

        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].SetSlotNumber(i + 1);
        }

        Debug.Log("Hotbar inicializada con " + slots.Count + " slots");
    }

    void Update()
    {
        if (Keyboard.current.f1Key.wasPressedThisFrame)
        {
            AddItem(itemDev);
        }

        if (Keyboard.current.f2Key.wasPressedThisFrame)
        {
            AddItem(itemDev2);
        }
    }

    public int AddItem(ItemData itemData)
    {
        int slotFijo = GetSlotFijo(itemData);

        // Si el item tiene un slot fijo asignado (pistola/rifle), va directo ahí
        if (slotFijo >= 0 && slotFijo < slots.Count)
        {
            slots[slotFijo].SetItem(itemData);
            return 0;
        }

        // Si no tiene slot fijo, busca el primer slot vacío (comportamiento anterior)
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].itemData == null)
            {
                slots[i].SetItem(itemData);
                return 0;
            }
        }

        return -1; // No hay espacio
    }

    // Define en qué slot va cada tipo de arma. -1 significa "sin slot fijo".
    int GetSlotFijo(ItemData itemData)
    {
        if (itemData.tipoArma == ItemData.TipoArma.Pistola) return 0; // Slot 1
        if (itemData.tipoArma == ItemData.TipoArma.Rifle) return 1;   // Slot 2
        return -1;
    }
}