using HarmonyLib;

namespace UpgradeSolver.Patches;

[HarmonyPatch(typeof(GearDetailsWindow))]
public class GearDetailsWindowPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(GearDetailsWindow.OnOpen))]
    private static void OnOpen(GearDetailsWindow __instance)
    {
        Plugin.Instance.OnGearDetailsWindowOpen(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(GearDetailsWindow.OnCloseCallback))]
    private static void OnCloseCallback(GearDetailsWindow __instance)
    {
        Plugin.Instance.OnGearDetailsWindowClosed();
    }
}