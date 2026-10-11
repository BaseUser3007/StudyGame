using UnityEngine;

/// <summary>
/// Выставляет sortingOrder всем SpriteRenderer объекта (и дочерних) по его высоте на карте
/// (см. <see cref="DepthSorting"/>). Опорная точка — transform.position, т.е. основание спрайта.
/// Статичные объекты пересчитываются при включении и через Refresh(),
/// динамичные (игрок) — каждый кадр, когда меняется Y.
/// </summary>
[DisallowMultipleComponent]
public class DepthSortedSprite : MonoBehaviour
{
    [Tooltip("Пересчитывать порядок при движении (для игрока и других подвижных объектов)")]
    [SerializeField] private bool dynamic;

    private SpriteRenderer[] _renderers;
    // Сохраняем исходную разницу между рендерерами (например, тень под персонажем)
    private int[] _offsets;
    private float _lastY = float.NaN;

    public bool Dynamic
    {
        get => dynamic;
        set => dynamic = value;
    }

    // ─── Lifecycle ────────────────────────────────────────────

    private void Awake()
    {
        CacheRenderers();
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void LateUpdate()
    {
        if (dynamic && !Mathf.Approximately(transform.position.y, _lastY))
            Refresh();
    }

    // ─── Public API ───────────────────────────────────────────

    public void Refresh()
    {
        if (_renderers == null) CacheRenderers();

        _lastY = transform.position.y;
        int order = DepthSorting.GetOrder(_lastY);

        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null)
                _renderers[i].sortingOrder = order + _offsets[i];
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void CacheRenderers()
    {
        _renderers = GetComponentsInChildren<SpriteRenderer>(true);
        _offsets = new int[_renderers.Length];

        int baseOrder = int.MaxValue;
        foreach (SpriteRenderer sr in _renderers)
            baseOrder = Mathf.Min(baseOrder, sr.sortingOrder);

        for (int i = 0; i < _renderers.Length; i++)
            _offsets[i] = _renderers[i].sortingOrder - baseOrder;
    }
}
