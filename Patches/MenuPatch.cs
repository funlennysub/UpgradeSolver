using HarmonyLib;

namespace UpgradeSolver.Patches;

[HarmonyPatch(typeof(Menu))]
public class MenuPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(Menu.Open))]
    private static void Open(Menu __instance)
    {
        Plugin.Instance.OnMenuOpen(__instance);
    }
}