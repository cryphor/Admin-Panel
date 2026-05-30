using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Puck Admin Panel Mod
// Draggable, minimisable, searchable. Press N to toggle.

public class PluginMain : IPuckPlugin
{
    private Harmony harmony;
    private GameObject panelGO;

    public bool OnEnable()
    {
        harmony = new Harmony("com.puck.adminpanel");
        harmony.PatchAll();
        panelGO = new GameObject("AdminPanelView");
        UnityEngine.Object.DontDestroyOnLoad(panelGO);
        panelGO.AddComponent<AdminPanelBehaviour>();
        return true;
    }

    public bool OnDisable()
    {
        if (panelGO != null) UnityEngine.Object.Destroy(panelGO);
        harmony?.UnpatchSelf();
        AdminPanel.Destroy();
        return true;
    }
}

public class AdminPanelBehaviour : MonoBehaviour
{
    void Start() { StartCoroutine(InitDelayed()); }

    System.Collections.IEnumerator InitDelayed()
    {
        yield return null;
        yield return null;
        if (UIManager.Instance != null && UIManager.Instance.UIDocument != null)
            AdminPanel.Create(UIManager.Instance.UIDocument.rootVisualElement);
    }

    void Update() { AdminPanel.Tick(); }
    void OnDestroy() { AdminPanel.Destroy(); }
}

public static class AdminPanel
{
    private static VisualElement panel;
    private static VisualElement bodyContainer;
    private static VisualElement playerListContainer;
    private static Label statusLabel;
    private static TextField steamIdField;
    private static TextField searchField;
    private static Player selectedPlayer;
    private static bool panelVisible = false;
    private static bool minimized = false;
    private static float refreshTimer;
    private static string searchFilter = "";

    private static bool isDragging = false;
    private static Vector2 dragOffset;
    private static bool localPlayerIsAdmin = false;

    private static readonly Color ColBg = new Color(0.10f, 0.10f, 0.13f, 0.97f);
    private static readonly Color ColHeader = new Color(0.16f, 0.16f, 0.22f, 1f);
    private static readonly Color ColRow = new Color(0.12f, 0.12f, 0.15f, 1f);
    private static readonly Color ColRowAlt = new Color(0.14f, 0.14f, 0.17f, 1f);
    private static readonly Color ColRowHover = new Color(0.20f, 0.20f, 0.28f, 1f);
    private static readonly Color ColAccent = new Color(0.22f, 0.50f, 0.95f, 1f);
    private static readonly Color ColRed = new Color(0.85f, 0.18f, 0.18f, 1f);
    private static readonly Color ColOrange = new Color(0.85f, 0.50f, 0.08f, 1f);
    private static readonly Color ColGreen = new Color(0.18f, 0.70f, 0.30f, 1f);
    private static readonly Color ColText = new Color(0.90f, 0.90f, 0.92f, 1f);
    private static readonly Color ColTextDim = new Color(0.48f, 0.48f, 0.54f, 1f);
    private static readonly Color ColTextMuted = new Color(0.32f, 0.32f, 0.38f, 1f);
    private static readonly Color ColBlueTeam = new Color(0.30f, 0.55f, 1.00f, 1f);
    private static readonly Color ColRedTeam = new Color(1.00f, 0.30f, 0.30f, 1f);
    private static readonly Color ColBorder = new Color(0.25f, 0.25f, 0.32f, 1f);
    private static readonly Color ColInputBg = new Color(0.14f, 0.14f, 0.18f, 1f);

    // ── LIFECYCLE ──────────────────────────────────────────────

    public static void Create(VisualElement uiRoot)
    {
        BuildPanel(uiRoot);
        Hide();
    }

    public static void Destroy()
    {
        if (panel != null && panel.parent != null)
            panel.parent.Remove(panel);
        panel = null;
    }

    public static void Tick()
    {
        if (panel == null) return;

        if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame && !IsTyping())
            Toggle();

        if (isDragging)
        {
            var mousePos = Mouse.current.position.ReadValue();
            float screenH = Screen.height;
            float newX = mousePos.x - dragOffset.x;
            float newY = screenH - mousePos.y - dragOffset.y;
            newX = Mathf.Max(0, Mathf.Min(newX, Screen.width - 50));
            newY = Mathf.Max(0, Mathf.Min(newY, Screen.height - 20));
            panel.style.left = newX;
            panel.style.top = newY;
        }

        refreshTimer += Time.deltaTime;
        if (refreshTimer >= 1.5f)
        {
            refreshTimer = 0f;
            if (panelVisible && !minimized)
                RefreshPlayerList();
        }
    }

    private static bool IsTyping()
    {
        var focused = panel?.focusController?.focusedElement;
        return focused is TextField;
    }

    // ── BUILD PANEL ────────────────────────────────────────────

    private static void BuildPanel(VisualElement uiRoot)
    {
        panel = new VisualElement();
        panel.name = "AdminPanel";
        panel.pickingMode = PickingMode.Position;
        panel.style.position = Position.Absolute;
        panel.style.left = 40;
        panel.style.top = 40;
        panel.style.width = 600;
        panel.style.backgroundColor = ColBg;
        panel.style.borderTopLeftRadius = 10;
        panel.style.borderTopRightRadius = 10;
        panel.style.borderBottomLeftRadius = 10;
        panel.style.borderBottomRightRadius = 10;
        panel.style.borderLeftWidth = 1;
        panel.style.borderRightWidth = 1;
        panel.style.borderTopWidth = 1;
        panel.style.borderBottomWidth = 1;
        panel.style.borderLeftColor = ColBorder;
        panel.style.borderRightColor = ColBorder;
        panel.style.borderTopColor = ColBorder;
        panel.style.borderBottomColor = ColBorder;

        // ── HEADER ──
        var header = new VisualElement();
        header.name = "AdminPanel_Header";
        header.pickingMode = PickingMode.Position;
        header.style.height = 34;
        header.style.backgroundColor = ColHeader;
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.paddingLeft = 10;
        header.style.paddingRight = 6;
        header.style.borderTopLeftRadius = 10;
        header.style.borderTopRightRadius = 10;

        var grip = new VisualElement();
        grip.style.width = 12;
        grip.style.height = 18;
        grip.style.marginRight = 6;
        grip.style.flexDirection = FlexDirection.Column;
        grip.style.justifyContent = Justify.SpaceEvenly;
        for (int i = 0; i < 3; i++)
        {
            var dotRow = new VisualElement();
            dotRow.style.flexDirection = FlexDirection.Row;
            dotRow.style.justifyContent = Justify.SpaceEvenly;
            dotRow.style.height = 3;
            for (int j = 0; j < 2; j++)
            {
                var dot = new VisualElement();
                dot.style.width = 2;
                dot.style.height = 2;
                dot.style.backgroundColor = new Color(0.45f, 0.45f, 0.50f, 0.5f);
                dot.style.borderTopLeftRadius = 1;
                dot.style.borderTopRightRadius = 1;
                dot.style.borderBottomLeftRadius = 1;
                dot.style.borderBottomRightRadius = 1;
                dotRow.Add(dot);
            }
            grip.Add(dotRow);
        }

        var title = new Label("ADMIN PANEL");
        title.style.color = ColText;
        title.style.fontSize = 12;

        var headerSpacer = new VisualElement();
        headerSpacer.style.flexGrow = 1;

        var hintLabel = new Label("N");
        hintLabel.style.color = ColTextMuted;
        hintLabel.style.fontSize = 10;
        hintLabel.style.marginRight = 8;
        hintLabel.style.paddingLeft = 6;
        hintLabel.style.paddingRight = 6;
        hintLabel.style.paddingTop = 2;
        hintLabel.style.paddingBottom = 2;
        hintLabel.style.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 1f);
        hintLabel.style.borderTopLeftRadius = 3;
        hintLabel.style.borderTopRightRadius = 3;
        hintLabel.style.borderBottomLeftRadius = 3;
        hintLabel.style.borderBottomRightRadius = 3;

        var minBtn = new Button(() => ToggleMinimize());
        var minLbl = new Label("-");
        minLbl.style.color = ColTextDim;
        minLbl.style.fontSize = 11;
        minLbl.style.flexGrow = 1;
        minBtn.Add(minLbl);
        minBtn.style.width = 24;
        minBtn.style.height = 20;
        minBtn.style.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 1f);
        minBtn.style.borderTopLeftRadius = 3;
        minBtn.style.borderTopRightRadius = 3;
        minBtn.style.borderBottomLeftRadius = 3;
        minBtn.style.borderBottomRightRadius = 3;
        minBtn.style.marginRight = 3;
        minBtn.style.paddingLeft = 0;
        minBtn.style.paddingRight = 0;
        minBtn.style.justifyContent = Justify.Center;
        minBtn.style.alignItems = Align.Center;

        var closeBtn = new Button(() => Hide());
        var closeLbl = new Label("x");
        closeLbl.style.color = new Color(1f, 0.40f, 0.40f, 1f);
        closeLbl.style.fontSize = 11;
        closeLbl.style.flexGrow = 1;
        closeBtn.Add(closeLbl);
        closeBtn.style.width = 24;
        closeBtn.style.height = 20;
        closeBtn.style.backgroundColor = new Color(0.28f, 0.10f, 0.10f, 1f);
        closeBtn.style.borderTopLeftRadius = 3;
        closeBtn.style.borderTopRightRadius = 3;
        closeBtn.style.borderBottomLeftRadius = 3;
        closeBtn.style.borderBottomRightRadius = 3;
        closeBtn.style.paddingLeft = 0;
        closeBtn.style.paddingRight = 0;
        closeBtn.style.justifyContent = Justify.Center;
        closeBtn.style.alignItems = Align.Center;

        header.Add(grip);
        header.Add(title);
        header.Add(headerSpacer);
        header.Add(hintLabel);
        header.Add(minBtn);
        header.Add(closeBtn);
        panel.Add(header);

        header.RegisterCallback<PointerDownEvent>(OnHeaderPointerDown);
        header.RegisterCallback<PointerMoveEvent>(OnHeaderPointerMove);
        header.RegisterCallback<PointerUpEvent>(OnHeaderPointerUp);

        // ── BODY ──
        bodyContainer = new VisualElement();
        bodyContainer.style.paddingLeft = 8;
        bodyContainer.style.paddingRight = 8;
        bodyContainer.style.paddingBottom = 8;

        // Toolbar
        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.height = 34;
        toolbar.style.alignItems = Align.Center;
        toolbar.style.marginTop = 4;
        toolbar.style.marginBottom = 4;

        // Search field - filters player list by name or Steam ID
        searchField = new TextField();
        searchField.style.flexGrow = 1;
        searchField.style.height = 26;
        searchField.style.backgroundColor = ColInputBg;
        searchField.style.color = ColText;
        searchField.style.borderTopLeftRadius = 4;
        searchField.style.borderTopRightRadius = 4;
        searchField.style.borderBottomLeftRadius = 4;
        searchField.style.borderBottomRightRadius = 4;
        searchField.style.paddingLeft = 8;
        searchField.style.paddingRight = 8;
        searchField.style.fontSize = 11;
        searchField.style.borderLeftWidth = 0;
        searchField.style.borderRightWidth = 0;
        searchField.style.borderTopWidth = 0;
        searchField.style.borderBottomWidth = 0;
        searchField.RegisterCallback<ChangeEvent<string>>(OnSearchChanged);
        toolbar.Add(searchField);

        toolbar.Add(MakeToolButton("Kick", ColOrange, () => ConfirmAction("kick", DoKick)));
        toolbar.Add(MakeToolButton("Ban", ColRed, () => ConfirmAction("ban", DoBan)));
        toolbar.Add(MakeToolButton("Refresh", ColAccent, () => RefreshPlayerList()));
        bodyContainer.Add(toolbar);

        // Status
        statusLabel = new Label("No players connected");
        statusLabel.style.height = 18;
        statusLabel.style.color = ColTextDim;
        statusLabel.style.fontSize = 10;
        statusLabel.style.paddingLeft = 4;
        statusLabel.style.paddingTop = 2;
        statusLabel.style.marginBottom = 2;
        bodyContainer.Add(statusLabel);

        // Player list
        var scrollView = new ScrollView(ScrollViewMode.Vertical);
        scrollView.style.minHeight = 220;
        scrollView.style.maxHeight = 560;
        scrollView.style.backgroundColor = new Color(0.07f, 0.07f, 0.09f, 1f);
        scrollView.style.borderTopLeftRadius = 5;
        scrollView.style.borderTopRightRadius = 5;
        scrollView.style.borderBottomLeftRadius = 5;
        scrollView.style.borderBottomRightRadius = 5;
        scrollView.style.paddingTop = 2;
        scrollView.style.paddingBottom = 2;
        scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

        var colHeader = new VisualElement();
        colHeader.style.flexDirection = FlexDirection.Row;
        colHeader.style.height = 20;
        colHeader.style.backgroundColor = new Color(0.13f, 0.13f, 0.16f, 1f);
        colHeader.style.alignItems = Align.Center;
        colHeader.style.paddingLeft = 6;
        colHeader.style.paddingRight = 4;
        colHeader.style.borderTopLeftRadius = 5;
        colHeader.style.borderTopRightRadius = 5;
        colHeader.Add(MakeHeaderLabel("PLAYER", 190));
        colHeader.Add(MakeHeaderLabel("TEAM", 80));
        colHeader.Add(MakeHeaderLabel("ROLE", 70));
        colHeader.Add(MakeHeaderLabel("STEAM ID", 160));
        colHeader.Add(MakeHeaderLabel("", 70));
        scrollView.Add(colHeader);

        playerListContainer = new VisualElement();
        scrollView.Add(playerListContainer);
        bodyContainer.Add(scrollView);

        // Bottom bar
        var bottomBar = new VisualElement();
        bottomBar.style.flexDirection = FlexDirection.Row;
        bottomBar.style.height = 30;
        bottomBar.style.alignItems = Align.Center;
        bottomBar.style.marginTop = 4;

        var steamIdPrefix = new Label("ID:");
        steamIdPrefix.style.color = ColTextMuted;
        steamIdPrefix.style.fontSize = 10;
        steamIdPrefix.style.width = 20;

        steamIdField = new TextField();
        steamIdField.style.flexGrow = 1;
        steamIdField.style.height = 24;
        steamIdField.style.backgroundColor = ColInputBg;
        steamIdField.style.color = ColText;
        steamIdField.style.fontSize = 11;
        steamIdField.style.borderTopLeftRadius = 4;
        steamIdField.style.borderTopRightRadius = 4;
        steamIdField.style.borderBottomLeftRadius = 4;
        steamIdField.style.borderBottomRightRadius = 4;
        steamIdField.style.paddingLeft = 6;
        steamIdField.style.borderLeftWidth = 0;
        steamIdField.style.borderRightWidth = 0;
        steamIdField.style.borderTopWidth = 0;
        steamIdField.style.borderBottomWidth = 0;

        bottomBar.Add(steamIdPrefix);
        bottomBar.Add(steamIdField);
        bottomBar.Add(MakeToolButton("Kick", ColOrange, () => DoKickBySteamId()));
        bottomBar.Add(MakeToolButton("Ban", ColRed, () => DoBanBySteamId()));
        bodyContainer.Add(bottomBar);

        panel.Add(bodyContainer);
        uiRoot.Add(panel);
    }

    // ── SEARCH ─────────────────────────────────────────────────

    private static void OnSearchChanged(ChangeEvent<string> evt)
    {
        searchFilter = evt.newValue != null ? evt.newValue.ToLowerInvariant() : "";
        RefreshPlayerList();
    }

    private static bool MatchesFilter(Player player)
    {
        if (string.IsNullOrEmpty(searchFilter)) return true;
        string name = SafeNetString(player.Username).ToLowerInvariant();
        string steamId = SafeNetString(player.SteamId).ToLowerInvariant();
        return name.Contains(searchFilter) || steamId.Contains(searchFilter);
    }

    // ── DRAG ───────────────────────────────────────────────────

    private static void OnHeaderPointerDown(PointerDownEvent evt)
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            isDragging = true;
            var mousePos = Mouse.current.position.ReadValue();
            float screenH = Screen.height;
            dragOffset = new Vector2(
                mousePos.x - panel.style.left.value.value,
                screenH - mousePos.y - panel.style.top.value.value
            );
            panel.BringToFront();
            evt.StopPropagation();
        }
    }

    private static void OnHeaderPointerMove(PointerMoveEvent evt)
    {
        if (isDragging)
        {
            var mousePos = Mouse.current.position.ReadValue();
            float screenH = Screen.height;
            float newX = mousePos.x - dragOffset.x;
            float newY = screenH - mousePos.y - dragOffset.y;
            newX = Mathf.Max(0, Mathf.Min(newX, Screen.width - 50));
            newY = Mathf.Max(0, Mathf.Min(newY, Screen.height - 20));
            panel.style.left = newX;
            panel.style.top = newY;
            evt.StopPropagation();
        }
    }

    private static void OnHeaderPointerUp(PointerUpEvent evt)
    {
        if (isDragging)
        {
            isDragging = false;
            evt.StopPropagation();
        }
    }

    // ── UI HELPERS ─────────────────────────────────────────────

    private static Button MakeToolButton(string text, Color bg, Action action)
    {
        var lbl = new Label(text);
        lbl.style.color = Color.white;
        lbl.style.fontSize = 11;
        lbl.style.flexGrow = 1;

        var btn = new Button(action);
        btn.Add(lbl);
        btn.style.height = 26;
        btn.style.minWidth = 40;
        btn.style.paddingLeft = 6;
        btn.style.paddingRight = 6;
        btn.style.marginLeft = 2;
        btn.style.marginRight = 2;
        btn.style.backgroundColor = bg;
        btn.style.borderTopLeftRadius = 4;
        btn.style.borderTopRightRadius = 4;
        btn.style.borderBottomLeftRadius = 4;
        btn.style.borderBottomRightRadius = 4;
        btn.style.borderLeftWidth = 0;
        btn.style.borderRightWidth = 0;
        btn.style.borderTopWidth = 0;
        btn.style.borderBottomWidth = 0;
        btn.style.justifyContent = Justify.Center;
        btn.style.alignItems = Align.Center;
        return btn;
    }

    private static Label MakeHeaderLabel(string text, float width)
    {
        var l = new Label(text);
        l.style.color = ColTextMuted;
        l.style.fontSize = 10;
        l.style.letterSpacing = 1;
        l.style.width = width;
        return l;
    }

    // ── VISIBILITY ─────────────────────────────────────────────

    public static void Toggle()
    {
        // Only allow opening if in a server AND local player is admin
        if (!panelVisible)
        {
            if (!IsInServer())
                return;
            CheckLocalPlayerAdmin();
            if (!localPlayerIsAdmin)
                return;
        }
        if (panelVisible) Hide(); else Show();
    }

    public static void Show()
    {
        if (panel == null) return;
        panel.style.display = DisplayStyle.Flex;
        panelVisible = true;
        panel.BringToFront();
        RefreshPlayerList();
    }

    public static void Hide()
    {
        if (panel == null) return;
        panel.style.display = DisplayStyle.None;
        panelVisible = false;
    }

    public static void ToggleMinimize()
    {
        if (panel == null) return;
        minimized = !minimized;
        bodyContainer.style.display = minimized ? DisplayStyle.None : DisplayStyle.Flex;
        panel.style.width = minimized ? 280 : 600;
    }

    // ── PLAYER LIST ────────────────────────────────────────────

    public static void RefreshPlayerList()
    {
        if (playerListContainer == null || PlayerManager.Instance == null) return;
        playerListContainer.Clear();

        var players = PlayerManager.Instance.GetPlayers();
        int idx = 0;
        foreach (var player in players)
        {
            if (!MatchesFilter(player)) continue;
            playerListContainer.Add(BuildPlayerRow(player, idx % 2 == 0 ? ColRow : ColRowAlt));
            idx++;
        }
        statusLabel.text = "Showing " + idx + " / " + players.Count;
    }

    private static VisualElement BuildPlayerRow(Player player, Color bg)
    {
        var row = new VisualElement();
        row.name = "AP_Row_" + SafeNetString(player.SteamId);
        row.pickingMode = PickingMode.Position;
        row.style.flexDirection = FlexDirection.Row;
        row.style.height = 26;
        row.style.alignItems = Align.Center;
        row.style.backgroundColor = bg;
        row.style.paddingLeft = 6;
        row.style.paddingRight = 4;
        row.style.marginBottom = 1;
        row.style.borderTopLeftRadius = 3;
        row.style.borderTopRightRadius = 3;
        row.style.borderBottomLeftRadius = 3;
        row.style.borderBottomRightRadius = 3;

        row.RegisterCallback<MouseEnterEvent>(_ => row.style.backgroundColor = ColRowHover);
        row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = bg);

        string playerName = SafeNetString(player.Username);
        string steamIdStr = SafeNetString(player.SteamId);
        var gs = player.GameState.Value;

        var nameLabel = new Label(playerName);
        nameLabel.style.width = 190;
        nameLabel.style.fontSize = 11;
        nameLabel.style.color = ColText;
        nameLabel.style.overflow = Overflow.Hidden;

        var teamLabel = new Label(gs.Team.ToString());
        teamLabel.style.width = 80;
        teamLabel.style.fontSize = 10;
        switch (gs.Team)
        {
            case PlayerTeam.Blue: teamLabel.style.color = ColBlueTeam; break;
            case PlayerTeam.Red: teamLabel.style.color = ColRedTeam; break;
            default: teamLabel.style.color = ColTextDim; break;
        }

        var roleLabel = new Label(gs.Role.ToString());
        roleLabel.style.width = 70;
        roleLabel.style.fontSize = 10;
        roleLabel.style.color = ColTextDim;

        var steamIdLabel = new Label(steamIdStr);
        steamIdLabel.style.width = 160;
        steamIdLabel.style.fontSize = 9;
        steamIdLabel.style.color = ColTextMuted;

        var selectBtn = new Button(() => SelectPlayer(player));
        var selLbl = new Label("Sel");
        selLbl.style.color = ColText;
        selLbl.style.fontSize = 9;
        selLbl.style.flexGrow = 1;
        selectBtn.Add(selLbl);
        selectBtn.style.width = 44;
        selectBtn.style.height = 18;
        selectBtn.style.backgroundColor = new Color(0.16f, 0.20f, 0.30f, 1f);
        selectBtn.style.borderTopLeftRadius = 3;
        selectBtn.style.borderTopRightRadius = 3;
        selectBtn.style.borderBottomLeftRadius = 3;
        selectBtn.style.borderBottomRightRadius = 3;
        selectBtn.style.borderLeftWidth = 0;
        selectBtn.style.borderRightWidth = 0;
        selectBtn.style.borderTopWidth = 0;
        selectBtn.style.borderBottomWidth = 0;
        selectBtn.style.paddingLeft = 2;
        selectBtn.style.paddingRight = 2;
        selectBtn.style.justifyContent = Justify.Center;
        selectBtn.style.alignItems = Align.Center;

        row.Add(nameLabel);
        row.Add(teamLabel);
        row.Add(roleLabel);
        row.Add(steamIdLabel);
        row.Add(selectBtn);

        return row;
    }

    private static string SafeNetString(object netVar)
    {
        if (netVar == null) return "N/A";
        var t = netVar.GetType();
        if (t.IsGenericType && t.GetGenericTypeDefinition().Name.StartsWith("NetworkVariable"))
        {
            var valProp = t.GetProperty("Value");
            if (valProp != null)
            {
                var v = valProp.GetValue(netVar);
                return v != null ? v.ToString() : "N/A";
            }
        }
        return netVar.ToString() ?? "N/A";
    }

    // ── ADMIN / SERVER CHECK ───────────────────────────────────

    private static bool IsInServer()
    {
        try
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null) return false;
            return nm.IsServer || nm.IsConnectedClient;
        }
        catch { return false; }
    }

    private static void CheckLocalPlayerAdmin()
    {
        localPlayerIsAdmin = false;
        try
        {
            if (!IsInServer()) return;
            var pm = PlayerManager.Instance;
            if (pm == null) return;
            var players = pm.GetPlayers();
            if (players == null) return;
            foreach (var p in players)
            {
                if (p != null && p.IsLocalPlayer)
                {
                    // Quick check: AdminLevel > 0 means admin
                    if (p.AdminLevel.Value > 0)
                    {
                        localPlayerIsAdmin = true;
                        return;
                    }
                    // Fallback: check Steam ID against admin list
                    string steamId = SafeNetString(p.SteamId);
                    var sm = ServerManager.Instance;
                    if (sm != null && sm.AdminManager != null)
                        localPlayerIsAdmin = sm.AdminManager.IsSteamIdAdmin(steamId);
                    break;
                }
            }
        }
        catch { localPlayerIsAdmin = false; }
    }

    // ── SELECTION ──────────────────────────────────────────────

    private static void SelectPlayer(Player player)
    {
        selectedPlayer = player;
        steamIdField.value = SafeNetString(player.SteamId);
        statusLabel.text = "Selected: " + SafeNetString(player.Username);
        statusLabel.style.color = ColGreen;
    }

    // ── CONFIRMATION ───────────────────────────────────────────

    private static void ConfirmAction(string actionName, Action confirmedAction)
    {
        if (selectedPlayer == null)
        {
            StatusWarn("No player selected");
            return;
        }

        string playerName = SafeNetString(selectedPlayer.Username);

        var overlay = new VisualElement();
        overlay.name = "AdminPanel_ConfirmOverlay";
        overlay.pickingMode = PickingMode.Position;
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0;
        overlay.style.top = 0;
        overlay.style.width = panel.style.width.value.value;
        overlay.style.height = 420;
        overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;

        var dialog = new VisualElement();
        dialog.style.width = 280;
        dialog.style.backgroundColor = new Color(0.13f, 0.13f, 0.16f, 1f);
        dialog.style.borderTopLeftRadius = 8;
        dialog.style.borderTopRightRadius = 8;
        dialog.style.borderBottomLeftRadius = 8;
        dialog.style.borderBottomRightRadius = 8;
        dialog.style.borderLeftWidth = 1;
        dialog.style.borderRightWidth = 1;
        dialog.style.borderTopWidth = 1;
        dialog.style.borderBottomWidth = 1;
        dialog.style.borderLeftColor = ColBorder;
        dialog.style.borderRightColor = ColBorder;
        dialog.style.borderTopColor = ColBorder;
        dialog.style.borderBottomColor = ColBorder;
        dialog.style.paddingLeft = 14;
        dialog.style.paddingRight = 14;
        dialog.style.paddingTop = 12;
        dialog.style.paddingBottom = 12;

        var confirmTitle = new Label("Confirm " + actionName);
        confirmTitle.style.color = ColText;
        confirmTitle.style.fontSize = 12;
        confirmTitle.style.marginBottom = 5;

        var confirmMsg = new Label("Are you sure you want to " + actionName + " \"" + playerName + "\"?");
        confirmMsg.style.color = ColTextDim;
        confirmMsg.style.fontSize = 11;
        confirmMsg.style.marginBottom = 12;
        confirmMsg.style.whiteSpace = WhiteSpace.Normal;

        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.justifyContent = Justify.FlexEnd;

        var cancelBtn = new Button(() => panel.Remove(overlay));
        var cancelLbl = new Label("Cancel");
        cancelLbl.style.color = ColText;
        cancelLbl.style.fontSize = 11;
        cancelLbl.style.flexGrow = 1;
        cancelBtn.Add(cancelLbl);
        cancelBtn.style.height = 24;
        cancelBtn.style.minWidth = 56;
        cancelBtn.style.marginRight = 5;
        cancelBtn.style.backgroundColor = new Color(0.18f, 0.18f, 0.22f, 1f);
        cancelBtn.style.borderTopLeftRadius = 4;
        cancelBtn.style.borderTopRightRadius = 4;
        cancelBtn.style.borderBottomLeftRadius = 4;
        cancelBtn.style.borderBottomRightRadius = 4;
        cancelBtn.style.justifyContent = Justify.Center;
        cancelBtn.style.alignItems = Align.Center;

        var okBtn = new Button(() =>
        {
            panel.Remove(overlay);
            confirmedAction();
        });
        var okText = char.ToUpper(actionName[0]) + actionName.Substring(1);
        var okLbl = new Label(okText);
        okLbl.style.color = Color.white;
        okLbl.style.fontSize = 11;
        okLbl.style.flexGrow = 1;
        okBtn.Add(okLbl);
        okBtn.style.height = 24;
        okBtn.style.minWidth = 56;
        okBtn.style.backgroundColor = actionName == "ban" ? ColRed : ColOrange;
        okBtn.style.borderTopLeftRadius = 4;
        okBtn.style.borderTopRightRadius = 4;
        okBtn.style.borderBottomLeftRadius = 4;
        okBtn.style.borderBottomRightRadius = 4;
        okBtn.style.justifyContent = Justify.Center;
        okBtn.style.alignItems = Align.Center;

        btnRow.Add(cancelBtn);
        btnRow.Add(okBtn);

        dialog.Add(confirmTitle);
        dialog.Add(confirmMsg);
        dialog.Add(btnRow);
        overlay.Add(dialog);
        panel.Add(overlay);
    }

    // ── STATUS HELPERS ─────────────────────────────────────────

    private static void StatusOk(string msg)
    {
        statusLabel.text = msg;
        statusLabel.style.color = ColGreen;
    }

    private static void StatusErr(string msg)
    {
        statusLabel.text = msg;
        statusLabel.style.color = ColRed;
    }

    private static void StatusWarn(string msg)
    {
        statusLabel.text = msg;
        statusLabel.style.color = ColOrange;
    }

    // ── ACTIONS ────────────────────────────────────────────────

    private static string GetReason()
    {
        return "Kicked by admin";
    }

    private static void DoKick()
    {
        if (selectedPlayer == null) return;
        try
        {
            ServerManager.Instance.Server_KickPlayer(selectedPlayer, DisconnectionCode.Kicked, GetReason(), true);
            StatusOk("Kicked " + SafeNetString(selectedPlayer.Username));
        }
        catch (Exception ex)
        {
            StatusErr("Kick failed: " + ex.Message);
        }
    }

    private static void DoBan()
    {
        if (selectedPlayer == null) return;
        string steamId = SafeNetString(selectedPlayer.SteamId);
        if (string.IsNullOrEmpty(steamId) || steamId == "N/A")
        {
            StatusErr("Could not read Steam ID");
            return;
        }
        try
        {
            BanManager.Instance.AddBannedSteamId(steamId);
            ServerManager.Instance.Server_KickPlayer(selectedPlayer, DisconnectionCode.Banned, GetReason(), true);
            StatusOk("Banned " + SafeNetString(selectedPlayer.Username));
        }
        catch (Exception ex)
        {
            StatusErr("Ban failed: " + ex.Message);
        }
    }

    private static Player FindPlayerBySteamId(string steamId)
    {
        foreach (var p in PlayerManager.Instance.GetPlayers())
            if (SafeNetString(p.SteamId) == steamId) return p;
        return null;
    }

    private static void DoKickBySteamId()
    {
        string steamId = steamIdField.value != null ? steamIdField.value.Trim() : "";
        if (string.IsNullOrEmpty(steamId)) { StatusWarn("Enter a Steam ID first"); return; }
        var player = FindPlayerBySteamId(steamId);
        if (player == null) { StatusWarn("No player found: " + steamId); return; }
        SelectPlayer(player);
        DoKick();
    }

    private static void DoBanBySteamId()
    {
        string steamId = steamIdField.value != null ? steamIdField.value.Trim() : "";
        if (string.IsNullOrEmpty(steamId)) { StatusWarn("Enter a Steam ID first"); return; }
        var player = FindPlayerBySteamId(steamId);
        if (player != null)
        {
            SelectPlayer(player);
            DoBan();
        }
        else
        {
            try
            {
                BanManager.Instance.AddBannedSteamId(steamId);
                StatusOk("Banned offline: " + steamId);
            }
            catch (Exception ex)
            {
                StatusErr("Ban failed: " + ex.Message);
            }
        }
    }
}

// ── HARMONY PATCHES ──────────────────────────────────────────

[HarmonyPatch(typeof(PlayerManager), "AddPlayer")]
static class AddPlayerPatch
{
    static void Postfix()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.StartCoroutine(HarmonyHelpers.RefreshNextFrame());
    }
}

[HarmonyPatch(typeof(PlayerManager), "RemovePlayer")]
static class RemovePlayerPatch
{
    static void Postfix()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.StartCoroutine(HarmonyHelpers.RefreshNextFrame());
    }
}

internal static class HarmonyHelpers
{
    internal static IEnumerator RefreshNextFrame()
    {
        yield return null;
        AdminPanel.RefreshPlayerList();
    }
}
