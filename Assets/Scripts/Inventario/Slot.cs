using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class Slot : MonoBehaviour
{
    [HideInInspector] public ItemData itemData;

    [Header("UI")]
    public Image icono;
    public TextMeshProUGUI numberText; // Número del slot: 1, 2, 3... (se asigna desde Hotbar)

    public void SetItem(ItemData itemData)
    {
        this.itemData = itemData;
        icono.sprite = itemData.icono;
    }

    public void ClearItem()
    {
        itemData = null;
        icono.sprite = null;
    }

    // Asigna el número que se muestra abajo a la izquierda del slot (llamado desde Hotbar)
    public void SetSlotNumber(int number)
    {
        if (numberText != null)
        {
            numberText.text = number.ToString();
        }
    }
}