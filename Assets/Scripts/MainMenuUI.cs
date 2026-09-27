using UnityEngine;
using UnityEngine.UIElements;

public class MainMenuUI : MonoBehaviour
{
    private PanelRenderer panelRenderer;

    [SerializeField] private NetworkLobbyManager networkLobbyManager;

    // Menu principal
    private VisualElement mainMenuPanel;
    private Button playButton;
    private Button settingsButton;
    private Button quitButton;

    // Opciones
    private VisualElement optionsPanel;
    private Button backButton;
    private Slider volumeSlider;
    private Label volumeValue;

    // Lobby
    private VisualElement mainLobbyPanel;
    private Button iniciarHostButton;
    private Button iniciarClienteButton;
    private TextField joinCodeInput;     // <-- NUEVO
    private Label joinCodeLabel;         // <-- NUEVO (para mostrar el código al host)

    private void Awake()
    {
        panelRenderer = GetComponent<PanelRenderer>();
        panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement root)
    {
        // Paneles
        mainMenuPanel = root.Q<VisualElement>("MainMenuPanel");
        optionsPanel = root.Q<VisualElement>("SettingPanel");
        mainLobbyPanel = root.Q<VisualElement>("MainLobbyPanel");

        if (optionsPanel != null) optionsPanel.style.display = DisplayStyle.None;
        if (mainLobbyPanel != null) mainLobbyPanel.style.display = DisplayStyle.None;
        if (mainMenuPanel != null) mainMenuPanel.style.display = DisplayStyle.Flex;

        // Botones
        playButton = root.Q<Button>("PlayButton");
        settingsButton = root.Q<Button>("SettingsButton");
        quitButton = root.Q<Button>("QuitButton");
        backButton = root.Q<Button>("BackButton");
        iniciarHostButton = root.Q<Button>("IniciarHostButton");
        iniciarClienteButton = root.Q<Button>("IniciarClienteButton");

        // Inputs de lobby
        joinCodeInput = root.Q<TextField>("JoinCodeInput");     // <-- NUEVO
        joinCodeLabel = root.Q<Label>("JoinCodeLabel");         // <-- NUEVO

        // Volumen
        volumeSlider = root.Q<Slider>("VolumeSlider");

        // Eventos
        if (playButton != null) playButton.clicked += PlayGame;
        if (settingsButton != null) settingsButton.clicked += OpenOptions;
        if (quitButton != null) quitButton.clicked += QuitGame;
        if (backButton != null) backButton.clicked += CloseOptions;
        if (iniciarHostButton != null) iniciarHostButton.clicked += IniciarServidor;
        if (iniciarClienteButton != null) iniciarClienteButton.clicked += UnirseServidor;

        if (volumeSlider != null) volumeSlider.RegisterValueChangedCallback(OnVolumeChanged);
    }

    private void PlayGame()
    {
        mainMenuPanel.style.display = DisplayStyle.None;
        mainLobbyPanel.style.display = DisplayStyle.Flex;
    }

    private void OpenOptions()
    {
        mainMenuPanel.style.display = DisplayStyle.None;
        optionsPanel.style.display = DisplayStyle.Flex;
    }

    private void CloseOptions()
    {
        optionsPanel.style.display = DisplayStyle.None;
        mainMenuPanel.style.display = DisplayStyle.Flex;
    }

    private void OnVolumeChanged(ChangeEvent<float> evt)
    {
        AudioListener.volume = evt.newValue / 100f;
    }

    private void QuitGame()
    {
        Application.Quit();
    }

    private void OnDestroy()
    {
        if (panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(OnUIReload);
    }

    // ------------------ LOBBY ---------------------

    private async void IniciarServidor()
    {
        // Iniciar el host (crea la asignacion en Relay)
        networkLobbyManager.IniciarHost();

        // Esperar a que el Join Code este disponible (lo genera Relay de forma asincrona)
        // Timeout de seguridad: 10 segundos
        float t = 0f;
        while (string.IsNullOrEmpty(NetworkLobbyManager.JoinCodeActual) && t < 10f)
        {
            await System.Threading.Tasks.Task.Delay(100);
            t += 0.1f;
        }

        if (joinCodeLabel != null && !string.IsNullOrEmpty(NetworkLobbyManager.JoinCodeActual))
            joinCodeLabel.text = $"Código: {NetworkLobbyManager.JoinCodeActual}";
        else
            Debug.LogWarning("[UI] No se pudo obtener el Join Code");
    }

    private void UnirseServidor()
    {
        // Leer el codigo que escribio el jugador y pasarselo al NetworkLobbyManager
        string codigo = joinCodeInput != null ? joinCodeInput.value.Trim() : "";

        if (string.IsNullOrEmpty(codigo))
        {
            Debug.LogWarning("[UI] Ingresá un Join Code antes de unirte");
            return;
        }

        networkLobbyManager.IniciarCliente(codigo);
    }
}
