using UnityEngine;

/// <summary>
/// Тонкая полоса здоровья над врагом (спрайты мира, не UI Toolkit).
/// Видна, только если враг жив, видим (<see cref="Enemy.IsVisible"/>) и ранен (Health &lt; MaxHealth).
/// Самодостаточна: рендереры фона и заливки создаются в коде из сгенерированного белого спрайта,
/// так что префабу достаточно самого Enemy — он добавляет полосу сам (см. Enemy.Awake).
///
/// Сортировка: рендереры создаются в Start, уже после того как DepthSortedSprite и Enemy (в Awake)
/// закешировали свои рендереры, поэтому полоса не попадает ни в общий sortingOrder, ни в SetVisible/flipX врага.
/// Порядок полосы считается здесь же от той же высоты (DepthSorting.GetOrder) + наибольшее смещение
/// рендереров врага + 1/2 — поверх своего врага, но ниже тумана (DepthSorting.OverlayOrder).
/// Пул: состояние не хранится — каждый кадр сравниваются Health/MaxHealth, поэтому после Setup
/// (здоровье полное) полоса скрывается сама.
/// </summary>
[DisallowMultipleComponent]
public class EnemyHealthBar : MonoBehaviour
{
    [Tooltip("Высота полосы над опорной точкой врага (основанием), в локальных единицах врага")]
    [SerializeField] private float heightOffset = 1.25f;
    [Tooltip("Ширина полосы")]
    [SerializeField] private float width = 0.7f;
    [Tooltip("Толщина полосы")]
    [SerializeField] private float thickness = 0.09f;
    [Tooltip("Отступ заливки от края фона")]
    [SerializeField] private float border = 0.015f;
    [SerializeField] private Color backgroundColor = new Color(0.08f, 0.06f, 0.06f, 0.85f);
    [SerializeField] private Color fillColor = new Color(0.86f, 0.18f, 0.16f, 1f);

    [Header("Вожак лагеря")]
    [Tooltip("TypeId вожака: у него полоса шире и другого цвета")]
    [SerializeField] private string leaderTypeId = "goblin_guard";
    [SerializeField] private float leaderWidthMultiplier = 1.3f;
    [SerializeField] private Color leaderFillColor = new Color(0.95f, 0.6f, 0.12f, 1f);

    // Общий белый спрайт 1×1 (пиксель = 1 единица): с опорой в центре — фон, у левого края — заливка
    private static Sprite _centerSprite;
    private static Sprite _leftSprite;

    private Enemy _enemy;
    private SpriteRenderer[] _enemyRenderers;
    private int _maxOrderOffset;
    private Transform _root;
    private SpriteRenderer _background;
    private SpriteRenderer _fill;
    private Transform _fillTransform;

    private bool _shown;
    private int _lastHealth = -1;
    private int _lastMaxHealth = -1;
    private string _lastTypeId;
    private float _fillWidth;

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Привязать к врагу. Вызывает Enemy в конце Awake; рендереры создаются позже, в Start.
    /// Повторный вызов ничего не делает.
    /// </summary>
    public void Bind(Enemy enemy)
    {
        if (_enemy != null || enemy == null) return;
        _enemy = enemy;
    }

    // ─── Lifecycle ────────────────────────────────────────────

    /// <summary>
    /// Рендереры создаются в Start — после Awake всех компонентов объекта: даже если DepthSortedSprite
    /// стоит на префабе и проснулся позже Enemy, он уже закешировал свои рендереры без полосы.
    /// </summary>
    private void Start()
    {
        if (_enemy == null && !TryGetComponent(out _enemy))
        {
            Debug.LogWarning($"EnemyHealthBar: на {name} нет Enemy — полоса отключена", this);
            enabled = false;
            return;
        }
        CreateBar();
    }

    private void CreateBar()
    {
        if (_root != null) return;
        Enemy enemy = _enemy;

        _enemyRenderers = enemy.GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer reference = _enemyRenderers.Length > 0 ? _enemyRenderers[0] : null;

        // Рендереры врага сохраняют исходные смещения порядка (DepthSortedSprite) — полоса над самым верхним
        int min = int.MaxValue, max = int.MinValue;
        foreach (SpriteRenderer sr in _enemyRenderers)
        {
            min = Mathf.Min(min, sr.sortingOrder);
            max = Mathf.Max(max, sr.sortingOrder);
        }
        _maxOrderOffset = _enemyRenderers.Length > 0 ? max - min : 0;

        EnsureSprites();

        _root = new GameObject("HealthBar").transform;
        _root.SetParent(enemy.transform, false);
        _root.localPosition = new Vector3(0f, heightOffset, 0f);

        _background = CreateRenderer("Background", _centerSprite, reference);
        _fill = CreateRenderer("Fill", _leftSprite, reference);
        _fillTransform = _fill.transform;

        SetShown(false);
    }

    private void LateUpdate()
    {
        if (_enemy == null || _background == null) return;

        bool show = _enemy.IsAlive && _enemy.IsVisible && _enemy.MaxHealth > 0 && _enemy.Health < _enemy.MaxHealth;
        SetShown(show);
        if (!show) return;

        // Враг из пула мог стать другим типом — пересчитываем размеры и цвет
        if (_enemy.TypeId != _lastTypeId)
            ApplyLayout();

        if (_enemy.Health != _lastHealth || _enemy.MaxHealth != _lastMaxHealth)
            ApplyHealth();

        // Тот же расчёт, что у DepthSortedSprite (от высоты врага), — не зависит от порядка LateUpdate
        int order = DepthSorting.GetOrder(_enemy.transform.position.y) + _maxOrderOffset;
        _background.sortingOrder = order + 1;
        _fill.sortingOrder = order + 2;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void SetShown(bool shown)
    {
        if (_background == null || (_shown == shown && _background.enabled == shown)) return;
        _shown = shown;
        _background.enabled = shown;
        _fill.enabled = shown;

        // При следующем показе всё пересчитать (враг мог быть выдан из пула заново)
        if (!shown)
        {
            _lastHealth = -1;
            _lastMaxHealth = -1;
            _lastTypeId = null;
        }
    }

    private void ApplyLayout()
    {
        _lastTypeId = _enemy.TypeId;
        bool leader = !string.IsNullOrEmpty(leaderTypeId) && _lastTypeId == leaderTypeId;

        float barWidth = width * (leader ? leaderWidthMultiplier : 1f);
        _background.transform.localScale = new Vector3(barWidth, thickness, 1f);
        _background.color = backgroundColor;

        _fillWidth = Mathf.Max(0f, barWidth - border * 2f);
        _fillTransform.localPosition = new Vector3(-barWidth * 0.5f + border, 0f, 0f);
        _fill.color = leader ? leaderFillColor : fillColor;

        // Размер заливки зависит от ширины — обновить
        _lastHealth = -1;
    }

    private void ApplyHealth()
    {
        _lastHealth = _enemy.Health;
        _lastMaxHealth = _enemy.MaxHealth;

        float t = Mathf.Clamp01((float)_lastHealth / _lastMaxHealth);
        _fillTransform.localScale = new Vector3(_fillWidth * t, Mathf.Max(0f, thickness - border * 2f), 1f);
    }

    private SpriteRenderer CreateRenderer(string objectName, Sprite sprite, SpriteRenderer reference)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(_root, false);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        if (reference != null)
        {
            // Тот же материал и слой, что у спрайта врага (освещение URP 2D, слой сортировки)
            sr.sharedMaterial = reference.sharedMaterial;
            sr.sortingLayerID = reference.sortingLayerID;
        }
        sr.enabled = false;
        return sr;
    }

    private static void EnsureSprites()
    {
        // Проверка через Unity-null: после выхода из Play Mode без перезагрузки домена текстура уничтожена
        if (_centerSprite != null && _leftSprite != null) return;

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "EnemyHealthBarPixel",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        texture.SetPixel(0, 0, Color.white);
        texture.Apply(false, true);

        Rect rect = new Rect(0f, 0f, 1f, 1f);
        _centerSprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 1f);
        _centerSprite.name = "EnemyHealthBarCenter";
        _centerSprite.hideFlags = HideFlags.DontSave;
        _leftSprite = Sprite.Create(texture, rect, new Vector2(0f, 0.5f), 1f);
        _leftSprite.name = "EnemyHealthBarLeft";
        _leftSprite.hideFlags = HideFlags.DontSave;
    }
}
