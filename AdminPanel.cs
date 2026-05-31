using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Puck Admin Panel Mod
// Draggable, minimisable, searchable. Press X to toggle.

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
    private static TextField commandField;
    private static Player selectedPlayer;
    private static bool panelVisible = false;
    private static bool minimized = false;
    private static float refreshTimer;
    private static string searchFilter = "";
    private static int selectedActionTab = 0; // 0=Players, 1=Commands

    private static bool isDragging = false;
    private static Vector2 dragOffset;
    private static bool localPlayerIsAdmin = false;
    private static readonly HashSet<ulong> pausedPlayers = new HashSet<ulong>();

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
    private static readonly Color ColCyan = new Color(0.18f, 0.70f, 0.80f, 1f);
    private static readonly Color ColPurple = new Color(0.60f, 0.30f, 0.90f, 1f);
    private static readonly Color ColYellow = new Color(0.90f, 0.85f, 0.20f, 1f);
    private static readonly Color ColTabActive = new Color(0.18f, 0.35f, 0.65f, 1f);
    private static readonly Color ColTabInactive = new Color(0.12f, 0.12f, 0.15f, 1f);
    private static readonly Color ColCmdBg = new Color(0.08f, 0.08f, 0.10f, 1f);

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

        if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame && !IsTyping())
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

        // ── TAB BAR ──
        var tabBar = new VisualElement();
        tabBar.style.flexDirection = FlexDirection.Row;
        tabBar.style.height = 28;
        tabBar.style.marginTop = 4;
        tabBar.style.marginBottom = 2;

        var playersTab = MakeTab("PLAYERS", 0, tabBar);
        var commandsTab = MakeTab("COMMANDS", 1, tabBar);
        bodyContainer.Add(tabBar);

        // ── PLAYERS TAB CONTENT ──
        var playersContent = new VisualElement();
        playersContent.name = "PlayersContent";

        // Toolbar
        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.height = 34;
        toolbar.style.alignItems = Align.Center;
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
        playersContent.Add(toolbar);

        // Status
        statusLabel = new Label("No players connected");
        statusLabel.style.height = 18;
        statusLabel.style.color = ColTextDim;
        statusLabel.style.fontSize = 10;
        statusLabel.style.paddingLeft = 4;
        statusLabel.style.paddingTop = 2;
        statusLabel.style.marginBottom = 2;
        playersContent.Add(statusLabel);

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
        colHeader.Add(MakeHeaderLabel("PLAYER", 160));
        colHeader.Add(MakeHeaderLabel("TEAM", 60));
        colHeader.Add(MakeHeaderLabel("ROLE", 55));
        colHeader.Add(MakeHeaderLabel("STATUS", 48));
        colHeader.Add(MakeHeaderLabel("STEAM ID", 130));
        colHeader.Add(MakeHeaderLabel("", 90));
        scrollView.Add(colHeader);

        playerListContainer = new VisualElement();
        scrollView.Add(playerListContainer);
        playersContent.Add(scrollView);

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
        playersContent.Add(bottomBar);
        bodyContainer.Add(playersContent);

        // ── COMMANDS TAB CONTENT ──
        var commandsContent = new VisualElement();
        commandsContent.name = "CommandsContent";
        commandsContent.style.display = DisplayStyle.None;

        // Command input
        var cmdInputRow = new VisualElement();
        cmdInputRow.style.flexDirection = FlexDirection.Row;
        cmdInputRow.style.height = 30;
        cmdInputRow.style.alignItems = Align.Center;
        cmdInputRow.style.marginBottom = 6;

        var cmdPrefix = new Label(">");
        cmdPrefix.style.color = ColCyan;
        cmdPrefix.style.fontSize = 12;
        cmdPrefix.style.width = 14;

        commandField = new TextField();
        commandField.style.flexGrow = 1;
        commandField.style.height = 26;
        commandField.style.backgroundColor = ColCmdBg;
        commandField.style.color = ColText;
        commandField.style.fontSize = 11;
        commandField.style.borderTopLeftRadius = 4;
        commandField.style.borderTopRightRadius = 4;
        commandField.style.borderBottomLeftRadius = 4;
        commandField.style.borderBottomRightRadius = 4;
        commandField.style.paddingLeft = 6;
        commandField.style.borderLeftWidth = 0;
        commandField.style.borderRightWidth = 0;
        commandField.style.borderTopWidth = 0;
        commandField.style.borderBottomWidth = 0;
        commandField.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                ExecuteCommand(commandField.value);
                commandField.value = "";
                evt.StopPropagation();
            }
        });

        var cmdRunBtn = new Button(() => { ExecuteCommand(commandField.value); commandField.value = ""; });
        var cmdRunLbl = new Label("Run");
        cmdRunLbl.style.color = Color.white;
        cmdRunLbl.style.fontSize = 10;
        cmdRunLbl.style.flexGrow = 1;
        cmdRunBtn.Add(cmdRunLbl);
        cmdRunBtn.style.height = 26;
        cmdRunBtn.style.minWidth = 40;
        cmdRunBtn.style.paddingLeft = 8;
        cmdRunBtn.style.paddingRight = 8;
        cmdRunBtn.style.marginLeft = 4;
        cmdRunBtn.style.backgroundColor = ColCyan;
        cmdRunBtn.style.borderTopLeftRadius = 4;
        cmdRunBtn.style.borderTopRightRadius = 4;
        cmdRunBtn.style.borderBottomLeftRadius = 4;
        cmdRunBtn.style.borderBottomRightRadius = 4;
        cmdRunBtn.style.justifyContent = Justify.Center;
        cmdRunBtn.style.alignItems = Align.Center;

        cmdInputRow.Add(cmdPrefix);
        cmdInputRow.Add(commandField);
        cmdInputRow.Add(cmdRunBtn);
        commandsContent.Add(cmdInputRow);

        // Quick-action buttons grid
        var cmdScroll = new ScrollView(ScrollViewMode.Vertical);
        cmdScroll.style.minHeight = 220;
        cmdScroll.style.maxHeight = 560;
        cmdScroll.style.backgroundColor = new Color(0.07f, 0.07f, 0.09f, 1f);
        cmdScroll.style.borderTopLeftRadius = 5;
        cmdScroll.style.borderTopRightRadius = 5;
        cmdScroll.style.borderBottomLeftRadius = 5;
        cmdScroll.style.borderBottomRightRadius = 5;
        cmdScroll.style.paddingTop = 4;
        cmdScroll.style.paddingBottom = 4;
        cmdScroll.style.paddingLeft = 4;
        cmdScroll.style.paddingRight = 4;
        cmdScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

        var cmdGrid = new VisualElement();

        // Game Flow section
        cmdGrid.Add(MakeSectionLabel("GAME FLOW"));
        var flowRow1 = new VisualElement(); flowRow1.style.flexDirection = FlexDirection.Row; flowRow1.style.marginBottom = 3;
        flowRow1.Add(MakeCmdButton("Warmup", ColBlueTeam, () => ExecuteCommand("/warmup")));
        flowRow1.Add(MakeCmdButton("Start", ColGreen, () => ExecuteCommand("/start")));
        flowRow1.Add(MakeCmdButton("Pause", ColOrange, () => ExecuteCommand("/pause")));
        flowRow1.Add(MakeCmdButton("Resume", ColGreen, () => ExecuteCommand("/resume")));
        cmdGrid.Add(flowRow1);
        var flowRow2 = new VisualElement(); flowRow2.style.flexDirection = FlexDirection.Row; flowRow2.style.marginBottom = 3;
        flowRow2.Add(MakeCmdButton("Pause All", ColRed, () => ExecuteCommand("/pauseall")));
        flowRow2.Add(MakeCmdButton("Resume All", ColGreen, () => ExecuteCommand("/resumeall")));
        flowRow2.Add(MakeCmdButton("Freeze All", ColCyan, () => ExecuteCommand("/freezeall")));
        flowRow2.Add(MakeCmdButton("Unfreeze All", ColGreen, () => ExecuteCommand("/unfreezeall")));
        cmdGrid.Add(flowRow2);

        // Player Actions section
        cmdGrid.Add(MakeSectionLabel("PLAYER ACTIONS (uses selected player)"));
        var actionRow1 = new VisualElement(); actionRow1.style.flexDirection = FlexDirection.Row; actionRow1.style.marginBottom = 3;
        actionRow1.Add(MakeCmdButton("Freeze", ColCyan, () => ExecuteCommand("/freeze")));
        actionRow1.Add(MakeCmdButton("Unfreeze", ColGreen, () => ExecuteCommand("/unfreeze")));
        actionRow1.Add(MakeCmdButton("Slap", ColYellow, () => ExecuteCommand("/slap")));
        actionRow1.Add(MakeCmdButton("Jump", ColPurple, () => ExecuteCommand("/jump")));
        cmdGrid.Add(actionRow1);
        var actionRow2 = new VisualElement(); actionRow2.style.flexDirection = FlexDirection.Row; actionRow2.style.marginBottom = 3;
        actionRow2.Add(MakeCmdButton("Mute", ColOrange, () => ExecuteCommand("/mute")));
        actionRow2.Add(MakeCmdButton("Unmute", ColGreen, () => ExecuteCommand("/unmute")));
        actionRow2.Add(MakeCmdButton("Kick", ColRed, () => ConfirmAction("kick", DoKick)));
        actionRow2.Add(MakeCmdButton("Ban", ColRed, () => ConfirmAction("ban", DoBan)));
        cmdGrid.Add(actionRow2);

        // Team Management section
        cmdGrid.Add(MakeSectionLabel("TEAM MANAGEMENT"));
        var teamRow1 = new VisualElement(); teamRow1.style.flexDirection = FlexDirection.Row; teamRow1.style.marginBottom = 3;
        teamRow1.Add(MakeCmdButton("To Blue", ColBlueTeam, () => ExecuteCommand("/changeteam blue")));
        teamRow1.Add(MakeCmdButton("To Red", ColRedTeam, () => ExecuteCommand("/changeteam red")));
        teamRow1.Add(MakeCmdButton("To Spec", ColTextDim, () => ExecuteCommand("/changeteam spectator")));
        teamRow1.Add(MakeCmdButton("Swap Teams", ColPurple, () => ExecuteCommand("/swap")));
        cmdGrid.Add(teamRow1);

        // Game State section
        cmdGrid.Add(MakeSectionLabel("GAME STATE"));
        var stateRow1 = new VisualElement(); stateRow1.style.flexDirection = FlexDirection.Row; stateRow1.style.marginBottom = 3;
        stateRow1.Add(MakeCmdButton("P1", ColAccent, () => ExecuteCommand("/setstate 1")));
        stateRow1.Add(MakeCmdButton("P2", ColAccent, () => ExecuteCommand("/setstate 2")));
        stateRow1.Add(MakeCmdButton("P3", ColAccent, () => ExecuteCommand("/setstate 3")));
        stateRow1.Add(MakeCmdButton("OT", ColOrange, () => ExecuteCommand("/setstate 4")));
        cmdGrid.Add(stateRow1);
        var stateRow2 = new VisualElement(); stateRow2.style.flexDirection = FlexDirection.Row; stateRow2.style.marginBottom = 3;
        stateRow2.Add(MakeCmdButton("Blue +1", ColBlueTeam, () => ExecuteCommand("/setgoals blue +1")));
        stateRow2.Add(MakeCmdButton("Blue -1", ColBlueTeam, () => ExecuteCommand("/setgoals blue -1")));
        stateRow2.Add(MakeCmdButton("Red +1", ColRedTeam, () => ExecuteCommand("/setgoals red +1")));
        stateRow2.Add(MakeCmdButton("Red -1", ColRedTeam, () => ExecuteCommand("/setgoals red -1")));
        cmdGrid.Add(stateRow2);

        // Info section
        cmdGrid.Add(MakeSectionLabel("INFO"));
        var infoRow1 = new VisualElement(); infoRow1.style.flexDirection = FlexDirection.Row; infoRow1.style.marginBottom = 3;
        infoRow1.Add(MakeCmdButton("Who Am I", ColAccent, () => ExecuteCommand("/whoami")));
        infoRow1.Add(MakeCmdButton("Muted List", ColOrange, () => ExecuteCommand("/muted")));
        infoRow1.Add(MakeCmdButton("Freeze Puck", ColCyan, () => ExecuteCommand("/freeze puck")));
        infoRow1.Add(MakeCmdButton("Unfreeze Puck", ColGreen, () => ExecuteCommand("/unfreeze puck")));
        cmdGrid.Add(infoRow1);

        // Help text
        var helpLabel = new Label(
            "Commands: /warmup [s] /start /pause /resume /mute <p> [dur] /unmute <p> /muted /whoami [p] " +
            "/changeteam <p> <team> /swap <p1> <p2> /freeze <p|puck> /unfreeze <p|puck> /freezeall /unfreezeall " +
            "/pauseall /resumeall /kick <p> /kicksteamid <id> /slap <p> /jump <p> /settime <s> /setgoals <team> <n> /setstate <n>"
        );
        helpLabel.style.color = ColTextMuted;
        helpLabel.style.fontSize = 9;
        helpLabel.style.whiteSpace = WhiteSpace.Normal;
        helpLabel.style.marginTop = 6;
        cmdGrid.Add(helpLabel);

        cmdScroll.Add(cmdGrid);
        commandsContent.Add(cmdScroll);
        bodyContainer.Add(commandsContent);

        panel.Add(bodyContainer);
        uiRoot.Add(panel);
    }

    private static Button MakeTab(string label, int tabIndex, VisualElement tabBar)
    {
        var btn = new Button(() => SelectTab(tabIndex));
        var lbl = new Label(label);
        lbl.style.color = selectedActionTab == tabIndex ? ColText : ColTextDim;
        lbl.style.fontSize = 10;
        lbl.style.letterSpacing = 1;
        lbl.style.flexGrow = 1;
        btn.Add(lbl);
        btn.style.height = 24;
        btn.style.minWidth = 80;
        btn.style.paddingLeft = 8;
        btn.style.paddingRight = 8;
        btn.style.marginRight = 2;
        btn.style.backgroundColor = selectedActionTab == tabIndex ? ColTabActive : ColTabInactive;
        btn.style.borderTopLeftRadius = 4;
        btn.style.borderTopRightRadius = 4;
        btn.style.borderBottomLeftRadius = 0;
        btn.style.borderBottomRightRadius = 0;
        btn.style.borderLeftWidth = 0;
        btn.style.borderRightWidth = 0;
        btn.style.borderTopWidth = 0;
        btn.style.borderBottomWidth = 0;
        btn.style.justifyContent = Justify.Center;
        btn.style.alignItems = Align.Center;
        btn.name = "Tab_" + tabIndex;
        tabBar.Add(btn);
        return btn;
    }

    private static void SelectTab(int index)
    {
        selectedActionTab = index;
        var playersContent = bodyContainer.Q<VisualElement>("PlayersContent");
        var commandsContent = bodyContainer.Q<VisualElement>("CommandsContent");
        if (playersContent != null) playersContent.style.display = index == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        if (commandsContent != null) commandsContent.style.display = index == 1 ? DisplayStyle.Flex : DisplayStyle.None;

        // Update tab button styles
        for (int i = 0; i < 2; i++)
        {
            var tabBtn = bodyContainer.Q<Button>("Tab_" + i);
            if (tabBtn != null)
            {
                tabBtn.style.backgroundColor = i == index ? ColTabActive : ColTabInactive;
                var tabLbl = tabBtn.Q<Label>();
                if (tabLbl != null) tabLbl.style.color = i == index ? ColText : ColTextDim;
            }
        }
    }

    private static Label MakeSectionLabel(string text)
    {
        var l = new Label(text);
        l.style.color = ColTextMuted;
        l.style.fontSize = 9;
        l.style.letterSpacing = 1;
        l.style.marginTop = 6;
        l.style.marginBottom = 3;
        l.style.paddingLeft = 2;
        return l;
    }

    private static Button MakeCmdButton(string text, Color bg, Action action)
    {
        var lbl = new Label(text);
        lbl.style.color = Color.white;
        lbl.style.fontSize = 10;
        lbl.style.flexGrow = 1;

        var btn = new Button(action);
        btn.Add(lbl);
        btn.style.height = 24;
        btn.style.flexGrow = 1;
        btn.style.paddingLeft = 4;
        btn.style.paddingRight = 4;
        btn.style.marginLeft = 1;
        btn.style.marginRight = 1;
        btn.style.backgroundColor = bg;
        btn.style.borderTopLeftRadius = 3;
        btn.style.borderTopRightRadius = 3;
        btn.style.borderBottomLeftRadius = 3;
        btn.style.borderBottomRightRadius = 3;
        btn.style.borderLeftWidth = 0;
        btn.style.borderRightWidth = 0;
        btn.style.borderTopWidth = 0;
        btn.style.borderBottomWidth = 0;
        btn.style.justifyContent = Justify.Center;
        btn.style.alignItems = Align.Center;
        return btn;
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
        bool isMuted = player.IsMuted.Value;
        bool isAdmin = player.AdminLevel.Value > 0;

        var nameLabel = new Label((isAdmin ? "★ " : "") + playerName);
        nameLabel.style.width = 160;
        nameLabel.style.fontSize = 11;
        nameLabel.style.color = isAdmin ? ColYellow : ColText;
        nameLabel.style.overflow = Overflow.Hidden;

        var teamLabel = new Label(gs.Team.ToString());
        teamLabel.style.width = 60;
        teamLabel.style.fontSize = 10;
        switch (gs.Team)
        {
            case PlayerTeam.Blue: teamLabel.style.color = ColBlueTeam; break;
            case PlayerTeam.Red: teamLabel.style.color = ColRedTeam; break;
            default: teamLabel.style.color = ColTextDim; break;
        }

        var roleLabel = new Label(gs.Role.ToString());
        roleLabel.style.width = 55;
        roleLabel.style.fontSize = 10;
        roleLabel.style.color = ColTextDim;

        // Status indicators: Muted, Frozen
        string status = "";
        Color statusCol = ColTextDim;
        if (isMuted) { status = "MUTED"; statusCol = ColOrange; }
        var statusLabel = new Label(status);
        statusLabel.style.width = 48;
        statusLabel.style.fontSize = 9;
        statusLabel.style.color = statusCol;

        var steamIdLabel = new Label(steamIdStr);
        steamIdLabel.style.width = 130;
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
        row.Add(statusLabel);
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

    // ── CHAT HELPERS ────────────────────────────────────────────

    /// <summary>Send an admin chat message to all players (Color overload).</summary>
    private static void ChatAuto(string message, Color color)
    {
        ChatAuto(message, ColToHex(color));
    }

    /// <summary>Send an admin chat message to all players (hex string overload).</summary>
    private static void ChatAuto(string message, string hexColor)
    {
        var players = PlayerManager.Instance.GetPlayers();
        ulong[] allIds = new ulong[players.Count];
        for (int i = 0; i < players.Count; i++)
            allIds[i] = players[i].OwnerClientId;
        ChatManager.Instance.Server_SendChatMessage(message, hexColor, allIds);
    }

    // ── PLAYER LOOKUP ───────────────────────────────────────────

    private static Player FindPlayerByNeedle(string needle)
    {
        if (string.IsNullOrEmpty(needle)) return null;
        var players = PlayerManager.Instance.GetPlayers();
        needle = needle.ToLowerInvariant();
        // Exact Steam ID match
        foreach (var p in players)
            if (SafeNetString(p.SteamId).ToLowerInvariant() == needle) return p;
        // Exact name match
        foreach (var p in players)
            if (SafeNetString(p.Username).ToLowerInvariant() == needle) return p;
        // Partial name match
        foreach (var p in players)
            if (SafeNetString(p.Username).ToLowerInvariant().Contains(needle)) return p;
        // Partial Steam ID match
        foreach (var p in players)
            if (SafeNetString(p.SteamId).ToLowerInvariant().Contains(needle)) return p;
        return null;
    }

    private static Player FindPlayerBySteamId(string steamId)
    {
        return FindPlayerByNeedle(steamId);
    }

    // ── COMMAND EXECUTOR ────────────────────────────────────────

    private static void ExecuteCommand(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput)) return;
        string input = rawInput.Trim();
        if (!input.StartsWith("/")) input = "/" + input;

        string[] parts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        string cmd = parts[0].ToLowerInvariant();
        string[] args = parts.Skip(1).ToArray();

        try
        {
            switch (cmd)
            {
                case "/warmup": CmdWarmup(args); break;
                case "/start": CmdStart(args); break;
                case "/pause": CmdPause(args); break;
                case "/resume": CmdResume(args); break;
                case "/mute": CmdMute(args); break;
                case "/unmute": CmdUnmute(args); break;
                case "/muted": CmdMuted(args); break;
                case "/whoami": CmdWhoAmI(args); break;
                case "/changeteam": CmdChangeTeam(args); break;
                case "/swap": CmdSwap(args); break;
                case "/freeze": CmdFreeze(args); break;
                case "/unfreeze": CmdUnfreeze(args); break;
                case "/freezeall": CmdFreezeAll(args); break;
                case "/unfreezeall": CmdUnfreezeAll(args); break;
                case "/pauseall": CmdPauseAll(args); break;
                case "/resumeall": CmdResumeAll(args); break;
                case "/kick": CmdKick(args); break;
                case "/kicksteamid": CmdKickSteamId(args); break;
                case "/slap": CmdSlap(args); break;
                case "/jump": CmdJump(args); break;
                case "/settime": CmdSetTime(args); break;
                case "/setgoals": CmdSetGoals(args); break;
                case "/setstate": CmdSetState(args); break;
                default:
                    StatusErr("Unknown: " + cmd);
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusErr("Cmd error: " + ex.Message);
        }
    }

    // ── GAME FLOW ───────────────────────────────────────────────

    private static void CmdWarmup(string[] args)
    {
        float seconds = 60f;
        if (args.Length > 0 && !float.TryParse(args[0], out seconds)) seconds = 60f;
        var gm = GameManager.Instance;
        ChatAuto($"Warmup started ({seconds}s)", ColYellow);
        gm.Server_SetGameState(phase: GamePhase.Warmup, tick: (int)seconds, period: gm.GameState.Value.Period,
            blueScore: gm.GameState.Value.BlueScore, redScore: gm.GameState.Value.RedScore, isOvertime: gm.GameState.Value.IsOvertime);
        gm.Server_StartTicking();
        StatusOk($"Warmup: {seconds}s");
    }

    private static void CmdStart(string[] args)
    {
        ChatAuto("Game started!", ColGreen);
        GameManager.Instance.Server_SetGameState(phase: GamePhase.Play, tick: 300, period: GameManager.Instance.GameState.Value.Period,
            blueScore: 0, redScore: 0, isOvertime: false);
        GameManager.Instance.Server_StartTicking();
        StatusOk("Game started");
    }

    private static void CmdPause(string[] args)
    {
        var gm = GameManager.Instance;
        var gs = gm.GameState.Value;
        gm.Server_SetGameState(phase: GamePhase.Warmup, tick: gs.Tick, period: gs.Period,
            blueScore: gs.BlueScore, redScore: gs.RedScore, isOvertime: false);
        gm.Server_StopTicking();
        ChatAuto("Game paused", ColOrange);
        StatusOk("Paused");
    }

    private static void CmdResume(string[] args)
    {
        var gm = GameManager.Instance;
        var gs = gm.GameState.Value;
        gm.Server_SetGameState(phase: GamePhase.Play, tick: gs.Tick, period: gs.Period,
            blueScore: gs.BlueScore, redScore: gs.RedScore, isOvertime: gs.IsOvertime);
        gm.Server_StartTicking();
        ChatAuto("Game resumed", ColGreen);
        StatusOk("Resumed");
    }

    private static void CmdPauseAll(string[] args)
    {
        var players = PlayerManager.Instance.GetPlayers();
        foreach (var p in players)
        {
            if (p.IsLocalPlayer) continue;
            try
            {
                var bodyComp = GetPlayerBodyComponent(p);
                if (bodyComp != null)
                {
                    bodyComp.Server_Freeze(RigidbodyConstraints.FreezeAll);
                    pausedPlayers.Add(p.OwnerClientId);
                }
            }
            catch { }
        }
        CmdPause(args);
        StatusOk("Paused & froze all");
    }

    private static void CmdResumeAll(string[] args)
    {
        var players = PlayerManager.Instance.GetPlayers();
        foreach (var p in players)
        {
            try
            {
                var bodyComp = GetPlayerBodyComponent(p);
                if (bodyComp != null) bodyComp.Server_Unfreeze();
            }
            catch { }
        }
        pausedPlayers.Clear();
        CmdResume(args);
        StatusOk("Resumed & unfroze all");
    }

    // ── MUTE SYSTEM ─────────────────────────────────────────────

    private static void CmdMute(string[] args)
    {
        if (args.Length < 1) { StatusWarn("Usage: /mute <player> [duration]"); return; }
        string needle = args[0];
        string duration = args.Length > 1 ? args[1] : "permanent";
        var player = FindPlayerByNeedle(needle);
        if (player == null) { StatusErr("Player not found: " + needle); return; }
        player.IsMuted.Value = true;
        ChatAuto(
            SafeNetString(player.Username) + " muted (" + duration + ")", ColToHex(ColOrange));
        StatusOk("Muted " + SafeNetString(player.Username));
    }

    private static void CmdUnmute(string[] args)
    {
        if (args.Length < 1) { StatusWarn("Usage: /unmute <player>"); return; }
        var player = FindPlayerByNeedle(args[0]);
        if (player == null) { StatusErr("Player not found: " + args[0]); return; }
        player.IsMuted.Value = false;
        ChatAuto(
            SafeNetString(player.Username) + " unmuted", ColToHex(ColGreen));
        StatusOk("Unmuted " + SafeNetString(player.Username));
    }

    private static void CmdMuted(string[] args)
    {
        var players = PlayerManager.Instance.GetPlayers();
        var muted = players.Where(p => p.IsMuted.Value).ToList();
        if (muted.Count == 0) { StatusOk("No muted players"); return; }
        string list = string.Join(", ", muted.Select(p => SafeNetString(p.Username)));
        StatusOk("Muted: " + list);
        ChatAuto(
            "Muted players: " + list, ColToHex(ColOrange));
    }

    // ── WHOAMI ──────────────────────────────────────────────────

    private static void CmdWhoAmI(string[] args)
    {
        Player target = null;
        if (args.Length > 0)
        {
            target = FindPlayerByNeedle(args[0]);
        }
        else if (selectedPlayer != null)
        {
            target = selectedPlayer;
        }
        else
        {
            // Use local player
            var local = PlayerManager.Instance.GetPlayers().FirstOrDefault(p => p.IsLocalPlayer);
            target = local;
        }
        if (target == null) { StatusErr("No player found"); return; }
        var gs = target.GameState.Value;
        string info = $"User: {SafeNetString(target.Username)} | Steam: {SafeNetString(target.SteamId)} | Team: {gs.Team} | Role: {gs.Role} | AdminLvl: {target.AdminLevel.Value} | Muted: {target.IsMuted.Value}";
        StatusOk(info);
        ChatAuto(info, ColToHex(ColCyan));
    }

    // ── TEAM MANAGEMENT ─────────────────────────────────────────

    private static void CmdChangeTeam(string[] args)
    {
        if (args.Length < 2) { StatusWarn("Usage: /changeteam <player> <blue|red|spectator>"); return; }
        var player = FindPlayerByNeedle(args[0]);
        if (player == null) { StatusErr("Player not found: " + args[0]); return; }
        PlayerTeam team = ParseTeam(args[1]);
        if (team == PlayerTeam.None) { StatusErr("Invalid team: " + args[1]); return; }
        player.Server_SetGameState(phase: null, team: team, role: null);
        ChatAuto(
            SafeNetString(player.Username) + " → " + team, ColToHex(ColAccent));
        StatusOk("Team changed");
    }

    private static void CmdSwap(string[] args)
    {
        if (args.Length < 2) { StatusWarn("Usage: /swap <player1> <player2>"); return; }
        var p1 = FindPlayerByNeedle(args[0]);
        var p2 = FindPlayerByNeedle(args[1]);
        if (p1 == null) { StatusErr("Player not found: " + args[0]); return; }
        if (p2 == null) { StatusErr("Player not found: " + args[1]); return; }
        PlayerTeam t1 = p1.GameState.Value.Team;
        PlayerTeam t2 = p2.GameState.Value.Team;
        p1.Server_SetGameState(phase: null, team: t2, role: null);
        p2.Server_SetGameState(phase: null, team: t1, role: null);
        ChatAuto(
            $"Swapped {SafeNetString(p1.Username)} ↔ {SafeNetString(p2.Username)}", ColToHex(ColPurple));
        StatusOk("Teams swapped");
    }

    private static PlayerTeam ParseTeam(string s)
    {
        s = s.ToLowerInvariant();
        if (s == "blue" || s == "b") return PlayerTeam.Blue;
        if (s == "red" || s == "r") return PlayerTeam.Red;
        if (s == "spectator" || s == "spec" || s == "s") return PlayerTeam.Spectator;
        return PlayerTeam.None;
    }

    // ── FREEZE / UNFREEZE ───────────────────────────────────────

    /// <summary>
    /// Get the PlayerBody component for a player. Tries the private field first, then GetComponentInChildren.
    /// Returns the PlayerBody (not the Rigidbody) so we can call Server_Freeze/Server_Unfreeze on it.
    /// </summary>
    private static global::PlayerBody GetPlayerBodyComponent(Player player)
    {
        try
        {
            // The Player class has a private field that holds the PlayerBody
            var playerType = player.GetType();
            var bodyField = playerType.GetField("playerBody", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (bodyField == null)
                bodyField = playerType.GetField("PlayerBody", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (bodyField != null)
            {
                var body = bodyField.GetValue(player) as global::PlayerBody;
                if (body != null) return body;
            }

            // Alternative: find PlayerBody in children
            var bodies = player.GetComponentsInChildren<global::PlayerBody>();
            if (bodies.Length > 0) return bodies[0];
        }
        catch { }
        return null;
    }

    /// <summary>Get a player's Rigidbody for physics actions (slap, jump).</summary>
    private static Rigidbody GetPlayerRigidbody(Player player)
    {
        var bodyComp = GetPlayerBodyComponent(player);
        if (bodyComp != null)
        {
            try { return bodyComp.Rigidbody; }
            catch { }
        }
        return null;
    }

    private static void CmdFreeze(string[] args)
    {
        if (args.Length > 0 && args[0].ToLowerInvariant() == "puck")
        {
            CmdFreezePuck();
            return;
        }
        Player target = GetTargetOrSelected(args);
        if (target == null) { StatusWarn("Select a player or specify one"); return; }
        var bodyComp = GetPlayerBodyComponent(target);
        if (bodyComp != null)
        {
            bodyComp.Server_Freeze(RigidbodyConstraints.FreezeAll);
            ChatAuto(
                SafeNetString(target.Username) + " frozen", ColToHex(ColCyan));
            StatusOk("Frozen " + SafeNetString(target.Username));
        }
        else
        {
            StatusErr("No body found for " + SafeNetString(target.Username));
        }
    }

    private static void CmdUnfreeze(string[] args)
    {
        if (args.Length > 0 && args[0].ToLowerInvariant() == "puck")
        {
            CmdUnfreezePuck();
            return;
        }
        Player target = GetTargetOrSelected(args);
        if (target == null) { StatusWarn("Select a player or specify one"); return; }
        var bodyComp = GetPlayerBodyComponent(target);
        if (bodyComp != null)
        {
            bodyComp.Server_Unfreeze();
            ChatAuto(
                SafeNetString(target.Username) + " unfrozen", ColToHex(ColGreen));
            StatusOk("Unfrozen " + SafeNetString(target.Username));
        }
        else
        {
            StatusErr("No body found for " + SafeNetString(target.Username));
        }
    }

    private static void CmdFreezePuck()
    {
        // Find puck in scene
        var puck = UnityEngine.Object.FindFirstObjectByType<Puck>();
        if (puck != null)
        {
            puck.Rigidbody.constraints = RigidbodyConstraints.FreezeAll;
            ChatAuto("Puck frozen", ColToHex(ColCyan));
            StatusOk("Puck frozen");
        }
        else
        {
            StatusErr("Puck not found in scene");
        }
    }

    private static void CmdUnfreezePuck()
    {
        var puck = UnityEngine.Object.FindFirstObjectByType<Puck>();
        if (puck != null)
        {
            puck.Rigidbody.constraints = RigidbodyConstraints.None;
            ChatAuto("Puck unfrozen", ColToHex(ColGreen));
            StatusOk("Puck unfrozen");
        }
        else
        {
            StatusErr("Puck not found in scene");
        }
    }

    private static void CmdFreezeAll(string[] args)
    {
        var players = PlayerManager.Instance.GetPlayers();
        int count = 0;
        foreach (var p in players)
        {
            var bodyComp = GetPlayerBodyComponent(p);
            if (bodyComp != null)
            {
                bodyComp.Server_Freeze(RigidbodyConstraints.FreezeAll);
                count++;
            }
        }
        // Also freeze puck
        try
        {
            var puck = UnityEngine.Object.FindFirstObjectByType<Puck>();
            if (puck != null) puck.Rigidbody.constraints = RigidbodyConstraints.FreezeAll;
        }
        catch { }
        ChatAuto("All players frozen (" + count + ")", ColToHex(ColCyan));
        StatusOk("Froze all (" + count + " players)");
    }

    private static void CmdUnfreezeAll(string[] args)
    {
        var players = PlayerManager.Instance.GetPlayers();
        int count = 0;
        foreach (var p in players)
        {
            var bodyComp = GetPlayerBodyComponent(p);
            if (bodyComp != null)
            {
                bodyComp.Server_Unfreeze();
                count++;
            }
        }
        // Also unfreeze puck
        try
        {
            var puck = UnityEngine.Object.FindFirstObjectByType<Puck>();
            if (puck != null) puck.Rigidbody.constraints = RigidbodyConstraints.None;
        }
        catch { }
        ChatAuto("All players unfrozen (" + count + ")", ColToHex(ColGreen));
        StatusOk("Unfroze all (" + count + " players)");
    }

    // ── KICK / BAN ──────────────────────────────────────────────

    private static void CmdKick(string[] args)
    {
        Player target = GetTargetOrSelected(args);
        if (target == null) { StatusWarn("Select a player or specify one"); return; }
        try
        {
            ServerManager.Instance.Server_KickPlayer(target, DisconnectionCode.Kicked, GetReason());
            StatusOk("Kicked " + SafeNetString(target.Username));
        }
        catch (Exception ex) { StatusErr("Kick failed: " + ex.Message); }
    }

    private static void CmdKickSteamId(string[] args)
    {
        if (args.Length < 1) { StatusWarn("Usage: /kicksteamid <steamid>"); return; }
        string steamId = args[0];
        var player = FindPlayerBySteamId(steamId);
        if (player != null)
        {
            SelectPlayer(player);
            CmdKick(new string[0]);
        }
        else
        {
            StatusWarn("Player not online: " + steamId);
        }
    }

    private static string GetReason()
    {
        return "Kicked by admin";
    }

    private static void DoKick()
    {
        ConfirmAction("kick", () =>
        {
            if (selectedPlayer == null) return;
            try
            {
                ServerManager.Instance.Server_KickPlayer(selectedPlayer, DisconnectionCode.Kicked, GetReason());
                StatusOk("Kicked " + SafeNetString(selectedPlayer.Username));
            }
            catch (Exception ex) { StatusErr("Kick failed: " + ex.Message); }
        });
    }

    private static void DoBan()
    {
        ConfirmAction("ban", () =>
        {
            if (selectedPlayer == null) return;
            string steamId = SafeNetString(selectedPlayer.SteamId);
            if (string.IsNullOrEmpty(steamId) || steamId == "N/A") { StatusErr("Could not read Steam ID"); return; }
            try
            {
                BanManager.Instance.AddBannedSteamId(steamId);
                ServerManager.Instance.Server_KickPlayer(selectedPlayer, DisconnectionCode.Banned, "Banned by admin");
                StatusOk("Banned " + SafeNetString(selectedPlayer.Username));
            }
            catch (Exception ex) { StatusErr("Ban failed: " + ex.Message); }
        });
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
            catch (Exception ex) { StatusErr("Ban failed: " + ex.Message); }
        }
    }

    // ── PHYSICS ACTIONS ─────────────────────────────────────────

    private static System.Random _rng = new System.Random();

    private static void CmdSlap(string[] args)
    {
        Player target = GetTargetOrSelected(args);
        if (target == null) { StatusWarn("Select a player or specify one"); return; }
        var rb = GetPlayerRigidbody(target);
        if (rb != null)
        {
            Vector3 force = new Vector3(
                (float)(_rng.NextDouble() * 2 - 1) * 15f,
                (float)(_rng.NextDouble()) * 10f + 5f,
                (float)(_rng.NextDouble() * 2 - 1) * 15f
            );
            rb.AddForce(force, ForceMode.Impulse);
            ChatAuto(
                "Slapped " + SafeNetString(target.Username) + "!", ColToHex(ColYellow));
            StatusOk("Slapped " + SafeNetString(target.Username));
        }
        else
        {
            StatusErr("No body found for " + SafeNetString(target.Username));
        }
    }

    private static void CmdJump(string[] args)
    {
        Player target = GetTargetOrSelected(args);
        if (target == null) { StatusWarn("Select a player or specify one"); return; }
        var rb = GetPlayerRigidbody(target);
        if (rb != null)
        {
            rb.AddForce(Vector3.up * 12f, ForceMode.Impulse);
            StatusOk("Jumped " + SafeNetString(target.Username));
        }
        else
        {
            StatusErr("No body found for " + SafeNetString(target.Username));
        }
    }

    // ── GAME STATE SETTERS ──────────────────────────────────────

    private static void CmdSetTime(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int seconds))
        {
            StatusWarn("Usage: /settime <seconds>");
            return;
        }
        GameManager.Instance.Server_SetGameState(phase: null, tick: seconds, period: null,
            blueScore: null, redScore: null, isOvertime: null);
        StatusOk("Time set to " + seconds + "s");
    }

    private static void CmdSetGoals(string[] args)
    {
        if (args.Length < 2) { StatusWarn("Usage: /setgoals <blue|red> <amount|+N|-N>"); return; }
        string team = args[0].ToLowerInvariant();
        string amountStr = args[1];
        int blueScore = GameManager.Instance.GameState.Value.BlueScore;
        int redScore = GameManager.Instance.GameState.Value.RedScore;

        if (team == "blue" || team == "b")
        {
            if (amountStr.StartsWith("+") && int.TryParse(amountStr.Substring(1), out int addB))
                blueScore += addB;
            else if (amountStr.StartsWith("-") && int.TryParse(amountStr.Substring(1), out int subB))
                blueScore = Mathf.Max(0, blueScore - subB);
            else if (int.TryParse(amountStr, out int absB))
                blueScore = absB;
            else { StatusErr("Invalid amount: " + amountStr); return; }
        }
        else if (team == "red" || team == "r")
        {
            if (amountStr.StartsWith("+") && int.TryParse(amountStr.Substring(1), out int addR))
                redScore += addR;
            else if (amountStr.StartsWith("-") && int.TryParse(amountStr.Substring(1), out int subR))
                redScore = Mathf.Max(0, redScore - subR);
            else if (int.TryParse(amountStr, out int absR))
                redScore = absR;
            else { StatusErr("Invalid amount: " + amountStr); return; }
        }
        else
        {
            StatusErr("Invalid team: " + team);
            return;
        }
        blueScore = Mathf.Max(0, blueScore);
        redScore = Mathf.Max(0, redScore);
        GameManager.Instance.Server_SetGameState(phase: null, tick: null, period: null,
            blueScore: blueScore, redScore: redScore, isOvertime: null);
        StatusOk($"Score: Blue {blueScore} - Red {redScore}");
    }

    private static void CmdSetState(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int period))
        {
            StatusWarn("Usage: /setstate <period> (1-3, 4+=OT)");
            return;
        }
        GameManager.Instance.Server_SetGameState(phase: null, tick: null, period: period,
            blueScore: null, redScore: null, isOvertime: period > 3);
        string label = period > 3 ? "OT" : "P" + period;
        ChatAuto("Period: " + label, ColToHex(ColAccent));
        StatusOk("Period set: " + label);
    }

    // ── HELPERS ─────────────────────────────────────────────────

    private static Player GetTargetOrSelected(string[] args)
    {
        if (args.Length > 0)
        {
            var p = FindPlayerByNeedle(args[0]);
            if (p != null) return p;
        }
        return selectedPlayer;
    }

    private static string ColToHex(Color c)
    {
        int r = Mathf.RoundToInt(c.r * 255);
        int g = Mathf.RoundToInt(c.g * 255);
        int b = Mathf.RoundToInt(c.b * 255);
        return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
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
