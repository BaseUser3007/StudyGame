using System;
using System.Collections.Generic;
using System.IO;
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
/// Сооружение, поставленное в хабе (этап 4): id определения (HubBuildingDefinition.Id) и клетка сетки
/// хаба — левый нижний угол занимаемой области (0,0 — левый нижний угол сетки).
/// </summary>
[Serializable]
public class PlacedHubBuilding
{
    public string buildingId;
    public int x;
    public int y;
}

/// <summary>
/// Прогресс счётчика незавершённого достижения (JsonUtility не умеет Dictionary — список пар).
/// </summary>
[Serializable]
public class AchievementProgressEntry
{
    public string achievementId;
    public int value;
}

/// <summary>
/// Мета-прогресс — всё, что переживает забеги: открытые команды, конструкции, рецепты и постройки хаба,
/// достижения и их прогресс, склад и постройки хаба, события на карте, флаги туториала, настройки.
/// Хранится в JSON-файле в Application.persistentDataPath (по образцу CodeLibrary).
/// Изменения пишутся на диск только по Save().
/// </summary>
public class MetaProgress
{
    private const string FileName = "meta_progress.json";
    /// <summary>Суффикс копии сохранения, отложенной при сбросе прогресса (ResetAll / ResetFile).</summary>
    public const string ResetBackupSuffix = ".reset.bak";

    [Serializable]
    private class ProgressData
    {
        public List<string> unlockedCommands = new List<string>();
        public List<string> unlockedConstructs = new List<string>();
        // Ключи результатов открытых рецептов (itemId, иначе имя ассета — GetItemKey)
        public List<string> unlockedRecipes = new List<string>();
        // Id открытых (доступных для постройки) сооружений хаба — этап 4
        public List<string> unlockedHubBuildings = new List<string>();
        public List<string> achievements = new List<string>();
        // Счётчики незавершённых достижений; у выполненных запись удаляется
        public List<AchievementProgressEntry> achievementProgress = new List<AchievementProgressEntry>();
        public List<string> unlockedEvents = new List<string>();
        public List<string> tutorialFlags = new List<string>();
        public List<StoredMaterial> materials = new List<StoredMaterial>();
        public List<PlacedHubBuilding> hubBuildings = new List<PlacedHubBuilding>();
        public bool hintsEnabled = true;
        // Язык интерфейса (Loc): "en" / "ru"; пусто — по языку системы при первом запуске
        public string language = "";
        public int runsFinished;
        public int defeats;
    }

    private readonly string _filePath;
    private ProgressData _data = new ProgressData();

    /// <summary>Путь сохранения игрока (persistentDataPath) — для пункта меню редактора.</summary>
    public static string DefaultFilePath => JsonFileStore.GetPath(FileName);

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

    /// <summary>Язык интерфейса ("en" / "ru"; пусто — не выбран). Это настройка: ResetAll её сохраняет.</summary>
    public string Language
    {
        get => _data.language ?? "";
        set => _data.language = value ?? "";
    }

    public MetaProgress() : this(DefaultFilePath)
    {
    }

    /// <summary>
    /// Прогресс в указанном файле (тесты пишут во временную папку, не трогая сохранение игрока).
    /// </summary>
    public MetaProgress(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    // ─── Открытия ─────────────────────────────────────────────

    public bool IsCommandUnlocked(string name) => _data.unlockedCommands.Contains(name);
    public bool UnlockCommand(string name) => AddUnique(_data.unlockedCommands, name);

    public bool IsConstructUnlocked(string keyword) => _data.unlockedConstructs.Contains(keyword);
    public bool UnlockConstruct(string keyword) => AddUnique(_data.unlockedConstructs, keyword);

    public bool IsRecipeUnlocked(string key) => _data.unlockedRecipes.Contains(key);
    public bool UnlockRecipe(string key) => AddUnique(_data.unlockedRecipes, key);

    public bool IsHubBuildingUnlocked(string id) => _data.unlockedHubBuildings.Contains(id);
    public bool UnlockHubBuilding(string id) => AddUnique(_data.unlockedHubBuildings, id);

    public bool IsEventUnlocked(string id) => _data.unlockedEvents.Contains(id);
    public bool UnlockEvent(string id) => AddUnique(_data.unlockedEvents, id);

    public bool HasTutorialFlag(string flag) => _data.tutorialFlags.Contains(flag);
    public bool SetTutorialFlag(string flag) => AddUnique(_data.tutorialFlags, flag);
    /// <summary>Сброс туториала: пройденные шаги и показанные подсказки (флаги tutorialFlags).</summary>
    public void ClearTutorialFlags() => _data.tutorialFlags.Clear();

    // ─── Достижения ───────────────────────────────────────────

    public IReadOnlyList<string> CompletedAchievements => _data.achievements;

    public bool HasAchievement(string id) => _data.achievements.Contains(id);

    /// <summary>
    /// Отмечает достижение выполненным (счётчик прогресса больше не нужен и удаляется).
    /// True — если выполнено впервые.
    /// </summary>
    public bool CompleteAchievement(string id)
    {
        if (!AddUnique(_data.achievements, id)) return false;
        _data.achievementProgress.RemoveAll(p => p.achievementId == id);
        return true;
    }

    /// <summary>Сохранённое значение счётчика достижения (0 — нет записи).</summary>
    public int GetAchievementProgress(string id)
    {
        AchievementProgressEntry entry = FindProgress(id);
        return entry != null ? entry.value : 0;
    }

    /// <summary>Значение счётчика достижения; 0 и меньше — запись удаляется.</summary>
    public void SetAchievementProgress(string id, int value)
    {
        if (string.IsNullOrEmpty(id)) return;

        AchievementProgressEntry entry = FindProgress(id);
        if (value <= 0)
        {
            if (entry != null) _data.achievementProgress.Remove(entry);
            return;
        }

        if (entry == null)
        {
            entry = new AchievementProgressEntry { achievementId = id };
            _data.achievementProgress.Add(entry);
        }
        entry.value = value;
    }

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
    /// Списывает материал со склада (оплата постройки хаба). False — если на складе меньше count,
    /// тогда склад не меняется. Запись с нулём удаляется.
    /// </summary>
    public bool RemoveMaterial(string itemId, int count)
    {
        if (count <= 0) return true;

        StoredMaterial stored = FindMaterial(itemId);
        if (stored == null || stored.count < count) return false;

        stored.count -= count;
        if (stored.count <= 0) _data.materials.Remove(stored);
        return true;
    }

    /// <summary>
    /// Ключ предмета на складе: itemId, у предметов без него — имя ассета.
    /// </summary>
    public static string GetItemKey(ResourceItem item) =>
        string.IsNullOrEmpty(item.itemId) ? item.name : item.itemId;

    // ─── Постройки хаба ───────────────────────────────────────

    /// <summary>Постройка с этим id уже стоит в хабе (в срезе каждая — один раз).</summary>
    public bool HasHubBuilding(string buildingId) => FindHubBuilding(buildingId) != null;

    public PlacedHubBuilding FindHubBuilding(string buildingId)
    {
        foreach (PlacedHubBuilding placed in _data.hubBuildings)
            if (placed.buildingId == buildingId) return placed;
        return null;
    }

    /// <summary>
    /// Записывает постройку в расстановку хаба (клетка — левый нижний угол). False — если пустой id
    /// или постройка с таким id уже есть. Проверки места и оплату делает HubService.
    /// </summary>
    public bool AddHubBuilding(string buildingId, Vector2Int cell)
    {
        if (string.IsNullOrEmpty(buildingId) || HasHubBuilding(buildingId)) return false;

        _data.hubBuildings.Add(new PlacedHubBuilding { buildingId = buildingId, x = cell.x, y = cell.y });
        return true;
    }

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
            UIEventBus.PublishNotification(Loc.F("Failed to save progress: {0}", e.Message), LogEntryType.Error);
            return false;
        }
    }

    /// <summary>
    /// Сброс всего мета-прогресса (этап 8): открытия, достижения и их счётчики, склад, постройки хаба,
    /// события, флаги туториала, статистика забегов; подсказки снова включены. Прежний файл
    /// откладывается копией FilePath + ResetBackupSuffix (перезаписывает предыдущую), затем на диск
    /// пишется пустой прогресс. Библиотеку кода (CodeLibrary) не трогает — это код игрока.
    /// Объект остаётся тем же: системы сцены, держащие ссылку, видят сброшенные данные.
    /// Возвращает false, если записать не удалось (в памяти прогресс всё равно сброшен).
    /// </summary>
    public bool ResetAll()
    {
        BackupForReset(_filePath, move: false);
        // Язык — настройка, а не прогресс: переживает сброс
        string language = Language;
        _data = new ProgressData { language = language };
        return Save();
    }

    /// <summary>
    /// Сброс сохранения без загрузки (пункт меню редактора вне Play Mode): файл переносится
    /// в копию path + ResetBackupSuffix, следующий запуск начнётся с пустого прогресса.
    /// True — если файл был и перенесён.
    /// </summary>
    public static bool ResetFile(string path)
    {
        bool moved = BackupForReset(path, move: true);
        // Недописанный временный файл атомарной записи больше не нужен
        TryDelete(path + ".tmp");
        return moved;
    }

    private static bool BackupForReset(string path, bool move)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

        string backupPath = path + ResetBackupSuffix;
        try
        {
            if (move)
            {
                TryDelete(backupPath);
                File.Move(path, backupPath);
            }
            else
            {
                File.Copy(path, backupPath, true);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"MetaProgress: reset backup failed for {path}: {e.Message}");
            // Без копии файл всё равно надо убрать, иначе сброс не сработает
            if (move) TryDelete(path);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"MetaProgress: failed to delete {path}: {e.Message}");
        }
    }

    private void Load()
    {
        if (!JsonFileStore.TryLoad(_filePath, out ProgressData data)) return;

        _data = data;
        // Старый или отредактированный руками файл мог потерять списки
        _data.unlockedCommands ??= new List<string>();
        _data.unlockedConstructs ??= new List<string>();
        _data.unlockedRecipes ??= new List<string>();
        _data.unlockedHubBuildings ??= new List<string>();
        _data.achievements ??= new List<string>();
        _data.achievementProgress ??= new List<AchievementProgressEntry>();
        _data.unlockedEvents ??= new List<string>();
        _data.tutorialFlags ??= new List<string>();
        _data.materials ??= new List<StoredMaterial>();
        _data.hubBuildings ??= new List<PlacedHubBuilding>();
        _data.materials.RemoveAll(m => m == null || string.IsNullOrEmpty(m.itemId) || m.count <= 0);
        _data.hubBuildings.RemoveAll(b => b == null || string.IsNullOrEmpty(b.buildingId));
        // Каждая постройка стоит один раз — дубли (правка файла руками) отбрасываем, первая остаётся
        var placedIds = new HashSet<string>(StringComparer.Ordinal);
        _data.hubBuildings.RemoveAll(b => !placedIds.Add(b.buildingId));
        _data.achievementProgress.RemoveAll(p => p == null || string.IsNullOrEmpty(p.achievementId) || p.value <= 0);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private StoredMaterial FindMaterial(string itemId)
    {
        foreach (StoredMaterial stored in _data.materials)
            if (stored.itemId == itemId) return stored;
        return null;
    }

    private AchievementProgressEntry FindProgress(string id)
    {
        foreach (AchievementProgressEntry entry in _data.achievementProgress)
            if (entry.achievementId == id) return entry;
        return null;
    }

    private static bool AddUnique(List<string> list, string value)
    {
        if (string.IsNullOrEmpty(value) || list.Contains(value)) return false;
        list.Add(value);
        return true;
    }
}
