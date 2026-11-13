using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class PickupUI : MonoBehaviour
{
    [SerializeField] private string tecla = "E";
    [SerializeField] private string texto = "Recoger arma";
    [SerializeField] private float alcance = 5f;

    private PanelRenderer panelRenderer;
    private VisualElement prompt;
    private Label keyLabel, textLabel;
    private PlayerNetworkMovement jugador;

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
        prompt = root.Q<VisualElement>("Pickup");
        keyLabel = root.Q<Label>("PickupKey");
        textLabel = root.Q<Label>("PickupText");

        keyLabel.text = tecla;
        textLabel.text = texto;
        prompt.style.display = DisplayStyle.None;
    }

    void Update()
    {
        if (prompt == null) return;

        bool visible = HayArmaMirada();
        var d = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (prompt.style.display != d) prompt.style.display = d;
    }

    private bool HayArmaMirada()
    {
        if (PauseMenuUI.Abierto || PlayerNetworkMovement.PartidaTerminada) return false;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return false;

        if (jugador == null)
        {
            var obj = nm.LocalClient?.PlayerObject;
            if (obj != null) jugador = obj.GetComponent<PlayerNetworkMovement>();
            if (jugador == null) return false;
        }

        if (jugador.Muerto) return false;

        Camera cam = Camera.main;
        if (cam == null) return false;

        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, alcance))
            return false;

        return hit.collider.GetComponent<AgarrarItem>() != null;
    }
}