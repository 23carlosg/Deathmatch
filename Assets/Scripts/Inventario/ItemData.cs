using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(fileName = "Nuevo Item", menuName = "Inventario/Item")]
public class ItemData : ScriptableObject
{
    public int id = 0;
    public string nombre = "";
    public Sprite icono;
    public int maxstock = 1;

    public enum TipoArma { Ninguna, Rifle, Pistola }
    public TipoArma tipoArma = TipoArma.Ninguna;
}