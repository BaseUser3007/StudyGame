using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Всплывающие цифры урона: белые над врагом (удар игрока — CombatService.OnEnemyDamaged),
/// красные над игроком (удар врага — EnemyManager.OnPlayerAttacked). Число всплывает и гаснет за Lifetime.
/// Цифры — пиксельный шрифт 3×5 с чёрной обводкой, рисуется в рантайм-текстуру (без шрифтов, TMP и Canvas).
/// Объекты из пула (не больше MaxCount; при переполнении переиспользуется самое старое число) —
/// после разогрева удары не создают ни объектов, ни мусора.
/// Порядок — DepthSorting.FloatingTextOrder: над объектами, под туманом. Враг под туманом — числа нет.
/// Создаётся в Level; от событий отписывается в OnDestroy.
/// </summary>
public class DamageNumbers : MonoBehaviour
{
    // ─── Константы ────────────────────────────────────────────

    private const float Lifetime = 0.8f;
    // На сколько число поднимается за жизнь (в мировых единицах)
    private const float RiseHeight = 0.6f;
    // Доля жизни, после которой число начинает гаснуть
    private const float FadeStart = 0.45f;
    // «Выскакивание» в начале: масштаб падает с PopScale до 1 за PopDuration доли жизни
    private const float PopScale = 1.35f;
    private const float PopDuration = 0.15f;

    private const int MaxDigits = 4;
    private const int MaxValue = 9999;
    private const int PrewarmCount = 8;
    private const int MaxCount = 32;

    // Глиф: 3×5 пикселей + обводка по 1 с каждой стороны; в атласе между глифами пиксель зазора
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int CellWidth = GlyphWidth + 1;
    // Шаг между цифрами: обводки соседних цифр перекрываются
    private const int GlyphAdvance = 4;
    private const float PixelsPerUnit = 16f;

    // Цифры 0–9: 5 строк по 3 пикселя, сверху вниз
    private static readonly string[] DigitPatterns =
    {
        "111101101101111", // 0
        "010110010010111", // 1
        "111001111100111", // 2
        "111001111001111", // 3
        "101101111001001", // 4
        "111100111001111", // 5
        "111100111101111", // 6
        "111001010010010", // 7
        "111101111101111", // 8
        "111101111001111"  // 9
    };

    // Небольшой сдвиг по X, чтобы частые удары по одной цели не сливались в одно число
    private static readonly float[] JitterX = { 0f, 0.12f, -0.12f };

    private static Sprite[] _digitSprites;

    /// <summary>Play Mode без перезагрузки домена: статический шрифт (DontSave) уничтожается и строится заново.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        if (_digitSprites == null) return;
        if (_digitSprites[0] != null) Object.Destroy(_digitSprites[0].texture);
        foreach (Sprite sprite in _digitSprites)
            if (sprite != null) Object.Destroy(sprite);
        _digitSprites = null;
    }

    // ─── Настройки ────────────────────────────────────────────

    [Tooltip("Высота появления над опорной точкой (основанием) врага, в его локальных единицах")]
    [SerializeField] private float enemyHeight = 1.45f;
    [Tooltip("Высота появления над опорной точкой (основанием) игрока, в его локальных единицах")]
    [SerializeField] private float playerHeight = 1.3f;
    [SerializeField] private Color enemyDamageColor = Color.white;
    [SerializeField] private Color playerDamageColor = new Color(1f, 0.25f, 0.2f, 1f);

    // ─── Состояние ────────────────────────────────────────────

    private sealed class FloatingNumber
    {
        public GameObject GameObject;
        public Transform Transform;
        public SpriteRenderer[] Digits;
        public int DigitCount;
        public Color Color;
        public Vector3 Origin;
        public float Age;
    }

    private EnemyManager _enemies;
    private CombatService _combat;
    private Transform _player;

    private Material _material;
    private int _sortingLayerId;

    // Активные числа в порядке появления (самое старое — первое)
    private readonly List<FloatingNumber> _active = new List<FloatingNumber>(MaxCount);
    private readonly Stack<FloatingNumber> _free = new Stack<FloatingNumber>(MaxCount);
    private int _created;
    private int _spawnCounter;

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Подписаться на события боя. Материал и слой сортировки берутся у спрайта игрока (освещение URP 2D).
    /// </summary>
    public void Initialize(EnemyManager enemies, CombatService combat, Transform player)
    {
        Debug.Assert(enemies != null, "DamageNumbers: EnemyManager не передан");
        Debug.Assert(combat != null, "DamageNumbers: CombatService не передан");
        Debug.Assert(player != null, "DamageNumbers: игрок не передан");

        Unsubscribe();
        _enemies = enemies;
        _combat = combat;
        _player = player;

        SpriteRenderer reference = player != null ? player.GetComponentInChildren<SpriteRenderer>() : null;
        if (reference != null)
        {
            _material = reference.sharedMaterial;
            _sortingLayerId = reference.sortingLayerID;
        }

        EnsureSprites();
        for (int i = 0; i < PrewarmCount; i++)
            _free.Push(CreateNumber());

        if (_enemies != null) _enemies.OnPlayerAttacked += HandlePlayerAttacked;
        if (_combat != null) _combat.OnEnemyDamaged += HandleEnemyDamaged;
    }

    /// <summary>Показать число в мировой точке (низ числа — в точке).</summary>
    public void Show(Vector3 worldPosition, int value, Color color)
    {
        if (value <= 0) return;
        value = Mathf.Min(value, MaxValue);

        FloatingNumber number = Acquire();

        int digitCount = CountDigits(value);
        number.DigitCount = digitCount;
        float totalWidth = (digitCount - 1) * GlyphAdvance + GlyphWidth;
        float left = -totalWidth * 0.5f + GlyphWidth * 0.5f;

        // Цифры справа налево: младшая — последняя
        int rest = value;
        for (int i = digitCount - 1; i >= 0; i--)
        {
            SpriteRenderer digit = number.Digits[i];
            digit.sprite = _digitSprites[rest % 10];
            digit.transform.localPosition = new Vector3((left + i * GlyphAdvance) / PixelsPerUnit, 0f, 0f);
            digit.enabled = true;
            rest /= 10;
        }
        for (int i = digitCount; i < MaxDigits; i++)
            number.Digits[i].enabled = false;

        float jitter = JitterX[_spawnCounter % JitterX.Length];
        _spawnCounter++;

        number.Color = color;
        number.Origin = worldPosition + new Vector3(jitter, 0f, 0f);
        number.Age = 0f;
        number.GameObject.SetActive(true);
        Apply(number);
        _active.Add(number);
    }

    // ─── Обработчики событий ──────────────────────────────────

    private void HandleEnemyDamaged(Enemy enemy, int damage)
    {
        // Враг под туманом: удар был, но числа не видно
        if (enemy == null || !enemy.IsVisible) return;

        Transform target = enemy.transform;
        Show(target.position + Vector3.up * (enemyHeight * target.lossyScale.y), damage, enemyDamageColor);
    }

    private void HandlePlayerAttacked(Enemy attacker, float damage)
    {
        if (_player == null || damage <= 0f) return;

        Show(_player.position + Vector3.up * (playerHeight * _player.lossyScale.y),
            Mathf.Max(1, Mathf.RoundToInt(damage)), playerDamageColor);
    }

    // ─── Lifecycle ────────────────────────────────────────────

    private void Update()
    {
        if (_active.Count == 0) return;

        float dt = Time.deltaTime;
        // С конца: RemoveAt сохраняет порядок остальных (самое старое — первое)
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            FloatingNumber number = _active[i];
            number.Age += dt;
            if (number.Age >= Lifetime)
            {
                Release(number);
                _active.RemoveAt(i);
                continue;
            }
            Apply(number);
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    // ─── Пул ──────────────────────────────────────────────────

    private FloatingNumber Acquire()
    {
        if (_free.Count > 0) return _free.Pop();
        if (_created < MaxCount) return CreateNumber();

        // Пул исчерпан — переиспользуем самое старое активное число
        FloatingNumber oldest = _active[0];
        _active.RemoveAt(0);
        return oldest;
    }

    private void Release(FloatingNumber number)
    {
        number.GameObject.SetActive(false);
        _free.Push(number);
    }

    private FloatingNumber CreateNumber()
    {
        _created++;
        GameObject root = new GameObject("DamageNumber");
        root.transform.SetParent(transform, false);

        FloatingNumber number = new FloatingNumber
        {
            GameObject = root,
            Transform = root.transform,
            Digits = new SpriteRenderer[MaxDigits]
        };

        for (int i = 0; i < MaxDigits; i++)
        {
            GameObject digitObject = new GameObject("Digit");
            digitObject.transform.SetParent(root.transform, false);

            SpriteRenderer sr = digitObject.AddComponent<SpriteRenderer>();
            if (_material != null) sr.sharedMaterial = _material;
            sr.sortingLayerID = _sortingLayerId;
            sr.sortingOrder = DepthSorting.FloatingTextOrder;
            sr.enabled = false;
            number.Digits[i] = sr;
        }

        root.SetActive(false);
        return number;
    }

    // ─── Анимация ─────────────────────────────────────────────

    private static void Apply(FloatingNumber number)
    {
        float t = Mathf.Clamp01(number.Age / Lifetime);

        // Подъём с замедлением к концу
        float rise = 1f - (1f - t) * (1f - t);
        number.Transform.position = number.Origin + new Vector3(0f, RiseHeight * rise, 0f);

        float scale = t < PopDuration ? Mathf.Lerp(PopScale, 1f, t / PopDuration) : 1f;
        number.Transform.localScale = new Vector3(scale, scale, 1f);

        float alpha = t <= FadeStart ? 1f : 1f - (t - FadeStart) / (1f - FadeStart);
        Color color = number.Color;
        color.a *= alpha;
        for (int i = 0; i < number.DigitCount; i++)
            number.Digits[i].color = color;
    }

    private static int CountDigits(int value)
    {
        int count = 1;
        while (value >= 10 && count < MaxDigits)
        {
            value /= 10;
            count++;
        }
        return count;
    }

    // ─── Шрифт ────────────────────────────────────────────────

    private static void EnsureSprites()
    {
        // Проверка через Unity-null: после выхода из Play Mode без перезагрузки домена текстура уничтожена
        if (_digitSprites != null && _digitSprites[0] != null) return;

        int width = CellWidth * DigitPatterns.Length;
        Color32[] pixels = new Color32[width * GlyphHeight];
        Color32 outline = new Color32(0, 0, 0, 255);
        Color32 fill = new Color32(255, 255, 255, 255);

        for (int g = 0; g < DigitPatterns.Length; g++)
        {
            string pattern = DigitPatterns[g];
            // Сначала обводка вокруг каждого закрашенного пикселя, потом сам глиф поверх
            for (int pass = 0; pass < 2; pass++)
            for (int row = 0; row < 5; row++)
            for (int col = 0; col < 3; col++)
            {
                if (pattern[row * 3 + col] != '1') continue;
                int x = g * CellWidth + 1 + col;
                int y = 1 + (4 - row);
                if (pass == 1)
                {
                    pixels[y * width + x] = fill;
                    continue;
                }
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    pixels[(y + dy) * width + x + dx] = outline;
            }
        }

        Texture2D texture = new Texture2D(width, GlyphHeight, TextureFormat.RGBA32, false)
        {
            name = "DamageDigits",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        _digitSprites = new Sprite[DigitPatterns.Length];
        for (int g = 0; g < DigitPatterns.Length; g++)
        {
            Rect rect = new Rect(g * CellWidth, 0f, GlyphWidth, GlyphHeight);
            Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0f), PixelsPerUnit);
            sprite.name = "DamageDigit" + g;
            sprite.hideFlags = HideFlags.DontSave;
            _digitSprites[g] = sprite;
        }
    }

    private void Unsubscribe()
    {
        if (_enemies != null) _enemies.OnPlayerAttacked -= HandlePlayerAttacked;
        if (_combat != null) _combat.OnEnemyDamaged -= HandleEnemyDamaged;
    }
}
