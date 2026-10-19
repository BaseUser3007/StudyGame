using UnityEngine;

/// <summary>
/// Обелиск с задачей на код (code_task). Вид задачи постоянный, вход генерируется заново в каждом
/// запуске кода (см. <see cref="CodeTaskSource"/>). Решение — через PuzzleManager.TrySolveCodeTask.
/// Висит на экземпляре obeliskPrefab (добавляется генератором, если на префабе нет).
/// </summary>
public class CodeTaskPuzzle : PuzzleStructure
{
    private CodeTaskSource _source;
    private Rect _footprint;
    private DepthSortedSprite _depthSorting;

    public override string TypeId => PuzzleTypes.CodeTask;
    public CodeTaskKind Kind => _source != null ? _source.Kind : CodeTaskKind.Sum;
    public override Rect Bounds => new Rect(_footprint.position + Position, _footprint.size);

    private void Awake()
    {
        // Сортировка по высоте, как у построек
        if (!TryGetComponent(out _depthSorting))
            _depthSorting = gameObject.AddComponent<DepthSortedSprite>();
    }

    /// <param name="footprint">След Collider2D префаба относительно позиции (ObjectFootprint).</param>
    public void Initialize(CodeTaskKind kind, int seed, Rect footprint)
    {
        _source = new CodeTaskSource(kind, seed);
        _footprint = footprint;
        if (_depthSorting != null) _depthSorting.Refresh();
    }

    /// <summary>
    /// Задача текущего запуска кода (GameEventBus.ActiveRunId; вне запуска — «запуск 0»).
    /// </summary>
    public CodeTask GetTask() => _source.GetTask(GameEventBus.ActiveRunId);

    /// <summary>Верен ли ответ для задачи текущего запуска (без побочных эффектов).</summary>
    public bool CheckAnswer(double answer) => _source.Check(GameEventBus.ActiveRunId, answer);

    protected override void OnSolved()
    {
        // Решённый обелиск чуть темнеет — видно, что награда получена
        foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>())
            sr.color = new Color(sr.color.r * 0.6f, sr.color.g * 0.6f, sr.color.b * 0.75f, sr.color.a);
    }
}
