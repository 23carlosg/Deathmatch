using UnityEngine;

/// <summary>
/// Alinea el modelo con el piso y ajusta la capsula del CharacterController a la altura medida
/// (una sola vez por instancia). La suela se apoya en -skinWidth para que las botas rendericen
/// sobre el piso; ajusteDePies permite un retoque fino desde el Inspector.
/// </summary>
public class PlayerBodySetup : MonoBehaviour
{
    public float ajusteDePies = 0f;

    bool cuerpoAlineado;

    public void Configurar(PlayerNetworkMovement jugador)
    {
        CharacterController controller = jugador.Controller;
        Animator animator = jugador.animator;
        Camera camara = jugador.Camara;
        PlayerWeapons armas = jugador.Weapons;
        PlayerCameraRig rig = jugador.CameraRig;

        float tope = 0f;
        float suela = float.MaxValue;
        bool medido = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            if (armas.EsParteDeUnArma(r.transform)) continue;
            float yMax = r.bounds.max.y - transform.position.y;
            float yMin = r.bounds.min.y - transform.position.y;
            if (yMax > tope) tope = yMax;
            if (yMin < suela) suela = yMin;
            medido = true;
        }

        Transform modelo = animator != null ? animator.transform : null;
        if (medido && modelo != null && !cuerpoAlineado)
        {
            cuerpoAlineado = true;
            float objetivoSuela = -controller.skinWidth;
            float subida = objetivoSuela - suela + ajusteDePies;
            float escala = transform.lossyScale.y != 0f ? transform.lossyScale.y : 1f;
            modelo.localPosition += Vector3.up * (subida / escala);
            tope += subida;
        }

        if (tope > 0.2f)
        {
            controller.height = tope;
            controller.center = new Vector3(0f, tope * 0.5f, 0f);
        }

        if (camara != null)
        {
            float altura = tope > 0.2f ? tope * 0.9f : rig.alturaCamara;
            camara.transform.localPosition = new Vector3(0f, altura, -rig.atrasCamara);
        }
    }
}