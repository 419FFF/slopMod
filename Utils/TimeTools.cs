using UnityEngine;

namespace SlopMod
{
    /// <summary>
    /// "Pause time" debug toggle (hotkey F9). It sets <c>Time.timeScale = 0</c> - exactly the mechanism
    /// the game itself uses in <c>GameplayManager.PauseTime()</c> - so gameplay, physics and every
    /// scaled animation stop, but WITHOUT opening the pause menu or touching the gameplay state.
    ///
    /// The camera can still move while time is paused: <see cref="FreeCam"/> reads input from the new
    /// Input System and integrates on <c>Time.unscaledDeltaTime</c> in <c>LateUpdate</c>, neither of
    /// which the time scale affects - so press F6 and fly around the frozen world.
    ///
    /// The scale is remembered when pausing and restored when resuming, so a game that had time slowed
    /// (not the case here) would be put back exactly as it was.
    /// </summary>
    internal static class TimeTools
    {
        private static bool _paused;
        private static float _savedTimeScale = 1f;

        /// <summary>True while slopMod is holding time paused.</summary>
        internal static bool Paused { get { return _paused; } }

        internal static void Toggle()
        {
            SetPaused(!_paused);
        }

        internal static void SetPaused(bool paused)
        {
            if (paused == _paused)
            {
                // Already in the requested state; just make sure the clock matches (the game may have
                // reset it behind our back, e.g. on a scene load).
                if (paused) Time.timeScale = 0f;
                return;
            }

            _paused = paused;

            if (paused)
            {
                _savedTimeScale = Time.timeScale > 0.0001f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = _savedTimeScale > 0.0001f ? _savedTimeScale : 1f;
                _savedTimeScale = 1f;
            }

            Info("Time " + (paused ? "paused" : "resumed") + " (timeScale = " + Time.timeScale.ToString("0.##") + ").");
        }

        /// <summary>
        /// Keeps the toggle honest: if something else resumed time (a scene load and the game's own
        /// pause menu both set <c>Time.timeScale = 1</c>), drop back to "not paused" instead of showing
        /// a stale "ON". Called once a frame from <see cref="ExtrasRuntime"/>.
        /// </summary>
        internal static void SyncWithGame()
        {
            if (_paused && Time.timeScale > 0.0001f) _paused = false;
        }

        private static void Info(string message)
        {
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo(message);
        }
    }
}
