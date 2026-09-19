using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mira (crosshair) creada 100% por codigo, sin assets:
/// un punto central y cuatro patas cuya separacion (apertura) se controla desde Player.
/// </summary>
public class Crosshair : MonoBehaviour
{
    public Color color = new Color(1f, 1f, 1f, 0.85f);
    public float grosorPatas = 2f;
    public float largoPata = 8f;
    public float tamanoPunto = 3f;
    public float aperturaMaxima = 40f;

    float apertura = 9f;
    Canvas canvas;
    RectTransform[] patas;
    RectTransform punto;
    static readonly Vector2[] direcciones = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

    public static Crosshair Crear()
    {
        return new GameObject("Crosshair").AddComponent<Crosshair>();
    }

    void Awake()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        patas = new RectTransform[4];
        for (int i = 0; i < 4; i++)
            patas[i] = CrearImagen("Pata" + i);
        punto = CrearImagen("Punto");

        Aplicar();
    }

    RectTransform CrearImagen(string nombre)
    {
        GameObject hijo = new GameObject(nombre);
        hijo.transform.SetParent(transform, false);
        Image imagen = hijo.AddComponent<Image>();
        imagen.color = color;
        imagen.raycastTarget = false;
        RectTransform rt = hijo.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); // anclado al centro de la pantalla
        return rt;
    }

    public void SetApertura(float valor)
    {
        float nueva = Mathf.Clamp(valor, 0f, aperturaMaxima);
        if (Mathf.Approximately(nueva, apertura)) return;
        apertura = nueva;
        Aplicar();
    }

    public void Mostrar(bool visible)
    {
        if (canvas != null) canvas.enabled = visible;
    }

    // Estado actual del canvas (para saber si hay que re-crearla tras un cambio de escena)
    public bool EstaVisible => canvas != null && canvas.enabled;

    void Aplicar()
    {
        if (patas == null) return;
        for (int i = 0; i < 4; i++)
        {
            bool vertical = direcciones[i].y != 0f;
            patas[i].sizeDelta = vertical ? new Vector2(grosorPatas, largoPata) : new Vector2(largoPata, grosorPatas);
            patas[i].anchoredPosition = direcciones[i] * (apertura + largoPata * 0.5f);
        }
        punto.sizeDelta = Vector2.one * tamanoPunto;
    }
}
