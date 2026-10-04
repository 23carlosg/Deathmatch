using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
using Unity.Netcode;

public class PauseMenuUI : MonoBehaviour
{
    public static bool Abierto { get; private set; }

    private bool saliendo = false;

    [SerializeField] private string escenaMenu = "Menu";

    private PanelRenderer panelRenderer;
    private VisualElement pausePanel;
    private Label joinCodeLabel;

    //BOTONES
    private Button reanudarButton;
    private Button disconnectButton;

    void Awake()
    {
        panelRenderer = GetComponent<PanelRenderer>();
        panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDestroy()
    {
        if (panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        Abierto = false;
    }

    void OnEnable()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += AlDesconectarse;
    }

    void OnDisable()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= AlDesconectarse;
    }

    private void AlDesconectarse(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || saliendo) return;

        // En el host este callback se dispara por CADA cliente que se va: se ignora.
        // Solo nos interesa cuando el que se desconecta somos nosotros (cliente puro).
        if (nm.IsServer) return;
        if (clientId != nm.LocalClientId) return;

        Desconectar();
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement root)
    {
        // Este UXML tambien tiene los paneles del menu: en la escena de juego se ocultan
        Ocultar(root, "MainMenuPanel");
        Ocultar(root, "SettingPanel");
        Ocultar(root, "MainLobbyPanel");

        pausePanel = root.Q<VisualElement>("PausePanel");
        joinCodeLabel = root.Q<Label>("PauseJoinCodeLabel");

        //BOTONES
        reanudarButton = root.Q<Button>("ReanudarButton");
        disconnectButton = root.Q<Button>("DisconnectButton");

        if (reanudarButton != null)
        {
            reanudarButton.clicked -= Cerrar;
            reanudarButton.clicked += Cerrar;
        }

        if (disconnectButton != null)
        {
            disconnectButton.clicked -= Desconectar;
            disconnectButton.clicked += Desconectar;
        }

        if (pausePanel != null) pausePanel.style.display = DisplayStyle.None;
        Abierto = false;
    }

    private void Ocultar(VisualElement root, string nombre)
    {
        var panel = root.Q<VisualElement>(nombre);
        if (panel != null) panel.style.display = DisplayStyle.None;
    }

    public void Alternar()
    {
        if (Abierto) Cerrar(); else Abrir();
    }

    private void Abrir()
    {
        Abierto = true;

        if (joinCodeLabel != null)
        {
            string codigo = NetworkLobbyManager.JoinCodeActual;
            joinCodeLabel.text = string.IsNullOrEmpty(codigo) ? "" : $"Código: {codigo}";
            joinCodeLabel.style.display = string.IsNullOrEmpty(codigo) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (pausePanel != null) pausePanel.style.display = DisplayStyle.Flex;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Cerrar()
    {
        Abierto = false;
        if (pausePanel != null) pausePanel.style.display = DisplayStyle.None;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Desconectar()
    {
        if (saliendo) return;
        saliendo = true;

        Abierto = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        NetworkLobbyManager.ResetearMapaListo();

        UnityEngine.SceneManagement.SceneManager.LoadScene(escenaMenu);
    }
}