using UnityEngine;
using UnityEngine.InputSystem;

namespace SlopMod
{
    /// <summary>
    /// Runtime port of the legacy <c>DynamicDebugOverlay</c> MonoBehaviour.
    ///
    /// UI fixes over the original:
    ///   * a semi-transparent backdrop behind the monitor text (it used to be unreadable green-on-gameplay);
    ///   * the toolbox is an auto-sized/scrollable GUILayout window instead of fixed rects that could clip;
    ///   * noclip status no longer performs a full scene scan every OnGUI event.
    ///
    /// The component is created once (DontDestroyOnLoad) via <see cref="EnsureExists"/>.
    /// </summary>
    internal sealed class DebugOverlay : MonoBehaviour
    {
        internal static DebugOverlay Instance { get; private set; }

        private const int ToolboxWindowId = 0x51A70001;

        private bool _monitorVisible;
        private bool _toolboxVisible;
        private Rect _toolboxRect = new Rect(40f, 40f, 340f, 400f);
        private int _tab;
        private bool _categoriesOpen = true;
        private bool _displayOpen = true;
        private bool _frequencyOpen;
        private bool[] _categoryOpen;

        private static readonly string[] TabNames = { "World", "Player", "Episode", "About" };

        private GUIStyle _monitorStyle;
        private bool _stylesReady;

        internal static void EnsureExists()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("SlopMod_DebugOverlay");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<DebugOverlay>();
        }

        internal static void DestroyInstance()
        {
            if (Instance == null) return;
            UnityEngine.Object.Destroy(Instance.gameObject);
            Instance = null;
        }

        internal static void ToggleMonitor()
        {
            EnsureExists();
            Instance._monitorVisible = !Instance._monitorVisible;
        }

        internal static void ToggleToolbox()
        {
            EnsureExists();
            Instance._toolboxVisible = !Instance._toolboxVisible;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                UnityEngine.Object.Destroy(gameObject);
                return;
            }
            Instance = this;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            // Gamepad: mirror the F8 practice-restart hotkey on the "View" button (Xbox View,
            // PlayStation Share/Create, Switch Minus -> Gamepad.selectButton on every layout).
            // Read before the keyboard check so it also works with no keyboard attached.
            Gamepad pad = Gamepad.current;
            if (pad != null && pad.selectButton.wasPressedThisFrame) EpisodeTools.RestartPracticeLevel();

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f1Key.wasPressedThisFrame) _monitorVisible = !_monitorVisible;
            if (kb.f2Key.wasPressedThisFrame) _toolboxVisible = !_toolboxVisible;
            if (kb.f3Key.wasPressedThisFrame) EpisodeTools.ToggleStickerBook();
            if (kb.f4Key.wasPressedThisFrame) EpisodeTools.EndCutscene();
            if (kb.f5Key.wasPressedThisFrame) ExtrasRuntime.ToggleFreePlayers();
            if (kb.f6Key.wasPressedThisFrame) FreeCam.Toggle();
            if (kb.f7Key.wasPressedThisFrame) ExtrasRuntime.ReloadLevel();
            if (kb.f8Key.wasPressedThisFrame) EpisodeTools.RestartPracticeLevel();
            if (kb.f9Key.wasPressedThisFrame) TimeTools.Toggle();
            if (kb.f10Key.wasPressedThisFrame) EpisodeTools.ToggleNoclip();
            if (kb.tKey.wasPressedThisFrame) WorldDebugView.ToggleTriggers();
            if (kb.cKey.wasPressedThisFrame) WorldDebugView.ToggleColliders();

            if (EpisodeTools.HasSave)
            {
                if (kb.pageUpKey.wasPressedThisFrame) EpisodeTools.EndCurrentCheckpoint();
                if (kb.pageDownKey.wasPressedThisFrame) EpisodeTools.AdjustPhase(-1);
            }
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            _monitorStyle = new GUIStyle(GUI.skin.label);
            _monitorStyle.fontSize = 14;
            _monitorStyle.normal.textColor = Color.green;

            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (_monitorVisible) DrawMonitor();
            if (_toolboxVisible)
            {
                // Draggable window - move it by its title bar or the grab strip at the top.
                _toolboxRect = GUILayout.Window(ToolboxWindowId, _toolboxRect, DrawToolbox, "slopMod v" + SlopModPlugin.PluginVersion + " (F2 to hide)");
            }
        }

        /// <summary>Draws a translucent panel behind text (the old GUIStyle backdrop didn't render).</summary>
        private static void DrawBackdrop(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawMonitor()
        {
            const float width = 460f;
            float lines = EpisodeTools.HasSave ? 13f : 2f;

            // Anchored to the BOTTOM-LEFT corner. Drawn inside a group, so every child rect below uses
            // group-local coordinates.
            const float panelX = 12f;
            float height = 34f + lines * 22f;
            float panelY = Mathf.Max(12f, Screen.height - height - 12f);

            GUI.BeginGroup(new Rect(panelX, panelY, width, height));
            DrawBackdrop(new Rect(0f, 0f, width, height));

            // slopMod title indicator - shows which build of the mod is running.
            GUI.Label(new Rect(8f, 6f, width - 16f, 22f), "slopMod v" + SlopModPlugin.PluginVersion, _monitorStyle);
            GUI.Label(new Rect(8f, 28f, width - 16f, 22f), "F1 Mon | F2 Box | F3 Book | F4 Cut | F5 Free | F7 Reload | F10 Noclip | PgUp/PgDn", _monitorStyle);

            if (!EpisodeTools.HasSave)
            {
                GUI.Label(new Rect(8f, 50f, width - 16f, 22f), "Waiting for an active save profile...", _monitorStyle);
                GUI.EndGroup();
                return;
            }

            EpisodesProgress p = EpisodeTools.GetProgress();
            float y = 50f;
            GUI.Label(new Rect(8f, y, width - 16f, 22f), "Unlocked Episode: " + p.unlockedEpisode, _monitorStyle); y += 22f;
            GUI.Label(new Rect(8f, y, width - 16f, 22f), "Active Episode: " + p.activeEpisode, _monitorStyle); y += 22f;
            GUI.Label(new Rect(8f, y, width - 16f, 22f), "Episode Phase: " + p.episodePhase, _monitorStyle); y += 22f;
            GUI.Label(new Rect(8f, y, width - 16f, 22f), "Running State: " + p.wasEpisodeRunning, _monitorStyle); y += 22f;
            GUI.Label(new Rect(8f, y, width - 16f, 22f), "All Completed: " + p.allEpisodesCompleted, _monitorStyle); y += 22f;
            GUI.Label(new Rect(8f, y, width - 16f, 22f), "Unlocked Activities: " + (p.unlockedActivities != null ? p.unlockedActivities.Count : 0), _monitorStyle); y += 22f;
            GUI.Label(new Rect(8f, y, width - 16f, 22f), "Unlocked Collectibles: " + (p.unlockedCollectiblesArea != null ? p.unlockedCollectiblesArea.Count : 0), _monitorStyle); y += 22f;
            GUI.Label(new Rect(8f, y, width - 16f, 22f), "Noclip (F10): " + EpisodeTools.GetNoclipStatus(), _monitorStyle); y += 22f;
            GUI.Label(new Rect(8f, y, width - 16f, 22f), "Cutscene: " + (EpisodeTools.IsCutscenePlaying() ? "PLAYING" : "none"), _monitorStyle); y += 22f;
            GUI.Label(new Rect(8f, y, width - 16f, 22f), "Stickerbook: " + (EpisodeTools.IsStickerBookOpen() ? "OPEN" : "closed"), _monitorStyle);

            int trashCount;
            int trashTotal;
            if (EpisodeTools.TryGetTrashBagCounter(out trashCount, out trashTotal))
            {
                y += 22f;
                GUI.Label(new Rect(8f, y, width - 16f, 22f), "Trash in bag: " + trashCount + " / " + trashTotal, _monitorStyle);
            }

            // Live "throw detach" window (the ~0.4 s that makes the trash-bag dupe possible). Always drawn
            // so the exact moment a re-throw counts can be seen - see ThrowWindowIndicator.
            y += 22f;
            ThrowWindowIndicator throwWindow = ThrowWindowIndicator.Instance;
            string throwText = (throwWindow != null && throwWindow.WindowActive)
                ? "ACTIVE (" + throwWindow.WindowRemainingSeconds.ToString("0.00") + " s left)"
                : "idle";
            GUI.Label(new Rect(8f, y, width - 16f, 22f), "Throw window (0.4 s): " + throwText, _monitorStyle);
            GUI.EndGroup();
        }

        private void DrawToolbox(int windowId)
        {
            // Fixed grab strip at the top: dragging anywhere in this band moves the window (the title
            // bar works too). A literal rect is used because GetRect-based drag areas are unreliable.
            GUILayout.Space(16f);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 16f));

            // The menu is split into separate pages.
            _tab = GUILayout.Toolbar(_tab, TabNames);
            GUILayout.Space(4f);

            switch (_tab)
            {
                case 0: DrawWorldTab(); break;
                case 1: DrawPlayerTab(); break;
                case 2: DrawEpisodeTab(); break;
                default: DrawAboutTab(); break;
            }
        }

        /// <summary>World / collider-view page (works without a save profile).</summary>
        private void DrawWorldTab()
        {
            if (GUILayout.Button("Show triggers (T): " + (WorldDebugView.TriggersVisible ? "ON" : "OFF"))) WorldDebugView.ToggleTriggers();
            if (GUILayout.Button("Show colliders (C): " + (WorldDebugView.CollidersVisible ? "ON" : "OFF"))) WorldDebugView.ToggleColliders();
            if (GUILayout.Button("Hide world while viewing: " + (WorldDebugView.HideWorld ? "ON" : "OFF"))) WorldDebugView.ToggleHideWorld();
            if (WorldDebugView.TriggersVisible || WorldDebugView.CollidersVisible)
            {
                // --- Display submenu ---
                _displayOpen = GUILayout.Toggle(_displayOpen, (_displayOpen ? " v " : " > ") + "Display");
                if (_displayOpen)
                {
                    GUILayout.Label("  View distance: " + WorldDebugView.CullRadius.ToString("0") + " m");
                    WorldDebugView.CullRadius = GUILayout.HorizontalSlider(WorldDebugView.CullRadius, WorldDebugView.MinCullRadius, WorldDebugView.MaxCullRadius);

                    GUILayout.Label("  Wire thickness: " + WorldDebugView.WireThickness.ToString("0.000") + " m  (0 = 1 px)");
                    WorldDebugView.WireThickness = GUILayout.HorizontalSlider(WorldDebugView.WireThickness, WorldDebugView.MinWireThickness, WorldDebugView.MaxWireThickness);

                    GUILayout.Label("  Opacity: " + WorldDebugView.Opacity.ToString("0.00"));
                    WorldDebugView.Opacity = GUILayout.HorizontalSlider(WorldDebugView.Opacity, WorldDebugView.MinOpacity, WorldDebugView.MaxOpacity);
                }

                GUILayout.Space(4f);

                // --- Update frequency submenu (sets every category at once) ---
                _frequencyOpen = GUILayout.Toggle(_frequencyOpen, (_frequencyOpen ? " v " : " > ") + "Update frequency (all): " + FormatHz(WorldDebugView.UpdateFrequency));
                if (_frequencyOpen)
                {
                    float hz = WorldDebugView.UpdateFrequency;
                    GUILayout.Label("  All categories: " + FormatHz(hz));
                    float newHz = GUILayout.HorizontalSlider(hz, WorldDebugView.MinUpdateFrequency, WorldDebugView.MaxUpdateFrequency);
                    if (!Mathf.Approximately(newHz, hz)) WorldDebugView.SetAllUpdateFrequencies(newHz);
                    GUILayout.Label("  0 = freeze; lower = cheaper, higher = smoother");
                }

                GUILayout.Space(4f);

                // --- Colliders submenu: one collapsible submenu per category ---
                _categoriesOpen = GUILayout.Toggle(_categoriesOpen, (_categoriesOpen ? " v " : " > ") + "Colliders (" + ShownCategoryCount() + "/" + WorldDebugView.CategoryCount + ")");
                if (_categoriesOpen)
                {
                    EnsureCategoryFoldouts();
                    for (int i = 0; i < WorldDebugView.CategoryCount; i++)
                    {
                        WorldDebugView.CategoryInfo info = WorldDebugView.GetCategory(i);
                        bool visible = WorldDebugView.IsCategoryVisible(i);
                        bool filled = WorldDebugView.IsCategoryFilled(i);

                        Color previous = GUI.color;
                        GUI.color = new Color(info.Color.r, info.Color.g, info.Color.b, 1f);
                        _categoryOpen[i] = GUILayout.Toggle(_categoryOpen[i],
                            " " + info.Name + (filled ? " (filled)" : "") + (visible ? "" : "  (off)"), GUILayout.ExpandWidth(true));
                        GUI.color = previous;

                        if (_categoryOpen[i])
                        {
                            bool vis = GUILayout.Toggle(visible, "    Visible");
                            WorldDebugView.SetCategoryVisible(i, vis);

                            bool fill = GUILayout.Toggle(filled, "    Fill");
                            WorldDebugView.SetCategoryFilled(i, fill);

                            float catHz = WorldDebugView.GetCategoryFrequency(i);
                            GUILayout.Label("    Update: " + FormatHz(catHz));
                            float newCatHz = GUILayout.HorizontalSlider(catHz, WorldDebugView.MinUpdateFrequency, WorldDebugView.MaxUpdateFrequency);
                            if (!Mathf.Approximately(newCatHz, catHz)) WorldDebugView.SetCategoryFrequency(i, newCatHz);
                        }
                    }
                }
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("Teleport to level spawn")) TeleportTools.TeleportToLevelSpawn();
            if (GUILayout.Button("Teleport to next cutscene")) TeleportTools.TeleportToNextCutscene();

            // Only meaningful while the detached camera (F6) is active.
            bool previousEnabled = GUI.enabled;
            GUI.enabled = FreeCam.Active;
            if (GUILayout.Button("Teleport player to freecam (F6 active)")) TeleportTools.TeleportPlayersToFreecam();
            GUI.enabled = previousEnabled;

            if (GUILayout.Button("Reload level (F7)")) ExtrasRuntime.ReloadLevel();
        }

        private void EnsureCategoryFoldouts()
        {
            if (_categoryOpen == null || _categoryOpen.Length != WorldDebugView.CategoryCount)
                _categoryOpen = new bool[WorldDebugView.CategoryCount];
        }

        private static string FormatHz(float hz)
        {
            if (hz <= 0.001f) return "frozen";
            return hz.ToString("0.#") + " Hz (" + (1000f / hz).ToString("0") + " ms)";
        }

        /// <summary>Player / gameplay page.</summary>
        private void DrawPlayerTab()
        {
            if (GUILayout.Button("Free player (F5): " + (ExtrasRuntime.FreePlayers ? "ON" : "OFF"))) ExtrasRuntime.ToggleFreePlayers();
            if (GUILayout.Button("Freecam (F6): " + (FreeCam.Active ? "ON" : "OFF"))) FreeCam.Toggle();
            if (GUILayout.Button("Pause time (F9): " + (TimeTools.Paused ? "ON" : "OFF") + "  - freecam still moves")) TimeTools.Toggle();
            if (GUILayout.Button("Toggle sticker book (F3)")) EpisodeTools.ToggleStickerBook();
            GUILayout.Label("Noclip (F10): " + EpisodeTools.GetNoclipStatus());
            if (GUILayout.Button("Toggle noclip")) EpisodeTools.ToggleNoclip();
            if (GUILayout.Button("End cutscene (F4)")) EpisodeTools.EndCutscene();

            bool autoSkip = SlopModPlugin.AutoSkipCutscenes != null && SlopModPlugin.AutoSkipCutscenes.Value;
            if (GUILayout.Button("Auto-skip cutscenes: " + (autoSkip ? "ON" : "OFF")) && SlopModPlugin.AutoSkipCutscenes != null)
            {
                SlopModPlugin.AutoSkipCutscenes.Value = !SlopModPlugin.AutoSkipCutscenes.Value;
            }

            if (GUILayout.Button("Throw-window indicator: " + (ThrowWindowIndicator.Enabled ? "ON" : "OFF"))) ThrowWindowIndicator.Toggle();
        }

        /// <summary>Episode / save page.</summary>
        private void DrawEpisodeTab()
        {
            if (GUILayout.Button("Practice extended (clear save, Ep4 / phase 51)")) EpisodeTools.StartPracticeExtended();
            if (GUILayout.Button("Practice E1 jump (clear save, Ep1 / phase 26)")) EpisodeTools.StartPracticeE1Jump();
            if (GUILayout.Button("Restart practice level (F8)")) EpisodeTools.RestartPracticeLevel();
            GUILayout.Space(6f);

            if (!EpisodeTools.HasSave)
            {
                GUILayout.Label("No save profile loaded (episode tools unavailable).");
                return;
            }

            EpisodesProgress p = EpisodeTools.GetProgress();

            GUILayout.Label("Unlocked Episode: " + p.unlockedEpisode);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-", GUILayout.Width(32f))) EpisodeTools.AdjustUnlockedEpisode(-1);
            if (GUILayout.Button("+", GUILayout.Width(32f))) EpisodeTools.AdjustUnlockedEpisode(1);
            GUILayout.EndHorizontal();

            GUILayout.Label("Active Episode: " + p.activeEpisode);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-", GUILayout.Width(32f))) EpisodeTools.AdjustActiveEpisode(-1);
            if (GUILayout.Button("+", GUILayout.Width(32f))) EpisodeTools.AdjustActiveEpisode(1);
            GUILayout.EndHorizontal();

            GUILayout.Label("Episode Phase: " + p.episodePhase);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-", GUILayout.Width(32f))) EpisodeTools.AdjustPhase(-1);
            if (GUILayout.Button("+", GUILayout.Width(32f))) EpisodeTools.AdjustPhase(1);
            if (GUILayout.Button("Skip checkpoint")) EpisodeTools.EndCurrentCheckpoint();
            GUILayout.EndHorizontal();

            GUILayout.Label("Cutscene playing: " + EpisodeTools.IsCutscenePlaying());

            GUILayout.Label("Running State: " + p.wasEpisodeRunning);
            if (GUILayout.Button("Toggle running state")) EpisodeTools.ToggleRunning();

            GUILayout.Label("All Completed: " + p.allEpisodesCompleted);
            if (GUILayout.Button("Toggle all completed")) EpisodeTools.ToggleAllCompleted();

            GUILayout.Label("Unlocked Activities: " + (p.unlockedActivities != null ? p.unlockedActivities.Count : 0));
            GUILayout.Label("Unlocked Collectibles: " + (p.unlockedCollectiblesArea != null ? p.unlockedCollectiblesArea.Count : 0));

            int trashCount;
            int trashTotal;
            if (EpisodeTools.TryGetTrashBagCounter(out trashCount, out trashTotal))
            {
                GUILayout.Label("Trash in bag (Ep4 / Creek): " + trashCount + " / " + trashTotal);
            }

            if (GUILayout.Button("Save episode progress")) EpisodeTools.Save();
        }

        /// <summary>About / hotkey reference page.</summary>
        private void DrawAboutTab()
        {
            GUILayout.Label("Keyboard shortcuts");
            GUILayout.Space(4f);
            GUILayout.Label("F1   - toggle the state monitor");
            GUILayout.Label("F2   - toggle this window");
            GUILayout.Label("F3   - open / close the sticker book");
            GUILayout.Label("F4   - end the current cutscene");
            GUILayout.Label("F5   - free the player");
            GUILayout.Label("F6   - freecam (RMB look, WASD move, wheel = speed)");
            GUILayout.Label("F7   - reload the current level");
            GUILayout.Label("F8   - restart the current practice level (Ep1 phase 26 / Ep4 phase 51)");
            GUILayout.Label("F9   - pause / resume time (freecam still moves)");
            GUILayout.Label("F10  - toggle noclip");
            GUILayout.Label("PgUp - end the current checkpoint");
            GUILayout.Label("PgDn - go back one episode phase");
            GUILayout.Label("T    - show / hide trigger volumes");
            GUILayout.Label("C    - show / hide solid colliders");

            GUILayout.Space(8f);
            GUILayout.Label("Drag the window by its title bar or the strip at the top.");
            GUILayout.Label("Each category rebuilds on its own schedule (World tab > Update frequency).");
            GUILayout.Label("World tab: Display / Update frequency submenus, one submenu per");
            GUILayout.Label("category (Visible + Fill toggles) and a 'Hide world' toggle.");
        }

        private static int ShownCategoryCount()
        {
            int count = 0;
            for (int i = 0; i < WorldDebugView.CategoryCount; i++)
            {
                if (WorldDebugView.IsCategoryVisible(i)) count++;
            }
            return count;
        }
    }
}
