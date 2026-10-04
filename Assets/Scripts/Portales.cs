using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Portales : MonoBehaviour
{
    [Header("Configuración")]
    public string playerTag = "Player";
    public float teleportCooldown = 0.5f; // evita el bucle de teletransporte
    public Vector3 exitOffset = new Vector3(0, 1f, 0); // offset para que el jugador no quede dentro del portal al teletransportarse

    // Lista compartida para todos los portales
    private static List<Portales> allPortals = new List<Portales>();

    private bool onCooldown = false;

    private void OnEnable()
    {
        if (!allPortals.Contains(this))
            allPortals.Add(this);

        AvisarSiElColliderNoSirve();
    }

    private void AvisarSiElColliderNoSirve()
    {
        foreach (Collider c in GetComponents<Collider>())
        {
            if (c.isTrigger) return;
        }
    }

    private void OnDisable()
    {
        allPortals.Remove(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (onCooldown) return;
        if (!other.CompareTag(playerTag)) return;

        var cuerpoEnRed = other.GetComponentInParent<Unity.Netcode.NetworkObject>();
        if (cuerpoEnRed != null && !cuerpoEnRed.IsOwner) return;

        // Si el jugador está muerto, no se teletransporta
        var jugador = other.GetComponentInParent<PlayerNetworkMovement>();
        if (jugador != null && jugador.Muerto) return;

        // Teletransporte random en los portales
        List<Portales> otros = allPortals.FindAll(p => p != this);

        if (otros.Count == 0)
        {
            Debug.LogWarning("No hay otros portales a los cuales teletransportar.");
            return;
        }

        Portales destino = otros[Random.Range(0, otros.Count)];

        // Deshabilitar temporalmente el CharacterController para evitar que se quede trabado en el portal
        CharacterController cc = other.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        other.transform.position = destino.transform.position + destino.exitOffset;

        if (cc != null) cc.enabled = true;

        // Cooldown en el portal de destino para que no rebote
        destino.StartCoroutine(destino.ActivarCooldown());
    }

    private IEnumerator ActivarCooldown()
    {
        onCooldown = true;
        yield return new WaitForSeconds(teleportCooldown);
        onCooldown = false;
    }
}