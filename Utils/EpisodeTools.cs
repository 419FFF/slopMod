using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KinematicCharacterController;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopMod
{
    /// <summary>
    /// Shared helpers that read/modify the save-file episode state and toggle noclip.
    ///
    /// This is the port of <c>DynamicDebugOverlay.ModifyEpisodeData</c>,
    /// <c>TriggerNativeNoclipToggle</c> and <c>GetNoclipStatusString</c>. Unlike the original it
    /// prefers the game's public API (UpdateEpisodeState / UpdateUnlockedEpisode) and only falls
    /// back to cached reflection for the members that have no public setter.
    /// </summary>
    internal static class EpisodeTools
    {
        // --- cached reflection (original used un-cached FieldInfo lookups every call) ---
        private static FieldInfo _gameProgressField;
        private static FieldInfo _episodesProgressField;
        private static FieldInfo _modifiedDataField;
        private static FieldInfo _ignoreCollidersField;

        private static FieldInfo GameProgressField
        {
            get
            {
                if (_gameProgressField == null) _gameProgressField = AccessTools.Field(typeof(SaveManager), "_gameProgress");
                return _gameProgressField;
            }
        }

        private static FieldInfo EpisodesProgressField
        {
            get
            {
                if (_episodesProgressField == null) _episodesProgressField = AccessTools.Field(typeof(GameProgressData), "episodesProgress");
                return _episodesProgressField;
            }
        }

        private static FieldInfo ModifiedDataField
        {
            get
            {
                if (_modifiedDataField == null) _modifiedDataField = AccessTools.Field(typeof(SaveManager), "_modifiedData");
                return _modifiedDataField;
            }
        }

        private static FieldInfo IgnoreCollidersField
        {
            get
            {
                if (_ignoreCollidersField == null) _ignoreCollidersField = AccessTools.Field(typeof(PlayerMovementKinematic), "_ignoreAllCollidersButGround");
                return _ignoreCollidersField;
            }
        }

        // EnumClass.Episodes has exactly four members (Episode1..Episode4).
        private static int EpisodeCount
        {
            get { return Enum.GetValues(typeof(EnumClass.Episodes)).Length; }
        }

        internal static bool HasSave
        {
            get { return GameManager.m_saveManager != null; }
        }

        internal static EpisodesProgress GetProgress()
        {
            SaveManager sm = GameManager.m_saveManager;
            return sm != null ? sm.GetEpisodesProgress() : default(EpisodesProgress);
        }

        // --- episode editing (feature: ModifyEpisodeData) ---

        internal static void AdjustUnlockedEpisode(int delta)
        {
            SaveManager sm = GameManager.m_saveManager;
            if (sm == null) return;

            EpisodesProgress p = sm.GetEpisodesProgress();
            // 0..EpisodeCount (EpisodeCount == "all unlocked").
            int value = Mathf.Clamp(p.unlockedEpisode + delta, 0, EpisodeCount);
            sm.UpdateUnlockedEpisode(value);
        }

        internal static void AdjustActiveEpisode(int delta)
        {
            SaveManager sm = GameManager.m_saveManager;
            if (sm == null) return;

            EpisodesProgress p = sm.GetEpisodesProgress();
            int value = Mathf.Clamp((int)p.activeEpisode + delta, 0, EpisodeCount - 1);
            Commit(sm, p, p.unlockedEpisode, value, p.episodePhase, p.wasEpisodeRunning, p.allEpisodesCompleted);
        }

        internal static void AdjustPhase(int delta)
        {
            SaveManager sm = GameManager.m_saveManager;
            if (sm == null) return;

            EpisodesProgress p = sm.GetEpisodesProgress();
            int value = Mathf.Max(0, p.episodePhase + delta);
            Commit(sm, p, p.unlockedEpisode, (int)p.activeEpisode, value, p.wasEpisodeRunning, p.allEpisodesCompleted);
        }

        internal static void ToggleRunning()
        {
            SaveManager sm = GameManager.m_saveManager;
            if (sm == null) return;

            EpisodesProgress p = sm.GetEpisodesProgress();
            Commit(sm, p, p.unlockedEpisode, (int)p.activeEpisode, p.episodePhase, !p.wasEpisodeRunning, p.allEpisodesCompleted);
        }

        internal static void ToggleAllCompleted()
        {
            SaveManager sm = GameManager.m_saveManager;
            if (sm == null) return;

            EpisodesProgress p = sm.GetEpisodesProgress();
            Commit(sm, p, p.unlockedEpisode, (int)p.activeEpisode, p.episodePhase, p.wasEpisodeRunning, !p.allEpisodesCompleted);
        }

        internal static void EndCurrentCheckpoint()
        {
            GameplayManager gm = GameManager.m_gameplayManager;
            if (gm == null || gm.m_episodeManager == null) return;

            // EndCurrentCheckPoint() dereferences the current checkpoint, so guard against running it
            // when no episode is active (otherwise it throws a NullReferenceException).
            if (!gm.m_episodeManager.EpisodeActive()) return;

            gm.m_episodeManager.EndCurrentCheckPoint();
        }

        // --- cutscene (feature: end the current cinematic) ---

        internal static bool IsCutscenePlaying()
        {
            GameplayManager gm = GameManager.m_gameplayManager;
            return gm != null && gm.m_cinematicManager != null && gm.m_cinematicManager.IsCinematicPlaying();
        }

        /// <summary>
        /// Ends the cutscene that is currently playing by invoking the game's own skip logic
        /// (CinematicManager.SkipCinematic jumps the PlayableDirector to the end of the timeline).
        /// </summary>
        internal static void EndCutscene()
        {
            GameplayManager gm = GameManager.m_gameplayManager;
            if (gm == null || gm.m_cinematicManager == null) return;

            if (!gm.m_cinematicManager.IsCinematicPlaying())
            {
                if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo("No cutscene is playing.");
                return;
            }

            gm.m_cinematicManager.SkipCinematic();
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo("Cutscene skipped.");
        }

        // --- sticker book (feature: open it anywhere) ---

        internal static bool IsStickerBookOpen()
        {
            GameplayManager gm = GameManager.m_gameplayManager;
            return gm != null && gm.m_stickerManager != null && gm.m_stickerManager.IsMenuOpen();
        }

        /// <summary>
        /// Opens (or closes) the sticker book from anywhere using the game's own
        /// StickerManager.OpenMenu/CloseMenu - normally this is only reachable from the bedroom.
        /// </summary>
        internal static void ToggleStickerBook()
        {
            GameplayManager gm = GameManager.m_gameplayManager;
            if (gm == null || gm.m_stickerManager == null)
            {
                if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogWarning("Sticker book unavailable (no active gameplay).");
                return;
            }

            if (gm.m_stickerManager.IsMenuOpen())
            {
                gm.m_stickerManager.CloseMenu();
                if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo("Sticker book closed.");
                return;
            }

            PlayerController player = gm.GetFirstPlayerControllerByInputID();
            if (player == null)
            {
                if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogWarning("Sticker book unavailable (no active player).");
                return;
            }

            gm.m_stickerManager.OpenMenu(player.m_inputID, false, false);
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo("Sticker book opened.");
        }

        internal static void Save()
        {
            SaveManager sm = GameManager.m_saveManager;
            if (sm != null) sm.SaveEpisodesProgress();
        }

        /// <summary>
        /// Mirrors the legacy ModifyEpisodeData exactly: it calls UpdateEpisodeState for the fields
        /// that API owns and then writes the *whole* episodesProgress struct (unlockedEpisode,
        /// activeEpisode, episodePhase, wasEpisodeRunning, allEpisodesCompleted) back through
        /// reflection. The legacy version did this so that the "Running State" / "All Completed"
        /// toggles persist - writing only some fields left wasEpisodeRunning unchanged.
        /// </summary>
        private static void Commit(SaveManager sm, EpisodesProgress original, int unlocked, int active, int phase, bool running, bool allCompleted)
        {
            EnumClass.Episodes activeEpisode = (EnumClass.Episodes)active;
            Vector3 returnPoints = new Vector3(original.returnPositions.x, original.returnPositions.y, original.returnPositions.z);

            sm.UpdateEpisodeState(running, activeEpisode, phase, original.spawnPoints, returnPoints); // also marks dirty [2]

            FieldInfo gpField = GameProgressField;
            FieldInfo epField = EpisodesProgressField;
            if (gpField == null || epField == null) return;

            // GameProgressData is a struct -> get the boxed value, mutate it and write it back.
            object gp = gpField.GetValue(sm);
            if (gp == null) return;

            EpisodesProgress ep = (EpisodesProgress)epField.GetValue(gp);
            ep.unlockedEpisode = unlocked;
            ep.activeEpisode = activeEpisode;
            ep.episodePhase = phase;
            ep.wasEpisodeRunning = running;
            ep.allEpisodesCompleted = allCompleted;
            epField.SetValue(gp, ep);
            gpField.SetValue(sm, gp);

            bool[] modified = ModifiedDataField.GetValue(sm) as bool[];
            if (modified != null && modified.Length > 2) modified[2] = true; // CachedElements.Episodes
        }

        // --- noclip (feature: TriggerNativeNoclipToggle / GetNoclipStatusString) ---

        internal static string GetNoclipStatus()
        {
            FieldInfo field = IgnoreCollidersField;
            if (field == null) return "n/a";

            List<PlayerMovementKinematic> movements = FindPlayerMovements();
            if (movements.Count == 0) return "n/a";

            for (int i = 0; i < movements.Count; i++)
            {
                if ((bool)field.GetValue(movements[i])) return "ENABLED";
            }
            return "DISABLED";
        }

        internal static void ToggleNoclip()
        {
            FieldInfo field = IgnoreCollidersField;
            if (field == null) return;

            List<PlayerMovementKinematic> movements = FindPlayerMovements();
            if (movements.Count == 0)
            {
                if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogWarning("No PlayerMovementKinematic found - noclip unavailable.");
                return;
            }

            bool anyEnabled = false;
            for (int i = 0; i < movements.Count; i++)
            {
                if ((bool)field.GetValue(movements[i])) { anyEnabled = true; break; }
            }

            bool enable = !anyEnabled;
            for (int i = 0; i < movements.Count; i++)
            {
                // Use the game's own noclip API (it also clears the nav-mesh-jump flag, like the
                // legacy mod's on-field toggle but through the public methods).
                if (enable) movements[i].IgnoreAllColliders();
                else movements[i].DisableIgnoreAllColliders();
            }

            if (SlopModPlugin.Log != null)
            {
                SlopModPlugin.Log.LogInfo("Noclip " + (enable ? "ENABLED" : "DISABLED") + " (" + movements.Count + " player(s))");
            }
        }

        // --- free player (release the game's movement lock) ---

        /// <summary>
        /// Sets the game's own movement lock on every active player. The game locks the player in
        /// place with <c>CanMove(false)</c> / <c>CanMoveWithInput(false)</c>; passing <c>true</c>
        /// releases them.
        /// </summary>
        internal static void SetPlayersCanMove(bool canMove)
        {
            List<PlayerMovementKinematic> movements = FindPlayerMovements();
            for (int i = 0; i < movements.Count; i++)
            {
                movements[i].CanMove(canMove);
                movements[i].CanMoveWithInput(canMove);
            }
        }

        /// <summary>
        /// Collects the movement controller of EVERY active player. The legacy mod toggled all active
        /// PlayerControllers (matching the component by name via a child search); toggling only the
        /// first one (via GetComponent) is why noclip did not work properly.
        /// </summary>
        private static List<PlayerMovementKinematic> FindPlayerMovements()
        {
            List<PlayerMovementKinematic> result = new List<PlayerMovementKinematic>();

            GameplayManager gm = GameManager.m_gameplayManager;
            if (gm != null && gm.m_playerControllers != null)
            {
                for (int i = 0; i < gm.m_playerControllers.Length; i++)
                {
                    PlayerController pc = gm.m_playerControllers[i];
                    if (pc == null || !pc.gameObject.activeInHierarchy) continue;
                    PlayerMovementKinematic pm = pc.GetComponentInChildren<PlayerMovementKinematic>(true);
                    if (pm != null && !result.Contains(pm)) result.Add(pm);
                }
            }

            if (result.Count == 0)
            {
                // Fallback for odd scene states (menu, transitions): scan like the legacy mod.
                PlayerMovementKinematic[] all = UnityEngine.Object.FindObjectsOfType<PlayerMovementKinematic>();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].gameObject.activeInHierarchy) result.Add(all[i]);
                }
            }
            return result;
        }

        // --- practice mode: jump straight into a chosen episode / phase ---

        internal const int PracticeEpisodePhase = 51;   // Episode 4 - the trash area of The Creek
        internal const int PracticeE1EpisodePhase = 26; // Episode 1

        /// <summary>
        /// True under the same conditions the Episode 4 trash counter appears: the active save is on
        /// Episode 4 and the loaded scene is The Creek.
        /// </summary>
        internal static bool IsTrashScene()
        {
            SaveManager sm = GameManager.m_saveManager;
            if (sm == null) return false;

            EpisodesProgress progress = sm.GetEpisodesProgress();
            if (progress.activeEpisode != EnumClass.Episodes.Episode4) return false;
            return SceneManager.GetActiveScene().name == EnumClass.Scenes.TheCreek.ToString();
        }

        /// <summary>
        /// "PRACTICE EXTENDED": wipes the save, parks it at Episode 4 / phase 51 and loads the level.
        /// </summary>
        internal static void StartPracticeExtended()
        {
            StartPractice(EnumClass.Episodes.Episode4, PracticeEpisodePhase, "extended");
        }

        /// <summary>
        /// "PRACTICE E1 JUMP": wipes the save, parks it at Episode 1 / phase 26 and loads the level.
        /// </summary>
        internal static void StartPracticeE1Jump()
        {
            StartPractice(EnumClass.Episodes.Episode1, PracticeE1EpisodePhase, "E1 jump");
        }

        /// <summary>
        /// Wipes the save, writes a fresh slot parked at <paramref name="episode"/> /
        /// <paramref name="phase"/> and restarts the level (the main menu Start Game / F7 flow).
        /// </summary>
        private static void StartPractice(EnumClass.Episodes episode, int phase, string label)
        {
            SaveManager sm = GameManager.m_saveManager;
            if (sm == null)
            {
                LogInfo("Practice " + label + ": no SaveManager.");
                return;
            }

            LogInfo("Practice " + label + ": clearing the save, starting " + episode + " phase " + phase + "...");

            // 1. Clear the save.
            SaveManager.DeleteAll();

            // 2. Fresh slot 1 in memory, parked at the practice state.
            sm.LoadAllProgress(1);
            ApplyPracticeState(sm, episode, phase);
            sm.SaveEpisodesProgress(); // persists the slot and marks it as used

            // 3. Start the level (the main menu's Start Game / F7 reload flow).
            ExtrasRuntime.ReloadLevel();
        }

        /// <summary>
        /// Restarts the level in the practice state matching the currently-active practice episode
        /// (Episode 1 -> phase 26, Episode 4 -> phase 51). Safe to bind to a hotkey while practising.
        /// </summary>
        internal static void RestartPracticeLevel()
        {
            SaveManager sm = GameManager.m_saveManager;
            if (sm == null) return;

            EpisodesProgress p = sm.GetEpisodesProgress();
            EnumClass.Episodes episode;
            int phase;
            if (p.activeEpisode == EnumClass.Episodes.Episode1)
            {
                episode = EnumClass.Episodes.Episode1;
                phase = PracticeE1EpisodePhase;
            }
            else if (p.activeEpisode == EnumClass.Episodes.Episode4)
            {
                episode = EnumClass.Episodes.Episode4;
                phase = PracticeEpisodePhase;
            }
            else
            {
                LogInfo("Practice restart ignored (active episode is not a practice episode).");
                return;
            }

            LogInfo("Practice restart: reloading " + episode + " phase " + phase + "...");
            ApplyPracticeState(sm, episode, phase);
            sm.SaveEpisodesProgress();
            ExtrasRuntime.ReloadLevel();
        }

        private static void ApplyPracticeState(SaveManager sm, EnumClass.Episodes episode, int phase)
        {
            EpisodesProgress p = sm.GetEpisodesProgress();
            Vector3 returnPoints = new Vector3(p.returnPositions.x, p.returnPositions.y, p.returnPositions.z);
            sm.UpdateUnlockedEpisode(EpisodeCount); // 4 == everything unlocked
            sm.UpdateEpisodeState(true, episode, phase, p.spawnPoints, returnPoints);
        }

        private static void LogInfo(string message)
        {
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo(message);
        }

        // --- Episode 4 "throw the trash into the bag" counter (The Creek) ---

        private static int _trashBagCount = -1;
        private static int _trashBagTotal;
        private static float _trashBagNextCheck;

        /// <summary>
        /// While in Episode 4 and the loaded scene is The Creek, reports how many items are in the
        /// trash bag (<c>TrashBag.count</c>) and the total number of trash points. Returns false
        /// anywhere else.
        /// </summary>
        internal static bool TryGetTrashBagCounter(out int count, out int total)
        {
            count = 0;
            total = 0;

            if (!IsTrashScene()) return false;

            float now = Time.unscaledTime;
            if (now >= _trashBagNextCheck)
            {
                _trashBagNextCheck = now + 0.5f;

                TrashBag bag = null;
                TrashBag[] found = UnityEngine.Object.FindObjectsOfType<TrashBag>(true);
                if (found != null && found.Length > 0) bag = found[0];

                if (bag != null)
                {
                    _trashBagCount = bag.count;
                    _trashBagTotal = bag.m_trashPoints != null ? bag.m_trashPoints.Length : 0;
                }
                else
                {
                    _trashBagCount = -1;
                }
            }

            if (_trashBagCount < 0) return false;
            count = _trashBagCount;
            total = _trashBagTotal;
            return true;
        }
    }
}
