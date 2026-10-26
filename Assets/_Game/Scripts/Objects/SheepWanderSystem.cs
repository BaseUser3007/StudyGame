using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Овцы (SheepObject) медленно бродят около точки появления: шаг к случайной точке суши в радиусе
/// WanderRadius, пауза WaitMin…WaitMax секунд. Время идёт только пока выполняется код игрока
/// (LuaSandbox.OnRunningChanged, как часы EnemyManager) и не идёт на точке останова (IsPaused).
/// Не заходят в воду, на головоломки (с отступом PuzzleMargin) и в лагеря врагов; упираясь в коллайдер
/// (Collider2D.Cast своей формой), останавливаются и выбирают другую точку.
/// Случайность — System.Random от seed карты и точки появления овцы (SheepWanderPlanner.MakeSeed).
/// Позиция меняется через ObjectManager.Move (индекс по тайлам, Lua видит текущие x/y).
/// Plain-класс: создаётся в Level, Attach(luaSandbox), Dispose в Level.OnDestroy.
/// </summary>
public class SheepWanderSystem : IDisposable
{
    public const float WanderRadius = 2f;
    // Тайлов в секунду — заметно медленнее игрока
    public const float Speed = 0.5f;
    public const float WaitMin = 2f;
    public const float WaitMax = 4f;
    // Меньше этого шага новая точка не нужна
    private const float MinStep = 0.5f;
    private const float CollisionSkin = 0.05f;
    // Не ближе стольких тайлов к области головоломки
    private const int PuzzleMargin = 1;
    // Запас к радиусу лагеря (в тайлах)
    private const float CampMargin = 1f;
    // Большой кадр (сворачивание, отладка) не телепортирует овцу
    private const float MaxDeltaTime = 0.1f;

    private class Sheep
    {
        public SheepObject Obj;
        public Vector2 Home;
        public System.Random Random;
        public Rect Footprint;
        public Collider2D Body;
        public SpriteRenderer[] Renderers;
        public bool HasTarget;
        public Vector2 Target;
        public float Wait;
    }

    private readonly IMapDataProvider _mapData;
    private readonly ObjectManager _objects;
    private readonly PuzzleManager _puzzles;
    private readonly IReadOnlyList<Vector2> _campCenters;
    private readonly float _campRadius;

    private readonly Dictionary<SheepObject, Sheep> _sheep = new Dictionary<SheepObject, Sheep>();
    private readonly List<Sheep> _tickBuffer = new List<Sheep>();
    private readonly List<RaycastHit2D> _hits = new List<RaycastHit2D>();
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

    private LuaSandbox _sandbox;
    private CancellationTokenSource _clock;
    private bool _disposed;

    /// <summary>Сколько овец бродит (для отладки).</summary>
    public int Count => _sheep.Count;

    /// <param name="puzzles">Может быть null — без проверки головоломок.</param>
    /// <param name="campCenters">EnemyManager.CampCenters (может быть null — без лагерей).</param>
    /// <param name="campRadius">EnemyConfig.campRadius.</param>
    public SheepWanderSystem(IMapDataProvider mapData, ObjectManager objects, PuzzleManager puzzles,
        IReadOnlyList<Vector2> campCenters, float campRadius)
    {
        _mapData = mapData;
        _objects = objects;
        _puzzles = puzzles;
        _campCenters = campCenters;
        _campRadius = campRadius;

        foreach (SheepObject sheep in _objects.GetAll<SheepObject>())
            Register(sheep);
        _objects.OnObjectAdded += HandleObjectAdded;
    }

    /// <summary>Двигать овец только пока выполняется код.</summary>
    public void Attach(LuaSandbox sandbox)
    {
        _sandbox = sandbox;
        _sandbox.OnRunningChanged += HandleRunningChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopClock();
        _lifetime.Cancel();
        _lifetime.Dispose();

        if (_sandbox != null) _sandbox.OnRunningChanged -= HandleRunningChanged;
        if (_objects != null) _objects.OnObjectAdded -= HandleObjectAdded;
        foreach (SheepObject obj in _sheep.Keys)
            if (obj != null) obj.OnConsumed -= HandleConsumed;
        _sheep.Clear();
    }

    // ─── Registration ─────────────────────────────────────────

    private void HandleObjectAdded(ResourceObject obj)
    {
        if (obj is SheepObject sheep) Register(sheep);
    }

    private void Register(SheepObject obj)
    {
        if (obj == null || _sheep.ContainsKey(obj)) return;

        var random = new System.Random(SheepWanderPlanner.MakeSeed(_mapData.Seed, obj.Position));
        _sheep[obj] = new Sheep
        {
            Obj = obj,
            Home = obj.Position,
            Random = random,
            Footprint = ObjectFootprint.Get(obj),
            Body = FindBodyCollider(obj),
            Renderers = obj.GetComponentsInChildren<SpriteRenderer>(true),
            // Первая пауза короче и у всех разная — стадо не трогается разом
            Wait = SheepWanderPlanner.NextWait(random, 0f, WaitMax),
        };
        obj.OnConsumed += HandleConsumed;
    }

    // Добыта или убрана с карты — больше не ходит (экземпляр может вернуться из пула — тогда регистрируется заново)
    private void HandleConsumed(ResourceObject obj)
    {
        obj.OnConsumed -= HandleConsumed;
        if (obj is SheepObject sheep) _sheep.Remove(sheep);
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

        _clock = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        RunClockAsync(_clock.Token).Forget();
    }

    private void StopClock()
    {
        if (_clock == null) return;

        _clock.Cancel();
        _clock.Dispose();
        _clock = null;
    }

    private async UniTaskVoid RunClockAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Код стоит на точке останова — овцы тоже стоят
                if (_sandbox == null || !_sandbox.IsPaused)
                    Tick(Mathf.Min(Time.deltaTime, MaxDeltaTime));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Код закончился — овцы замирают
        }
    }

    // ─── Tick ─────────────────────────────────────────────────

    private void Tick(float deltaTime)
    {
        if (_sheep.Count == 0) return;

        _tickBuffer.Clear();
        _tickBuffer.AddRange(_sheep.Values);
        // Коллайдеры двигались (игрок, враги, овцы) — Cast должен видеть их текущие места
        Physics2D.SyncTransforms();

        foreach (Sheep sheep in _tickBuffer)
        {
            if (sheep.Obj == null || !sheep.Obj.IsAvailable) continue;
            TickSheep(sheep, deltaTime);
        }
    }

    private void TickSheep(Sheep sheep, float deltaTime)
    {
        if (sheep.Wait > 0f)
        {
            sheep.Wait -= deltaTime;
            return;
        }

        if (!sheep.HasTarget)
        {
            sheep.HasTarget = SheepWanderPlanner.TryChooseTarget(
                sheep.Home, sheep.Obj.Position, WanderRadius, MinStep, sheep.Random,
                point => IsAllowed(point, sheep.Footprint), out sheep.Target);
            if (!sheep.HasTarget) StartWait(sheep);
            return;
        }

        Vector2 current = sheep.Obj.Position;
        Vector2 next = Vector2.MoveTowards(current, sheep.Target, Speed * deltaTime);
        Vector2 move = next - current;
        float distance = move.magnitude;

        // Путь проверен при выборе цели, но на нём мог оказаться игрок, враг или постройка
        if (distance > 1e-5f && (!IsAllowed(next, sheep.Footprint) || IsBlocked(sheep, move / distance, distance)))
        {
            StartWait(sheep);
            return;
        }

        _objects.Move(sheep.Obj, next);
        Face(sheep, move);

        if ((sheep.Target - next).sqrMagnitude < 1e-6f)
            StartWait(sheep);
    }

    private static void StartWait(Sheep sheep)
    {
        sheep.HasTarget = false;
        sheep.Wait = SheepWanderPlanner.NextWait(sheep.Random, WaitMin, WaitMax);
    }

    // ─── Placement ────────────────────────────────────────────

    /// <summary>
    /// Под коллайдером овцы в точке — только суша, рядом нет головоломок (лабиринт со стенами, обелиски),
    /// точка не в лагере врагов.
    /// </summary>
    private bool IsAllowed(Vector2 point, Rect footprint)
    {
        Rect area = new Rect(footprint.position + point, footprint.size);
        Vector2Int min = MapPosition.ToTile(area.min);
        Vector2Int max = MapPosition.ToTile(area.max);

        for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
                if (!_mapData.IsWalkable(x, y))
                    return false;

        if (_puzzles != null && _puzzles.Puzzles.Count > 0)
            for (int x = min.x - PuzzleMargin; x <= max.x + PuzzleMargin; x++)
                for (int y = min.y - PuzzleMargin; y <= max.y + PuzzleMargin; y++)
                {
                    var tile = new Vector2Int(x, y);
                    if (_puzzles.GetPuzzleAt(tile) != null || _puzzles.IsMazeWall(tile))
                        return false;
                }

        if (_campCenters != null)
            foreach (Vector2 center in _campCenters)
                if (Vector2.Distance(point, center) < _campRadius + CampMargin)
                    return false;

        return true;
    }

    /// <summary>
    /// «Протаскивает» коллайдер овцы на distance тайлов: впереди чужой не-триггерный коллайдер — true.
    /// </summary>
    private bool IsBlocked(Sheep sheep, Vector2 direction, float distance)
    {
        if (sheep.Body == null) return false;

        float scale = Mathf.Max(WorldScale, 0.0001f);
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        int count = sheep.Body.Cast(direction, filter, _hits, (distance + CollisionSkin) * scale, true);

        Transform own = sheep.Obj.transform;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = _hits[i];
            if (hit.collider.transform.IsChildOf(own)) continue;
            // Уже касаемся сзади или сбоку — уходить от препятствия можно
            if (Vector2.Dot(direction, hit.normal) >= 0f) continue;
            return true;
        }

        return false;
    }

    private float WorldScale => Mathf.Abs(_mapData.MapToWorld(Vector2.right).x - _mapData.MapToWorld(Vector2.zero).x);

    // ─── Helpers ──────────────────────────────────────────────

    private static Collider2D FindBodyCollider(SheepObject obj)
    {
        foreach (Collider2D collider in obj.GetComponentsInChildren<Collider2D>(true))
            if (!collider.isTrigger)
                return collider;
        return null;
    }

    /// <summary>Спрайты Tiny Swords смотрят вправо — поворачиваем по направлению шага.</summary>
    private static void Face(Sheep sheep, Vector2 move)
    {
        if (Mathf.Abs(move.x) < 1e-4f) return;
        bool flip = move.x < 0f;

        foreach (SpriteRenderer sr in sheep.Renderers)
            if (sr != null)
                sr.flipX = flip;
    }
}
