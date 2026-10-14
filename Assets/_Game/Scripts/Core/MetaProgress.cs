using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Материал на складе хаба. Имя хранится вместе с id, чтобы склад можно было показать без ассетов.
/// </summary>
[Serializable]
public class StoredMaterial
{
    public string itemId;
    public string displayName;
    public int count;
}

/// <summary>
/// Сооружение, поставленное в хабе (этап 4): id определения и клетка.
/// </summary>
[Serializable]
public class PlacedHubBuilding
{
    public string buildingId;
    public int x;
    public int y;
}

/// <summary>
/// Мета-прогресс — всё, что переживает забеги: открытые команды и конструкции, достижения,
/// склад и постройки хаба, события на карте, флаги туториала, настройки.
/// Хранится в JSON-файле в Application.persistentDataPath (по образцу CodeLibrary).
/// Изменения пишутся на диск только по Save().
/// </summary>
public class MetaProgress
{
    private const string FileName = "meta_progress.json";

    [Serializable]
    private class ProgressData
    {
        public List<string> unlockedCommands = new List<string>();
        public List<string> unlockedConstructs = new List<string>();
        public List<string> achievements = new List<string>();
        public List<string> unlockedEvents = new List<string>();
        public List<string> tutorialFlags = new List<string>();
        public List<StoredMaterial> materials = new List<StoredMaterial>();
        public List<PlacedHubBuilding> hubBuildings = new List<PlacedHubBuilding>();
        public bool hintsEnabled = true;
        public int runsFinished;
        public int defeats;
    }

    private readonly string _filePath;
    private ProgressData _data = new ProgressData();

    public string FilePath => _filePath;
    public IReadOnlyList<StoredMaterial> Materials => _data.materials;
    public IReadOnlyList<PlacedHubBuilding> HubBuildings => _data.hubBuildings;
    public int RunsFinished => _data.runsFinished;
    public int Defeats => _data.defeats;

    public bool HintsEnabled
    {
        get => _data.hintsEnabled;
        set => _data.hintsEnabled = value;
    }

    public MetaProgress()
    {
        _filePath = JsonFileStore.GetPath(FileName);
        Load();
    }

    // ─── Открытия ─────────────────────────────────────────────

    public bool IsCommandUnlocked(string name) => _data.unlockedCommands.Contains(name);
    public bool UnlockCommand(string name) => AddUnique(_data.unlockedCommands, name);

    public bool IsConstructUnlocked(string keyword) => _data.unlockedConstructs.Contains(keyword);
    public bool UnlockConstruct(string keyword) => AddUnique(_data.unlockedConstructs, keyword);

    public bool HasAchievement(string id) => _data.achievements.Contains(id);
    public bool CompleteAchievement(string id) => AddUnique(_data.achievements, id);

    public bool IsEventUnlocked(string id) => _data.unlockedEvents.Contains(id);
    public bool UnlockEvent(string id) => AddUnique(_data.unlockedEvents, id);

    public bool HasTutorialFlag(string flag) => _data.tutorialFlags.Contains(flag);
    public bool SetTutorialFlag(string flag) => AddUnique(_data.tutorialFlags, flag);

    // ─── Склад хаба ───────────────────────────────────────────

    public int GetMaterialCount(string itemId)
    {
        StoredMaterial stored = FindMaterial(itemId);
        return stored != null ? stored.count : 0;
    }

    public void AddMaterial(ResourceItem item, int count)
    {
        if (item == null || count <= 0) return;

        string id = GetItemKey(item);
        StoredMaterial stored = FindMaterial(id);
        if (stored == null)
        {
            stored = new StoredMaterial { itemId = id };
            _data.materials.Add(stored);
        }

        stored.displayName = item.DisplayName;
        stored.count += count;
    }

    /// <summary>
    /// Ключ предмета на складе: itemId, у предметов без него — имя ассета.
    /// </summary>
    public static string GetItemKey(ResourceItem item) =>
        string.IsNullOrEmpty(item.itemId) ? item.name : item.itemId;

    // ─── Забеги ───────────────────────────────────────────────

    public void RecordRunFinished(bool defeated)
    {
        _data.runsFinished++;
        if (defeated) _data.defeats++;
    }

    // ─── File ─────────────────────────────────────────────────

    /// <summary>
    /// Записывает прогресс на диск. Ошибки ввода-вывода логируются — игра продолжается
    /// с прогрессом в памяти. Возвращает false, если записать не удалось.
    /// </summary>
    public bool Save()
    {
        try
        {
            JsonFileStore.Write(_filePath, _data);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            UIEventBus.PublishNotification($"Failed to save progress: {e.Message}", LogEntryType.Error);
            return false;
        }
    }

    private void Load()
    {
        if (!JsonFileStore.TryLoad(_filePath, out ProgressData data)) return;

        _data = data;
        // Старый или отредактированный руками файл мог потерять списки
        _data.unlockedCommands ??= new List<string>();
        _data.unlockedConstructs ??= new List<string>();
        _data.achievements ??= new List<string>();
        _data.unlockedEvents ??= new List<string>();
        _data.tutorialFlags ??= new List<string>();
        _data.materials ??= new List<StoredMaterial>();
        _data.hubBuildings ??= new List<PlacedHubBuilding>();
        _data.materials.RemoveAll(m => m == null || string.IsNullOrEmpty(m.itemId) || m.count <= 0);
        _data.hubBuildings.RemoveAll(b => b == null || string.IsNullOrEmpty(b.buildingId));
    }

    // ─── Helpers ──────────────────────────────────────────────

    private StoredMaterial FindMaterial(string itemId)
    {
        foreach (StoredMaterial stored in _data.materials)
            if (stored.itemId == itemId) return stored;
        return null;
    }

    private static bool AddUnique(List<string> list, string value)
    {
        if (string.IsNullOrEmpty(value) || list.Contains(value)) return false;
        list.Add(value);
        return true;
    }
}
