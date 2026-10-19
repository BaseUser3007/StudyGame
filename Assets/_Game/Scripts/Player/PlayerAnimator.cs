using UnityEngine;

/// <summary>
/// Анимация персонажа (как у врага, см. Enemy): Animator ищется на игроке или в детях (обычно на Visual),
/// может отсутствовать — тогда остаётся только разворот спрайта.
/// Параметры (необязательные, отсутствующие в контроллере не выставляются):
///   • IsMoving (Bool) — пока идёт движение PlayerCommandExecutor;
///   • Interact (Trigger) — harvest/mine/build (PlayerCommandExecutor.OnActionPlayed) и craft (GameEventBus Crafted);
///   • Attack (Trigger) — удар CombatService.
/// Спрайты Tiny Swords смотрят вправо: при движении влево и при действии с целью слева — flipX.
/// Анимация игрока не замораживается (в отличие от врагов, мир для неё не важен).
/// Добавляется в Level.SpawnPlayer, Initialize — после PlayerCommandExecutor.Initialize.
/// </summary>
public class PlayerAnimator : MonoBehaviour
{
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int InteractHash = Animator.StringToHash("Interact");
    private static readonly int AttackHash = Animator.StringToHash("Attack");

    // Меньше этого горизонтального смещения к цели — не разворачиваемся (движение строго вверх/вниз)
    private const float FaceThreshold = 0.05f;

    private PlayerCommandExecutor _executor;
    private Animator _animator;
    private SpriteRenderer[] _renderers;

    private bool _animParamsResolved;
    private bool _hasIsMovingParam;
    private bool _hasInteractParam;
    private bool _hasAttackParam;

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(PlayerCommandExecutor executor)
    {
        ValidateReferences(executor);

        _executor = executor;
        _animator = GetComponentInChildren<Animator>(true);
        _renderers = GetComponentsInChildren<SpriteRenderer>(true);

        _executor.OnActionPlayed += HandleActionPlayed;
        GameEventBus.OnGameEvent += HandleGameEvent;
    }

    // ─── Validation ───────────────────────────────────────────

    private void ValidateReferences(PlayerCommandExecutor executor)
    {
        Debug.Assert(executor != null, "PlayerAnimator: executor is not assigned");
    }

    // ─── Unity ────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (_executor == null) return;

        // Опрос в LateUpdate, а не события начала/конца движения: следующая команда moveTo обычно
        // стартует в том же кадре, где закончилась предыдущая, — без мигания Idle между ними
        bool moving = _executor.IsMoving;
        SetIsMoving(moving);
        if (moving)
            FaceTowards(_executor.MoveTarget);
    }

    private void OnDestroy()
    {
        if (_executor != null)
            _executor.OnActionPlayed -= HandleActionPlayed;
        GameEventBus.OnGameEvent -= HandleGameEvent;
    }

    // ─── Handlers ─────────────────────────────────────────────

    private void HandleActionPlayed(PlayerActionKind kind, Vector2? worldTarget)
    {
        if (worldTarget.HasValue)
            FaceTowards(worldTarget.Value);

        SetTrigger(kind == PlayerActionKind.Attack ? AttackHash : InteractHash);
    }

    private void HandleGameEvent(GameEvent gameEvent)
    {
        // Крафт идёт в команде craft() без точки на карте — только анимация, без разворота
        if (gameEvent.Type == GameEventType.Crafted)
            SetTrigger(InteractHash);
    }

    // ─── Facing ───────────────────────────────────────────────

    private void FaceTowards(Vector2 worldPoint)
    {
        float dx = worldPoint.x - transform.position.x;
        if (Mathf.Abs(dx) < FaceThreshold) return;
        bool flip = dx < 0f;

        foreach (SpriteRenderer sr in _renderers)
            if (sr != null)
                sr.flipX = flip;
    }

    // ─── Animation ────────────────────────────────────────────

    private void SetIsMoving(bool moving)
    {
        if (!CanAnimate()) return;
        if (_hasIsMovingParam && _animator.GetBool(IsMovingHash) != moving)
            _animator.SetBool(IsMovingHash, moving);
    }

    private void SetTrigger(int hash)
    {
        if (!CanAnimate()) return;
        bool has = hash == AttackHash ? _hasAttackParam : _hasInteractParam;
        if (has) _animator.SetTrigger(hash);
    }

    private bool CanAnimate()
    {
        if (_animator == null || !_animator.isActiveAndEnabled) return false;
        ResolveAnimatorParameters();
        return true;
    }

    /// <summary>
    /// Какие параметры есть в контроллере (без них SetBool/SetTrigger сыплют предупреждениями).
    /// </summary>
    private void ResolveAnimatorParameters()
    {
        if (_animParamsResolved || _animator.runtimeAnimatorController == null) return;

        foreach (AnimatorControllerParameter p in _animator.parameters)
        {
            if (p.nameHash == IsMovingHash && p.type == AnimatorControllerParameterType.Bool)
                _hasIsMovingParam = true;
            else if (p.nameHash == InteractHash && p.type == AnimatorControllerParameterType.Trigger)
                _hasInteractParam = true;
            else if (p.nameHash == AttackHash && p.type == AnimatorControllerParameterType.Trigger)
                _hasAttackParam = true;
        }
        _animParamsResolved = true;
    }
}
