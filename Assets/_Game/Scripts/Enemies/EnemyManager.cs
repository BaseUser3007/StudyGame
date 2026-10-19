using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Враги забега: создание (пул), список живых, запросы для Lua и боя, игровое время.
/// Создаётся в Level (только в забеге создаёт врагов; в лагере список пуст), экземпляры лежат
/// под объектом "Enemies" на сцене.
///   • Статические враги (лагеря) — при старте забега, детерминированно от IMapDataProvider.Seed.
///   • Патрули — в начале каждого дня после наступления угрозы (DayCycle.OnDayStarted), под туманом
///     у его края (кольцо patrolSpawnMinDistance…MaxDistance от игрока), иначе — подальше от игрока.
///   • Время в мире идёт только пока выполняется код игрока (LuaSandbox.OnRunningChanged):
///     тогда каждый кадр враги делают Tick, между запусками всё стоит.
/// Id врагов — целые с 1 в порядке появления, не повторяются за забег.
/// Врагов немного (десятки), поэтому запросы — перебором, без индекса по тайлам.
/// </summary>
public class EnemyManager : IEnemyWorld, IDisposable
{
    public event Action<Enemy> OnEnemySpawned;
    /// <summary>Враг погиб (уже снят с карты, добыча выпала).</summary>
    public event Action<Enemy> OnEnemyKilled;
    /// <summary>Враг ударил игрока: кто и на сколько.</summary>
    public event Action<Enemy, float> OnPlayerAttacked;
    /// <summary>Враг впервые за забег оказался на видимом тайле (тогда же GameEventBus EnemyFound).</summary>
    public event Action<Enemy> OnEnemySpotted;

    private const int PlaceAttempts = 30;
    private const float LootScatterRadius = 0.7f;
    private const int LootPlaceAttempts = 10;
    private const int PatrolPointAttempts = 12;
    // Патрули не появляются ближе этого (в тайлах) к области головоломки
    private const int PuzzleMargin = 1;

    private readonly IMapDataProvider _mapData;
    private readonly IFogProvider _fog;
    private readonly PlayerCommandExecutor _executor;
    private readonly PlayerHealth _playerHealth;
    private readonly PlayerInventory _inventory;
    private readonly ObjectManager _objects;
    private readonly EnemyConfig _config;
    private readonly DayCycle _dayCycle;
    private readonly bool _spawnEnabled;

    private readonly Transform _container;
    private readonly List<Enemy> _enemies = new List<Enemy>();
    private readonly Dictionary<int, Enemy> _byId = new Dictionary<int, Enemy>();
    private readonly Dictionary<Enemy, Queue<Enemy>> _pool = new Dictionary<Enemy, Queue<Enemy>>();
    private readonly List<Enemy> _tickBuffer = new List<Enemy>();
    private readonly List<Enemy> _visibilityBuffer = new List<Enemy>();
    private readonly List<Collider2D> _overlaps = new List<Collider2D>();
    private readonly List<Vector2> _campCenters = new List<Vector2>();
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

    private LuaSandbox _sandbox;
    // Головоломки создаются после врагов — ссылка приходит позже (SetPuzzles); до того проверки нет
    private PuzzleManager _puzzles;
    private CancellationTokenSource _clock;
    private System.Random _patrolRandom = new System.Random();
    private int _nextId = 1;
    // «Фора» в начале запуска: враги не бьют, пока она не истекла
    private float _graceLeft;
    private bool _disposed;

    /// <summary>Живые враги на карте.</summary>
    public IReadOnlyList<Enemy> Enemies => _enemies;
    public int Count => _enemies.Count;
    /// <summary>Центры поставленных лагерей (позиции карты) — головоломки держатся от них подальше.</summary>
    public IReadOnlyList<Vector2> CampCenters => _campCenters;
    /// <summary>Идёт игровое время (выполняется код игрока).</summary>
    public bool IsTimeRunning => _clock != null;

    /// <param name="spawnEnabled">false — лагерь (хаб): враги не создаются.</param>
    public EnemyManager(
        IMapDataProvider mapData, IFogProvider fog, PlayerCommandExecutor executor, PlayerHealth playerHealth,
        PlayerInventory inventory, ObjectManager objects, EnemyConfig config, DayCycle dayCycle, bool spawnEnabled)
    {
        _mapData = mapData;
        _fog = fog;
        _executor = executor;
        _playerHealth = playerHealth;
        _inventory = inventory;
        _objects = objects;
        _config = config;
        _dayCycle = dayCycle;
        _spawnEnabled = spawnEnabled;
        _container = new GameObject("Enemies").transform;

        if (_config == null && spawnEnabled)
            Debug.LogWarning("EnemyManager: EnemyConfig не назначен на Level — врагов в забеге не будет");

        if (_dayCycle != null)
            _dayCycle.OnDayStarted += HandleDayStarted;
    }

    /// <summary>
    /// Двигать время только пока выполняется код. Вызывается после инициализации песочницы.
    /// </summary>
    public void Attach(LuaSandbox sandbox)
    {
        _sandbox = sandbox;
        _sandbox.OnRunningChanged += HandleRunningChanged;
    }

    /// <summary>
    /// Области головоломок (лабиринт, обелиски): патрули не появляются в них и не выбирают там точки обхода.
    /// Головоломки генерируются после лагерей врагов, поэтому ссылка передаётся отдельно.
    /// </summary>
    public void SetPuzzles(PuzzleManager puzzles)
    {
        _puzzles = puzzles;
    }

    /// <summary>
    /// Опубликовать EnemyFound для врагов, видимых со спавна. Level вызывает после создания
    /// AchievementTracker и UI (туториала), иначе событие уходит до подписчиков.
    /// </summary>
    public void PublishInitialVisibility()
    {
        if (!_disposed) RefreshVisibility();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopClock();
        _lifetime.Cancel();
        _lifetime.Dispose();

        if (_sandbox != null) _sandbox.OnRunningChanged -= HandleRunningChanged;
        if (_dayCycle != null) _dayCycle.OnDayStarted -= HandleDayStarted;
    }

    // ─── Generation ───────────────────────────────────────────

    /// <summary>
    /// Начало забега: лагеря статических врагов (детерминированно от seed карты). Объекты карты к этому
    /// моменту уже разложены. Видимость выставляется при создании, а EnemyFound для видимых со спавна —
    /// в PublishInitialVisibility.
    /// </summary>
    public void SpawnInitial(Vector2Int spawnTile)
    {
        if (!_spawnEnabled || _config == null) return;

        SpawnCamps(new Vector2(spawnTile.x, spawnTile.y));
    }

    /// <summary>
    /// Патрули первого дня, если угроза идёт с первого дня. Вызывается после генерации головоломок
    /// и SetPuzzles — иначе патруль может появиться там, где потом встанет лабиринт.
    /// </summary>
    public void SpawnInitialPatrols()
    {
        if (!_spawnEnabled || _config == null || _dayCycle == null || !_dayCycle.IsThreatActive) return;

        SpawnPatrolsForDay(_dayCycle.Day);
    }

    private void SpawnCamps(Vector2 spawn)
    {
        List<EnemyDefinition> kinds = ValidKinds(_config.guardEnemies);
        if (kinds.Count == 0)
        {
            Debug.LogWarning("EnemyManager: в EnemyConfig.guardEnemies нет врагов с префабом — лагерей не будет");
            return;
        }
        if (_config.camps == null || _config.camps.Count == 0) return;

        // Свой сдвиг seed, чтобы не совпадать с биомами и объектами
        var random = new System.Random(unchecked(_mapData.Seed * 73 + 104729));
        // Коллайдеры объектов карты должны участвовать в проверке места
        Physics2D.SyncTransforms();

        var land = new List<Vector2Int>();
        for (int x = 0; x < _mapData.MapWidth; x++)
            for (int y = 0; y < _mapData.MapHeight; y++)
                if (_mapData.IsWalkable(x, y))
                    land.Add(new Vector2Int(x, y));
        if (land.Count == 0) return;

        // Самые требовательные к месту (дальние) — первыми; порядок стабилен, генерация детерминирована
        var specs = new List<EnemyCampSpec>(_config.camps);
        specs.Sort((a, b) => b.minDistanceFromSpawn.CompareTo(a.minDistanceFromSpawn));

        var centers = new List<Vector2>();
        foreach (EnemyCampSpec spec in specs)
        {
            EnemyDefinition kind = kinds[random.Next(kinds.Count)];
            ResourceObject valuable = spec.valuableInCenter ? _config.campValuablePrefab : null;

            if (!TryPickCampCenter(random, land, centers, spawn, spec.minDistanceFromSpawn, valuable, kind,
                    out Vector2 center))
            {
                Debug.LogWarning($"EnemyManager: не нашлось места для лагеря на {spec.enemyCount} " +
                                 $"(от спавна ≥ {spec.minDistanceFromSpawn}, между лагерями ≥ {_config.minCampSpacing})");
                continue;
            }
            centers.Add(center);
            _campCenters.Add(center);

            if (valuable != null)
                _objects.Spawn(valuable, center);

            bool hasLeader = spec.leader != null && spec.leader.prefab != null;
            int placed = 0;
            for (int i = 0; i < spec.enemyCount; i++)
            {
                EnemyDefinition enemyKind = hasLeader && i == 0 ? spec.leader : kind;
                for (int attempt = 0; attempt < PlaceAttempts; attempt++)
                {
                    Vector2 position = valuable == null && i == 0 && attempt == 0
                        ? center
                        : center + RandomInCircle(random, _config.campRadius);
                    if (!IsValidPlacement(enemyKind.prefab, position, spawn, 0f)) continue;

                    Spawn(enemyKind, position, patrol: false);
                    placed++;
                    break;
                }
            }

            if (placed < spec.enemyCount)
                Debug.LogWarning($"EnemyManager: в лагере поставлено врагов {placed} из {spec.enemyCount}");
        }
    }

    /// <summary>
    /// Центр лагеря: суша не ближе minDistance к спавну и minCampSpacing к другим лагерям; если в центре
    /// ценный объект — помещается он, иначе — враг.
    /// </summary>
    private bool TryPickCampCenter(System.Random random, List<Vector2Int> land, List<Vector2> centers,
        Vector2 spawn, float minDistance, ResourceObject valuable, EnemyDefinition kind, out Vector2 center)
    {
        var candidates = new List<Vector2Int>();
        foreach (Vector2Int tile in land)
        {
            Vector2 point = new Vector2(tile.x, tile.y);
            if (Vector2.Distance(spawn, point) < minDistance) continue;

            bool tooClose = false;
            foreach (Vector2 other in centers)
                if (Vector2.Distance(point, other) < _config.minCampSpacing)
                {
                    tooClose = true;
                    break;
                }
            if (!tooClose) candidates.Add(tile);
        }

        Component prefab = valuable != null ? (Component)valuable : kind.prefab;
        for (int attempt = 0; attempt < PlaceAttempts * 2 && candidates.Count > 0; attempt++)
        {
            Vector2Int tile = candidates[random.Next(candidates.Count)];
            center = new Vector2(tile.x, tile.y);
            if (IsValidPlacement(prefab, center, spawn, 0f)) return true;
        }

        center = default;
        return false;
    }

    // ─── Patrols ──────────────────────────────────────────────

    private void HandleDayStarted(int day)
    {
        if (!_spawnEnabled || _config == null || _dayCycle == null || !_dayCycle.IsThreatActive) return;

        int spawned = SpawnPatrolsForDay(day);
        RefreshVisibility();

        if (spawned > 0)
            UIEventBus.PublishNotification(
                spawned == 1 ? "An enemy patrol came out of the fog" : $"{spawned} enemy patrols came out of the fog",
                LogEntryType.Warning);
    }

    /// <summary>
    /// Патрули дня: min(patrolsOnThreatDay + patrolsPerDayGrowth × (day − threatDay), maxNewPatrolsPerDay) новых
    /// (по умолчанию min(1 + (day − threatDay), 4)); старые остаются. maxPatrols > 0 — предел живых.
    /// Возвращает, сколько появилось.
    /// </summary>
    private int SpawnPatrolsForDay(int day)
    {
        List<EnemyDefinition> kinds = ValidKinds(_config.patrolEnemies);
        if (kinds.Count == 0)
        {
            Debug.LogWarning("EnemyManager: в EnemyConfig.patrolEnemies нет врагов с префабом — патрулей не будет");
            return 0;
        }

        int daysOfThreat = Mathf.Max(0, day - _dayCycle.ThreatDay);
        int wanted = Mathf.Min(
            _config.patrolsOnThreatDay + _config.patrolsPerDayGrowth * daysOfThreat, _config.maxNewPatrolsPerDay);
        if (_config.maxPatrols > 0)
        {
            int alive = 0;
            foreach (Enemy enemy in _enemies)
                if (enemy.IsPatrol) alive++;
            wanted = Mathf.Min(wanted, Mathf.Max(0, _config.maxPatrols - alive));
        }
        if (wanted <= 0) return 0;

        // От seed и дня: один и тот же забег с тем же seed повторяет появления
        _patrolRandom = new System.Random(unchecked(_mapData.Seed * 131 + day * 7919));
        Vector2 player = _executor.Position;
        List<Vector2Int> candidates = CollectPatrolTiles(player);
        if (candidates.Count == 0) return 0;

        Physics2D.SyncTransforms();
        int spawned = 0;
        for (int i = 0; i < wanted; i++)
        {
            EnemyDefinition kind = kinds[_patrolRandom.Next(kinds.Count)];
            for (int attempt = 0; attempt < PlaceAttempts; attempt++)
            {
                Vector2Int tile = candidates[_patrolRandom.Next(candidates.Count)];
                Vector2 position = (Vector2)tile + RandomInCircle(_patrolRandom, 0.4f);
                if (!IsValidPlacement(kind.prefab, position, player, _config.patrolSpawnMinDistance)) continue;

                Spawn(kind, position, patrol: true);
                spawned++;
                break;
            }
        }

        return spawned;
    }

    /// <summary>
    /// Тайлы суши под туманом в кольце вокруг игрока (край тумана); нет таких — любые суша подальше от игрока.
    /// Области головоломок (с отступом PuzzleMargin) исключены.
    /// </summary>
    private List<Vector2Int> CollectPatrolTiles(Vector2 player)
    {
        var ring = new List<Vector2Int>();
        var far = new List<Vector2Int>();
        float min = _config.patrolSpawnMinDistance;
        float max = Mathf.Max(min + 1f, _config.patrolSpawnMaxDistance);

        for (int x = 0; x < _mapData.MapWidth; x++)
            for (int y = 0; y < _mapData.MapHeight; y++)
            {
                if (!_mapData.IsWalkable(x, y)) continue;
                if (IsNearPuzzle(new Vector2Int(x, y), PuzzleMargin)) continue;

                float distance = Vector2.Distance(player, new Vector2(x, y));
                if (distance < min) continue;

                far.Add(new Vector2Int(x, y));
                if (distance <= max && (_fog == null || !_fog.IsVisible(x, y)))
                    ring.Add(new Vector2Int(x, y));
            }

        return ring.Count > 0 ? ring : far;
    }

    // ─── Spawn / Pool ─────────────────────────────────────────

    /// <summary>
    /// Создаёт врага в точке карты без проверок места (генерация проверяет сама; пригодится для событий карты).
    /// </summary>
    public Enemy Spawn(EnemyDefinition definition, Vector2 position, bool patrol)
    {
        if (definition == null || definition.prefab == null) return null;

        Enemy enemy = GetFromPool(definition.prefab, _mapData.MapToWorld(position));
        enemy.Setup(_nextId++, definition, EnemyStats.Resolve(definition, _config), position, patrol, this);
        enemy.SetVisible(IsTileVisible(enemy.Tile));
        enemy.OnKilled += HandleEnemyKilled;

        _enemies.Add(enemy);
        _byId[enemy.Id] = enemy;
        // Коллайдер нового врага сразу участвует в движении игрока и проверке места
        Physics2D.SyncTransforms();

        OnEnemySpawned?.Invoke(enemy);
        return enemy;
    }

    private Enemy GetFromPool(Enemy prefab, Vector3 worldPosition)
    {
        Enemy enemy = _pool.TryGetValue(prefab, out Queue<Enemy> queue) && queue.Count > 0
            ? queue.Dequeue()
            : UnityEngine.Object.Instantiate(prefab, _container);

        enemy.PrefabKey = prefab;
        enemy.transform.position = worldPosition;
        // Включаем до Setup: Awake (кэш рендереров и коллайдера) должен уже пройти
        enemy.gameObject.SetActive(true);
        return enemy;
    }

    private void ReturnToPool(Enemy enemy)
    {
        enemy.gameObject.SetActive(false);
        if (enemy.PrefabKey == null)
        {
            UnityEngine.Object.Destroy(enemy.gameObject);
            return;
        }

        if (!_pool.TryGetValue(enemy.PrefabKey, out Queue<Enemy> queue))
            _pool[enemy.PrefabKey] = queue = new Queue<Enemy>();
        queue.Enqueue(enemy);
    }

    private void HandleEnemyKilled(Enemy enemy)
    {
        enemy.OnKilled -= HandleEnemyKilled;
        _enemies.Remove(enemy);
        _byId.Remove(enemy.Id);

        if (enemy.Definition != null)
            foreach (LootEntry entry in enemy.Definition.Loot)
                DropLoot(entry.item, entry.amount, enemy.Position);

        GameEventBus.Publish(GameEventType.EnemyKilled, enemy.TypeId, 1, enemy.IsPatrol ? "patrol" : "guard");
        ReturnToPool(enemy);
        Physics2D.SyncTransforms();
        OnEnemyKilled?.Invoke(enemy);
    }

    /// <summary>
    /// Добыча с World Prefab ложится на землю (по объекту на штуку) с меткой запуска — как в ResourceHarvester,
    /// чтобы достижения EfficientGather засчитали её подбор в этом же запуске; без префаба — сразу в инвентарь.
    /// </summary>
    private void DropLoot(ResourceItem item, int amount, Vector2 origin)
    {
        if (item.worldPrefab == null)
        {
            _inventory?.Add(item, amount);
            return;
        }

        for (int i = 0; i < amount; i++)
        {
            ResourceObject loot = _objects.Spawn(item.worldPrefab, FindLootPoint(origin));
            if (loot != null) loot.SpawnRunId = GameEventBus.ActiveRunId;
        }
    }

    private Vector2 FindLootPoint(Vector2 origin)
    {
        for (int attempt = 0; attempt < LootPlaceAttempts; attempt++)
        {
            Vector2 point = origin + UnityEngine.Random.insideUnitCircle * LootScatterRadius;
            if (IsWalkable(point)) return point;
        }

        return origin;
    }

    // ─── Time ─────────────────────────────────────────────────

    private void HandleRunningChanged(bool running)
    {
        if (running) StartClock();
        else StopClock();
    }

    private void StartClock()
    {
        if (_disposed) return;
        StopClock();

        _graceLeft = _config != null ? _config.runStartGrace : 0.5f;
        _clock = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        RunClockAsync(_clock.Token).Forget();
    }

    private void StopClock()
    {
        if (_clock == null) return;

        _clock.Cancel();
        _clock.Dispose();
        _clock = null;

        if (!_disposed) RefreshVisibility();
    }

    private async UniTaskVoid RunClockAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Tick(Time.deltaTime);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Код закончился — мир замирает
        }
    }

    /// <summary>
    /// Один шаг игрового времени для всех врагов (копия списка: враг может погибнуть по ходу).
    /// </summary>
    private void Tick(float deltaTime)
    {
        _graceLeft = Mathf.Max(0f, _graceLeft - deltaTime);
        _tickBuffer.Clear();
        _tickBuffer.AddRange(_enemies);

        foreach (Enemy enemy in _tickBuffer)
            if (enemy != null && enemy.IsAlive)
                enemy.Tick(deltaTime);

        RefreshVisibility();
    }

    /// <summary>
    /// Враги под туманом не рисуются (но действуют). Впервые за забег увиденный враг — EnemyFound (Id = тип).
    /// </summary>
    public void RefreshVisibility()
    {
        _visibilityBuffer.Clear();
        _visibilityBuffer.AddRange(_enemies);

        foreach (Enemy enemy in _visibilityBuffer)
        {
            if (enemy == null || !enemy.IsAlive) continue;

            bool visible = IsTileVisible(enemy.Tile);
            enemy.SetVisible(visible);

            if (visible && !enemy.WasSeen)
            {
                enemy.WasSeen = true;
                GameEventBus.Publish(GameEventType.EnemyFound, enemy.TypeId, 1, enemy.IsPatrol ? "patrol" : "guard");
                OnEnemySpotted?.Invoke(enemy);
            }
        }
    }

    // ─── Queries ──────────────────────────────────────────────

    public Enemy GetEnemyById(int id)
    {
        return _byId.TryGetValue(id, out Enemy enemy) && enemy.IsAlive ? enemy : null;
    }

    /// <summary>
    /// Живые враги не дальше radius от точки карты, ближние — первыми.
    /// </summary>
    public List<Enemy> GetEnemiesInRadius(Vector2 mapPosition, float radius)
    {
        var result = new List<Enemy>();
        foreach (Enemy enemy in _enemies)
            if (enemy.IsAlive && Vector2.Distance(mapPosition, enemy.Position) <= radius)
                result.Add(enemy);

        result.Sort((a, b) =>
            Vector2.Distance(mapPosition, a.Position).CompareTo(Vector2.Distance(mapPosition, b.Position)));
        return result;
    }

    /// <summary>
    /// Живые враги, точка которых находится в тайле.
    /// </summary>
    public List<Enemy> GetEnemiesAtTile(Vector2Int tile)
    {
        var result = new List<Enemy>();
        foreach (Enemy enemy in _enemies)
            if (enemy.IsAlive && enemy.Tile == tile)
                result.Add(enemy);
        return result;
    }

    /// <summary>
    /// Ближайший к точке живой враг не дальше maxDistance, или null.
    /// </summary>
    public Enemy GetNearestEnemy(Vector2 mapPosition, float maxDistance)
    {
        Enemy nearest = null;
        float best = maxDistance;

        foreach (Enemy enemy in _enemies)
        {
            if (!enemy.IsAlive) continue;
            float distance = Vector2.Distance(mapPosition, enemy.Position);
            if (distance <= best)
            {
                best = distance;
                nearest = enemy;
            }
        }

        return nearest;
    }

    // ─── IEnemyWorld ──────────────────────────────────────────

    public Vector2 PlayerPosition => _executor.Position;
    public bool IsPlayerAlive => _playerHealth == null || !_playerHealth.IsDead;
    public bool CanAttack => _graceLeft <= 0f;

    public void DamagePlayer(Enemy attacker, float damage)
    {
        if (_playerHealth == null || damage <= 0f) return;

        _playerHealth.TakeDamage(damage);
        OnPlayerAttacked?.Invoke(attacker, damage);
    }

    public bool IsWalkable(Vector2 mapPosition)
    {
        Vector2Int tile = MapPosition.ToTile(mapPosition);
        return _mapData.IsWalkable(tile.x, tile.y);
    }

    public Vector3 MapToWorld(Vector2 mapPosition) => _mapData.MapToWorld(mapPosition);

    public float WorldScale => Mathf.Abs(_mapData.MapToWorld(Vector2.right).x - _mapData.MapToWorld(Vector2.zero).x);

    public Vector2 PickPatrolPoint(Vector2 from, float radius)
    {
        for (int attempt = 0; attempt < PatrolPointAttempts; attempt++)
        {
            Vector2 point = from + RandomInCircle(_patrolRandom, radius);
            // В лабиринт и на обелиски патруль не заходит
            if (IsWalkable(point) && !IsNearPuzzle(MapPosition.ToTile(point), 0)) return point;
        }

        return from;
    }

    // ─── Placement ────────────────────────────────────────────

    /// <summary>
    /// Под коллайдером врага суша, он не ближе avoidDistance к avoidPoint, не задевает объекты,
    /// постройки и других врагов (по расстоянию до точек и пересечению коллайдеров).
    /// </summary>
    private bool IsValidPlacement(Component prefab, Vector2 position, Vector2 avoidPoint, float avoidDistance)
    {
        if (Vector2.Distance(position, avoidPoint) < avoidDistance) return false;

        Rect area = ObjectFootprint.Get(prefab);
        area.position += position;

        Vector2Int min = MapPosition.ToTile(area.min);
        Vector2Int max = MapPosition.ToTile(area.max);
        for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
                if (!_mapData.IsWalkable(x, y) || IsNearPuzzle(new Vector2Int(x, y), 0))
                    return false;

        if (_objects != null && _objects.GetNearestObjectAt(position, _config.objectSpacing) != null)
            return false;

        foreach (Enemy enemy in _enemies)
            if (Vector2.Distance(position, enemy.Position) < _config.enemySpacing)
                return false;

        return !IsOccupied(area);
    }

    private bool IsOccupied(Rect area)
    {
        if (area.width <= 0f || area.height <= 0f) return false;

        float scale = WorldScale;
        Vector2 center = _mapData.MapToWorld(area.center);
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        return Physics2D.OverlapBox(center, area.size * scale, 0f, filter, _overlaps) > 0;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private bool IsTileVisible(Vector2Int tile) => _fog == null || _fog.IsVisible(tile.x, tile.y);

    /// <summary>Тайл в области головоломки или не дальше margin тайлов от неё (без PuzzleManager — нет).</summary>
    private bool IsNearPuzzle(Vector2Int tile, int margin)
    {
        if (_puzzles == null || _puzzles.Puzzles.Count == 0) return false;

        for (int dx = -margin; dx <= margin; dx++)
            for (int dy = -margin; dy <= margin; dy++)
                if (_puzzles.GetPuzzleAt(new Vector2Int(tile.x + dx, tile.y + dy)) != null)
                    return true;
        return false;
    }

    private static List<EnemyDefinition> ValidKinds(List<EnemyDefinition> list)
    {
        var result = new List<EnemyDefinition>();
        if (list == null) return result;

        foreach (EnemyDefinition definition in list)
            if (definition != null && definition.prefab != null)
                result.Add(definition);
        return result;
    }

    private static int RollCount(System.Random random, float expected)
    {
        int count = Mathf.FloorToInt(expected);
        if (random.NextDouble() < expected - count) count++;
        return count;
    }

    private static Vector2 RandomInCircle(System.Random random, float radius)
    {
        double angle = random.NextDouble() * Math.PI * 2.0;
        float distance = radius * Mathf.Sqrt((float)random.NextDouble());
        return new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * distance;
    }
}
