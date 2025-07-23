using System;
using System.Collections.Generic;
using System.Linq;
using Pigeon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace UpgradeSolver;

public class SolverUI(GearDetailsWindow? gearDetailsWindow, Menu? menu)
{
    private readonly Color _defaultSecondaryColor = new(0.9434f, 0.9434f, 0.9434f, 1);
    private readonly Color _grayedOutColor = new(0.9434f, 0.9434f, 0.9434f, 0.1484f);

    internal GearDetailsWindow? GearDetailsWindow = gearDetailsWindow;
    internal Menu? Menu = menu;

    private readonly Dictionary<int, UnityEvent> _originalOnClicks = new();
    private readonly Dictionary<int, GearUpgradeUI> _selectedUpgrades = new();
    private DefaultButton? _solveButton;

    internal void Close()
    {
        _originalOnClicks.Clear();
        _selectedUpgrades.Clear();
        var coroutine = Plugin.Instance.solverCoroutine;
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

    internal void AddButtonWithPopout()
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
        solveRect.anchoredPosition = new Vector2(sortRect.sizeDelta.x + 10, sortRect.anchoredPosition.y);

        searchRect.anchoredPosition = new Vector2(solveRect.offsetMax.x + 10, solveRect.anchoredPosition.y);
        searchRect.sizeDelta = new Vector2(400, searchRect.sizeDelta.y);

        countRect.anchoredPosition = new Vector2(searchRect.offsetMax.x + 25, searchRect.offsetMin.y);

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

            var upgrades = _selectedUpgrades.Select(u => u.Value.Upgrade).OrderByDescending(u => u.Upgrade.Rarity)
                .ThenBy(u => u.Upgrade.Name).ToList();

            var solver = new Solver(this.GearDetailsWindow,
                upgrades);
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
        foreach (var upgradeUI in GearDetailsWindow.upgradeUIs)
        {
            var instanceID = upgradeUI.Upgrade.InstanceID;
            if (_originalOnClicks.ContainsKey(instanceID)) continue;

            var originalOnClick = upgradeUI.button.OnClickUp;
            _originalOnClicks[instanceID] = originalOnClick;

            upgradeUI.button.OnClickUp = new UnityEvent();

            upgradeUI.button.OnClickUp.AddListener(() =>
            {
                var button = upgradeUI.button;
                var upgrade = upgradeUI.Upgrade;

                if (button.LastPressButton != PointerEventData.InputButton.Middle)
                {
                    originalOnClick.Invoke();
                    return;
                }

                if (!upgrade.IsUnlocked) return;

                Plugin.Logger.LogInfo($"Selecting {FormatUpgrade(upgradeUI)}");

                var rarity = Global.Instance.Rarities[(int)upgrade.Upgrade.Rarity];

                if (_selectedUpgrades.Remove(upgrade.InstanceID))
                {
                    Plugin.Logger.LogInfo("Removing selection");
                    button.SetDefaultColor(rarity.backgroundColor);
                    SetSolveButtonInteractable(_selectedUpgrades.Count > 0);
                    return;
                }

                var previousUI = _selectedUpgrades.Values
                    .FirstOrDefault(u => u.Upgrade.upgradeID == upgrade.upgradeID);

                if (previousUI != null)
                {
                    Plugin.Logger.LogInfo(
                        $"Switching upgrade instances: {previousUI.Upgrade.InstanceID} -> {upgrade.InstanceID}");
                    previousUI.button.SetDefaultColor(rarity.backgroundColor);
                    _selectedUpgrades.Remove(previousUI.Upgrade.InstanceID);
                }

                button.SetDefaultColor(button.hoverColor);
                _selectedUpgrades[upgrade.InstanceID] = upgradeUI;
                SetSolveButtonInteractable(_selectedUpgrades.Count > 0);
            });
        }
    }
}