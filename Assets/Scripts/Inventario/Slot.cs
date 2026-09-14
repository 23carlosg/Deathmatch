using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class Slot : MonoBehaviour
{
    [HideInInspector] public ItemData itemData;
    [HideInInspector] public int cantidad;

    public Image icono;
    private TextMeshProUGUI cantidadText;

    void Start()
    {
        cantidadText = GetComponentInChildren<TextMeshProUGUI>();
    }

    public void SetItem(ItemData itemData, int cantidad)
    {
        this.itemData = itemData;
        this.cantidad = cantidad;
           
        icono.sprite = itemData.icono;
        cantidadText.text = cantidad.ToString();
    }      

    public void ClearItem()
    {
        itemData = null;
        cantidad = 0;
        icono.sprite = null;
        cantidadText.text = "";
    } 
}