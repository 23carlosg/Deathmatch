using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "Nuevo DB", menuName = "Inventario/Base de Datos")]
public class ItemDataBase : ScriptableObject
{
    // Lista de items en la base de datos
    public List<ItemData> items = new List<ItemData>();
    // Diccionario para acceder a los items por nombre (Oculto)
    private Dictionary<string, ItemData> itemDictionary = new Dictionary<string, ItemData>();
    
    public void InitializeDatabase()
    {
        itemDictionary.Clear();
        foreach (ItemData item in items)
        {
            if(string.IsNullOrEmpty(item.id.ToString()))
            {
                continue;
            }

            if(!itemDictionary.ContainsKey(item.id.ToString()))
            {
            itemDictionary.Add(item.id.ToString(), item);
            }
            
        }
        Debug.Log("Base de datos inicializada con " + itemDictionary.Count + " items.");
    }
    
    public ItemData BuscarItem(string id)
    {
       //Sistema de seguridad: si el diccionario esta vacio, lo arranca
       if (itemDictionary.Count == 0 && items.Count > 0)
       {
           InitializeDatabase();
       }

       if(itemDictionary.TryGetValue(id.ToString(), out ItemData itemData))
       {
           return itemData;
       }
       else
       {
           return null;
       }
    }
}
