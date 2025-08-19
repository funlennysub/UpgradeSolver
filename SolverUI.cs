using System.Collections.Generic;
using System.Linq;
using Pigeon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace UpgradeSolver;

public class SolverUI
{
    private readonly Color _defaultSecondaryColor = new(0.9434f, 0.9434f, 0.9434f, 1);
    private readonly Color _grayedOutColor = new(0.9434f, 0.9434f, 0.9434f, 0.1484f);

    internal GearDetailsWindow? GearDetailsWindow;

    private readonly Dictionary<int, UnityEvent> _originalOnHoverEnters = new();
    private Dictionary<int, GearUpgradeUI> _selectedUpgrades = new();
    private GearUpgradeUI? _hoveredUpgrade;
    private DefaultButton? _solveButton;

    private readonly InputActionMap _solverControls;
    private readonly InputAction _addForSolve;

    public SolverUI(GearDetailsWindow? gearDetailsWindow)
    {
        GearDetailsWindow = gearDetailsWindow;

        _solverControls = new InputActionMap("SolverControls");
        _addForSolve = _solverControls.AddAction("AddForSolve");
        _addForSolve.AddBinding("<Keyboard>/n");

        _addForSolve.performed += _ => { SelectUpgrade(); };
    }

    private void SelectUpgrade()
    {
        if (_hoveredUpgrade == null || !_hoveredUpgrade.Upgrade.IsUnlocked)
            return;

        var upgrade = _hoveredUpgrade.Upgrade;
        var rarity = Global.Instance.Rarities[(int)upgrade.Upgrade.Rarity];

        if (_selectedUpgrades.TryGetValue(upgrade.InstanceID, out var selectedUI))
        {
            Plugin.Logger.LogInfo($"Deselecting {upgrade.Upgrade.Name}");
            selectedUI.button.SetDefaultColor(rarity.backgroundColor);
            _selectedUpgrades.Remove(upgrade.InstanceID);
            SetSolveButtonInteractable(_selectedUpgrades.Count > 0);
            return;
        }

        if (!upgrade.Upgrade.CanStack || upgrade.Upgrade.UpgradeType == Upgrade.Type.OnlyOneOfThisType)
        {
            var conflictingUpgrade = _selectedUpgrades.Values
                .FirstOrDefault(u =>
                    (!upgrade.Upgrade.CanStack && u.Upgrade.upgradeID == upgrade.upgradeID) ||
                    (upgrade.Upgrade.UpgradeType == Upgrade.Type.OnlyOneOfThisType &&
                     u.Upgrade.Upgrade.UpgradeType == Upgrade.Type.OnlyOneOfThisType));

            if (conflictingUpgrade != null)
            {
                Plugin.Logger.LogInfo(
                    $"Replacing {conflictingUpgrade.Upgrade.Upgrade.Name} with {upgrade.Upgrade.Name}");
                var backgroundColor = Global.Instance.Rarities[(int)conflictingUpgrade.Upgrade.Upgrade.Rarity]
                    .backgroundColor;
                conflictingUpgrade.button.SetDefaultColor(backgroundColor);
                _selectedUpgrades.Remove(conflictingUpgrade.Upgrade.InstanceID);
            }
        }

        Plugin.Logger.LogInfo($"Selecting {upgrade.Upgrade.Name}");
        _hoveredUpgrade.button.SetDefaultColor(_hoveredUpgrade.button.hoverColor);
        _selectedUpgrades[upgrade.InstanceID] = _hoveredUpgrade;
        SetSolveButtonInteractable(true);
    }

    // Unity is trolling and causes GearUpgradeUIs to change with the view to a random UI
    internal void RebuildSelectedUpgrades()
    {
        Plugin.Logger.LogInfo($"Rebuilding {nameof(_selectedUpgrades)}");

        _selectedUpgrades = GearDetailsWindow.upgradeUIs
            .Where(ui => _selectedUpgrades.ContainsKey(ui.Upgrade.InstanceID))
            .ToDictionary(ui => ui.Upgrade.InstanceID);

        foreach (var (_, selectedUpgrade) in _selectedUpgrades)
        {
            selectedUpgrade.button.SetDefaultColor(selectedUpgrade.button.hoverColor);
        }

        SetSolveButtonInteractable(_selectedUpgrades.Count > 0);
    }

    internal void Close()
    {
        _solverControls.Disable();

        _originalOnHoverEnters.Clear();
        _selectedUpgrades.Clear();
        var coroutine = Plugin.Instance.SolverCoroutine;
        if (coroutine is not null) Plugin.Instance.StopCoroutine(coroutine);
    }

    private void SetSolveButtonInteractable(bool interactable)
    {
        if (_solveButton is null) return;
        var color = interactable ? _defaultSecondaryColor : _grayedOutColor;

        _solveButton.IgnoreEvents = !interactable;
        _solveButton.SetSecondaryColor(color);

        var rect = _solveButton.gameObject.GetComponent<Pigeon.UI.Rect>();
        rect.color = color;
    }

    internal void AddSolveButton()
    {
        if (GearDetailsWindow is null) return;
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

        var solveButtonGo = Object.Instantiate(sort, upgradeList.transform);
        solveButtonGo.name = "Solve";

        var solveRect = solveButtonGo.GetComponent<RectTransform>();
        solveRect.anchorMin = new Vector2(0, 1);
        solveRect.anchorMax = new Vector2(0, 1);
        solveRect.pivot = new Vector2(0, 0);

        solveRect.sizeDelta = new Vector2(125, 42);
        solveRect.anchoredPosition = new Vector2(sortRect.offsetMax.x + 10, sortRect.anchoredPosition.y);

        searchRect.anchoredPosition = new Vector2(solveRect.offsetMax.x + 10, solveRect.anchoredPosition.y);
        searchRect.sizeDelta = new Vector2(400, searchRect.sizeDelta.y);

        countRect.anchoredPosition = new Vector2(searchRect.offsetMax.x + 10, searchRect.offsetMin.y);

        var btnText = solveButtonGo.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
        if (btnText != null) btnText.text = "Solve";

        var solveButton = solveButtonGo.GetComponent<DefaultButton>();
        solveButton.OnClickDown = new UnityEvent();
        solveButton.OnClickUp = new UnityEvent();

        solveButton.OnClickUp ??= new UnityEvent();
        solveButton.OnClickUp.AddListener(() =>
        {
            Plugin.Logger.LogInfo($"Selected upgrades ({_selectedUpgrades.Count}):");
            foreach (var selectedUpgrade in _selectedUpgrades.Values)
                Plugin.Logger.LogInfo($"\t • {FormatUpgrade(selectedUpgrade)}");

            var upgrades = _selectedUpgrades
                .Select(u => u.Value.Upgrade)
                .OrderByDescending(u => u.Upgrade.Rarity)
                .ThenBy(u => u.Upgrade.Name).ToList();

            var solver = new Solver(GearDetailsWindow, upgrades);
            Plugin.Logger.LogInfo($"Can fit in theory?: {solver.CanFitAll()}");
            solver.TrySolve(success =>
                Plugin.Logger.LogInfo(success ? "Found a solution" : "No solution")
            );
        });

        _solveButton = solveButton;
        SetSolveButtonInteractable(false);
    }

    private static string FormatUpgrade(GearUpgradeUI upgrade)
    {
        var upgradeInstance = upgrade.Upgrade;
        return $"[{upgradeInstance.Upgrade.RarityName}] {upgradeInstance.Upgrade.Name} ({upgradeInstance.InstanceID})";
    }

    internal void PatchUpgradeClick()
    {
        _solverControls.Enable();

        foreach (var upgradeUI in GearDetailsWindow.upgradeUIs)
        {
            var instanceID = upgradeUI.Upgrade.InstanceID;
            if (_originalOnHoverEnters.ContainsKey(instanceID)) continue;

            var originalOnHoverEnter = upgradeUI.button.OnHoverEnter;
            _originalOnHoverEnters[instanceID] = originalOnHoverEnter;

            upgradeUI.button.OnHoverExit.AddListener(() => _hoveredUpgrade = null);
            upgradeUI.button.OnHoverEnter.AddListener(() => _hoveredUpgrade = upgradeUI);
        }
    }
}