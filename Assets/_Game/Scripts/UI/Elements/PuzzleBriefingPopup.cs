using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Карточка начатой головоломки (T25): название, что сделать, награда и правило одной попытки.
/// Показывается по <see cref="PuzzleManager.OnPuzzleStarted"/>. Старт посреди запуска прерывает его,
/// поэтому, пока выполняется код, карточка ждёт (SetSuspended, как UnlockPopup) и появляется в следующем
/// кадре после окончания запуска. Одновременно начата только одна головоломка — ждёт одна карточка;
/// провал до показа (OnPuzzleFailed) её снимает. Кнопка не фокусируемая, управление только мышью.
/// Элементы — экземпляр шаблона PuzzleBriefingPopup.uxml (хост выдаёт RunScreens.GetOverlay).
/// Тексты брифинга уже переведены (PuzzleManager.GetBriefing); `код` в обратных кавычках подсвечивается.
/// </summary>
public class PuzzleBriefingPopup
{
    private const string HiddenClass = "hidden";

    private readonly LuaSyntaxHighlighter _highlighter;

    private readonly VisualElement _backdrop;
    private readonly Label _title;
    private readonly Label _description;
    private readonly VisualElement _rewardBlock;
    private readonly Label _reward;
    private readonly Label _rule;
    private readonly Button _btnOk;

    private PuzzleStructure _pendingPuzzle;
    private PuzzleBriefing _pendingBriefing;
    private bool _hasPending;
    private bool _suspended;
    private bool _showScheduled;

    public bool IsOpen => !_backdrop.ClassListContains(HiddenClass);

    /// <param name="root">Хост шаблона PuzzleBriefingPopup.uxml.</param>
    /// <param name="highlighter">Подсветка `кода` в описании; null — код без цвета.</param>
    public PuzzleBriefingPopup(VisualElement root, LuaSyntaxHighlighter highlighter)
    {
        _highlighter = highlighter;

        _backdrop = root.Q<VisualElement>("puzzle-briefing-backdrop");
        _title = root.Q<Label>("puzzle-briefing-title");
        _description = root.Q<Label>("puzzle-briefing-description");
        _rewardBlock = root.Q<VisualElement>("puzzle-briefing-reward-block");
        _reward = root.Q<Label>("puzzle-briefing-reward");
        _rule = root.Q<Label>("puzzle-briefing-rule");
        _btnOk = root.Q<Button>("btn-puzzle-briefing-ok");

        Debug.Assert(_backdrop != null && _title != null && _description != null && _rewardBlock != null
                     && _reward != null && _rule != null && _btnOk != null,
            "PuzzleBriefingPopup: elements not found");

        _title.enableRichText = false;
        _reward.enableRichText = false;
        _rule.enableRichText = false;

        // UI управляется только мышью — кнопка не должна забирать фокус
        _btnOk.focusable = false;
        _btnOk.clicked += HandleOkClicked;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Головоломка начата: карточка покажется, как только код не выполняется (обычно — сразу после
    /// прерванного запуска). Новая начатая заменяет ждущую.
    /// </summary>
    public void Enqueue(PuzzleStructure puzzle, PuzzleBriefing briefing)
    {
        _pendingPuzzle = puzzle;
        _pendingBriefing = briefing;
        _hasPending = true;
        ScheduleShow();
    }

    /// <summary>Головоломка исчезла (провал) — её ждущая карточка уже не нужна. Открытую закрывает кнопка.</summary>
    public void Drop(PuzzleStructure puzzle)
    {
        if (_hasPending && _pendingPuzzle == puzzle)
        {
            _hasPending = false;
            _pendingPuzzle = null;
        }
    }

    /// <summary>
    /// true — выполняется код: карточка не показывается. После false она появляется в следующем кадре.
    /// Уже открытая не закрывается.
    /// </summary>
    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        if (!suspended) ScheduleShow();
    }

    // ─── Тексты (чистые, для TopBar и лога) ───────────────────

    /// <summary>Индикатор в верхней панели по состоянию начатой головоломки; null — индикатор скрыт.</summary>
    public static string FormatStatus(PuzzleState state)
    {
        switch (state)
        {
            case PuzzleState.Armed:
                return Loc.T("Puzzle: the next run is your attempt");
            case PuzzleState.Attempt:
                return Loc.T("Puzzle attempt in progress");
            default:
                return null;
        }
    }

    /// <summary>Строка лога запуска при старте: «Puzzle started: Obelisk. One attempt: …».</summary>
    public static string FormatLogLine(string title, string attemptRule) =>
        Loc.F("Puzzle started: {0}. {1}", title, attemptRule);

    // ─── Show / hide ──────────────────────────────────────────

    private void ScheduleShow()
    {
        if (_showScheduled || _suspended || IsOpen || !_hasPending) return;

        _showScheduled = true;
        _backdrop.schedule.Execute(HandleScheduledShow);
    }

    private void HandleScheduledShow()
    {
        _showScheduled = false;
        if (!_suspended && !IsOpen && _hasPending) Show();
    }

    private void Show()
    {
        PuzzleBriefing briefing = _pendingBriefing;
        _hasPending = false;
        _pendingPuzzle = null;

        _title.text = briefing.Title ?? string.Empty;

        _description.enableRichText = true;
        _description.text = FormatInline(briefing.Description);
        _description.EnableInClassList(HiddenClass, string.IsNullOrEmpty(briefing.Description));

        _reward.text = briefing.Reward ?? string.Empty;
        _rewardBlock.EnableInClassList(HiddenClass, string.IsNullOrEmpty(briefing.Reward));

        _rule.text = PuzzleManager.AttemptRuleText;
        _btnOk.text = Loc.T("Got it");

        _backdrop.RemoveFromClassList(HiddenClass);
    }

    private void HandleOkClicked()
    {
        _backdrop.AddToClassList(HiddenClass);
        // Пока карточка была открыта, могла начаться другая (её ждущая карточка — следующей)
        ScheduleShow();
    }

    /// <summary>
    /// Текст с `кодом`: обычные части — в noparse (никаких тегов из текста), код — подсветкой редактора
    /// (она сама экранирует '&lt;', видимые символы совпадают с исходником).
    /// </summary>
    private string FormatInline(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var builder = new StringBuilder();
        string[] parts = text.Split('`');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            if (i % 2 == 1 && _highlighter != null) builder.Append(_highlighter.Highlight(parts[i]));
            else builder.Append("<noparse>").Append(parts[i]).Append("</noparse>");
        }
        return builder.ToString();
    }
}
