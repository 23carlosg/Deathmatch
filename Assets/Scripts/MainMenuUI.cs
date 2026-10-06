using UnityEngine;
using UnityEngine.UIElements;

public class MainMenuUI : MonoBehaviour
{
    private PanelRenderer panelRenderer;

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

    //Asteroides
    public MeteorSettings meteorSettings;

    // Lobby
    private VisualElement mainLobbyPanel;
    private Button iniciarHostButton;
    private Button iniciarClienteButton;
    private TextField joinCodeInput;

    private NetworkLobbyManager Lobby => NetworkLobbyManager.Instancia;

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

        //Asteroides
        DropdownField asteroideDropdown = root.Q<DropdownField>("AsteroideSetting");

        if (asteroideDropdown != null && meteorSettings != null)
        {
            // Poner como seleccionado el valor actual guardado
            asteroideDropdown.value = meteorSettings.maxAsteroidsCount.ToString();

            // Escuchar cambios
            asteroideDropdown.RegisterValueChangedCallback(evt =>
            {
                meteorSettings.maxAsteroidsCount = int.Parse(evt.newValue);
                Debug.Log($"Meteoritos configurados: {meteorSettings.maxAsteroidsCount}");
            });
        }

        // Inputs de lobby
        joinCodeInput = root.Q<TextField>("JoinCodeInput");

        // Volumen
        volumeSlider = root.Q<Slider>("VolumeSlider");

        // Eventos
        if (joinCodeInput != null)
        {
            joinCodeInput.UnregisterValueChangedCallback(OnJoinCodeChanged);
            joinCodeInput.RegisterValueChangedCallback(OnJoinCodeChanged);
            joinCodeInput.maxLength = 6;
        }
        if (playButton != null) playButton.clicked += PlayGame;
        if (settingsButton != null) settingsButton.clicked += OpenOptions;
        if (quitButton != null) quitButton.clicked += QuitGame;
        if (backButton != null) backButton.clicked += CloseOptions;
        if (iniciarHostButton != null) iniciarHostButton.clicked += IniciarServidor;
        if (iniciarClienteButton != null) iniciarClienteButton.clicked += UnirseServidor;

        if (volumeSlider != null) volumeSlider.RegisterValueChangedCallback(OnVolumeChanged);
    }

    private void OnJoinCodeChanged(ChangeEvent<string> evt)
    {
        string mayus = evt.newValue.ToUpper();
        if (mayus != evt.newValue)
            joinCodeInput.SetValueWithoutNotify(mayus);
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
        if (Lobby == null)
        {
            Debug.LogWarning("Error interno: no hay NetworkLobbyManager");
            return;
        }
        if (iniciarHostButton != null) iniciarHostButton.SetEnabled(false);
        iniciarClienteButton.pickingMode = PickingMode.Ignore;
        string joinCode = await Lobby.IniciarHost();

        if (string.IsNullOrEmpty(joinCode))
        {
            Debug.LogWarning("[UI] No se pudo obtener el Join Code");
            if (iniciarHostButton != null) iniciarHostButton.SetEnabled(true);
            return;
        }
    }

    private async void UnirseServidor()
    {
        if (Lobby == null)
        {
            Debug.LogWarning("Error interno: no hay NetworkLobbyManager");
            return;
        }
        string codigo = joinCodeInput != null ? joinCodeInput.value.Trim().ToUpper() : "";

        if (string.IsNullOrEmpty(codigo))
        {
            Debug.LogWarning("[UI] Ingresá un Join Code antes de unirte");
            return;
        }

        // Deshabilitar el botón 3 segundos desde el clic
        if (iniciarClienteButton != null) iniciarClienteButton.SetEnabled(false);
        var espera = System.Threading.Tasks.Task.Delay(3000);

        bool ok = await Lobby.IniciarCliente(codigo);

        if (!ok)
        {
            Debug.LogWarning("[UI] No se pudo unir. Revisá el código e intentá de nuevo.");
            joinCodeInput.value = "";
            joinCodeInput.textEdition.placeholder = "Esta mal ingrese el codigo correcto";
        }
        // Esperar lo que falte para completar los 3 segundos
        await espera;

        if (iniciarClienteButton != null) iniciarClienteButton.SetEnabled(true);
    }
}
