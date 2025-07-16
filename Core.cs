using System.Collections;
using HarmonyLib;
using Il2Cpp;
using Il2CppPigeon.UI;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UpgradeSolver;
using Rect = Il2CppPigeon.UI.Rect;

[assembly: MelonInfo(typeof(Core), "UpgradeSolver", "0.0.1", "funlennysub")]
[assembly: MelonGame("Pigeons at Play", "Mycopunk")]

namespace UpgradeSolver;

[HarmonyPatch(typeof(GearDetailsWindow))]
internal class GearDetailsWindowPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(GearDetailsWindow.OnOpen))]
    private static void OnOpen(GearDetailsWindow __instance)
    {
        Melon<Core>.Instance.OnGearDetailsWindowOpened(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(GearDetailsWindow.OnCloseCallback))]
    private static void OnCloseCallback(GearDetailsWindow __instance)
    {
        Melon<Core>.Instance.OnGearDetailsWindowClosed(__instance);
    }
}

[HarmonyPatch(typeof(Menu))]
internal class MenuPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Menu.Open))]
    private static void Open(Menu __instance)
    {
        Melon<Core>.Instance.OnMenuOpen(__instance);
    }
}

internal class Solver
{
    private readonly IUpgradable _gear;
    private readonly GearDetailsWindow _gearDetailsWindow;
    private readonly HexMap _hexMap;
    // TODO: Replace this with `PlayerData.RotateUpgradesLevelThreshold` >= `PlayerData.GetPlayerLevel()`
    private readonly int _maxRotations = Melon<Core>.Instance._isRotateModInstalled ? 6 : 1;

    private readonly Dictionary<(UpgradeInstance, int), (int offsetX, int offsetY)> _offsetCache = new();
    private readonly List<UpgradeInstance> _upgrades;

    private bool _foundSolution;

    public Solver(GearDetailsWindow gearDetailsWindow, List<UpgradeInstance> upgrades)
    {
        _gearDetailsWindow = gearDetailsWindow;
        _gear = _gearDetailsWindow.UpgradablePrefab;
        _hexMap = _gearDetailsWindow.equipSlots.HexMap;
        _upgrades = upgrades;
    }

    private static HexMap.Node GetNode(HexMap map, int x, int y)
    {
        if (map.nodes is not { Length: > 0 } rows || y >= rows.Length)
            return null;
        if (rows[y]?.nodes is not { Length: > 0 } rowNodes || x >= rowNodes.Length)
            return null;

        return rowNodes[x];
    }

    private (int offsetX, int offsetY) GetOffsetsCached(UpgradeInstance upgrade, int rotation, UpgradeEquipCell cell)
    {
        var key = (upgrade, rotation);
        if (_offsetCache.TryGetValue(key, out var baseOffset))
            return (cell.X + baseOffset.offsetX, cell.Y + baseOffset.offsetY);
        var pattern = upgrade.Pattern;
        var modifiedMap = pattern.GetModifiedMap(rotation);
        var patternWidthHalf = modifiedMap.width / 2;

        var sumY = 0;
        var enabledCount = 0;

        for (var row = 0; row < modifiedMap.height; row++)
        {
            var node = GetNode(modifiedMap, patternWidthHalf, row);
            if (node?.enabled != true) continue;
            sumY += row;
            enabledCount++;
        }

        var avgY = enabledCount > 0 ? sumY / enabledCount : 0;
        var offsetY = -avgY;
        if (patternWidthHalf % 2 == 1) offsetY--;

        baseOffset = (-patternWidthHalf, offsetY);
        _offsetCache[key] = baseOffset;

        return (cell.X + baseOffset.offsetX, cell.Y + baseOffset.offsetY);
    }

    private void ClearSlots()
    {
        var prefab = _gearDetailsWindow.UpgradablePrefab;

        var gearUpgrades = PlayerData.GetAllUpgrades(prefab);

        var playerUpgrades = new Il2CppSystem.Collections.Generic.List<UpgradeInfo>();
        if (prefab.GearType == GearType.Character)
        {
            var playerUpgradesPrefab = Global.Instance.Cast<IUpgradable>();
            playerUpgrades = PlayerData.GetAllUpgrades(playerUpgradesPrefab);
        }

        var allUpgrades = gearUpgrades._items.Concat(playerUpgrades._items).Where(u => u is not null);
        foreach (var upgradeInfo in allUpgrades)
        {
            if (upgradeInfo.Instances is null) continue;
            foreach (var upgradeInstance in upgradeInfo.Instances)
            {
                if (upgradeInstance is null) continue;
                _gearDetailsWindow.equipSlots.Unequip(prefab, upgradeInstance);
            }
        }
    }

    private IEnumerator SolveDfs(int index)
    {
        if (index >= _upgrades.Count)
        {
            _foundSolution = true;
            yield break;
        }

        var upgrade = _upgrades[index];

        for (var y = 0; y < _hexMap.Height; y++)
        for (var x = 0; x < _hexMap.Width; x++)
        for (var rotation = 0; rotation < _maxRotations; rotation++)
        {
            var cell = _gearDetailsWindow.equipSlots.GetCell(x, y);
            if (cell == null || cell.Upgrade is not null)
                continue;

            var (offsetX, offsetY) = GetOffsetsCached(upgrade, rotation, cell);

            if (!_gearDetailsWindow.equipSlots.EquipModule(_gear, upgrade, offsetX, offsetY, (byte)rotation))
                continue;

            yield return SolveDfs(index + 1);

            if (_foundSolution)
                yield break;

            _gearDetailsWindow.equipSlots.Unequip(_gear, upgrade);
        }
    }

    private IEnumerator SolveAndNotify(Action<bool> onComplete)
    {
        yield return SolveDfs(0);

        onComplete.Invoke(_foundSolution);
    }

    public void TrySolve(Action<bool> onComplete)
    {
        _foundSolution = false;
        ClearSlots();

        if (!CanFitAll())
        {
            onComplete?.Invoke(false);
            return;
        }

        Melon<Core>.Instance.solverCoroutine = MelonCoroutines.Start(SolveAndNotify(onComplete));
    }

    public bool CanFitAll()
    {
        var gridCells = _hexMap.Height * _hexMap.Width;
        var sumOfPatternCells = _upgrades.Select(u => u.Pattern.GetCellCount()).Sum();
        return sumOfPatternCells <= gridCells;
    }
}

public class Core : MelonMod
{
    private readonly Color _defaultSecondaryColor = new(0.9434f, 0.9434f, 0.9434f, 1);
    private readonly Color _grayedOutColor = new(0.9434f, 0.9434f, 0.9434f, 0.1484f);
    private GearDetailsWindow _gearDetailsWindow;
    internal bool _isRotateModInstalled;

    private Menu _menu;

    private readonly Dictionary<int, UnityEvent> _originalOnClicks = new();
    private readonly Dictionary<int, GearUpgradeUI> _selectedUpgrades = new();
    private DefaultButton? _solveButton;
    internal object solverCoroutine;

    private void SetSolveButtonInteractable(bool interactable)
    {
        if (_solveButton is null) return;
        var color = interactable ? _defaultSecondaryColor : _grayedOutColor;

        _solveButton.IgnoreEvents = !interactable;
        _solveButton.SetSecondaryColor(color);

        var rect = _solveButton.gameObject.GetComponent<Rect>();
        rect.color = color;
    }

    private void AddButtonWithPopout(GearDetailsWindow window)
    {
        if (_solveButton != null)
        {
            SetSolveButtonInteractable(_selectedUpgrades.Count > 0);
            return;
        }

        var upgradeList = GameObject.Find("Gear Details/UpgradeList");

        var sort = GameObject.Find("Gear Details/UpgradeList/OpenSort");
        var search = GameObject.Find("Gear Details/UpgradeList/Search");
        var counter = GameObject.Find("Gear Details/UpgradeList/Count");
        if (upgradeList == null || sort == null || search == null || counter == null) return;

        var sortRect = sort.GetComponent<RectTransform>();
        var searchRect = search.GetComponent<RectTransform>();
        var countRect = counter.GetComponent<RectTransform>();

        var solveButtonGO = GameObject.Instantiate(sort, upgradeList.transform);
        solveButtonGO.name = "Solve";

        var solveRect = solveButtonGO.GetComponent<RectTransform>();
        solveRect.anchorMin = new Vector2(0, 1);
        solveRect.anchorMax = new Vector2(0, 1);
        solveRect.pivot = new Vector2(0, 0);

        solveRect.sizeDelta = new Vector2(125, 42);
        solveRect.anchoredPosition = new Vector2(sortRect.sizeDelta.x + 10, sortRect.anchoredPosition.y);

        searchRect.anchoredPosition = new Vector2(solveRect.offsetMax.x + 10, solveRect.anchoredPosition.y);
        searchRect.sizeDelta = new Vector2(400, searchRect.sizeDelta.y);

        countRect.anchoredPosition = new Vector2(searchRect.offsetMax.x + 25, searchRect.offsetMin.y);

        var btnText = solveButtonGO.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
        if (btnText != null) btnText.text = "Solve";

        var solveButton = solveButtonGO.GetComponent<DefaultButton>();
        solveButton.OnClickDown = new UnityEvent();
        solveButton.OnClickUp = new UnityEvent();

        solveButton.OnClickUp ??= new UnityEvent();
        solveButton.OnClickUp.AddListener((Action)(() =>
        {
            LoggerInstance.Msg($"Selected upgrades ({_selectedUpgrades.Count}):");
            foreach (var selectedUpgrade in _selectedUpgrades.Values)
                LoggerInstance.Msg($"\t • {FormatUpgrade(selectedUpgrade)}");

            var upgrades = _selectedUpgrades.Select(u => u.Value.Upgrade).OrderByDescending(u => u.Upgrade.Rarity)
                .ThenBy(u => u.Upgrade.Name).ToList();

            var solver = new Solver(window,
                upgrades);
            LoggerInstance.Msg($"Can fit in theory?: {solver.CanFitAll()}");
            solver.TrySolve(success =>
                Melon<Core>.Logger.Msg(success ? "Found a solution" : "No solution")
            );
        }));

        _solveButton = solveButton;
        SetSolveButtonInteractable(false);
    }

    private static string FormatUpgrade(GearUpgradeUI upgrade)
    {
        var upgradeInstance = upgrade.Upgrade;
        return $"[{upgradeInstance.Upgrade.RarityName}] {upgradeInstance.Upgrade.Name} ({upgradeInstance.InstanceID})";
    }

    private void PatchUpgradeClick()
    {
        foreach (var upgradeUI in GearDetailsWindow.upgradeUIs)
        {
            var instanceID = upgradeUI.Upgrade.InstanceID;
            if (_originalOnClicks.ContainsKey(instanceID)) continue;

            var originalOnClick = upgradeUI.button.OnClickUp;
            _originalOnClicks[instanceID] = originalOnClick;

            upgradeUI.button.OnClickUp = new UnityEvent();

            upgradeUI.button.OnClickUp.AddListener((Action)(() =>
            {
                var button = upgradeUI.button;
                var upgrade = upgradeUI.Upgrade;

                if (button.LastPressButton != PointerEventData.InputButton.Middle)
                {
                    originalOnClick.Invoke();
                    return;
                }

                if (!upgrade.IsUnlocked) return;

                LoggerInstance.Msg($"Selecting {FormatUpgrade(upgradeUI)}");

                var rarity = Global.Instance.Rarities[(int)upgrade.Upgrade.Rarity];

                if (_selectedUpgrades.Remove(upgrade.InstanceID))
                {
                    LoggerInstance.Msg("Removing selection");
                    button.SetDefaultColor(rarity.backgroundColor);
                    SetSolveButtonInteractable(_selectedUpgrades.Count > 0);
                    return;
                }

                var previousUI = _selectedUpgrades.Values
                    .FirstOrDefault(u => u.Upgrade.upgradeID == upgrade.upgradeID);

                if (previousUI != null)
                {
                    LoggerInstance.Msg(
                        $"Switching upgrade instances: {previousUI.Upgrade.InstanceID} -> {upgrade.InstanceID}");
                    previousUI.button.SetDefaultColor(rarity.backgroundColor);
                    _selectedUpgrades.Remove(previousUI.Upgrade.InstanceID);
                }

                button.SetDefaultColor(button.hoverColor);
                _selectedUpgrades[upgrade.InstanceID] = upgradeUI;
                SetSolveButtonInteractable(_selectedUpgrades.Count > 0);
            }));
        }
    }


    public void OnMenuOpen(Menu menu)
    {
        _menu = menu;
    }

    public void OnGearDetailsWindowOpened(GearDetailsWindow __instance)
    {
        _gearDetailsWindow = __instance;
        PatchUpgradeClick();
        AddButtonWithPopout(__instance);
    }

    public void OnGearDetailsWindowClosed(GearDetailsWindow __instance)
    {
        // TODO: check if colors return to default when going back and forth
        _selectedUpgrades.Clear();
        _originalOnClicks.Clear();
        var coroutine = Melon<Core>.Instance.solverCoroutine;
        if (coroutine is not null) MelonCoroutines.Stop(coroutine);
    }

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("Initialized!");
    }
}