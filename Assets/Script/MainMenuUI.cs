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

        optionsPanel.style.display = DisplayStyle.None;

        // Botones
        playButton = root.Q<Button>("PlayButton");
        settingsButton = root.Q<Button>("SettingsButton");
        quitButton = root.Q<Button>("QuitButton");
        backButton = root.Q<Button>("BackButton");

        // Volumen
        volumeSlider = root.Q<Slider>("VolumeSlider");

        // Eventos
        playButton.clicked += PlayGame;
        settingsButton.clicked += OpenOptions;
        quitButton.clicked += QuitGame;
        backButton.clicked += CloseOptions;

        volumeSlider.RegisterValueChangedCallback(OnVolumeChanged);

    }

    private void PlayGame()
    {
        Debug.Log("Comenzar Partida");
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
}