using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public ItemDataBase itemDatabase;

    void Awake()
    {
       if(Instance == null) {Instance = this;}
        else {Destroy(gameObject);}

        //inicizilamos la db
        itemDatabase.InitializeDatabase(); 
    }
}