using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Portales : MonoBehaviour
{
    [Header("Configuración")]
    public string playerTag = "Player";
    public float teleportCooldown = 0.5f; // evita el bucle de teletransporte
    public Vector3 exitOffset = new Vector3(0, 1f, 0); // ajustá el personaje

    // Lista compartida para todos los portales
    private static List<Portales> allPortals = new List<Portales>();

    private bool onCooldown = false;

    private void OnEnable()
    {
        if (!allPortals.Contains(this))
            allPortals.Add(this);

        AvisarSiElColliderNoSirve();
    }

    // El OnTriggerEnter de abajo es 3D: si le ponen un collider 2D (CircleCollider2D,
    // BoxCollider2D...) o un collider 3D sin IsTrigger, el portal muere en silencio.
    // Este aviso lo detecta apenas entra a escena.
    private void AvisarSiElColliderNoSirve()
    {
        foreach (Collider c in GetComponents<Collider>())
        {
            if (c.isTrigger) return;
        }
        Debug.LogWarning("[Portales] Este portal no tiene un collider 3D marcado como IsTrigger. " +
            "Los colliders 2D (CircleCollider2D, etc.) NO disparan OnTriggerEnter 3D: cambialo por un " +
            "SphereCollider con IsTrigger. El teletransporte no va a funcionar mientras tanto.", this);
    }

    private void OnDisable()
    {
        allPortals.Remove(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (onCooldown) return;
        if (!other.CompareTag(playerTag)) return;

        // En red, el portal solo lo atraviesa el DUEÑO del cuerpo: si cada maquina
        // teletransportara por su cuenta, los destinos aleatorios pelearian con la
        // replica del ClientNetworkTransform.
        var cuerpoEnRed = other.GetComponentInParent<Unity.Netcode.NetworkObject>();
        if (cuerpoEnRed != null && !cuerpoEnRed.IsOwner) return;

        // el cadaver no viaja por portales
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

        // Mover jugador (si usa CharacterController hay que desactivarlo un frame)
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