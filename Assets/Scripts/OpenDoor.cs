using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class OpenDoor : NetworkBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private string playerTag = "Player";

    private static readonly int CharacterNearby = Animator.StringToHash("character_nearby");

    // Solo el servidor escribe; todos leen
    private readonly NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private bool localPlayerInRange;

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

    private void Update()
    {
        if (!localPlayerInRange) return;

        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            ToggleServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleServerRpc()
    {
        isOpen.Value = !isOpen.Value;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsLocalPlayerDoor(other))
            localPlayerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsLocalPlayerDoor(other))
            localPlayerInRange = false;
    }

    // Solo cuenta MI jugador, no los remotos
    private bool IsLocalPlayerDoor(Collider other)
    {
        if (!other.CompareTag(playerTag)) return false;
        var netObj = other.GetComponentInParent<NetworkObject>();
        return netObj != null && netObj.IsOwner;
    }
}