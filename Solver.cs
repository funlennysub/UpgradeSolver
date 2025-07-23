using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UpgradeSolver;

public class Offset(int offsetX, int offsetY)
{
    public readonly int OffsetX = offsetX;
    public readonly int OffsetY = offsetY;
}

public class Solver(GearDetailsWindow gearDetailsWindow, List<UpgradeInstance> upgrades)
{
    private readonly IUpgradable _gear = gearDetailsWindow.UpgradablePrefab;
    private readonly HexMap _hexMap = gearDetailsWindow.equipSlots.HexMap;
    private readonly int _maxRotations = PlayerData.GetPlayerLevel() >= PlayerData.RotateUpgradesLevelThreshold ? 6 : 1;

    private readonly Dictionary<Tuple<UpgradeInstance, int>, Offset> _offsetCache = new();

    private bool _foundSolution;

    private Offset GetOffsetsCached(UpgradeInstance upgrade, int rotation, UpgradeEquipCell cell)
    {
        var key = new Tuple<UpgradeInstance, int>(upgrade, rotation);
        if (_offsetCache.TryGetValue(key, out var baseOffset))
            return new Offset(cell.X + baseOffset.OffsetX, cell.Y + baseOffset.OffsetY);
        var pattern = upgrade.Pattern;
        var modifiedMap = pattern.GetModifiedMap(rotation);
        var patternWidthHalf = modifiedMap.width / 2;

        var sumY = 0;
        var enabledCount = 0;

        for (var row = 0; row < modifiedMap.height; row++)
        {
            var node = modifiedMap[patternWidthHalf, row];
            if (node.enabled != true) continue;
            sumY += row;
            enabledCount++;
        }

        var avgY = enabledCount > 0 ? sumY / enabledCount : 0;
        var offsetY = -avgY;
        if (patternWidthHalf % 2 == 1) offsetY--;

        baseOffset = new Offset(-patternWidthHalf, offsetY);
        _offsetCache[key] = baseOffset;

        return new Offset(cell.X + baseOffset.OffsetX, cell.Y + baseOffset.OffsetY);
    }

    private void ClearSlots()
    {
        var prefab = gearDetailsWindow.UpgradablePrefab;

        var gearUpgrades = PlayerData.GetAllUpgrades(prefab);

        var playerUpgrades = new List<UpgradeInfo>();
        if (prefab.GearType == GearType.Character)
        {
            IUpgradable playerUpgradesPrefab = Global.Instance;
            playerUpgrades = PlayerData.GetAllUpgrades(playerUpgradesPrefab);
        }

        var allUpgrades = gearUpgrades.Concat(playerUpgrades).Where(u => u is not null);
        foreach (var upgradeInfo in allUpgrades)
        {
            if (upgradeInfo.Instances is null) continue;
            foreach (var upgradeInstance in upgradeInfo.Instances.OfType<UpgradeInstance>())
            {
                gearDetailsWindow.equipSlots.Unequip(prefab, upgradeInstance);
            }
        }
    }

    private IEnumerator SolveDfs(int index)
    {
        if (index >= upgrades.Count)
        {
            _foundSolution = true;
            yield break;
        }

        var upgrade = upgrades[index];

        for (var y = 0; y < _hexMap.Height; y++)
        for (var x = 0; x < _hexMap.Width; x++)
        for (var rotation = 0; rotation < _maxRotations; rotation++)
        {
            var cell = gearDetailsWindow.equipSlots.GetCell(x, y);
            if (cell is null || cell.Upgrade is not null)
                continue;

            var offset = GetOffsetsCached(upgrade, rotation, cell);
            var (offsetX, offsetY) = (offset.OffsetX, offset.OffsetY);

            if (!gearDetailsWindow.equipSlots.EquipModule(_gear, upgrade, offsetX, offsetY, (byte)rotation))
                continue;

            yield return SolveDfs(index + 1);

            if (_foundSolution)
                yield break;

            gearDetailsWindow.equipSlots.Unequip(_gear, upgrade);
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
            onComplete.Invoke(false);
            return;
        }

        Plugin.Instance.solverCoroutine = Plugin.Instance.StartCoroutine(SolveAndNotify(onComplete));
    }

    public bool CanFitAll()
    {
        var gridCells = _hexMap.Height * _hexMap.Width;
        var sumOfPatternCells = upgrades.Select(u => u.Pattern.GetCellCount()).Sum();
        return sumOfPatternCells <= gridCells;
    }
}