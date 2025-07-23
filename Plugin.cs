using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace UpgradeSolver;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public Coroutine? solverCoroutine;

    internal static Plugin Instance = null!;
    internal static new ManualLogSource Logger;
    private static Harmony _harmony = null!;

    private SolverUI _solverUI = new SolverUI(null, null);

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        _harmony = new Harmony("UpgradeSolverPatches");
        _harmony.PatchAll(typeof(Patches.GearDetailsWindowPatch));
        _harmony.PatchAll(typeof(Patches.MenuPatch));

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private void OnDestroy()
    {
        _harmony.UnpatchSelf();
    }

    internal void OnMenuOpen(Menu menu)
    {
        _solverUI.Menu = menu;
    }

    internal void OnGearDetailsWindowOpen(GearDetailsWindow window)
    {
        _solverUI.GearDetailsWindow = window;
        _solverUI.PatchUpgradeClick();
        _solverUI.AddButtonWithPopout();
    }

    internal void OnGearDetailsWindowClosed()
    {
        _solverUI.Close();
    }
}