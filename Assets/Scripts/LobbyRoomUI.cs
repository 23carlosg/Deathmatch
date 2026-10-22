using System.Collections.Generic;
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
    private Button copyButton, startButton, leaveButton;
    private bool Visible => panel != null && panel.style.display == DisplayStyle.Flex;
    private string ultimoEstado = "";
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

        if (copyButton != null)  { copyButton.clicked -= CopiarCodigo;    copyButton.clicked += CopiarCodigo; }
        if (startButton != null) { startButton.clicked -= IniciarPartida; startButton.clicked += IniciarPartida; }
        if (leaveButton != null) { leaveButton.clicked -= Salir;          leaveButton.clicked += Salir; }

        panel.style.display = DisplayStyle.None;

        // Si el menú se recargó estando ya conectado a una sala, volver a mostrarla
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening) Abrir();
    }

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

        // Rehabilitar los botones del lobby
        var host = root?.Q<Button>("IniciarHostButton");
        var cliente = root?.Q<Button>("IniciarClienteButton");
        if (host != null) host.SetEnabled(true);
        if (cliente != null) { cliente.SetEnabled(true); cliente.pickingMode = PickingMode.Position; }
    }

    void Update()
    {
        if (!Visible) return;

        // Si se cae la conexión, se vuelve al lobby
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            Cerrar();
            return;
        }

        if (Time.unscaledTime >= proximoRefresco)
        {
            proximoRefresco = Time.unscaledTime + 0.5f;
            Refrescar();
        }
    }

    private void Refrescar()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;

        bool esHost = nm.IsServer;

        // El código solo lo ve el anfitrión
        string codigo = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) ? NetworkLobbyManager.JoinCodeActual : "";
        bool mostrarCodigo = esHost && !string.IsNullOrEmpty(codigo);
        codeLabel.text = mostrarCodigo ? "Código: " + codigo : "";
        codeLabel.style.display = mostrarCodigo ? DisplayStyle.Flex : DisplayStyle.None;
        copyButton.style.display = mostrarCodigo ? DisplayStyle.Flex : DisplayStyle.None;

        // Solo el anfitrión ve el botón de iniciar
        startButton.style.display = esHost ? DisplayStyle.Flex : DisplayStyle.None;
        waitingLabel.style.display = esHost ? DisplayStyle.None : DisplayStyle.Flex;

        // Lista de jugadores
        list.Clear();
        string estado = $"[Sala] Instance={(LobbyRoomState.Instance != null)} " +
        $"spawned={(LobbyRoomState.Instance != null && LobbyRoomState.Instance.IsSpawned)} " +
        $"count={(LobbyRoomState.Instance != null ? LobbyRoomState.Instance.Players.Count : -1)} " +
        $"esHost={nm.IsServer}";
        if (estado != ultimoEstado) { ultimoEstado = estado; Debug.Log(estado); }
        if (LobbyRoomState.Instance == null) return;

        var jugadores = LobbyRoomState.Instance.Players;
        countLabel.text = "Jugadores: " + jugadores.Count;

        for (int i = 0; i < jugadores.Count; i++)
        {
            PlayerEntry p = jugadores[i];

            var fila = new VisualElement();
            fila.AddToClassList("room-row");
            if (p.ClientId == nm.LocalClientId) fila.AddToClassList("room-local");

            var nombre = new Label(p.Name.ToString());
            nombre.AddToClassList("room-name");
            fila.Add(nombre);

            if (p.ClientId == NetworkManager.ServerClientId)
            {
                var tag = new Label("Anfitrion");
                tag.AddToClassList("room-tag");
                fila.Add(tag);
            }

            list.Add(fila);
        }
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