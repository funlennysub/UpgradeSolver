using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace UpgradeSolver;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public Coroutine? SolverCoroutine;

    internal static Plugin Instance = null!;
    internal new static ManualLogSource Logger = null!;
    private static Harmony _harmony = null!;

    internal SolverUI SolverUI = new(null);

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        _harmony = new Harmony("UpgradeSolverPatches");
        _harmony.PatchAll(typeof(Patches.GearDetailsWindowPatch));

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private void OnDestroy()
    {
        _harmony.UnpatchSelf();
    }

    internal void OnGearDetailsWindowOpen(GearDetailsWindow window)
    {
        SolverUI.GearDetailsWindow = window;
        SolverUI.PatchUpgradeClick();
        SolverUI.AddSolveButton();
    }

    internal void OnGearDetailsWindowClosed()
    {
        SolverUI.Close();
    }
}