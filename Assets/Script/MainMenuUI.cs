using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;


public class MainMenuUI : MonoBehaviour
{
    private PanelRenderer panelRenderer;

    // Referencia al NetworkLobbyManager
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

        optionsPanel.style.display = DisplayStyle.None;
        mainLobbyPanel.style.display = DisplayStyle.None;
        if (mainMenuPanel != null)
            mainMenuPanel.style.display = DisplayStyle.Flex;

        // Botones
        playButton = root.Q<Button>("PlayButton");
        settingsButton = root.Q<Button>("SettingsButton");
        quitButton = root.Q<Button>("QuitButton");
        backButton = root.Q<Button>("BackButton");
        iniciarHostButton = root.Q<Button>("IniciarHostButton");
        iniciarClienteButton = root.Q<Button>("IniciarClienteButton");

        // Volumen
        volumeSlider = root.Q<Slider>("VolumeSlider");

        // Eventos
        playButton.clicked += PlayGame;
        settingsButton.clicked += OpenOptions;
        quitButton.clicked += QuitGame;
        backButton.clicked += CloseOptions;
        iniciarHostButton.clicked += IniciarServidor;
        iniciarClienteButton.clicked += UnirseServidor;

        volumeSlider.RegisterValueChangedCallback(OnVolumeChanged);

    }

    private void PlayGame()
    {
        Debug.Log("Comenzar Partida");
        // Oculta el menu principal
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
        AudioListener.volume = evt.newValue / 100f; // se usa 100f para que interprete el 100 como valor max 1
    }

    private void QuitGame()
    {
        Debug.Log("Salir de la partida");
        Application.Quit();
    }

    private void OnDestroy()
    {
        if (panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(OnUIReload);
    }
    
    // ------------------LOBBY---------------------

    private void IniciarServidor()
    {
        SceneManager.LoadScene("SampleScene");
        networkLobbyManager.IniciarHost();
    }

    private void UnirseServidor()
    {
        // llamar al script NetworkLobbyManager
        networkLobbyManager.IniciarCliente();
    }
}