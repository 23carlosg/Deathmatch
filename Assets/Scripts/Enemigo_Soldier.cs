using UnityEngine;

public class EnemigoSoldier : MonoBehaviour
{
    [Header("Salud")]
    public float saludMaxima = 100f;
    [Header("Respawn")]
    public bool revivirSolo = true;
    public float tiempoParaRevivir = 4f;

    [Header("Referencias (se asignan solas)")]
    public Animator animator;
    public CharacterController controller;

    float salud;
    bool muerto;
    float momentoDeMuerte;
    CapsuleCollider colliderPropio;

    void Awake()
    {
        // El animator y controller YA están en el hijo Sci-Fi_Soldier
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (controller == null) controller = GetComponentInChildren<CharacterController>();
        if (controller != null) controller.enabled = false;

        // Crear collider para recibir disparos
        colliderPropio = GetComponent<CapsuleCollider>();
        if (colliderPropio == null)
        {
            colliderPropio = gameObject.AddComponent<CapsuleCollider>();
            colliderPropio.direction = 1;
            colliderPropio.height = 1.8f;
            colliderPropio.radius = 0.35f;
            colliderPropio.center = new Vector3(0f, 0.9f, 0f);
        }

        salud = saludMaxima;
    }

    public void TakeDamage(float cantidad)
    {
        if (muerto) return;
        salud = Mathf.Max(0f, salud - cantidad);
        if (salud <= 0f) Morir();
        else if (animator != null)
        {
            animator.SetInteger("DamageID", Random.Range(0, 3));
            animator.SetTrigger("Damage");
        }
    }

    void Morir()
    {
        muerto = true;
        momentoDeMuerte = Time.time;
        if (colliderPropio != null) colliderPropio.enabled = false;
        if (animator != null) animator.SetTrigger("Death");
    }

    void Update()
    {
        if (muerto && revivirSolo && Time.time - momentoDeMuerte >= tiempoParaRevivir)
            Revivir();
    }

    public void Revivir()
    {
        muerto = false;
        salud = saludMaxima;
        if (colliderPropio != null) colliderPropio.enabled = true;
        if (animator != null) animator.Play("Idle", 0, 0f);
    }
}