using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Итог удара по врагу (для сообщения в Lua).
/// </summary>
public readonly struct AttackResult
{
    public readonly Enemy Target;
    public readonly int Damage;
    public readonly bool Killed;
    /// <summary>Чем били; null — кулаком.</summary>
    public readonly WeaponItem Weapon;
    /// <summary>Оружие сломалось этим ударом.</summary>
    public readonly bool WeaponBroken;

    public AttackResult(Enemy target, int damage, bool killed, WeaponItem weapon, bool weaponBroken)
    {
        Target = target;
        Damage = damage;
        Killed = killed;
        Weapon = weapon;
        WeaponBroken = weaponBroken;
    }
}

/// <summary>
/// Бой игрока (GameDesign §10.1): удар по врагу лучшим оружием из инвентаря (PlayerInventory.FindWeaponSlot)
/// или кулаком (5 урона, радиус 1,5). Удар длится StrikeDuration (0,5 с): урон наносится сразу, затем
/// команда ждёт (AttackAsync) — не больше двух ударов в секунду; враги в это время действуют.
/// Оружие изнашивается на wearPerUse за удар и ломается при нуле. Дистанция — от точной позиции игрока
/// до точки врага, не больше радиуса удара оружия (кулак — FistRadius).
/// Энергия: только цена вызова команды (её списывает базовый класс Lua-команды), сам удар бесплатный —
/// иначе бой кулаками стоил бы дороже, чем выгодно писать.
/// Общий для attack() и будущих готовых команд, чтобы удар везде работал одинаково.
/// </summary>
public class CombatService
{
    public const int FistDamage = 5;
    /// <summary>Сколько длится удар (игровое время идёт — враги действуют).</summary>
    public const float StrikeDuration = 0.5f;
    public const float FistRadius = 1.5f;
    // Насколько далеко от указанной точки может стоять враг (attack(x, y))
    private const float PickTolerance = 0.75f;

    private readonly PlayerCommandExecutor _executor;
    private readonly PlayerInventory _inventory;
    private readonly EnemyManager _enemies;

    /// <summary>
    /// Удар игрока попал во врага (урон уже нанесён). Враг мог умереть и уйти в пул, но позиция
    /// и IsVisible у него остаются как в момент удара. Только для визуала (цифры урона).
    /// </summary>
    public event Action<Enemy, int> OnEnemyDamaged;

    public CombatService(PlayerCommandExecutor executor, PlayerInventory inventory, EnemyManager enemies)
    {
        _executor = executor;
        _inventory = inventory;
        _enemies = enemies;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>Лучшее оружие в инвентаре или null (кулак).</summary>
    public WeaponItem CurrentWeapon
    {
        get
        {
            int slot = _inventory.FindWeaponSlot();
            return slot >= 0 ? _inventory.GetSlot(slot).Item as WeaponItem : null;
        }
    }

    /// <summary>Урон следующего удара (оружие или кулак).</summary>
    public int AttackDamage => CurrentWeapon is WeaponItem weapon ? weapon.Damage : FistDamage;

    /// <summary>Дальность следующего удара в тайлах.</summary>
    public float AttackRadius => CurrentWeapon is WeaponItem weapon ? weapon.AttackRadius : FistRadius;

    /// <summary>Дистанция от игрока до врага в тайлах (как её проверяет удар).</summary>
    public float DistanceTo(Enemy target) => Vector2.Distance(_executor.Position, target.Position);

    public bool TryAttack(Enemy target, out string error) => TryAttack(target, out _, out error);

    /// <summary>
    /// Удар с длительностью: урон сразу (TryAttack), затем ожидание StrikeDuration (только если удар был).
    /// Для Lua-команды attack — передайте ct запуска, чтобы Stop прерывал ожидание.
    /// </summary>
    public async UniTask<(bool success, AttackResult result, string error)> AttackAsync(
        Enemy target, CancellationToken ct)
    {
        bool success = TryAttack(target, out AttackResult result, out string error);
        if (success)
            await UniTask.Delay(System.TimeSpan.FromSeconds(StrikeDuration), cancellationToken: ct);
        return (success, result, error);
    }

    /// <summary>
    /// Ударить врага. false — удара не было (error — причина), ничего не изменилось.
    /// </summary>
    public bool TryAttack(Enemy target, out AttackResult result, out string error)
    {
        result = default;
        error = CheckAttack(target);
        if (error != null) return false;

        int slot = _inventory.FindWeaponSlot();
        WeaponItem weapon = slot >= 0 ? _inventory.GetSlot(slot).Item as WeaponItem : null;
        int damage = weapon != null ? weapon.Damage : FistDamage;

        bool broken = weapon != null && _inventory.WearTool(slot, weapon.wearPerUse);
        // Анимация удара с разворотом к врагу (позиция — до смерти и возврата в пул)
        _executor.PlayAction(PlayerActionKind.Attack, target.Position);
        bool killed = target.TakeDamage(damage);
        OnEnemyDamaged?.Invoke(target, damage);

        result = new AttackResult(target, damage, killed, weapon, broken);
        return true;
    }

    /// <summary>
    /// Ударить ближайшего к игроку врага в радиусе удара.
    /// </summary>
    public bool TryAttackNearest(out AttackResult result, out string error)
    {
        Enemy target = _enemies.GetNearestEnemy(_executor.Position, AttackRadius);
        if (target == null)
        {
            result = default;
            error = "no enemy within reach";
            return false;
        }

        return TryAttack(target, out result, out error);
    }

    /// <summary>
    /// Ударить врага, ближайшего к точке карты (не дальше PickTolerance от неё).
    /// </summary>
    public bool TryAttackAt(Vector2 point, out AttackResult result, out string error)
    {
        Enemy target = _enemies.GetNearestEnemy(point, PickTolerance);
        if (target == null)
        {
            result = default;
            error = System.FormattableString.Invariant($"no enemy at ({point.x:0.##}, {point.y:0.##})");
            return false;
        }

        return TryAttack(target, out result, out error);
    }

    /// <summary>
    /// Можно ли ударить врага сейчас: null — можно, иначе причина. Ничего не меняет.
    /// </summary>
    public string CheckAttack(Enemy target)
    {
        if (target == null || !target.IsAlive)
            return "enemy is gone";

        float radius = AttackRadius;
        float distance = DistanceTo(target);
        if (distance > radius)
            return System.FormattableString.Invariant($"enemy is too far: {distance:0.#} > {radius:0.#}");

        return null;
    }
}
