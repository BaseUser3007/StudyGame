using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Что знает туториал для условий шагов: факты сцены (события), день, открытия, достижения и разбор кода окон.
/// </summary>
public class TutorialState
{
    private readonly Dictionary<string, int> _facts = new Dictionary<string, int>();
    private readonly LuaTokenizer _tokenizer = new LuaTokenizer();
    private readonly HashSet<string> _mainCalls = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _keywords = new HashSet<string>(StringComparer.Ordinal);

    private readonly MetaProgress _progress;
    private readonly UnlockRegistry _unlocks;
    private readonly DayCycle _dayCycle;
    private readonly Func<string, bool> _isKeywordLocked;

    private int _sequence;
    private int _currentSince;

    public TutorialState(MetaProgress progress, UnlockRegistry unlocks, DayCycle dayCycle, Func<string, bool> isKeywordLocked)
    {
        _progress = progress;
        _unlocks = unlocks;
        _dayCycle = dayCycle;
        _isKeywordLocked = isKeywordLocked;
    }

    // ─── Факты ────────────────────────────────────────────────

    /// <summary>Отметить, что случилось. true — впервые в этой сцене.</summary>
    public bool AddFact(string fact)
    {
        if (string.IsNullOrEmpty(fact)) return false;
        bool isNew = !_facts.ContainsKey(fact);
        _facts[fact] = ++_sequence;
        return isNew;
    }

    /// <summary>Случилось в этой сцене (хоть раз).</summary>
    public bool Has(string fact) => fact != null && _facts.ContainsKey(fact);

    /// <summary>Случилось после того, как текущий шаг стал текущим.</summary>
    public bool HasSince(string fact) => fact != null && _facts.TryGetValue(fact, out int at) && at > _currentSince;

    /// <summary>Сменился текущий шаг: HasSince считает от этого момента.</summary>
    public void MarkCurrentChanged() => _currentSince = _sequence;

    // ─── Мир и прогресс ───────────────────────────────────────

    public int Day => _dayCycle != null ? _dayCycle.Day : 1;
    public int ThreatDay => _dayCycle != null ? _dayCycle.ThreatDay : int.MaxValue;
    public int RunsPerDay => _dayCycle != null ? _dayCycle.RunsPerDay : 3;

    public bool IsCommandUnlocked(string name) => _unlocks == null || _unlocks.IsCommandUnlocked(name);
    public bool IsConstructUnlocked(string id) => _unlocks == null || _unlocks.IsConstructUnlocked(id);
    public bool HasAchievement(string id) => _progress != null && _progress.HasAchievement(id);

    /// <summary>Есть ли постройка, которую можно поставить сейчас (задаёт CodeEditorManager в лагере).</summary>
    public Func<bool> HubBuildAvailable { get; set; }

    public bool CanBuildInHub => HubBuildAvailable != null && HubBuildAvailable();

    /// <summary>В лагере уже стоит хоть одна постройка (из сохранения).</summary>
    public bool HasHubBuilding => _progress != null && _progress.HubBuildings.Count > 0;

    public bool IsStepDone(string stepId) => _progress != null && _progress.HasTutorialFlag(TutorialController.StepFlag(stepId));

    // ─── Код ──────────────────────────────────────────────────

    /// <summary>В каком-нибудь окне есть вызов (имя и скобка).</summary>
    public bool CodeHasCall { get; private set; }
    /// <summary>В каком-нибудь окне есть вызов с аргументом в скобках.</summary>
    public bool CodeHasArguments { get; private set; }

    /// <summary>В главном окне есть вызов этой функции (комментарии и строки не считаются).</summary>
    public bool MainCodeCalls(string name) => _mainCalls.Contains(name);

    /// <summary>В каком-нибудь окне есть ключевое слово, и его конструкция открыта.</summary>
    public bool CodeHasKeyword(string keyword) =>
        _keywords.Contains(keyword) && (_isKeywordLocked == null || !_isKeywordLocked(keyword));

    /// <summary>Разобрать код окон по токенам (тем же токенизатором, что подсветка и проверки песочницы).</summary>
    public void AnalyzeCode(string mainCode, IEnumerable<string> allCode)
    {
        _mainCalls.Clear();
        _keywords.Clear();
        CodeHasCall = false;
        CodeHasArguments = false;

        CollectCalls(mainCode, _mainCalls);
        if (allCode == null) return;

        foreach (string code in allCode)
        {
            if (string.IsNullOrEmpty(code)) continue;

            List<LuaTokenizer.Token> tokens = Significant(code);
            for (int i = 0; i < tokens.Count; i++)
            {
                LuaTokenizer.Token token = tokens[i];
                if (token.Type == LuaTokenizer.TokenType.Keyword)
                {
                    _keywords.Add(token.Value);
                    continue;
                }

                if (!IsCallAt(tokens, i)) continue;
                CodeHasCall = true;
                if (i + 2 < tokens.Count && tokens[i + 2].Value != ")")
                    CodeHasArguments = true;
            }
        }
    }

    private void CollectCalls(string code, HashSet<string> calls)
    {
        if (string.IsNullOrEmpty(code)) return;

        List<LuaTokenizer.Token> tokens = Significant(code);
        for (int i = 0; i < tokens.Count; i++)
            if (IsCallAt(tokens, i))
                calls.Add(tokens[i].Value);
    }

    /// <summary>Имя и открывающая скобка; объявление function f( — не вызов.</summary>
    private static bool IsCallAt(List<LuaTokenizer.Token> tokens, int index) =>
        tokens[index].Type == LuaTokenizer.TokenType.Identifier
        && index + 1 < tokens.Count
        && tokens[index + 1].Type == LuaTokenizer.TokenType.Operator
        && tokens[index + 1].Value == "("
        && !(index > 0 && tokens[index - 1].Type == LuaTokenizer.TokenType.Keyword && tokens[index - 1].Value == "function");

    /// <summary>Токены без пробелов, переводов строк и комментариев.</summary>
    private List<LuaTokenizer.Token> Significant(string code)
    {
        List<LuaTokenizer.Token> tokens = _tokenizer.Tokenize(code);
        tokens.RemoveAll(t => t.Type == LuaTokenizer.TokenType.Whitespace
                              || t.Type == LuaTokenizer.TokenType.Newline
                              || t.Type == LuaTokenizer.TokenType.Comment);
        return tokens;
    }
}

/// <summary>
/// Туториал и подсказки: очередь шагов UI-гайда (<see cref="TutorialSteps.Steps"/>) и подсказки-понятия
/// при первом использовании в коде. Показывает одну карточку <see cref="TutorialOverlay"/>, привязанную к
/// элементу UI. Условия — факты из GameEventBus (Crafted, Mined, ConstructUsed, EnemyFound, запуски кода),
/// событий UI (их передаёт CodeEditorManager через Notify) и кода окон (SetCode).
/// Шаг с выполненным условием отмечается пройденным, даже если он ещё не текущий (игрок сделал это раньше).
/// Пока выполняется код, новые карточки не показываются (кроме ShowWhileRunning), пока открыто модальное
/// окно (isBlocked) — карточка скрыта.
/// Сохраняется в MetaProgress: флаги step:id, concept:id, skipped и HintsEnabled (сразу, Save()).
/// </summary>
public class TutorialController : IDisposable
{
    private const string SkippedFlag = "skipped";
    // После конца запуска окно «Unlocked: X» появляется в следующем кадре — не мелькаем карточкой до него
    private const float ResumeDelay = 0.35f;
    private const long TickIntervalMs = 33;

    /// <summary>Включились/выключились подсказки (кнопки в TopBar и в лагере обновляет CodeEditorManager).</summary>
    public event Action<bool> OnHintsChanged;

    private readonly TutorialOverlay _overlay;
    private readonly MetaProgress _progress;
    private readonly TutorialState _state;
    private readonly Func<TutorialAnchor, VisualElement> _resolveAnchor;
    private readonly Func<bool> _isBlocked;
    private readonly List<TutorialStep> _modeSteps = new List<TutorialStep>();

    private TutorialMode _mode;
    private bool _started;
    private bool _running;
    private float _resumeAt;
    private TutorialStep _current;
    // Что показано сейчас: "step:id" / "concept:id" / null
    private string _shownKey;
    private IVisualElementScheduledItem _tick;

    public bool HintsEnabled => _progress == null || _progress.HintsEnabled;
    public bool IsSkipped => _progress != null && _progress.HasTutorialFlag(SkippedFlag);

    public static string StepFlag(string stepId) => "step:" + stepId;
    public static string ConceptFlag(string conceptId) => "concept:" + conceptId;

    public TutorialController(
        TutorialOverlay overlay, MetaProgress progress, TutorialState state,
        Func<TutorialAnchor, VisualElement> resolveAnchor, Func<bool> isBlocked)
    {
        _overlay = overlay;
        _progress = progress;
        _state = state;
        _resolveAnchor = resolveAnchor;
        _isBlocked = isBlocked;

        Debug.Assert(_overlay != null, "TutorialController: overlay is null");
        Debug.Assert(_state != null, "TutorialController: state is null");

        _overlay.OnNextClicked += HandleNextClicked;
        _overlay.OnSkipClicked += HandleSkipClicked;
        _overlay.OnHintsOffClicked += HandleHintsOffClicked;

        GameEventBus.OnGameEvent += HandleGameEvent;
        GameEventBus.OnRunStarted += HandleRunStarted;
        GameEventBus.OnRunFinished += HandleRunFinished;
    }

    public void Dispose()
    {
        GameEventBus.OnGameEvent -= HandleGameEvent;
        GameEventBus.OnRunStarted -= HandleRunStarted;
        GameEventBus.OnRunFinished -= HandleRunFinished;

        _overlay.OnNextClicked -= HandleNextClicked;
        _overlay.OnSkipClicked -= HandleSkipClicked;
        _overlay.OnHintsOffClicked -= HandleHintsOffClicked;

        _tick?.Pause();
        _tick = null;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Запустить для режима сцены. В забеге шаг Start Run лагеря считается пройденным — забег уже начат.
    /// </summary>
    public void Start(TutorialMode mode)
    {
        _mode = mode;
        _started = true;

        _modeSteps.Clear();
        foreach (TutorialStep step in TutorialSteps.Steps)
            if (step.Mode == mode) _modeSteps.Add(step);

        if (mode == TutorialMode.Run && _progress != null && _progress.SetTutorialFlag(StepFlag("hub_start")))
            _progress.Save();

        _tick = _overlay.Root.schedule.Execute(Tick).Every(TickIntervalMs);
        Evaluate();
        Tick();
    }

    /// <summary>Событие UI (TutorialFacts) — открыта справка, панель, нажата кнопка…</summary>
    public void Notify(string fact)
    {
        _state.AddFact(fact);
        Evaluate();
    }

    /// <summary>Код окон изменился — разбираем заново (условие «в коде есть вызов», подсказки-понятия).</summary>
    public void SetCode(string mainCode, IEnumerable<string> allCode)
    {
        _state.AnalyzeCode(mainCode, allCode);
        Evaluate();
    }

    public void SetRunning(bool running)
    {
        _running = running;
        if (!running) _resumeAt = Time.unscaledTime + ResumeDelay;
        Tick();
    }

    public void SetHintsEnabled(bool enabled)
    {
        if (_progress == null || _progress.HintsEnabled == enabled) return;

        _progress.HintsEnabled = enabled;
        _progress.Save();
        OnHintsChanged?.Invoke(enabled);
        Evaluate();
        Tick();
    }

    /// <summary>Сбросить пройденные шаги и показанные понятия (кнопка Reset tutorial в лагере), подсказки — вкл.</summary>
    public void ResetTutorial()
    {
        if (_progress == null) return;

        _progress.ClearTutorialFlags();
        bool hintsChanged = !_progress.HintsEnabled;
        _progress.HintsEnabled = true;
        _progress.Save();

        if (hintsChanged) OnHintsChanged?.Invoke(true);
        _current = null;
        Hide();
        Evaluate();
        Tick();
    }

    // ─── Evaluate ─────────────────────────────────────────────

    /// <summary>
    /// Отмечает пройденными шаги с выполненным условием (и неактуальные — Skip), пересчитывает текущий шаг.
    /// </summary>
    private void Evaluate()
    {
        if (!_started || _progress == null || !HintsEnabled || IsSkipped) return;

        bool changed = false;
        bool again = true;
        // Шаг стал текущим — его CurrentOnly-условие проверяется уже от нового момента; повторяем до устойчивости
        while (again)
        {
            again = false;

            foreach (TutorialStep step in _modeSteps)
            {
                if (IsDone(step)) continue;

                bool skip = step.Skip != null && step.Skip(_state);
                bool done = step.Done != null && (!step.CurrentOnly || step == _current) && step.Done(_state);
                if (!skip && !done) continue;

                _progress.SetTutorialFlag(StepFlag(step.Id));
                changed = true;
            }

            TutorialStep current = FindCurrent();
            if (current != _current)
            {
                _current = current;
                _state.MarkCurrentChanged();
                again = current != null && current.CurrentOnly;
            }
        }

        if (changed) _progress.Save();
    }

    private TutorialStep FindCurrent()
    {
        foreach (TutorialStep step in _modeSteps)
        {
            if (IsDone(step)) continue;
            if (step.After != null && !_progress.HasTutorialFlag(StepFlag(step.After))) continue;
            if (step.Requires != null && !step.Requires(_state)) continue;
            return step;
        }
        return null;
    }

    private bool IsDone(TutorialStep step) => _progress.HasTutorialFlag(StepFlag(step.Id));

    private TutorialConcept FindPendingConcept()
    {
        if (_mode != TutorialMode.Run) return null;

        foreach (TutorialConcept concept in TutorialSteps.Concepts)
        {
            if (_progress.HasTutorialFlag(ConceptFlag(concept.Id))) continue;
            if (concept.Trigger != null && concept.Trigger(_state)) return concept;
        }
        return null;
    }

    // ─── Tick: что показать и где ─────────────────────────────

    private void Tick()
    {
        if (!_started) return;

        // День, открытия и достижения меняются без событий для туториала — условия проверяются и по таймеру
        Evaluate();

        if (_progress == null || !HintsEnabled || _isBlocked()
            || (!_running && Time.unscaledTime < _resumeAt))
        {
            Hide();
            return;
        }

        // Понятие — к моменту использования, поэтому раньше шага гайда; во время выполнения кода — только шаг
        // с ShowWhileRunning
        TutorialConcept concept = _running ? null : FindPendingConcept();
        TutorialStep step = null;
        if (concept == null && !IsSkipped && _current != null && (!_running || _current.ShowWhileRunning))
            step = _current;

        if (concept != null) ShowConcept(concept);
        else if (step != null) ShowStep(step);
        else
        {
            Hide();
            return;
        }

        _overlay.UpdateLayout();
    }

    private void ShowStep(TutorialStep step)
    {
        string key = StepFlag(step.Id);
        if (_shownKey != key)
        {
            _shownKey = key;

            // Шаг объясняет понятие — отдельная подсказка о нём уже не нужна
            if (step.CoversConcept != null && _progress.SetTutorialFlag(ConceptFlag(step.CoversConcept)))
                _progress.Save();

            int index = _modeSteps.IndexOf(step) + 1;
            string caption = _mode == TutorialMode.Hub
                ? Loc.F("Camp · {0}/{1}", index, _modeSteps.Count)
                : Loc.F("Tutorial · {0}/{1}", index, _modeSteps.Count);
            // Тексты шагов — английский исходник (ключ перевода — с «{runs}»); NextText переводит TutorialOverlay
            string text = Loc.T(step.Text).Replace("{runs}", _state.RunsPerDay.ToString());

            _overlay.Show(new TutorialOverlay.Content
            {
                Caption = caption,
                Text = text,
                ShowNext = step.HasNext,
                NextText = "Next",
                ShowSkip = true,
                ShowHintsOff = false,
                Waiting = step.Done != null ? Loc.T(step.Waiting) : null
            });
        }

        _overlay.SetTarget(_resolveAnchor?.Invoke(step.Anchor));
    }

    private void ShowConcept(TutorialConcept concept)
    {
        string key = ConceptFlag(concept.Id);
        if (_shownKey != key)
        {
            _shownKey = key;
            _overlay.Show(new TutorialOverlay.Content
            {
                Caption = Loc.T("Concept"),
                Title = Loc.T(concept.Title),
                Text = Loc.T(concept.Text),
                Example = concept.Example,
                ShowNext = true,
                NextText = "Got it",
                ShowSkip = false,
                ShowHintsOff = true
            });
        }

        _overlay.SetTarget(_resolveAnchor?.Invoke(TutorialAnchor.MainWindow));
    }

    private void Hide()
    {
        if (_shownKey == null && !_overlay.IsVisible) return;
        _shownKey = null;
        _overlay.Hide();
    }

    // ─── Кнопки карточки ──────────────────────────────────────

    private void HandleNextClicked()
    {
        if (_progress == null || _shownKey == null) return;

        // Ключ показанного — "concept:id" / "step:id", и флаги устроены так же
        _progress.SetTutorialFlag(_shownKey);

        _progress.Save();
        Hide();
        Evaluate();
        Tick();
    }

    private void HandleSkipClicked()
    {
        if (_progress == null) return;

        _progress.SetTutorialFlag(SkippedFlag);
        _progress.Save();
        Hide();
        UIEventBus.PublishNotification(
            Loc.T("Tutorial skipped. Concept hints stay on — turn them off with the Hints button."), LogEntryType.Warning);
    }

    private void HandleHintsOffClicked()
    {
        SetHintsEnabled(false);
        Hide();
        UIEventBus.PublishNotification(Loc.T("Hints are off. Turn them back on with the Hints button."), LogEntryType.Warning);
    }

    // ─── GameEventBus ─────────────────────────────────────────

    private void HandleGameEvent(GameEvent gameEvent)
    {
        string fact = null;
        switch (gameEvent.Type)
        {
            case GameEventType.Crafted: fact = TutorialFacts.Crafted(gameEvent.Id); break;
            case GameEventType.Mined: fact = TutorialFacts.Mined; break;
            case GameEventType.ConstructUsed: fact = TutorialFacts.ConstructUsed(gameEvent.Id); break;
            case GameEventType.EnemyFound: fact = TutorialFacts.EnemyFound; break;
            case GameEventType.HubBuildingBuilt: fact = TutorialFacts.HubBuilt; break;
        }

        if (fact != null) Notify(fact);
    }

    private void HandleRunStarted() => Notify(TutorialFacts.RunStarted);

    private void HandleRunFinished(CodeRunReport report) => Notify(TutorialFacts.RunFinished);
}
