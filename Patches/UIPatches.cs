using HarmonyLib;

namespace SlopMod.Patches
{
    /// <summary>
    /// Attaches the ported "ModMenuSpy" behaviour (<see cref="ModMenuController"/>) when the main
    /// menu appears.
    ///
    /// The legacy mod placed the ModMenuSpy component on the menu GameObject in the scene; a BepInEx
    /// plugin instead postfixes <c>MainMenu.Start</c> (the game's own main-menu entry point).
    /// </summary>
    [HarmonyPatch(typeof(MainMenu), "Start")]
    internal static class MainMenuStartPatch
    {
        private static void Postfix(MainMenu __instance)
        {
            if (SlopModPlugin.EnableModMenu != null && SlopModPlugin.EnableModMenu.Value)
            {
                ModMenuController.AttachTo(__instance);
            }
        }
    }
}
