using HarmonyLib;

namespace SlopMod.Patches
{
    /// <summary>
    /// Creates the slopMod developer overlay as soon as the game boots.
    ///
    /// The legacy mod injected the overlay MonoBehaviour directly into the game's scene
    /// (DynamicDebugOverlay). A BepInEx plugin cannot edit scenes, so instead we postfix the game's
    /// own boot method, <c>GameManager.Initialize</c>, and create the overlay from there.
    /// </summary>
    [HarmonyPatch(typeof(GameManager), "Initialize")]
    internal static class GameManagerInitializePatch
    {
        // GameManager.Initialize() takes no parameters, so a parameterless postfix is valid.
        private static void Postfix()
        {
            if (SlopModPlugin.EnableDebugOverlay != null && SlopModPlugin.EnableDebugOverlay.Value)
            {
                DebugOverlay.EnsureExists();
            }
        }
    }
}
