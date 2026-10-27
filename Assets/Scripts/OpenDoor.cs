using UnityEngine;
using UnityEngine.InputSystem; 

public class OpenDoor : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private string playerTag = "Player";

    private static readonly int CharacterNearby = Animator.StringToHash("character_nearby");

    private bool playerInRange;
    private bool isOpen;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (!playerInRange) return;

        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            Toggle();
    }

    private void Toggle()
    {
        isOpen = !isOpen;
        animator.SetBool(CharacterNearby, isOpen);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag))
            playerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        playerInRange = false;
    }
}