using UnityEngine;

public class PortalSound : MonoBehaviour
{
    private AudioSource audioSource;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }

    void OnTriggerEnter(Collider other)
    {
        // Solo suena si lo toca el jugador
        if (other.CompareTag("Player"))
        {
            audioSource.Play();
        }
    }
}