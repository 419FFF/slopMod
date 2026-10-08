using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace SlopMod
{
    /// <summary>
    /// slopMod - a clean, standalone BepInEx 6 (Unity Mono) plugin that ports the custom features
    /// that were previously edited directly into the game's Assembly-CSharp
    /// ("old slopMod implementation").
    ///
    /// Ported features:
    ///   * Developer overlay (F1 monitor / F2 toolbox) with live episode + save state.
    ///   * Episode state editing (unlocked / active episode, phase, running, all-completed).
    ///   * "Skip checkpoint" (EpisodeManager.EndCurrentCheckPoint).
    ///   * Noclip toggle (PlayerMovementKinematic._ignoreAllCollidersButGround).
    ///   * Free player (F5) releases the game's movement lock.
    ///   * A native EXTRAS submenu on the main menu (auto-skip cutscenes).
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class SlopModPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.slopmod.plugin";
        public const string PluginName = "SlopMod";
        public const string PluginVersion = "1.0.0";

        internal static SlopModPlugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        internal static ConfigEntry<bool> EnableDebugOverlay;
        internal static ConfigEntry<bool> EnableModMenu;
        internal static ConfigEntry<bool> AutoSkipCutscenes;
        internal static ConfigEntry<bool> ShowThrowWindowIndicator;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            EnableDebugOverlay = Config.Bind(
                "General", "EnableDebugOverlay", true,
                "Create the slopMod developer overlay (F1 = monitor, F2 = toolbox, F10 = noclip).");
            EnableModMenu = Config.Bind(
                "General", "EnableModMenu", true,
                "Add the slopMod EXTRAS entry to the main menu.");

            AutoSkipCutscenes = Config.Bind(
                "Extras", "AutoSkipCutscenes", false,
                "Automatically end every cutscene (same as pressing F4). Also toggled from the in-game EXTRAS menu.");

            ShowThrowWindowIndicator = Config.Bind(
                "Extras", "ShowThrowWindowIndicator", true,
                "Show an on-screen indicator while the ~0.4 s throw-detach window is active. Re-throwing " +
                "during that window is what makes the Episode 4 trash-bag dupe (and its phase skips) work.");

            // Create the Harmony instance and apply every [HarmonyPatch] in this assembly.
            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginGuid);

            if (EnableDebugOverlay.Value)
            {
                DebugOverlay.EnsureExists();
            }

            ExtrasRuntime.EnsureExists();

            ThrowWindowIndicator.Enabled = ShowThrowWindowIndicator.Value;
            ThrowWindowIndicator.EnsureExists();

            Log.LogInfo(PluginName + " v" + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
            DebugOverlay.DestroyInstance();
        }
    }
}
