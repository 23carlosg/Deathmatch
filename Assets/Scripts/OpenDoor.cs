using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class OpenDoor : NetworkBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private string[] validTags = { "Player", "Wheelbot" };

    private static readonly int CharacterNearby = Animator.StringToHash("character_nearby");

    private readonly NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Solo servidor: colliders actualmente dentro del trigger
    private readonly HashSet<Collider> occupants = new HashSet<Collider>();
    private readonly List<Collider> toRemove = new List<Collider>();

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += OnDoorStateChanged;
        animator.SetBool(CharacterNearby, isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged -= OnDoorStateChanged;
        occupants.Clear();
    }

    private void OnDoorStateChanged(bool previous, bool current)
    {
        animator.SetBool(CharacterNearby, current);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || !IsValidCharacter(other)) return;
        occupants.Add(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;
        occupants.Remove(other);
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // Limpiar colliders destruidos / desactivados / de personajes muertos
        toRemove.Clear();
        foreach (var col in occupants)
        {
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                toRemove.Add(col);
        }
        foreach (var col in toRemove)
            occupants.Remove(col);

        bool shouldBeOpen = occupants.Count > 0;
        if (isOpen.Value != shouldBeOpen)
            isOpen.Value = shouldBeOpen;
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