using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Снаряды врагов в полёте (стрелы лучников). Создаёт и тикает EnemyManager — только пока идёт игровое время
/// (выполняется код и он не на паузе), так что между запусками стрела висит в воздухе и долетает в следующем.
/// Цель — точка, где игрок стоял в момент выстрела; при прилёте урон, только если игрок ещё не дальше HitRadius
/// от неё (ушёл с линии — промах). Картинка — префаб Arrow из EnemyDefinition (пул по префабу); без префаба
/// снаряд летит невидимым, логика та же. Под туманом стрела не рисуется, если не виден ни тайл вылета,
/// ни тайл цели, ни тайл, над которым она сейчас.
/// </summary>
public sealed class EnemyProjectiles
{
    /// <summary>Игрок не дальше этого (в тайлах) от точки прилёта — попадание.</summary>
    public const float HitRadius = 0.6f;

    private sealed class Flight
    {
        public Enemy Attacker;
        public Arrow View;
        public Vector2 From;
        public Vector2 To;
        public float Damage;
        public float Elapsed;
        public float Duration;
    }

    private readonly IMapDataProvider _mapData;
    private readonly Func<Vector2Int, bool> _isTileVisible;
    private readonly Func<Vector2> _playerPosition;
    private readonly Func<bool> _isPlayerAlive;
    private readonly Action<Enemy, float> _damagePlayer;
    private readonly Func<float> _worldScale;
    private readonly Transform _container;

    private readonly List<Flight> _flights = new List<Flight>();
    private readonly List<Flight> _landed = new List<Flight>();
    private readonly Dictionary<Arrow, Queue<Arrow>> _pool = new Dictionary<Arrow, Queue<Arrow>>();

    public EnemyProjectiles(IMapDataProvider mapData, Transform container, Func<Vector2Int, bool> isTileVisible,
        Func<Vector2> playerPosition, Func<bool> isPlayerAlive, Action<Enemy, float> damagePlayer,
        Func<float> worldScale)
    {
        _mapData = mapData;
        _container = container;
        _isTileVisible = isTileVisible;
        _playerPosition = playerPosition;
        _isPlayerAlive = isPlayerAlive;
        _damagePlayer = damagePlayer;
        _worldScale = worldScale;
    }

    /// <summary>Снарядов в полёте.</summary>
    public int Count => _flights.Count;

    // ─── Public API ───────────────────────────────────────────

    /// <summary>Выстрел attacker в точку target (позиции карты). Урон и время полёта — из характеристик стрелка.</summary>
    public void Launch(Enemy attacker, Vector2 target)
    {
        if (attacker == null) return;

        var flight = new Flight
        {
            Attacker = attacker,
            From = attacker.Position,
            To = target,
            Damage = attacker.Damage,
            Duration = Mathf.Max(0f, attacker.Stats.ProjectileFlightTime)
        };

        Arrow prefab = attacker.Definition != null ? attacker.Definition.projectilePrefab : null;
        if (prefab != null)
        {
            flight.View = GetFromPool(prefab);
            flight.View.Place(_mapData.MapToWorld(flight.From), _mapData.MapToWorld(flight.To), 0f, _worldScale());
        }

        _flights.Add(flight);

        // Мгновенный снаряд (время полёта 0) — попадание в этом же тике
        if (flight.Duration <= 0f) Tick(0f);
        else RefreshVisibility(flight);
    }

    /// <summary>Шаг игрового времени: стрелы летят, долетевшие наносят урон и уходят в пул.</summary>
    public void Tick(float deltaTime)
    {
        if (_flights.Count == 0) return;

        float scale = _worldScale();
        _landed.Clear();

        foreach (Flight flight in _flights)
        {
            flight.Elapsed += deltaTime;
            float t = flight.Duration > 0f ? flight.Elapsed / flight.Duration : 1f;

            if (t >= 1f)
            {
                _landed.Add(flight);
                continue;
            }

            if (flight.View != null)
            {
                flight.View.Place(_mapData.MapToWorld(flight.From), _mapData.MapToWorld(flight.To), t, scale);
                RefreshVisibility(flight);
            }
        }

        foreach (Flight flight in _landed)
        {
            _flights.Remove(flight);
            Land(flight);
        }
    }

    /// <summary>
    /// Запуск кода закончился: стрелы в полёте исчезают без урона — иначе зависшая стрела ударила бы
    /// в первые тики следующего запуска (в обход runStartGrace), пока код игрока ещё ничего не сделал.
    /// </summary>
    public void Clear()
    {
        foreach (Flight flight in _flights)
            if (flight.View != null) ReturnToPool(flight.View);
        _flights.Clear();
    }

    /// <summary>Видимость всех стрел (туман мог измениться между тиками).</summary>
    public void RefreshVisibility()
    {
        foreach (Flight flight in _flights)
            RefreshVisibility(flight);
    }

    // ─── Internals ────────────────────────────────────────────

    private void Land(Flight flight)
    {
        if (flight.View != null) ReturnToPool(flight.View);

        if (!_isPlayerAlive()) return;
        if (!RangedEnemyPlanner.IsProjectileHit(flight.To, _playerPosition(), HitRadius)) return;

        // Стрелок мог погибнуть, пока стрела летела, — она всё равно долетает
        _damagePlayer(flight.Attacker, flight.Damage);
    }

    private void RefreshVisibility(Flight flight)
    {
        if (flight.View == null) return;

        float t = flight.Duration > 0f ? Mathf.Clamp01(flight.Elapsed / flight.Duration) : 1f;
        Vector2 current = Vector2.Lerp(flight.From, flight.To, t);
        bool visible = _isTileVisible(MapPosition.ToTile(flight.From))
                       || _isTileVisible(MapPosition.ToTile(flight.To))
                       || _isTileVisible(MapPosition.ToTile(current));
        flight.View.SetVisible(visible);
    }

    private Arrow GetFromPool(Arrow prefab)
    {
        Arrow arrow = _pool.TryGetValue(prefab, out Queue<Arrow> queue) && queue.Count > 0
            ? queue.Dequeue()
            : UnityEngine.Object.Instantiate(prefab, _container);

        arrow.PrefabKey = prefab;
        arrow.gameObject.SetActive(true);
        return arrow;
    }

    private void ReturnToPool(Arrow arrow)
    {
        arrow.gameObject.SetActive(false);
        if (arrow.PrefabKey == null)
        {
            UnityEngine.Object.Destroy(arrow.gameObject);
            return;
        }

        if (!_pool.TryGetValue(arrow.PrefabKey, out Queue<Arrow> queue))
            _pool[arrow.PrefabKey] = queue = new Queue<Arrow>();
        queue.Enqueue(arrow);
    }
}
