using UnityEngine;

/// <summary>
/// Видимая стрела стрелка (только картинка: полёт, попадание и урон считает EnemyProjectiles).
/// Префаб: этот компонент + SpriteRenderer (на себе или дочерний) со спрайтом остриём вправо, опорная точка — центр.
/// Летит по прямой от лука к цели с небольшой дугой и поворачивается по направлению полёта.
/// Рисуется над объектами мира и под туманом (DepthSorting.ProjectileOrder), DepthSortedSprite не нужен.
/// </summary>
[DisallowMultipleComponent]
public class Arrow : MonoBehaviour
{
    [Tooltip("Высота вылета над ногами стрелка (в тайлах)")]
    [SerializeField] private float launchHeight = 0.45f;
    [Tooltip("Высота над ногами игрока, куда прилетает стрела (в тайлах)")]
    [SerializeField] private float impactHeight = 0.35f;
    [Tooltip("Высота дуги в середине полёта (в тайлах)")]
    [SerializeField] private float arcHeight = 0.3f;
    [Tooltip("Поправка угла, если спрайт нарисован не остриём вправо (градусы)")]
    [SerializeField] private float spriteAngleOffset;

    private SpriteRenderer[] _renderers;

    /// <summary>Ключ пула — префаб, из которого создана стрела.</summary>
    internal Arrow PrefabKey { get; set; }

    public bool IsVisible { get; private set; } = true;

    // ─── Lifecycle ────────────────────────────────────────────

    private void Awake()
    {
        _renderers = GetComponentsInChildren<SpriteRenderer>(true);
        if (_renderers.Length == 0)
            Debug.LogWarning($"Arrow: на {name} нет SpriteRenderer — стрела невидима", this);

        // Порядок фиксированный: стрела в воздухе, над объектами, но под туманом
        if (TryGetComponent(out DepthSortedSprite sorting))
            sorting.enabled = false;
        foreach (SpriteRenderer sr in _renderers)
            if (sr != null)
                sr.sortingOrder = DepthSorting.ProjectileOrder;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Поставить стрелу в точку полёта t ∈ [0, 1] между ногами стрелка (from) и точкой попадания (to),
    /// мировые координаты; worldScale — мировых единиц в тайле.
    /// </summary>
    public void Place(Vector3 from, Vector3 to, float t, float worldScale)
    {
        t = Mathf.Clamp01(t);
        Vector3 start = from + Vector3.up * (launchHeight * worldScale);
        Vector3 end = to + Vector3.up * (impactHeight * worldScale);
        float arc = arcHeight * worldScale;

        Vector3 position = Vector3.Lerp(start, end, t) + Vector3.up * (arc * 4f * t * (1f - t));
        // Производная по t — направление полёта
        Vector3 velocity = end - start + Vector3.up * (arc * 4f * (1f - 2f * t));

        transform.position = new Vector3(position.x, position.y, transform.position.z);
        if (velocity.sqrMagnitude > 1e-8f)
        {
            float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg + spriteAngleOffset;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    public void SetVisible(bool visible)
    {
        if (IsVisible == visible) return;
        IsVisible = visible;

        foreach (SpriteRenderer sr in _renderers)
            if (sr != null)
                sr.enabled = visible;
    }
}
