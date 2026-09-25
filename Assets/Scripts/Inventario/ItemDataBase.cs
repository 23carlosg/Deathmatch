using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "Nuevo DB", menuName = "Inventario/Base de Datos")]
public class ItemDataBase : ScriptableObject
{
    public List<ItemData> items = new List<ItemData>();

    private static ItemDataBase _instance;
    public static ItemDataBase Instance
    {
        get
        {
            if (_instance == null)
            {
                // Busca el asset dentro de una carpeta llamada "Resources"
                _instance = Resources.Load<ItemDataBase>("ItemDataBase");

                if (_instance == null)
                    Debug.LogError("No se encontró ItemDataBase en una carpeta Resources.");
            }
            return _instance;
        }
    }

    // Llamado desde GameManager al iniciar el juego
    public void InitializeDatabase()
    {
        Debug.Log("Base de datos de items inicializada con " + items.Count + " items");
    }

    public int GetId(ItemData itemData)
    {
        return items.IndexOf(itemData);
    }

    public ItemData GetItemById(int id)
    {
        if (id < 0 || id >= items.Count) return null;
        return items[id];
    }
}
