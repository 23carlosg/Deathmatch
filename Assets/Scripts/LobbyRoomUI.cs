using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class LobbyRoomUI : MonoBehaviour
{
    public static LobbyRoomUI Instance { get; private set; }

    private PanelRenderer panelRenderer;
    private VisualElement root, panel, mainLobbyPanel;
    private Label codeLabel, countLabel, waitingLabel;
    private ScrollView list;
    private Button copyButton, startButton, leaveButton, readyButton;

    private float proximoRefresco;

    void Awake()
    {
        Instance = this;
        panelRenderer = GetComponent<PanelRenderer>();
        panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDestroy()
    {
        if (panelRenderer != null) panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        if (Instance == this) Instance = null;
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement rootElement)
    {
        root = rootElement;
        panel = root.Q<VisualElement>("WaitingRoomPanel");
        mainLobbyPanel = root.Q<VisualElement>("MainLobbyPanel");
        codeLabel = root.Q<Label>("RoomCodeLabel");
        countLabel = root.Q<Label>("RoomCountLabel");
        waitingLabel = root.Q<Label>("RoomWaitingLabel");
        list = root.Q<ScrollView>("RoomPlayerList");
        copyButton = root.Q<Button>("CopyCodeButton");
        startButton = root.Q<Button>("StartGameButton");
        leaveButton = root.Q<Button>("LeaveRoomButton");
        readyButton = root.Q<Button>("ReadyButton");

        if (panel == null)
        {
            Debug.LogError("LobbyRoomUI: no encontré WaitingRoomPanel en el UXML.");
            return;
        }

        if (copyButton != null)  { copyButton.clicked -= CopiarCodigo;    copyButton.clicked += CopiarCodigo; }
        if (startButton != null) { startButton.clicked -= IniciarPartida; startButton.clicked += IniciarPartida; }
        if (leaveButton != null) { leaveButton.clicked -= Salir;          leaveButton.clicked += Salir; }
        if (readyButton != null) { readyButton.clicked -= AlternarListo;  readyButton.clicked += AlternarListo; }

        panel.style.display = DisplayStyle.None;

        // Si el menú se recargó estando ya conectado a una sala, volver a mostrarla
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening) Abrir();
    }

    private bool Visible => panel != null && panel.style.display == DisplayStyle.Flex;

    // Lo llama MainMenuUI cuando creás o te unís a una sala
    public void Abrir()
    {
        if (panel == null) return;

        var menuPrincipal = root.Q<VisualElement>("MainMenuPanel");
        if (menuPrincipal != null) menuPrincipal.style.display = DisplayStyle.None;
        if (mainLobbyPanel != null) mainLobbyPanel.style.display = DisplayStyle.None;

        panel.style.display = DisplayStyle.Flex;
        Refrescar();
    }

    private void Cerrar()
    {
        if (panel != null) panel.style.display = DisplayStyle.None;
        if (mainLobbyPanel != null) mainLobbyPanel.style.display = DisplayStyle.Flex;

        var host = root?.Q<Button>("IniciarHostButton");
        var cliente = root?.Q<Button>("IniciarClienteButton");
        if (host != null) host.SetEnabled(true);
        if (cliente != null) { cliente.SetEnabled(true); cliente.pickingMode = PickingMode.Position; }
    }

    void Update()
    {
        if (!Visible) return;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            Cerrar();
            return;
        }

        if (Time.unscaledTime >= proximoRefresco)
        {
            proximoRefresco = Time.unscaledTime + 0.25f;
            Refrescar();
        }
    }

    private void Refrescar()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;

        bool esHost = nm.IsServer;

        // El código solo lo ve el anfitrión
        string codigo = NetworkLobbyManager.JoinCodeActual;
        bool mostrarCodigo = esHost && !string.IsNullOrEmpty(codigo);
        codeLabel.text = mostrarCodigo ? "Código: " + codigo : "";
        codeLabel.style.display = mostrarCodigo ? DisplayStyle.Flex : DisplayStyle.None;
        copyButton.style.display = mostrarCodigo ? DisplayStyle.Flex : DisplayStyle.None;

        // Botones según el rol
        startButton.style.display = esHost ? DisplayStyle.Flex : DisplayStyle.None;
        readyButton.style.display = esHost ? DisplayStyle.None : DisplayStyle.Flex;
        waitingLabel.style.display = DisplayStyle.Flex;

        list.Clear();
        var estado = LobbyRoomState.Instance;
        if (estado == null) return;

        var jugadores = estado.Players;
        countLabel.text = "Jugadores: " + jugadores.Count;

        bool yoListo = false;

        for (int i = 0; i < jugadores.Count; i++)
        {
            PlayerEntry p = jugadores[i];
            bool esLocal = p.ClientId == nm.LocalClientId;
            if (esLocal) yoListo = p.Listo;

            var fila = new VisualElement();
            fila.AddToClassList("room-row");
            if (esLocal) fila.AddToClassList("room-local");

            var nombre = new Label(p.Name.ToString());
            nombre.AddToClassList("room-name");
            fila.Add(nombre);

            if (p.ClientId == NetworkManager.ServerClientId)
            {
                var tag = new Label("Anfitrión");
                tag.AddToClassList("room-tag");
                fila.Add(tag);
            }
            else if (p.Listo)
            {
                var tag = new Label("Listo");
                tag.AddToClassList("room-listo");
                fila.Add(tag);
            }

            list.Add(fila);
        }

        if (esHost)
        {
            bool todos = estado.TodosListos();
            startButton.SetEnabled(todos);
            waitingLabel.text = estado.CantidadClientes() == 0
                ? "Esperando jugadores..."
                : $"Listos: {estado.CantidadListos()}/{estado.CantidadClientes()}";
        }
        else
        {
            waitingLabel.text = yoListo
                ? "Esperando que el anfitrión inicie la partida..."
                : "Apretá LISTO cuando estés preparado.";

            readyButton.text = yoListo ? "CANCELAR LISTO" : "LISTO";
            readyButton.EnableInClassList("room-ready-on", yoListo);
            readyButton.EnableInClassList("room-ready", !yoListo);
        }
    }

    private void AlternarListo()
    {
        if (LobbyRoomState.Instance != null) LobbyRoomState.Instance.AlternarListo();
    }

    private void CopiarCodigo()
    {
        GUIUtility.systemCopyBuffer = NetworkLobbyManager.JoinCodeActual ?? "";
    }

    private void IniciarPartida()
    {
        var lobby = NetworkLobbyManager.Instancia;
        if (lobby != null) lobby.IniciarPartida();
    }

    private void Salir()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening) nm.Shutdown();
        NetworkLobbyManager.ResetearMapaListo();
        Cerrar();
    }
}