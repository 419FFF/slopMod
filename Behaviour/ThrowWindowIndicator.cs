using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SlopMod
{
    /// <summary>
    /// On-screen indicator for the "throw detach" window - the ~0.4 s during which a thrown item is
    /// <b>still attached</b> to the player even though the throw animation has already started.
    ///
    /// While that window is open the held item has not been released yet, so anything that re-fires the
    /// throw (the Episode 4 trash-bin action, the gameplay cancel stack, re-picking) spawns extra throw
    /// coroutines and the item gets counted more than once. This is exactly the window that makes the
    /// "The Creek" trash-bag glitch (and the resulting episode-phase skips) possible, so the indicator
    /// lights up for as long as the timer runs and counts it down.
    ///
    /// The window is <c>PickableItem._isThrowing</c>:
    ///   * set true in <c>PickableItem.OnPlayerThrow</c> (which both the normal throw AND
    ///     <c>ManualThrowAnimationReset</c>, the entry point used by <c>TrashBag</c>, call), and
    ///   * set false at the end of <c>WaitThrowItemAnimation</c>, right after the
    ///     <c>_waitTimeThrowAnim</c> (0.4 s) <c>WaitForSeconds</c>.
    /// Both the flag and the duration are read with cached reflection, so no game code is patched and
    /// the plugin stays read-only against Assembly-CSharp.
    /// </summary>
    internal sealed class ThrowWindowIndicator : MonoBehaviour
    {
        internal static ThrowWindowIndicator Instance { get; private set; }

        /// <summary>Session toggle (toolbox button); seeded from the plugin config at boot.</summary>
        internal static bool Enabled = true;

        // --- cached reflection (the game exposes no public getter for either member) ---
        private static FieldInfo _isThrowingField;
        private static FieldInfo _durationField;

        // --- live window state, refreshed once a frame in Update() ---
        private bool _active;
        private float _startTime;
        private float _duration = 0.4f;

        private GUIStyle _titleStyle;
        private GUIStyle _valueStyle;
        private bool _stylesReady;

        /// <summary>True while the throw-detach timer is running (the dupe window is open).</summary>
        internal bool WindowActive { get { return _active; } }

        /// <summary>Seconds left on the detach timer (0 when it is not running).</summary>
        internal float WindowRemainingSeconds
        {
            get { return _active ? Mathf.Max(0f, _duration - (Time.time - _startTime)) : 0f; }
        }

        internal static void EnsureExists()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("SlopMod_ThrowWindowIndicator");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ThrowWindowIndicator>();
        }

        internal static void Toggle()
        {
            EnsureExists();
            Enabled = !Enabled;
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
            bool active = false;
            float duration = 0.4f;

            GameplayManager gm = GameManager.m_gameplayManager;
            if (gm != null)
            {
                PlayerController player = gm.GetFirstPlayerControllerByInputID();
                if (player != null)
                {
                    PickableItem item = player.GetPickableItem();
                    if (item != null && IsThrowing(item))
                    {
                        active = true;
                        duration = GetThrowDuration(item);
                    }
                }
            }

            // Rising edge: remember when the window opened so the bar can count down. The game waits with
            // WaitForSeconds (scaled time), so Time.time is the matching clock.
            if (active && !_active) _startTime = Time.time;

            _active = active;
            _duration = duration > 0.0001f ? duration : 0.4f;
        }

        private void OnGUI()
        {
            if (!Enabled || !_active) return;
            EnsureStyles();
            DrawIndicator();
        }

        private static bool IsThrowing(PickableItem item)
        {
            if (_isThrowingField == null) _isThrowingField = AccessTools.Field(typeof(PickableItem), "_isThrowing");
            if (_isThrowingField == null) return false;

            object value = _isThrowingField.GetValue(item);
            return value != null && (bool)value;
        }

        private static float GetThrowDuration(PickableItem item)
        {
            if (_durationField == null) _durationField = AccessTools.Field(typeof(PickableItem), "_waitTimeThrowAnim");
            if (_durationField == null) return 0.4f;

            object value = _durationField.GetValue(item);
            return value != null ? (float)value : 0.4f;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 14;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.normal.textColor = Color.white;

            _valueStyle = new GUIStyle(GUI.skin.label);
            _valueStyle.fontSize = 13;
            _valueStyle.normal.textColor = new Color(0.8f, 1f, 0.8f);

            _stylesReady = true;
        }

        private void DrawIndicator()
        {
            float remaining = WindowRemainingSeconds;
            float filled = _duration > 0.0001f ? Mathf.Clamp01(remaining / _duration) : 0f;

            const float width = 400f;
            const float height = 60f;
            float x = (Screen.width - width) * 0.5f;
            const float y = 16f;

            Color previous = GUI.color;

            // Backdrop so the text stays readable on top of gameplay.
            GUI.color = new Color(0f, 0f, 0f, 0.68f);
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);

            // Countdown bar (drains left -> right as the window closes).
            Rect barBg = new Rect(x + 10f, y + height - 20f, width - 20f, 10f);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(barBg, Texture2D.whiteTexture);

            GUI.color = BarColor(filled);
            GUI.DrawTexture(new Rect(barBg.x, barBg.y, barBg.width * filled, barBg.height), Texture2D.whiteTexture);

            GUI.color = previous;

            GUI.Label(new Rect(x + 10f, y + 6f, width - 20f, 20f), "THROW WINDOW ACTIVE", _titleStyle);
            GUI.Label(
                new Rect(x + 10f, y + 26f, width - 20f, 20f),
                _duration.ToString("0.0") + " s detach timer - " + remaining.ToString("0.00") + " s left (re-throw now to dupe)",
                _valueStyle);
        }

        private static Color BarColor(float filled)
        {
            if (filled > 0.5f) return new Color(0.25f, 0.9f, 0.3f);  // plenty of time left
            if (filled > 0.2f) return new Color(0.95f, 0.75f, 0.2f); // getting close
            return new Color(0.95f, 0.3f, 0.25f);                    // about to close
        }
    }
}
