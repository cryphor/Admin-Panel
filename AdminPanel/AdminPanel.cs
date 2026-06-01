using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

namespace AdminPanel
{
    //============================================================
    // Plugin entry point — loaded by Puck's PluginManager
    //============================================================
    public class PluginMain : IPuckPlugin
    {
        private static Harmony s_harmony;
        private static GameObject s_runner;

        public bool OnEnable()
        {
            s_harmony = new Harmony("com.puck.adminpanel");
            s_harmony.PatchAll();

            s_runner = new GameObject("AdminPanelRunner");
            UnityEngine.Object.DontDestroyOnLoad(s_runner);
            s_runner.AddComponent<AdminPanelRunner>();

            Debug.Log("[AdminPanel] Loaded successfully. Press F1 to toggle.");
            return true;
        }

        public void OnDisable()
        {
            UnityEngine.Object.Destroy(s_runner);
            s_harmony?.UnpatchSelf();
        }
    }

    //============================================================
    // MonoBehaviour runner — lives on a GO, handles update/tick
    //============================================================
    public class AdminPanelRunner : MonoBehaviour
    {
        private AdminPanelUI _ui;

        private void Start()
        {
            _ui = new AdminPanelUI();
            _ui.Building();
            _ui.Hide();
        }

        // Keep a static reference so Harmony patches can reach it
        public static AdminPanelRunner Instance { get; private set; }

        private void Awake() { Instance = this; }
        private void OnDestroy() { Instance = null; }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F1))
            {
                AdminPanelUI.Toggle();
            }
        }

        public static void RefreshPlayerList()
        {
            Instance?._ui?.Refresh();
        }
    }

    //============================================================
    // Admin Commands — reflection-cached helpers for server actions
    //============================================================
    public static class AdminCommands
    {
        // Helper: find a method across Puck-related assemblies
        private static MethodInfo FindPuckMethod(string methodName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (!name.Contains("Puck") && !name.Contains("Assembly-CSharp"))
                    continue;
                foreach (var t in asm.GetTypes())
                {
                    var m = t.GetMethod(methodName,
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.FlattenHierarchy);
                    if (m != null) return m;
                }
            }
            return null;
        }
        
        private static MethodInfo s_BanMethod;
        private static MethodInfo s_KickMethod;
        private static MethodInfo s_FreezeMethod;
        private static MethodInfo s_UnfreezeMethod;
        private static MethodInfo s_TeleportMethod;
        private static MethodInfo s_TeamMethod;
        private static MethodInfo s_RoleMethod;
        private static bool s_initialized;

        public static void Initialize()
        {
            if (s_initialized) return;
            try
            {
                // Direct references for methods we know exist
                s_BanMethod = AccessTools.Method(typeof(ServerManager), "Server_BanPlayer");
                s_KickMethod = AccessTools.Method(typeof(ServerManager), "Server_KickPlayer");

                // Search broadly for names that might be on different types
                s_FreezeMethod = FindPuckMethod("Server_Freeze");
                s_UnfreezeMethod = FindPuckMethod("Server_Unfreeze");
                s_TeleportMethod = FindPuckMethod("Server_Teleport");
                s_TeamMethod = FindPuckMethod("Server_SwitchTeam") ?? FindPuckMethod("SetTeam");
                s_RoleMethod = FindPuckMethod("Server_SetRole") ?? FindPuckMethod("SetRole");

                Debug.Log($"[AdminPanel] Reflection cache — Ban:{s_BanMethod!=null} Kick:{s_KickMethod!=null} " +
                    $"Freeze:{s_FreezeMethod!=null} TP:{s_TeleportMethod!=null} Team:{s_TeamMethod!=null} Role:{s_RoleMethod!=null}");
            }
            catch (Exception ex)
            {
                Debug.LogError("[AdminPanel] Reflection init: " + ex.Message);
            }
            s_initialized = true;
        }

        public static bool IsLocalAdmin(string steamId)
        {
            try
            {
                // Dedicated server: operator is inherently admin
                var nm = Unity.Netcode.NetworkManager.Singleton;
                if (nm != null && nm.IsServer && !nm.IsHost)
                    return true;
                return AdminManager.IsAdmin(steamId);
            }
            catch { return false; }
        }

        public static void KickPlayer(Player player, string reason = "")
        {
            Initialize();
            if (player == null || s_KickMethod == null) return;
            try
            {
                s_KickMethod.Invoke(
                    ServerManager.Instance,
                    new object[] { player, DisconnectionCode.Kicked, reason.Length > 0 ? reason : null, true }
                );
                Debug.Log($"[AdminPanel] Kicked {player.Username.Value}");
            }
            catch (Exception ex) { Debug.LogError("[AdminPanel] Kick failed: " + ex.Message); }
        }

        public static void BanPlayer(Player player)
        {
            Initialize();
            if (player == null) return;
            try
            {
                if (s_BanMethod != null)
                    s_BanMethod.Invoke(ServerManager.Instance, new object[] { player });
                else
                    BanManager.Instance?.AddBannedSteamId(player.SteamId.Value.ToString());
                KickPlayer(player, "Banned");
            }
            catch (Exception ex) { Debug.LogError("[AdminPanel] Ban failed: " + ex.Message); }
        }

        public static void SlayPlayer(Player player)
        {
            if (player == null) return;
            try
            {
                var pb = player.PlayerBody;
                if (pb == null) return;
                // Teleport player high up so they fall and respawn
                var pos = pb.transform.position;
                pos.y += 50f;
                if (s_TeleportMethod != null)
                    s_TeleportMethod.Invoke(pb, new object[] { pos, pb.transform.rotation });
            }
            catch (Exception ex) { Debug.LogError("[AdminPanel] Slay failed: " + ex.Message); }
        }

        public static void FreezePlayer(Player player)
        {
            if (player == null) return;
            var pb = player.PlayerBody;
            if (pb == null) return;
            try
            {
                var rb = pb.GetComponent<Rigidbody>();
                if (rb == null) return;
                if (rb.constraints == RigidbodyConstraints.None)
                {
                    // Freeze
                    if (s_FreezeMethod != null)
                        s_FreezeMethod.Invoke(pb, null);
                    else
                        rb.constraints = RigidbodyConstraints.FreezeAll;
                }
                else
                {
                    // Unfreeze
                    if (s_UnfreezeMethod != null)
                        s_UnfreezeMethod.Invoke(pb, null);
                    else
                        rb.constraints = RigidbodyConstraints.None;
                }
            }
            catch (Exception ex) { Debug.LogError("[AdminPanel] Freeze failed: " + ex.Message); }
        }

        public static void TeleportTo(Player target, Player source)
        {
            if (target == null || source == null) return;
            try
            {
                var pb = target.PlayerBody;
                if (pb == null) return;
                if (s_TeleportMethod != null)
                    s_TeleportMethod.Invoke(pb, new object[] { source.transform.position, source.transform.rotation });
                else
                    pb.transform.position = source.transform.position;
            }
            catch (Exception ex) { Debug.LogError("[AdminPanel] Teleport failed: " + ex.Message); }
        }

        public static void SwitchTeam(Player player)
        {
            if (player == null || s_TeamMethod == null) return;
            try
            {
                PlayerTeam next;
                switch (player.Team)
                {
                    case PlayerTeam.Blue: next = PlayerTeam.Red; break;
                    case PlayerTeam.Red: next = PlayerTeam.Spectator; break;
                    default: next = PlayerTeam.Blue; break;
                }
                var inst = s_TeamMethod.IsStatic ? null : player;
                s_TeamMethod.Invoke(inst, new object[] { next });
            }
            catch (Exception ex) { Debug.LogError("[AdminPanel] SwitchTeam failed: " + ex.Message); }
        }

        public static void SetRole(Player player, PlayerRole role)
        {
            if (player == null || s_RoleMethod == null) return;
            try
            {
                var inst = s_RoleMethod.IsStatic ? null : player;
                s_RoleMethod.Invoke(inst, new object[] { role });
            }
            catch (Exception ex) { Debug.LogError("[AdminPanel] SetRole failed: " + ex.Message); }
        }

        public static void ToggleMute(Player player)
        {
            if (player == null) return;
            try
            {
                var mutedVar = player.IsMuted;
                bool current = mutedVar.Value;
                // Try the Write method on NetworkVariable
                var writeMethod = mutedVar.GetType().GetMethod("Write",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (writeMethod != null)
                    writeMethod.Invoke(mutedVar, new object[] { !current });
                else
                {
                    // Fallback: set the backing field directly
                    var valField = AccessTools.Field(mutedVar.GetType(), "Value");
                    if (valField != null) valField.SetValue(mutedVar, !current);
                }
            }
            catch (Exception ex) { Debug.LogError("[AdminPanel] Mute toggle failed: " + ex.Message); }
        }
    }

    //============================================================
    // UI — Admin Panel built with UIElements (no UXML)
    //============================================================
    public class AdminPanelUI : UIView
    {
        private static AdminPanelUI s_instance;
        private VisualElement _root;
        private ScrollView _playerListScroll;
        private VisualElement _playerListContainer;
        private Label _selectedPlayerLabel;
        private Label _gameInfoLabel;
        private Label _selectedSteamId;
        private Player _selectedPlayer;
        private List<PlayerEntry> _playerEntries = new List<PlayerEntry>();

        private bool _showing = false;
        private bool _whitelistOn = false;
        private bool _passwordOn = false;

        public AdminPanelUI()
        {
            s_instance = this;
        }

        public static void Toggle()
        {
            if (s_instance == null) return;
            s_instance.ToggleShowing();
        }

        public static void Refresh()
        {
            s_instance?.RebuildPlayerList();
            s_instance?.UpdateGameInfo();
        }

        public void Building()
        {
            // Build the full panel
            _root = new VisualElement();
            _root.name = "AdminPanelRoot";

            // Position: top-right corner
            _root.style.position = Position.Absolute;
            _root.style.top = 10;
            _root.style.right = 10;
            _root.style.width = 360;
            _root.style.maxHeight = 600;

            // Dark theme
            _root.style.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.93f);
            _root.style.borderBottomLeftRadius = 6;
            _root.style.borderBottomRightRadius = 6;
            _root.style.borderTopLeftRadius = 6;
            _root.style.borderTopRightRadius = 6;
            _root.style.borderBottomWidth = 1;
            _root.style.borderLeftWidth = 1;
            _root.style.borderRightWidth = 1;
            _root.style.borderTopWidth = 1;
            _root.style.borderBottomColor = new Color(0.3f, 0.3f, 0.4f);
            _root.style.borderLeftColor = new Color(0.3f, 0.3f, 0.4f);
            _root.style.borderRightColor = new Color(0.3f, 0.3f, 0.4f);
            _root.style.borderTopColor = new Color(0.3f, 0.3f, 0.4f);
            _root.style.paddingLeft = 8;
            _root.style.paddingRight = 8;
            _root.style.paddingTop = 8;
            _root.style.paddingBottom = 8;

            // ========== Title Bar ==========
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.justifyContent = Justify.SpaceBetween;
            titleRow.style.alignItems = Align.Center;
            titleRow.style.marginBottom = 6;

            var titleLabel = new Label("⚙ ADMIN PANEL");
            titleLabel.style.color = new Color(0.9f, 0.6f, 0.1f);
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            var closeBtn = new Button(() => { HidePanel(); })
            {
                text = "✕"
            };
            closeBtn.MarkDirtyRepaint();
            closeBtn.style.width = 24;
            closeBtn.style.height = 24;
            closeBtn.style.fontSize = 12;
            closeBtn.style.backgroundColor = new Color(0.4f, 0.1f, 0.1f);
            closeBtn.style.color = Color.white;
            closeBtn.style.borderBottomLeftRadius = 3;
            closeBtn.style.borderBottomRightRadius = 3;
            closeBtn.style.borderTopLeftRadius = 3;
            closeBtn.style.borderTopRightRadius = 3;

            titleRow.Add(titleLabel);
            titleRow.Add(closeBtn);
            _root.Add(titleRow);

            // ========== Divider ==========
            _root.Add(MakeDivider());

            // ========== Toggle Row ==========
            var toggleRow = new VisualElement();
            toggleRow.style.flexDirection = FlexDirection.Row;
            toggleRow.style.justifyContent = Justify.SpaceBetween;
            toggleRow.style.marginTop = 4;
            toggleRow.style.marginBottom = 4;

            var whitelistBtn = new Button(() => { _whitelistOn = !_whitelistOn; UpdateToggleStyles(whitelistBtn, _whitelistOn, "Whitelist"); })
            {
                text = "Whitelist: OFF"
            };
            StyleToggleButton(whitelistBtn, false);

            var passwordBtn = new Button(() => { _passwordOn = !_passwordOn; UpdateToggleStyles(passwordBtn, _passwordOn, "Password"); })
            {
                text = "Password: OFF"
            };
            StyleToggleButton(passwordBtn, false);

            toggleRow.Add(whitelistBtn);
            toggleRow.Add(passwordBtn);
            _root.Add(toggleRow);

            // ========== Game Info ==========
            _gameInfoLabel = new Label("Loading...");
            _gameInfoLabel.style.color = new Color(0.7f, 0.85f, 1f);
            _gameInfoLabel.style.fontSize = 11;
            _gameInfoLabel.style.marginBottom = 4;
            _root.Add(_gameInfoLabel);

            _root.Add(MakeDivider());

            // ========== Player List Header ==========
            var pHeader = new Label("PLAYERS");
            pHeader.style.color = new Color(0.6f, 0.6f, 0.7f);
            pHeader.style.fontSize = 10;
            pHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            pHeader.style.marginTop = 2;
            pHeader.style.marginBottom = 2;
            _root.Add(pHeader);

            // ========== Player List (ScrollView) ==========
            _playerListScroll = new ScrollView(ScrollViewMode.Vertical);
            _playerListScroll.style.height = 250;
            _playerListScroll.style.backgroundColor = new Color(0.06f, 0.06f, 0.09f);
            _playerListScroll.style.borderBottomLeftRadius = 4;
            _playerListScroll.style.borderBottomRightRadius = 4;
            _playerListScroll.style.borderTopLeftRadius = 4;
            _playerListScroll.style.borderTopRightRadius = 4;
            _playerListScroll.style.paddingLeft = 4;
            _playerListScroll.style.paddingRight = 4;
            _playerListScroll.style.paddingTop = 4;
            _playerListScroll.style.paddingBottom = 4;

            _playerListContainer = new VisualElement();
            _playerListContainer.style.flexDirection = FlexDirection.Column;
            _playerListScroll.Add(_playerListContainer);
            _root.Add(_playerListScroll);

            _root.Add(MakeDivider());

            // ========== Selected Player ==========
            _selectedPlayerLabel = new Label("Select a player");
            _selectedPlayerLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            _selectedPlayerLabel.style.fontSize = 12;
            _selectedPlayerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _selectedPlayerLabel.style.marginTop = 2;
            _root.Add(_selectedPlayerLabel);

            _selectedSteamId = new Label("");
            _selectedSteamId.style.color = new Color(0.5f, 0.5f, 0.6f);
            _selectedSteamId.style.fontSize = 10;
            _selectedSteamId.style.marginBottom = 4;
            _root.Add(_selectedSteamId);

            // ========== Action Buttons Row 1 ==========
            var row1 = MakeButtonRow();
            var kickBtn = MakeActionButton("Kick", new Color(0.6f, 0.25f, 0.1f), () => { if (_selectedPlayer != null) AdminCommands.KickPlayer(_selectedPlayer); });
            var banBtn  = MakeActionButton("Ban",  new Color(0.7f, 0.1f, 0.1f), () => { if (_selectedPlayer != null) AdminCommands.BanPlayer(_selectedPlayer); });
            var slayBtn = MakeActionButton("Slay", new Color(0.5f, 0.3f, 0.1f), () => { if (_selectedPlayer != null) AdminCommands.SlayPlayer(_selectedPlayer); });
            var muteBtn = MakeActionButton("Mute", new Color(0.3f, 0.3f, 0.5f), () => { if (_selectedPlayer != null) AdminCommands.ToggleMute(_selectedPlayer); });
            row1.Add(kickBtn);
            row1.Add(banBtn);
            row1.Add(slayBtn);
            row1.Add(muteBtn);
            _root.Add(row1);

            // ========== Action Buttons Row 2 ==========
            var row2 = MakeButtonRow();
            var tpToThemBtn = MakeActionButton("TP →", new Color(0.1f, 0.4f, 0.6f), () => {
                if (_selectedPlayer != null) {
                    var local = PlayerManager.Instance?.GetLocalPlayer();
                    if (local != null) AdminCommands.TeleportTo(local, _selectedPlayer);
                }
            });
            var tpToMeBtn = MakeActionButton("TP ←", new Color(0.1f, 0.4f, 0.6f), () => {
                if (_selectedPlayer != null) {
                    var local = PlayerManager.Instance?.GetLocalPlayer();
                    if (local != null) AdminCommands.TeleportTo(_selectedPlayer, local);
                }
            });
            var freezeBtn = MakeActionButton("Freeze", new Color(0.4f, 0.4f, 0.6f), () => {
                if (_selectedPlayer != null) AdminCommands.FreezePlayer(_selectedPlayer);
            });
            var teamBtn  = MakeActionButton("Team", new Color(0.1f, 0.4f, 0.2f), () => { if (_selectedPlayer != null) AdminCommands.SwitchTeam(_selectedPlayer); });
            var roleBtn  = MakeActionButton("Role", new Color(0.3f, 0.2f, 0.5f), () => {
                if (_selectedPlayer != null) {
                    var next = _selectedPlayer.Role == PlayerRole.Attacker ? PlayerRole.Goalie : PlayerRole.Attacker;
                    AdminCommands.SetRole(_selectedPlayer, next);
                }
            });
            row2.Add(tpToThemBtn);
            row2.Add(tpToMeBtn);
            row2.Add(teamBtn);
            row2.Add(freezeBtn);
            _root.Add(row2);

            // Add the root visual element to our View property
            View = _root;
        }

        // ========== Public Methods ==========

        public void Show()
        {
            if (!_showing)
            {
                _showing = true;
                base.Show();
                try
                {
                    var uiDocParent = UIManager.Instance?.RootVisualElement;
                    if (uiDocParent != null && _root != null && _root.parent != uiDocParent)
                    {
                        uiDocParent.Add(_root);
                    }
                }
                catch (Exception ex) { Debug.LogError("[AdminPanel] Could not attach to UI: " + ex.Message); }
                RebuildPlayerList();
                UpdateGameInfo();
            }
        }

        public void Hide()
        {
            if (_showing)
            {
                _showing = false;
                base.Hide();
            }
        }

        public void ToggleShowing()
        {
            if (_showing) Hide(); else Show();
        }

        public void HidePanel()
        {
            Hide();
        }

        // ========== Player List ==========

        public void Refresh()
        {
            if (!_showing) return;
            RebuildPlayerList();
            UpdateGameInfo();
        }

        private void RebuildPlayerList()
        {
            if (_playerListContainer == null) return;
            _playerListContainer.Clear();
            _playerEntries.Clear();

            try
            {
                var players = PlayerManager.Instance?.GetPlayers();
                if (players == null || players.Count == 0)
                {
                    var empty = new Label("No players connected");
                    empty.style.color = new Color(0.5f, 0.5f, 0.5f);
                    empty.style.fontSize = 11;
                    _playerListContainer.Add(empty);
                    return;
                }

                foreach (var p in players)
                {
                    if (p == null) continue;
                    var entry = new PlayerEntry(p, SelectPlayer);
                    _playerEntries.Add(entry);
                    _playerListContainer.Add(entry.Element);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[AdminPanel] Player list build failed: " + ex.Message);
            }
        }

        private void SelectPlayer(Player p)
        {
            if (p == null) return;
            _selectedPlayer = p;
            try
            {
                _selectedPlayerLabel.text = $"Selected: {p.Username.Value}";
                _selectedSteamId.text = $"STEAM: {p.SteamId.Value}";
            }
            catch { }
        }

        private void UpdateGameInfo()
        {
            try
            {
                var gs = GlobalStateManager.Instance;
                if (gs != null)
                {
                    int timeLeft = Mathf.Max(0, gs.RemainingTimeSeconds);
                    int min = timeLeft / 60;
                    int sec = timeLeft % 60;
                    _gameInfoLabel.text = $"Score: Blue {gs.BlueGoals} - {gs.RedGoals} Red    Time: {min}:{sec:D2}";
                }
            }
            catch
            {
                _gameInfoLabel.text = "Game info unavailable";
            }
        }

        // ========== Styles / Helpers ==========

        private VisualElement MakeDivider()
        {
            var div = new VisualElement();
            div.style.height = 1;
            div.style.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
            div.style.marginTop = 2;
            div.style.marginBottom = 2;
            return div;
        }

        private VisualElement MakeButtonRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginTop = 3;
            return row;
        }

        private Button MakeActionButton(string label, Color bgColor, Action callback)
        {
            var btn = new Button(callback) { text = label };
            btn.style.height = 26;
            btn.style.flexGrow = 1;
            btn.style.marginLeft = 1;
            btn.style.marginRight = 1;
            btn.style.fontSize = 10;
            btn.style.color = new Color(0.9f, 0.9f, 0.9f);
            btn.style.backgroundColor = bgColor;
            btn.style.borderBottomLeftRadius = 3;
            btn.style.borderBottomRightRadius = 3;
            btn.style.borderTopLeftRadius = 3;
            btn.style.borderTopRightRadius = 3;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.borderTopWidth = 0;
            btn.style.paddingLeft = 4;
            btn.style.paddingRight = 4;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            return btn;
        }

        private void StyleToggleButton(Button btn, bool on)
        {
            btn.style.height = 24;
            btn.style.width = 160;
            btn.style.fontSize = 10;
            btn.style.color = Color.white;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.borderBottomLeftRadius = 3;
            btn.style.borderBottomRightRadius = 3;
            btn.style.borderTopLeftRadius = 3;
            btn.style.borderTopRightRadius = 3;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.borderTopWidth = 0;
            UpdateToggleStyles(btn, on, btn.text.Contains("Whitelist") ? "Whitelist" : "Password");
        }

        private void UpdateToggleStyles(Button btn, bool on, string prefix)
        {
            btn.style.backgroundColor = on ? new Color(0.1f, 0.5f, 0.2f) : new Color(0.25f, 0.25f, 0.3f);
            btn.text = $"{prefix}: {(on ? "ON" : "OFF")}";
        }
    }

    //============================================================
    // Player List Entry — row in the scroll view
    //============================================================
    public class PlayerEntry
    {
        public Player Player { get; }
        public VisualElement Element { get; }

        public PlayerEntry(Player player, Action<Player> onClick)
        {
            Player = player;
            Element = new VisualElement();
            Element.style.flexDirection = FlexDirection.Row;
            Element.style.justifyContent = Justify.SpaceBetween;
            Element.style.alignItems = Align.Center;
            Element.style.paddingTop = 3;
            Element.style.paddingBottom = 3;
            Element.style.paddingLeft = 4;
            Element.style.paddingRight = 4;
            Element.style.marginBottom = 2;
            Element.style.borderBottomLeftRadius = 3;
            Element.style.borderBottomRightRadius = 3;
            Element.style.borderTopLeftRadius = 3;
            Element.style.borderTopRightRadius = 3;
            Element.style.borderBottomWidth = 0;
            Element.style.borderLeftWidth = 0;
            Element.style.borderRightWidth = 0;
            Element.style.borderTopWidth = 0;

            // Background based on team
            try
            {
                var team = player.Team;
                Color bg;
                switch (team)
                {
                    case PlayerTeam.Blue: bg = new Color(0.08f, 0.12f, 0.25f); break;
                    case PlayerTeam.Red:  bg = new Color(0.25f, 0.08f, 0.08f); break;
                    default:              bg = new Color(0.12f, 0.12f, 0.15f); break;
                }
                Element.style.backgroundColor = bg;
            }
            catch { }

            // Click to select
            Element.RegisterCallback<ClickEvent>(_ => onClick?.Invoke(player));

            // Left side: name + team badge + role
            var leftSide = new VisualElement();
            leftSide.style.flexDirection = FlexDirection.Column;
            leftSide.style.flexGrow = 1;

            string teamIcon = "", roleStr = "", adminBadge = "";
            Color nameColor = Color.white;
            try
            {
                switch (player.Team)
                {
                    case PlayerTeam.Blue: teamIcon = "🔵"; nameColor = new Color(0.5f, 0.7f, 1f); break;
                    case PlayerTeam.Red:  teamIcon = "🔴"; nameColor = new Color(1f, 0.5f, 0.5f); break;
                    case PlayerTeam.Spectator: teamIcon = "👻"; nameColor = new Color(0.7f, 0.7f, 0.7f); break;
                }
                switch (player.Role)
                {
                    case PlayerRole.Attacker: roleStr = "ATK"; break;
                    case PlayerRole.Goalie:   roleStr = "GOL"; break;
                }
                int adminLvl = player.AdminLevel.Value;
                if (adminLvl > 0) adminBadge = $" ★{adminLvl}";
            }
            catch { }

            var nameLine = new VisualElement();
            nameLine.style.flexDirection = FlexDirection.Row;
            nameLine.style.alignItems = Align.Center;

            var nameLabel = new Label($"{teamIcon} {player.Username.Value}{adminBadge}");
            nameLabel.style.color = nameColor;
            nameLabel.style.fontSize = 12;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.flexShrink = 1;

            if (!string.IsNullOrEmpty(roleStr))
            {
                var roleLabel = new Label(roleStr);
                roleLabel.style.color = new Color(0.8f, 0.8f, 0.5f);
                roleLabel.style.fontSize = 9;
                roleLabel.style.marginLeft = 4;
                nameLine.Add(nameLabel);
                nameLine.Add(roleLabel);
            }
            else
            {
                nameLine.Add(nameLabel);
            }

            // Steam ID
            var steamIdLabel = new Label($"  {player.SteamId.Value}");
            steamIdLabel.style.color = new Color(0.4f, 0.4f, 0.45f);
            steamIdLabel.style.fontSize = 9;

            leftSide.Add(nameLine);
            leftSide.Add(steamIdLabel);

            // Right side: stats
            var rightSide = new VisualElement();
            rightSide.style.alignItems = Align.FlexEnd;
            rightSide.style.flexShrink = 0;

            string statsText = "";
            try
            {
                statsText = $"G:{player.Goals.Value} A:{player.Assists.Value} P:{player.Ping.Value}";
            }
            catch { statsText = "N/A"; }

            var statsLabel = new Label(statsText);
            statsLabel.style.color = new Color(0.5f, 0.55f, 0.6f);
            statsLabel.style.fontSize = 9;
            rightSide.Add(statsLabel);

            Element.Add(leftSide);
            Element.Add(rightSide);
        }
    }

    //============================================================
    // Harmony patches
    //============================================================
    [HarmonyPatch(typeof(PlayerManager), "AddPlayer")]
    static class AddPlayerPatch
    {
        static void Postfix() => AdminPanelRunner.RefreshPlayerList();
    }

    [HarmonyPatch(typeof(PlayerManager), "RemovePlayer")]
    static class RemovePlayerPatch
    {
        static void Postfix() => AdminPanelRunner.RefreshPlayerList();
    }
}
