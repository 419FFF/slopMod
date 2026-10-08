using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopMod
{
    /// <summary>
    /// Always-on runtime that drives the "Extras" game tweaks:
    ///   * Free player   - keeps every player movable even while the game locks them in place
    ///                     (the game blocks movement via <c>PlayerMovementKinematic.CanMove(false)</c> /
    ///                     <c>CanMoveWithInput(false)</c> during cutscenes, dialogs, character select...).
    ///                     Re-applied a few times a second so it wins even if the game re-locks.
    ///   * Auto-skip cutscenes - ends every cutscene exactly like pressing F4
    ///                     (<c>CinematicManager.SkipCinematic</c>).
    ///   * Reload level  - tears gameplay down and reloads the GameplayLoader scene (hotkey F7).
    ///
    /// It also keeps the "pause time" toggle (hotkey F9, <see cref="TimeTools"/>) honest: if the game
    /// resumes time by itself (a scene load or its own pause menu both set <c>Time.timeScale = 1</c>),
    /// the toggle drops back to "off" instead of showing a stale state.
    /// </summary>
    internal sealed class ExtrasRuntime : MonoBehaviour
    {
        internal static ExtrasRuntime Instance { get; private set; }

        /// <summary>"Free player" is a session-only toggle (hotkey F5).</summary>
        internal static bool FreePlayers;

        private float _nextCutsceneSkip;
        private float _nextFreeReapply;

        internal static void EnsureExists()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("SlopMod_ExtrasRuntime");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ExtrasRuntime>();
        }

        internal static void ToggleFreePlayers()
        {
            EnsureExists();
            FreePlayers = !FreePlayers;
            if (FreePlayers) EpisodeTools.SetPlayersCanMove(true);
            if (SlopModPlugin.Log != null)
            {
                SlopModPlugin.Log.LogInfo("Free player " + (FreePlayers ? "ON" : "OFF"));
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Object.Destroy(gameObject);
                return;
            }
            Instance = this;
            Object.DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            // Keep the "pause time" toggle (F9) honest if the game resumed time by itself.
            TimeTools.SyncWithGame();

            if (FreePlayers && now >= _nextFreeReapply)
            {
                _nextFreeReapply = now + 0.1f;
                EpisodeTools.SetPlayersCanMove(true);
            }

            if (SlopModPlugin.AutoSkipCutscenes != null && SlopModPlugin.AutoSkipCutscenes.Value && now >= _nextCutsceneSkip)
            {
                if (EpisodeTools.IsCutscenePlaying())
                {
                    _nextCutsceneSkip = now + 0.25f;
                    EpisodeTools.EndCutscene();
                }
            }
        }

        // --- reload level ---

        /// <summary>
        /// Fully reloads the current level. This mirrors the game's own "return to main menu, then
        /// Start Game" flow: drop the active GameplayManager (its OnDestroy runs
        /// UnregisterGameplayManager and tears everything down), then reload the GameplayLoader
        /// bootstrap scene in Single mode so the level is rebuilt from scratch at the current
        /// save/checkpoint.
        /// </summary>
        internal static void ReloadLevel()
        {
            EnsureExists();
            Instance.StartCoroutine(Instance.ReloadRoutine());
        }

        private IEnumerator ReloadRoutine()
        {
            SceneLoaderManager loader = GameManager.m_sceneLoaderManager;
            if (loader == null)
            {
                Warn("Reload level: no SceneLoaderManager.");
                yield break;
            }
            if (loader.IsLoadingScene())
            {
                Warn("Reload level: a scene is already loading.");
                yield break;
            }

            Info("Reload level: restarting the current level...");

            // Drop the current gameplay manager so the reload starts clean instead of stacking a
            // second manager (exactly what the game does when you return to the main menu).
            if (GameManager.m_gameplayManager != null)
            {
                Object.Destroy(GameManager.m_gameplayManager.gameObject);
            }

            // Let the Destroy take effect before the scene swap.
            yield return null;

            // Re-enter gameplay through the GameplayLoader bootstrap scene, exactly like "Start Game".
            loader.LoadScene(
                EnumClass.Scenes.GameplayLoader.ToString(),
                EnumClass.ScreenLoader.LoadingScreen,
                false,
                LoadSceneMode.Single,
                true,
                EnumClass.DayTime.Morning,
                null);

            if (GameManager.m_gameStateManager != null)
            {
                GameManager.m_gameStateManager.ChangeGameState(StatesClass.GameState.SandboxState);
            }
        }

        private static void Info(string message)
        {
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo(message);
        }

        private static void Warn(string message)
        {
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogWarning(message);
        }
    }
}
