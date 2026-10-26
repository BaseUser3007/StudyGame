using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Результат проверки или попытки постройки в хабе.</summary>
public enum HubBuildResult
{
    Ok,
    /// <summary>Определения нет в каталоге.</summary>
    Unknown,
    /// <summary>Постройка не открыта достижением.</summary>
    Locked,
    /// <summary>Уже стоит в хабе (в срезе каждая — один раз).</summary>
    AlreadyBuilt,
    /// <summary>Не хватает материалов на складе хаба.</summary>
    NotAffordable,
    /// <summary>Область выходит за сетку.</summary>
    OutOfBounds,
    /// <summary>Область пересекается с другой постройкой.</summary>
    Occupied,
    /// <summary>Постоянная постройка (портал) ставится автоматически — из панели не строится.</summary>
    Permanent
}

/// <summary>Результат проверки или попытки переноса постройки хаба.</summary>
public enum HubMoveResult
{
    Ok,
    /// <summary>Определения нет в каталоге.</summary>
    Unknown,
    /// <summary>Постройки ещё нет в хабе — переносить нечего.</summary>
    NotBuilt,
    /// <summary>Область выходит за сетку.</summary>
    OutOfBounds,
    /// <summary>Область пересекается с другой постройкой (своё старое место не мешает).</summary>
    Occupied
}

/// <summary>Результат проверки или попытки улучшения постройки хаба (T14).</summary>
public enum HubUpgradeResult
{
    Ok,
    /// <summary>Определения нет в каталоге.</summary>
    Unknown,
    /// <summary>Постройки ещё нет в хабе.</summary>
    NotBuilt,
    /// <summary>Уже максимальный уровень (или постройка одноуровневая).</summary>
    MaxLevel,
    /// <summary>Не хватает материалов на складе хаба для следующего уровня.</summary>
    NotAffordable
}

/// <summary>
/// Постройка, стоящая в хабе: определение (null — id из сохранения, которого нет в каталоге) и клетка
/// левого нижнего угла.
/// </summary>
public readonly struct HubPlacement
{
    public readonly string Id;
    public readonly HubBuildingDefinition Definition;
    public readonly Vector2Int Cell;
    /// <summary>Уровень улучшения, не меньше 1.</summary>
    public readonly int Level;

    public HubPlacement(string id, HubBuildingDefinition definition, Vector2Int cell, int level = 1)
    {
        Id = id;
        Definition = definition;
        Cell = cell;
        Level = Mathf.Max(1, level);
    }

    /// <summary>Занятая область; неизвестная постройка занимает одну клетку.</summary>
    public RectInt Area => new RectInt(Cell, Definition != null ? Definition.Size : Vector2Int.one);
}

/// <summary>
/// Сработавший бонус за соседство (T15): постройка-владелец записи (Owner), сосед, рядом с которым он
/// действует, и сам бонус. Owner/Neighbour — из расстановки (или предпросмотра размещения).
/// </summary>
public readonly struct HubAdjacencyMatch
{
    public readonly HubPlacement Owner;
    public readonly HubPlacement Neighbour;
    public readonly HubAdjacencyBonus Bonus;

    public HubAdjacencyMatch(HubPlacement owner, HubPlacement neighbour, HubAdjacencyBonus bonus)
    {
        Owner = owner;
        Neighbour = neighbour;
        Bonus = bonus;
    }

    public HubEffectType Effect => Bonus.effect;
    public float Amount => Bonus.amount;
    public HubBuildingEffect ToEffect() => Bonus.ToEffect();

    /// <summary>Вторая постройка пары относительно id (для подсказки при размещении: «рядом с кем»).</summary>
    public HubPlacement Other(string id) => Owner.Id == id ? Neighbour : Owner;

    /// <summary>Строка UI: «+5 energy limit (next to Shrine)», где Shrine — вторая постройка пары относительно id.</summary>
    public string Describe(string id)
    {
        HubPlacement other = Other(id);
        string title = other.Definition != null ? Loc.T(other.Definition.Title) : other.Id;
        return Loc.F("{0} (next to {1})", ToEffect().Describe(), title);
    }
}

/// <summary>
/// Логика хаба (этап 4, plain class из Level в режиме Hub): сетка, проверка места, оплата материалами
/// со склада MetaProgress, запись расстановки и сохранение. Клетки — (0,0) левый нижний угол сетки,
/// постройка ставится левым нижним углом в cell. Каждая постройка — один раз, без сноса.
/// Построенную можно улучшать до HubBuildingDefinition.MaxLevel (T14): CheckUpgrade / TryUpgrade —
/// и бесплатно переносить (CheckMove / TryMove), уровень сохраняется, соседство пересчитывается само.
/// Постоянные постройки (IsPermanent, портал) конструктор ставит сам, если их нет в сохранении
/// (EnsurePermanentBuildings): не строятся и не улучшаются, всегда открыты, переносятся.
/// Открытость — через UnlockRegistry (награды достижений). Мировую часть рисует HubGrid по OnBuilt/OnUpgraded/OnMoved.
/// </summary>
public class HubService
{
    /// <summary>Постройка поставлена (уже оплачена и сохранена).</summary>
    public event Action<HubBuildingDefinition, Vector2Int> OnBuilt;
    /// <summary>Постройка улучшена (оплачено и сохранено): определение и новый уровень.</summary>
    public event Action<HubBuildingDefinition, int> OnUpgraded;
    /// <summary>Постройка перенесена (сохранено): определение, старая и новая клетка левого нижнего угла.</summary>
    public event Action<HubBuildingDefinition, Vector2Int, Vector2Int> OnMoved;

    private readonly HubBuildingCatalog _catalog;
    private readonly MetaProgress _progress;
    private readonly UnlockRegistry _unlocks;

    public Vector2Int GridSize { get; }

    /// <summary>Все постройки каталога (порядок панели). Пусто без каталога.</summary>
    public IReadOnlyList<HubBuildingDefinition> Definitions { get; }

    public MetaProgress Progress => _progress;

    /// <param name="unlocks">Открытые постройки; null — все открыты (тесты).</param>
    public HubService(HubBuildingCatalog catalog, MetaProgress progress, UnlockRegistry unlocks)
    {
        Debug.Assert(progress != null, "HubService: MetaProgress is null");

        _catalog = catalog;
        _progress = progress;
        _unlocks = unlocks;
        GridSize = catalog != null ? catalog.GridSize : HubBuildingCatalog.DefaultGridSize;
        Definitions = CollectDefinitions(catalog);

        if (catalog == null)
            Debug.LogWarning("HubService: HubBuildingCatalog is not assigned — nothing can be built in the camp");

        foreach (HubPlacement placed in GetPlaced())
            if (placed.Definition == null)
                Debug.LogWarning($"HubService: saved camp building '{placed.Id}' is not in the catalog — shown as an occupied cell");

        // Портал стоит всегда: новое или старое сохранение без него — ставим сами и сохраняем
        EnsurePermanentBuildings();
    }

    // ─── Состояние ────────────────────────────────────────────

    public HubBuildingDefinition Find(string id) => _catalog != null ? _catalog.Find(id) : null;

    /// <summary>Открыта ли постройка; постоянная (портал) открыта всегда.</summary>
    public bool IsUnlocked(HubBuildingDefinition definition) =>
        definition != null
        && (definition.IsPermanent || _unlocks == null || _unlocks.IsHubBuildingUnlocked(definition.Id));

    /// <summary>
    /// Вход в лагерь — первая постоянная постройка каталога (портал), у которой стоит игрок; null — нет такой.
    /// </summary>
    public HubBuildingDefinition EntranceBuilding
    {
        get
        {
            foreach (HubBuildingDefinition definition in Definitions)
                if (definition.IsPermanent)
                    return definition;
            return null;
        }
    }

    public bool IsBuilt(HubBuildingDefinition definition) =>
        definition != null && _progress.HasHubBuilding(definition.Id);

    /// <summary>Сколько материала с этим ключом на складе хаба.</summary>
    public int GetStored(string key) => _progress.GetMaterialCount(key);

    /// <summary>Хватает ли материалов склада на постройку (уровень 1).</summary>
    public bool CanAfford(HubBuildingDefinition definition) => CanAfford(definition, 1);

    /// <summary>Хватает ли материалов склада на получение уровня level (1 — постройка, 2+ — улучшение).</summary>
    public bool CanAfford(HubBuildingDefinition definition, int level)
    {
        if (definition == null) return false;

        foreach (HubBuildingCost cost in definition.GetCost(level))
            if (_progress.GetMaterialCount(cost.Key) < cost.Amount)
                return false;
        return true;
    }

    /// <summary>Уровень постройки в хабе; 0 — не построена.</summary>
    public int GetLevel(HubBuildingDefinition definition)
    {
        PlacedHubBuilding placed = definition != null ? _progress.FindHubBuilding(definition.Id) : null;
        return placed != null ? ClampLevel(definition, placed.Level) : 0;
    }

    /// <summary>
    /// Уровень из сохранения не выше MaxLevel: у постройки могли убрать уровни в ассете
    /// (иначе карточка показала бы «Уровень 3/2»).
    /// </summary>
    private static int ClampLevel(HubBuildingDefinition definition, int level) =>
        definition != null ? Mathf.Min(level, definition.MaxLevel) : level;

    /// <summary>
    /// Можно ли сейчас выбрать постройку для размещения (без учёта клетки): известна, открыта,
    /// ещё не стоит, материалов хватает. Для кнопок панели.
    /// </summary>
    public HubBuildResult CheckAvailability(HubBuildingDefinition definition)
    {
        if (definition == null || Find(definition.Id) != definition) return HubBuildResult.Unknown;
        if (IsBuilt(definition)) return HubBuildResult.AlreadyBuilt;
        // Портал ставит EnsurePermanentBuildings; не стоит только если сетка забита — построить его нельзя
        if (definition.IsPermanent) return HubBuildResult.Permanent;
        if (!IsUnlocked(definition)) return HubBuildResult.Locked;
        if (!CanAfford(definition)) return HubBuildResult.NotAffordable;
        return HubBuildResult.Ok;
    }

    // ─── Сетка ────────────────────────────────────────────────

    /// <summary>Поставленные постройки (из сохранения) с определениями.</summary>
    public List<HubPlacement> GetPlaced() => CollectPlacements(_progress, _catalog);

    /// <summary>
    /// Расстановка из сохранения с определениями из catalog (null — все без определений). Повторы id
    /// пропускаются (действует первая запись). Нужна и вне хаба — RunModifiers считает соседство.
    /// </summary>
    public static List<HubPlacement> CollectPlacements(MetaProgress progress, HubBuildingCatalog catalog)
    {
        var result = new List<HubPlacement>();
        if (progress == null) return result;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (PlacedHubBuilding placed in progress.HubBuildings)
        {
            if (placed == null || placed.buildingId == null || !ids.Add(placed.buildingId)) continue;
            HubBuildingDefinition definition = catalog != null ? catalog.Find(placed.buildingId) : null;
            result.Add(new HubPlacement(
                placed.buildingId, definition,
                new Vector2Int(placed.x, placed.y), ClampLevel(definition, placed.Level)));
        }
        return result;
    }

    public bool IsCellInGrid(Vector2Int cell) =>
        cell.x >= 0 && cell.y >= 0 && cell.x < GridSize.x && cell.y < GridSize.y;

    /// <summary>Область постройки с левым нижним углом в cell целиком внутри сетки.</summary>
    public bool IsInBounds(HubBuildingDefinition definition, Vector2Int cell)
    {
        if (definition == null) return false;
        Vector2Int size = definition.Size;
        return cell.x >= 0 && cell.y >= 0 && cell.x + size.x <= GridSize.x && cell.y + size.y <= GridSize.y;
    }

    /// <summary>Область не пересекается с поставленными постройками.</summary>
    public bool IsAreaFree(HubBuildingDefinition definition, Vector2Int cell) => IsAreaFree(definition, cell, null);

    /// <summary>
    /// Область не пересекается с поставленными постройками, кроме постройки с id ignoreId (перенос: своё
    /// старое место не мешает; null — без исключений).
    /// </summary>
    public bool IsAreaFree(HubBuildingDefinition definition, Vector2Int cell, string ignoreId)
    {
        if (definition == null) return false;

        var area = new RectInt(cell, definition.Size);
        foreach (HubPlacement placed in GetPlaced())
            if (placed.Id != ignoreId && placed.Area.Overlaps(area))
                return false;
        return true;
    }

    /// <summary>Постройка, занимающая клетку, или null-структура (Id == null).</summary>
    public HubPlacement GetPlacementAt(Vector2Int cell)
    {
        foreach (HubPlacement placed in GetPlaced())
            if (placed.Area.Contains(cell))
                return placed;
        return default;
    }

    /// <summary>Только место: в границах и свободно (для подсветки призрака).</summary>
    public bool CanPlaceAt(HubBuildingDefinition definition, Vector2Int cell) =>
        IsInBounds(definition, cell) && IsAreaFree(definition, cell);

    /// <summary>Полная проверка постройки в клетке, без изменений.</summary>
    public HubBuildResult CheckBuild(HubBuildingDefinition definition, Vector2Int cell)
    {
        HubBuildResult availability = CheckAvailability(definition);
        if (availability != HubBuildResult.Ok) return availability;
        if (!IsInBounds(definition, cell)) return HubBuildResult.OutOfBounds;
        if (!IsAreaFree(definition, cell)) return HubBuildResult.Occupied;
        return HubBuildResult.Ok;
    }

    // ─── Соседство (T15) ──────────────────────────────────────

    /// <summary>Сработавшие сейчас бонусы за соседство (по сохранённой расстановке).</summary>
    public List<HubAdjacencyMatch> GetAdjacencyBonuses() => GetAdjacencyBonuses(GetPlaced());

    /// <summary>
    /// Бонусы за соседство расстановки placements. Правило: запись adjacencyBonuses постройки P срабатывает,
    /// если хоть одна другая постройка с id = neighbourId касается P стороной (<see cref="AreSideAdjacent"/>);
    /// каждая запись считается ОДИН раз, сколько бы подходящих соседей ни было (соседом берётся первый).
    /// Постройки без определения (нет в каталоге) не дают и не получают бонусов. Чистая логика — для тестов
    /// и RunModifiers.
    /// </summary>
    public static List<HubAdjacencyMatch> GetAdjacencyBonuses(IReadOnlyList<HubPlacement> placements)
    {
        var result = new List<HubAdjacencyMatch>();
        if (placements == null) return result;

        for (int i = 0; i < placements.Count; i++)
        {
            HubPlacement owner = placements[i];
            if (owner.Definition == null) continue;

            foreach (HubAdjacencyBonus bonus in owner.Definition.AdjacencyBonuses)
            {
                if (bonus == null) continue;
                string neighbourId = bonus.NeighbourId;
                if (neighbourId.Length == 0 || neighbourId == owner.Id) continue;

                for (int j = 0; j < placements.Count; j++)
                {
                    HubPlacement neighbour = placements[j];
                    if (j == i || neighbour.Definition == null || neighbour.Id != neighbourId) continue;
                    if (!AreSideAdjacent(owner.Area, neighbour.Area)) continue;

                    result.Add(new HubAdjacencyMatch(owner, neighbour, bonus));
                    break; // запись — один раз
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Предпросмотр для призрака: какие бонусы появятся, если поставить definition левым нижним углом в cell, —
    /// и те, что получит она сама, и те, что дадут ей уже стоящие соседи (их записи с её id). Пусто, если сюда
    /// поставить нельзя (вне сетки / занято). Уже стоящая постройка с тем же id в расчёте не участвует —
    /// так тот же предпросмотр работает и для переноса (своё старое место не мешает и не даёт бонусов).
    /// </summary>
    public List<HubAdjacencyMatch> PreviewAdjacencyBonuses(HubBuildingDefinition definition, Vector2Int cell)
    {
        var result = new List<HubAdjacencyMatch>();
        if (definition == null || !IsInBounds(definition, cell) || !IsAreaFree(definition, cell, definition.Id))
            return result;

        string id = definition.Id;
        var placements = new List<HubPlacement>();
        foreach (HubPlacement placed in GetPlaced())
            if (placed.Id != id)
                placements.Add(placed);
        placements.Add(new HubPlacement(id, definition, cell));

        foreach (HubAdjacencyMatch match in GetAdjacencyBonuses(placements))
            if (match.Owner.Id == id || match.Neighbour.Id == id)
                result.Add(match);
        return result;
    }

    /// <summary>
    /// Все бонусы за соседство, в которых участвует постройка (свои записи и записи других построек каталога
    /// с её id) — для карточки: (владелец записи, id второй постройки пары, бонус).
    /// </summary>
    public List<(HubBuildingDefinition Owner, string OtherId, HubAdjacencyBonus Bonus)> GetPossibleAdjacencyBonuses(
        HubBuildingDefinition definition)
    {
        var result = new List<(HubBuildingDefinition, string, HubAdjacencyBonus)>();
        if (definition == null) return result;

        string id = definition.Id;
        foreach (HubAdjacencyBonus bonus in definition.AdjacencyBonuses)
            if (bonus != null && bonus.NeighbourId.Length > 0 && bonus.NeighbourId != id)
                result.Add((definition, bonus.NeighbourId, bonus));

        foreach (HubBuildingDefinition other in Definitions)
        {
            if (other == null || other == definition || other.Id == id) continue;
            foreach (HubAdjacencyBonus bonus in other.AdjacencyBonuses)
                if (bonus != null && bonus.NeighbourId == id)
                    result.Add((other, other.Id, bonus));
        }

        return result;
    }

    /// <summary>
    /// Прямоугольники касаются стороной: не пересекаются и имеют общий отрезок границы ненулевой длины.
    /// Касание только углом (по диагонали) — нет.
    /// </summary>
    public static bool AreSideAdjacent(RectInt a, RectInt b)
    {
        bool touchX = a.xMax == b.xMin || b.xMax == a.xMin;
        bool touchY = a.yMax == b.yMin || b.yMax == a.yMin;
        int overlapX = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        int overlapY = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);

        return (touchX && overlapY > 0) || (touchY && overlapX > 0);
    }

    // ─── Постройка ────────────────────────────────────────────

    /// <summary>
    /// Ставит постройку: списывает материалы со склада, записывает расстановку, сохраняет MetaProgress,
    /// публикует GameEventBus HubBuildingBuilt (Id = id постройки) и OnBuilt. При отказе ничего не меняет.
    /// </summary>
    public HubBuildResult TryBuild(HubBuildingDefinition definition, Vector2Int cell)
    {
        HubBuildResult result = CheckBuild(definition, cell);
        if (result != HubBuildResult.Ok) return result;

        // CanAfford проверен в CheckBuild — списание всех строк пройдёт
        foreach (HubBuildingCost cost in definition.GetCost())
        {
            bool removed = _progress.RemoveMaterial(cost.Key, cost.Amount);
            Debug.Assert(removed, $"HubService: failed to take {cost.Amount} × {cost.Key} after CanAfford");
        }

        _progress.AddHubBuilding(definition.Id, cell);
        _progress.Save();

        GameEventBus.Publish(GameEventType.HubBuildingBuilt, definition.Id);
        OnBuilt?.Invoke(definition, cell);
        return HubBuildResult.Ok;
    }

    // ─── Улучшение ────────────────────────────────────────────

    /// <summary>
    /// Можно ли улучшить постройку до следующего уровня: известна, построена, не максимального уровня,
    /// материалов на следующий уровень хватает. Открытость не проверяется — построенная уже открыта.
    /// </summary>
    public HubUpgradeResult CheckUpgrade(HubBuildingDefinition definition)
    {
        if (definition == null || Find(definition.Id) != definition) return HubUpgradeResult.Unknown;

        int level = GetLevel(definition);
        if (level <= 0) return HubUpgradeResult.NotBuilt;
        // Постоянная постройка (портал) не улучшается, даже если в ассете остались уровни
        if (definition.IsPermanent || level >= definition.MaxLevel) return HubUpgradeResult.MaxLevel;
        if (!CanAfford(definition, level + 1)) return HubUpgradeResult.NotAffordable;
        return HubUpgradeResult.Ok;
    }

    public bool CanUpgrade(HubBuildingDefinition definition) => CheckUpgrade(definition) == HubUpgradeResult.Ok;

    /// <summary>
    /// Улучшает постройку на один уровень: списывает стоимость следующего уровня со склада, записывает
    /// уровень, сохраняет MetaProgress, публикует GameEventBus HubBuildingUpgraded (Id = id постройки,
    /// Amount = новый уровень) и OnUpgraded. При отказе ничего не меняет.
    /// </summary>
    public HubUpgradeResult TryUpgrade(HubBuildingDefinition definition)
    {
        HubUpgradeResult result = CheckUpgrade(definition);
        if (result != HubUpgradeResult.Ok) return result;

        PlacedHubBuilding placed = _progress.FindHubBuilding(definition.Id);
        int newLevel = placed.Level + 1;

        // CanAfford проверен в CheckUpgrade — списание всех строк пройдёт
        foreach (HubBuildingCost cost in definition.GetCost(newLevel))
        {
            bool removed = _progress.RemoveMaterial(cost.Key, cost.Amount);
            Debug.Assert(removed, $"HubService: failed to take {cost.Amount} × {cost.Key} after CanAfford");
        }

        placed.level = newLevel;
        _progress.Save();

        GameEventBus.Publish(GameEventType.HubBuildingUpgraded, definition.Id, newLevel);
        OnUpgraded?.Invoke(definition, newLevel);
        return HubUpgradeResult.Ok;
    }

    // ─── Перенос ──────────────────────────────────────────────

    /// <summary>
    /// Можно ли перенести построенную постройку левым нижним углом в cell: известна, стоит в хабе, область в
    /// границах и не пересекает другие постройки (своё старое место не мешает — можно сдвинуть на клетку).
    /// Бесплатно, открытость не проверяется. Та же клетка — Ok (TryMove ничего не меняет).
    /// </summary>
    public HubMoveResult CheckMove(HubBuildingDefinition definition, Vector2Int cell)
    {
        if (definition == null || Find(definition.Id) != definition) return HubMoveResult.Unknown;
        if (!IsBuilt(definition)) return HubMoveResult.NotBuilt;
        if (!IsInBounds(definition, cell)) return HubMoveResult.OutOfBounds;
        if (!IsAreaFree(definition, cell, definition.Id)) return HubMoveResult.Occupied;
        return HubMoveResult.Ok;
    }

    /// <summary>Только место для переноса (для подсветки призрака): в границах и свободно, кроме своего места.</summary>
    public bool CanMoveTo(HubBuildingDefinition definition, Vector2Int cell) =>
        CheckMove(definition, cell) == HubMoveResult.Ok;

    /// <summary>
    /// Переносит постройку: меняет клетку в расстановке (уровень остаётся), сохраняет MetaProgress и публикует
    /// OnMoved(def, from, to). Бонусы за соседство и RunModifiers считаются по расстановке — пересчитываются сами.
    /// Та же клетка — Ok без сохранения и события. При отказе ничего не меняет.
    /// </summary>
    public HubMoveResult TryMove(HubBuildingDefinition definition, Vector2Int cell)
    {
        HubMoveResult result = CheckMove(definition, cell);
        if (result != HubMoveResult.Ok) return result;

        PlacedHubBuilding placed = _progress.FindHubBuilding(definition.Id);
        var from = new Vector2Int(placed.x, placed.y);
        if (from == cell) return HubMoveResult.Ok;

        placed.x = cell.x;
        placed.y = cell.y;
        _progress.Save();

        OnMoved?.Invoke(definition, from, cell);
        return HubMoveResult.Ok;
    }

    /// <summary>Клетка постройки в хабе (левый нижний угол); false — не построена.</summary>
    public bool TryGetCell(HubBuildingDefinition definition, out Vector2Int cell)
    {
        PlacedHubBuilding placed = definition != null ? _progress.FindHubBuilding(definition.Id) : null;
        cell = placed != null ? new Vector2Int(placed.x, placed.y) : default;
        return placed != null;
    }

    // ─── Постоянные постройки (портал) ────────────────────────

    /// <summary>
    /// Ставит постоянные постройки каталога, которых нет в сохранении (новая игра, старое сохранение до портала),
    /// на первое свободное место ближе всего к <see cref="GetPreferredCell"/> (низ сетки по центру — там, где
    /// игрок стоял раньше) и сохраняет. Бесплатно, без событий (это не постройка игрока). Сетка забита —
    /// предупреждение, постройка не ставится. Возвращает, поставлено ли что-нибудь. Повторный вызов ничего не делает.
    /// </summary>
    public bool EnsurePermanentBuildings()
    {
        bool placedAny = false;

        foreach (HubBuildingDefinition definition in Definitions)
        {
            if (!definition.IsPermanent || IsBuilt(definition)) continue;

            if (!TryFindFreeCell(definition, GetPreferredCell(definition), out Vector2Int cell))
            {
                Debug.LogWarning($"HubService: no free place for the permanent camp building '{definition.Id}'");
                continue;
            }

            _progress.AddHubBuilding(definition.Id, cell);
            placedAny = true;
        }

        if (placedAny) _progress.Save();
        return placedAny;
    }

    /// <summary>Место постоянной постройки по умолчанию: нижний ряд сетки, по центру.</summary>
    public Vector2Int GetPreferredCell(HubBuildingDefinition definition)
    {
        Vector2Int size = definition != null ? definition.Size : Vector2Int.one;
        return new Vector2Int(Mathf.Max(0, (GridSize.x - size.x) / 2), 0);
    }

    /// <summary>
    /// Свободная клетка для постройки, ближайшая к preferred (по квадрату расстояния; при равенстве — ниже,
    /// затем левее). Детерминированно, без Random.
    /// </summary>
    public bool TryFindFreeCell(HubBuildingDefinition definition, Vector2Int preferred, out Vector2Int cell)
    {
        cell = default;
        if (definition == null) return false;

        bool found = false;
        int best = int.MaxValue;
        Vector2Int size = definition.Size;

        for (int y = 0; y + size.y <= GridSize.y; y++)
            for (int x = 0; x + size.x <= GridSize.x; x++)
            {
                var candidate = new Vector2Int(x, y);
                int distance = (candidate - preferred).sqrMagnitude;
                if (distance >= best || !IsAreaFree(definition, candidate)) continue;

                best = distance;
                cell = candidate;
                found = true;
            }

        return found;
    }

    /// <summary>Текст отказа переноса для UI (английский, переводится через Loc.T у вызывающего).</summary>
    public static string Describe(HubMoveResult result)
    {
        switch (result)
        {
            case HubMoveResult.Ok: return "Ready to move";
            case HubMoveResult.Unknown: return "Unknown building";
            case HubMoveResult.NotBuilt: return "Build it first";
            case HubMoveResult.OutOfBounds: return "Doesn't fit the camp grid";
            case HubMoveResult.Occupied: return "The place is occupied";
            default: return result.ToString();
        }
    }

    /// <summary>Текст отказа улучшения для UI (английский, переводится через Loc.T у вызывающего).</summary>
    public static string Describe(HubUpgradeResult result)
    {
        switch (result)
        {
            case HubUpgradeResult.Ok: return "Ready to upgrade";
            case HubUpgradeResult.Unknown: return "Unknown building";
            case HubUpgradeResult.NotBuilt: return "Build it first";
            case HubUpgradeResult.MaxLevel: return "Maximum level";
            case HubUpgradeResult.NotAffordable: return "Not enough materials to upgrade";
            default: return result.ToString();
        }
    }

    /// <summary>Текст отказа для UI (английский).</summary>
    public static string Describe(HubBuildResult result)
    {
        switch (result)
        {
            case HubBuildResult.Ok: return "Ready to build";
            case HubBuildResult.Unknown: return "Unknown building";
            case HubBuildResult.Locked: return "Not unlocked yet — complete an achievement";
            case HubBuildResult.AlreadyBuilt: return "Already built";
            case HubBuildResult.NotAffordable: return "Not enough materials";
            case HubBuildResult.OutOfBounds: return "Doesn't fit the camp grid";
            case HubBuildResult.Occupied: return "The place is occupied";
            case HubBuildResult.Permanent: return "Always in the camp — it can only be moved";
            default: return result.ToString();
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static IReadOnlyList<HubBuildingDefinition> CollectDefinitions(HubBuildingCatalog catalog)
    {
        var result = new List<HubBuildingDefinition>();
        if (catalog == null) return result;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (HubBuildingDefinition definition in catalog.Buildings)
            if (definition != null && ids.Add(definition.Id))
                result.Add(definition);
        return result;
    }
}
