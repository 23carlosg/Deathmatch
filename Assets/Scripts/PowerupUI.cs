using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class PowerupUI : MonoBehaviour
{
    private PanelRenderer panelRenderer;
    private VisualElement caja;
    private Label titulo, tecla, estado;
    private PlayerPowerup powerup;
    private bool estabaInvisible;
    private float inicioInvisible;

    void Awake()
    {
        panelRenderer = GetComponent<PanelRenderer>();
        panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDestroy()
    {
        if (panelRenderer != null) panelRenderer.UnregisterUIReloadCallback(OnUIReload);
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement root)
    {
        caja = new VisualElement();
        caja.pickingMode = PickingMode.Ignore;
        caja.style.position = Position.Absolute;
        caja.style.top = 24;
        caja.style.right = 24;
        caja.style.alignItems = Align.Center;
        caja.style.paddingTop = caja.style.paddingBottom = 8;
        caja.style.paddingLeft = caja.style.paddingRight = 16;
        caja.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);
        caja.style.borderTopLeftRadius = caja.style.borderTopRightRadius =
            caja.style.borderBottomLeftRadius = caja.style.borderBottomRightRadius = 8;
        caja.style.display = DisplayStyle.None;

        titulo = Crear("POWERUP POR RACHA", 18, FontStyle.Bold);
        tecla  = Crear("\"Q\"", 26, FontStyle.Bold);
        estado = Crear("", 16, FontStyle.Normal);

        caja.Add(titulo);
        caja.Add(tecla);
        caja.Add(estado);
        root.Add(caja);
    }

    private Label Crear(string texto, int tamano, FontStyle estilo)
    {
        var l = new Label(texto);
        l.pickingMode = PickingMode.Ignore;
        l.style.color = Color.white;
        l.style.fontSize = tamano;
        l.style.unityFontStyleAndWeight = estilo;
        return l;
    }

    void Update()
    {
        if (caja == null) return;

        if (powerup == null)
        {
            var nm = NetworkManager.Singleton;
            var obj = nm != null && nm.IsListening ? nm.LocalClient?.PlayerObject : null;
            if (obj != null) powerup = obj.GetComponent<PlayerPowerup>();
            if (powerup == null) { caja.style.display = DisplayStyle.None; return; }
            tecla.text = $"\"{powerup.TeclaTexto}\"";
        }

        if (PauseMenuUI.Abierto || PlayerNetworkMovement.PartidaTerminada)
        {
            caja.style.display = DisplayStyle.None;
            return;
        }

        bool tiene = powerup.TienePowerup.Value;
        bool invisible = powerup.Invisible.Value;

        if (invisible && !estabaInvisible) inicioInvisible = Time.time;
        estabaInvisible = invisible;

        if (tiene)
        {
            // parpadeo suave
            caja.style.display = DisplayStyle.Flex;
            caja.style.opacity = Mathf.Lerp(0.3f, 1f, (Mathf.Sin(Time.time * 6f) + 1f) / 2f);
            titulo.text = "POWERUP POR RACHA";
            tecla.style.display = DisplayStyle.Flex;
            estado.text = "";
        }
        else if (invisible)
        {
            // el dueño no se ve a si mismo, así que se le avisa con una cuenta regresiva
            float resto = Mathf.Max(0f, powerup.Duracion - (Time.time - inicioInvisible));
            caja.style.display = DisplayStyle.Flex;
            caja.style.opacity = 1f;
            titulo.text = "INVISIBLE";
            tecla.style.display = DisplayStyle.None;
            estado.text = resto.ToString("0.0") + " s";
        }
        else
        {
            caja.style.display = DisplayStyle.None;
        }
    }
}