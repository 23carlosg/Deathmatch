using UnityEngine;

/// <summary>
/// Camara en tercera persona del jugador LOCAL. La camara es hija del cuerpo (el yaw lo aporta el
/// cuerpo via PlayerCamera); aca se ajusta su posicion local: orbita alrededor del pecho segun el
/// pitch, se acerca al mirar abajo, se acerca si hay una pared detras (SphereCast) y funde el
/// cuerpo a semitransparente si la camara queda pegada a una pared.
/// </summary>
public class PlayerCameraRig : MonoBehaviour
{
    [Header("Camara (tercera persona)")]
    public float alturaCamara = 1.6f;
    public float atrasCamara = 2.5f;
    public float hombroApuntado = 0.45f;
    public float desvioCamaraLibre = 0.6f;
    public float fovApuntado = 30f;
    public float suavizadoCamara = 10f;

    [Header("Camara - Colision con paredes")]
    [Tooltip("Capas que acercan la camara y que bloquean los efectos del arma (marca solo Walls y el suelo; NO la capa del jugador ni bots)")]
    public LayerMask capasColisionCamara = ~0;
    public float radioColisionCamara = 0.25f;
    public float margenPared = 0.1f;
    public float distanciaMinimaCamara = 0.3f;

    [Header("Camara pegada a la pared")]
    [Tooltip("Por debajo de esta distancia al pivote se oculta el cuerpo (el arma sigue visible)")]
    public float distanciaOcultarCuerpo = 1.0f;
    [Tooltip("Evita parpadeo en el borde del umbral")]
    public float histeresisOcultar = 0.25f;
    [Tooltip("Opacidad del cuerpo cuando la camara queda pegada a una pared (0 = invisible, 1 = opaco)")]
    [Range(0f, 1f)] public float alphaCuerpoCerca = 0.35f;
    [Tooltip("Velocidad del fundido (mas alto = mas rapido)")]
    public float velocidadFundido = 8f;

    PlayerNetworkMovement jugador;
    Camera camara;
    Transform camaraTransform;

    // Buffer para el SphereCast sin generar basura
    static readonly RaycastHit[] golpesCamara = new RaycastHit[8];

    Renderer[] renderersCuerpo;
    bool cuerpoOculto;          // true = la camara esta pegada a una pared, el cuerpo se funde

    // Fundido: copias transparentes de los materiales (se crean la primera vez que hacen falta)
    Material[][] matsOriginales;
    Material[][] matsTransparentes;
    Color[][] coloresBase;
    float alphaCuerpo = 1f;
    bool usandoTransparentes;
    static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int IdColor = Shader.PropertyToID("_Color");

    public void Iniciar(PlayerNetworkMovement j)
    {
        jugador = j;
        camara = j.Camara;
        camaraTransform = j.CamaraTransform;

        // Renderers del cuerpo (sin armas ni particulas) para fundirlos si la camara queda pegada a una pared
        var lista = new System.Collections.Generic.List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (jugador.Weapons.EsParteDeUnArma(r.transform)) continue;   // el arma se sigue viendo
            lista.Add(r);
        }
        renderersCuerpo = lista.ToArray();
    }

    // ------------------------- POSICION DE LA CAMARA -------------------------

    public void MoverCamara(bool apuntando)
    {
        if (camaraTransform == null) return;

        // Todo se calcula en el espacio del PADRE de la camara, que es donde vive su localPosition
        Transform padre = camaraTransform.parent != null ? camaraTransform.parent : transform;

        float alturaObjetivo = apuntando ? alturaCamara * 0.85f : alturaCamara;
        float xObjetivo = apuntando ? hombroApuntado : desvioCamaraLibre;
        float zObjetivo = apuntando ? -Mathf.Max(0.5f, atrasCamara * 0.22f) : -atrasCamara;

        // Pitch positivo = mirando hacia abajo
        float pitch = jugador.controlCamara != null ? jugador.controlCamara.rotacionVertical : 0f;

        // Al mirar abajo, la camara se acerca (hasta el 55% de la distancia a 60° o mas)
        float abajo = Mathf.InverseLerp(0f, 60f, Mathf.Max(0f, pitch));
        float factorDistancia = Mathf.Lerp(1f, 0.55f, abajo);

        // Orbitar alrededor del pecho/hombros, no de la cabeza
        float alturaPivote = alturaObjetivo * 0.8f;
        Vector3 pivoteLocal = new Vector3(0f, alturaPivote, 0f);
        Vector3 desfase = new Vector3(xObjetivo, alturaObjetivo - alturaPivote, zObjetivo * factorDistancia);

        Vector3 objetivoLocal = pivoteLocal + Quaternion.Euler(pitch, 0f, 0f) * desfase;
        Vector3 idealLocal = objetivoLocal;

        // Colision con paredes: rayo grueso desde el pivote hacia la posicion ideal
        Vector3 idealMundo = padre.TransformPoint(objetivoLocal);
        Vector3 pivote = padre.TransformPoint(pivoteLocal);
        Vector3 haciaCamara = idealMundo - pivote;
        float distancia = haciaCamara.magnitude;
        bool choco = false;

        if (distancia > 0.001f)
        {
            Vector3 dir = haciaCamara / distancia;
            int n = Physics.SphereCastNonAlloc(pivote, radioColisionCamara, dir, golpesCamara,
                distancia, capasColisionCamara, QueryTriggerInteraction.Ignore);

            float masCerca = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = golpesCamara[i];
                if (h.distance <= 0f) continue;                              // ya solapaba al empezar
                if (jugador.DelPropioCuerpo(h.collider.transform)) continue; // ignorar al propio jugador
                if (h.distance < masCerca) masCerca = h.distance;
            }

            if (masCerca < float.MaxValue)
            {
                choco = true;
                float nueva = Mathf.Max(distanciaMinimaCamara, masCerca - margenPared);
                idealLocal = padre.InverseTransformPoint(pivote + dir * nueva);
            }
        }

        // Si hay obstaculo, acercar rapido; si no, volver suave
        bool acercando = (idealLocal - objetivoLocal).sqrMagnitude > 0.0001f;
        float velocidad = acercando ? suavizadoCamara * 3f : suavizadoCamara;
        float t = 1f - Mathf.Exp(-velocidad * Time.deltaTime);
        camaraTransform.localPosition = Vector3.Lerp(camaraTransform.localPosition, idealLocal, t);

        // Ocultar el cuerpo solo si una pared empujo la camara hacia adentro (no al apuntar)
        float distReal = Vector3.Distance(camaraTransform.position, pivote);
        if (!cuerpoOculto && choco && distReal < distanciaOcultarCuerpo)
            cuerpoOculto = true;
        else if (cuerpoOculto && (!choco || distReal > distanciaOcultarCuerpo + histeresisOcultar))
            cuerpoOculto = false;
        ActualizarFundidoCuerpo();

        // Zoom (FOV) al apuntar
        if (camara != null)
        {
            float tFov = 1f - Mathf.Exp(-suavizadoCamara * Time.deltaTime);
            camara.fieldOfView = Mathf.Lerp(camara.fieldOfView, apuntando ? fovApuntado : 60f, tFov);
        }
    }

    // ------------------------- FUNDIDO DEL CUERPO -------------------------

    // Crea una copia transparente de cada material del cuerpo (URP/Lit)
    void PrepararMaterialesTransparentes()
    {
        int n = renderersCuerpo.Length;
        matsOriginales = new Material[n][];
        matsTransparentes = new Material[n][];
        coloresBase = new Color[n][];

        for (int i = 0; i < n; i++)
        {
            Renderer r = renderersCuerpo[i];
            if (r == null)
            {
                matsOriginales[i] = new Material[0];
                matsTransparentes[i] = new Material[0];
                coloresBase[i] = new Color[0];
                continue;
            }

            Material[] orig = r.sharedMaterials;
            var trans = new Material[orig.Length];
            var cols = new Color[orig.Length];

            for (int j = 0; j < orig.Length; j++)
            {
                if (orig[j] == null) continue;
                Material m = new Material(orig[j]);
                HacerTransparente(m);
                trans[j] = m;
                cols[j] = m.HasProperty(IdBaseColor) ? m.GetColor(IdBaseColor)
                        : m.HasProperty(IdColor) ? m.GetColor(IdColor)
                        : Color.white;
            }

            matsOriginales[i] = orig;
            matsTransparentes[i] = trans;
            coloresBase[i] = cols;
        }
    }

    // Equivale a poner Surface Type = Transparent / Blend = Alpha en el material URP/Lit
    void HacerTransparente(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 1f);   // mejor orden de dibujado entre las partes del cuerpo
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    // Lleva la opacidad del cuerpo hacia alphaCuerpoCerca (pegado a pared) o 1 (normal)
    void ActualizarFundidoCuerpo()
    {
        if (renderersCuerpo == null) return;

        float objetivo = cuerpoOculto ? alphaCuerpoCerca : 1f;

        if (matsTransparentes == null)
        {
            if (objetivo >= 1f) return;           // todavia no hace falta
            PrepararMaterialesTransparentes();
        }

        alphaCuerpo = Mathf.MoveTowards(alphaCuerpo, objetivo, velocidadFundido * Time.deltaTime);

        // Totalmente opaco: volver a los materiales originales
        if (alphaCuerpo >= 0.999f)
        {
            if (usandoTransparentes)
            {
                for (int i = 0; i < renderersCuerpo.Length; i++)
                    if (renderersCuerpo[i] != null) renderersCuerpo[i].sharedMaterials = matsOriginales[i];
                usandoTransparentes = false;
            }
            return;
        }

        // Fundido: usar las copias transparentes y aplicar el alpha
        if (!usandoTransparentes)
        {
            for (int i = 0; i < renderersCuerpo.Length; i++)
                if (renderersCuerpo[i] != null) renderersCuerpo[i].sharedMaterials = matsTransparentes[i];
            usandoTransparentes = true;
        }

        for (int i = 0; i < matsTransparentes.Length; i++)
        {
            for (int j = 0; j < matsTransparentes[i].Length; j++)
            {
                Material m = matsTransparentes[i][j];
                if (m == null) continue;
                Color c = coloresBase[i][j];
                c.a = coloresBase[i][j].a * alphaCuerpo;
                if (m.HasProperty(IdBaseColor)) m.SetColor(IdBaseColor, c);
                else if (m.HasProperty(IdColor)) m.SetColor(IdColor, c);
            }
        }
    }
}