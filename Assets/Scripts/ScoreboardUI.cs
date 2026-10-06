using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ScoreboardUI : MonoBehaviour
{
    private PanelRenderer panelRenderer;
    private VisualElement scoreboardPanel;
    private ScrollView list;

    private NetworkList<PlayerEntry> suscritoA;

    void Awake()
    {
        panelRenderer = GetComponent<PanelRenderer>();
        panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDestroy()
    {
        if (panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(OnUIReload);

        Desuscribir();
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement root)
    {
        scoreboardPanel = root.Q<VisualElement>("ScoreboardPanel");
        list = root.Q<ScrollView>("ScoreboardList");

        if (scoreboardPanel == null || list == null)
        {
            Debug.LogError("ScoreboardUI: no encontré ScoreboardPanel o ScoreboardList en el UXML.");
            return;
        }

        scoreboardPanel.style.display = DisplayStyle.None;
    }

    void Update()
    {
        if (scoreboardPanel == null) return;

        // Suscribirse a la lista cuando ScoreboardState ya existe
        if (suscritoA == null && ScoreboardState.Instance != null)
        {
            suscritoA = ScoreboardState.Instance.Players;
            suscritoA.OnListChanged += OnPlayersChanged;
        }

        bool tabAbajo = false, tabSoltado = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            tabAbajo = Keyboard.current.tabKey.wasPressedThisFrame;
            tabSoltado = Keyboard.current.tabKey.wasReleasedThisFrame;
        }
#else
        tabAbajo = Input.GetKeyDown(KeyCode.Tab);
        tabSoltado = Input.GetKeyUp(KeyCode.Tab);
#endif

        // No mostrar el scoreboard si el menú de pausa está abierto
        if (PauseMenuUI.Abierto)
        {
            Ocultar();
            return;
        }

        if (tabAbajo) Mostrar();
        if (tabSoltado) Ocultar();
    }

    private void Desuscribir()
    {
        if (suscritoA != null)
        {
            suscritoA.OnListChanged -= OnPlayersChanged;
            suscritoA = null;
        }
    }

    private void OnPlayersChanged(NetworkListEvent<PlayerEntry> e)
    {
        if (EstaVisible) Refrescar();
    }

    private bool EstaVisible =>
        scoreboardPanel != null && scoreboardPanel.style.display == DisplayStyle.Flex;

    private void Mostrar()
    {
        scoreboardPanel.style.display = DisplayStyle.Flex;
        Refrescar();
    }

    private void Ocultar()
    {
        if (scoreboardPanel != null)
            scoreboardPanel.style.display = DisplayStyle.None;
    }

    private void Refrescar()
    {
        if (list == null) return;
        list.Clear();

        if (ScoreboardState.Instance == null || NetworkManager.Singleton == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        var jugadores = ScoreboardState.Instance.Players;

        // Copiar a una List normal para poder ordenar
        var copia = new List<PlayerEntry>();
        for (int i = 0; i < jugadores.Count; i++)
            copia.Add(jugadores[i]);

        copia.Sort((a, b) => b.Score.CompareTo(a.Score));

        foreach (var p in copia)
        {
            var fila = new VisualElement();
            fila.AddToClassList("sb-row");
            if (p.ClientId == localId) fila.AddToClassList("sb-local");

            var nombre = new Label(p.Name.ToString());
            nombre.AddToClassList("sb-name");

            var puntaje = new Label(p.Score.ToString());
            puntaje.AddToClassList("sb-score");

            fila.Add(nombre);
            fila.Add(puntaje);
            list.Add(fila);
        }
    }
}