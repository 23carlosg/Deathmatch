using Unity.Netcode;
using UnityEngine;

public class OpenDoor : NetworkBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private string[] validTags = { "Player", "Wheelbot"};

    private static readonly int CharacterNearby = Animator.StringToHash("character_nearby");

    // Solo el servidor escribe; todos leen
    private readonly NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Cantidad de personajes dentro del trigger (solo se usa en el servidor)
    private int occupants;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += OnDoorStateChanged;
        animator.SetBool(CharacterNearby, isOpen.Value); // estado actual (late join)
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged -= OnDoorStateChanged;
    }

    private void OnDoorStateChanged(bool previous, bool current)
    {
        animator.SetBool(CharacterNearby, current);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || !IsValidCharacter(other)) return;

        occupants++;
        isOpen.Value = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer || !IsValidCharacter(other)) return;

        occupants = Mathf.Max(0, occupants - 1);
        if (occupants == 0)
            isOpen.Value = false;
    }

    private bool IsValidCharacter(Collider other)
    {
        foreach (var tag in validTags)
        {
            if (other.CompareTag(tag))
                return true;
        }
        return false;
    }
}