using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }
    
    public GameObject playerInventoryUI;
    void Awake()
    {
        if (Instance == null) {Instance = this;}
        else {Destroy(gameObject);}
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            playerInventoryUI.SetActive(!playerInventoryUI.activeSelf);
        }
    }
}
