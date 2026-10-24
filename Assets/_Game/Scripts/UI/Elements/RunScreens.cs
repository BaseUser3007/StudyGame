using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Итог забега для экрана итогов.
/// </summary>
public class RunSummary
{
    public bool Defeated;
    public int Days;
    /// <summary>Материалы, которые попадут на склад лагеря.</summary>
    public readonly List<ItemStack> Carried = new List<ItemStack>();
    /// <summary>Что осталось в забеге: не-материалы, а при поражении — всё.</summary>
    public readonly List<ItemStack> Left = new List<ItemStack>();
    /// <summary>Достижения, выполненные в этом забеге.</summary>
    public readonly List<AchievementDefinition> Achievements = new List<AchievementDefinition>();

    // Статистика забега (RunStats на момент итога)
    /// <summary>Завершённых запусков кода.</summary>
    public int CodeRuns;
    /// <summary>Энергии потрачено за все запуски.</summary>
    public float EnergySpent;
    /// <summary>Запусков с ошибкой в коде.</summary>
    public int CodeErrors;
    public int EnemiesKilled;
    public int PuzzlesSolved;
    public int ItemsCrafted;
}

/// <summary>
/// Экраны режима игры: лагерь (хаб — боковые панели поверх карты с сеткой, HubPanel.uxml; панель построек —
/// HubBuildPanel), подтверждение выхода из забега, итоги забега.
/// Только показывает и сообщает о нажатиях — логика переходов в RunController.
/// </summary>
public class RunScreens : VisualElement
{
    private const string HiddenClass = "hidden";

    private VisualElement _hubScreen;
    private Label _hubStats;
    private ScrollView _hubMaterials;
    private Label _hubMaterialsEmpty;
    private Button _btnStartRun;
    private Button _btnHubAchievements;
    private Button _btnHubHints;
    private Button _btnHubResetTutorial;
    // Сброс туториала — вторым кликом в течение ResetConfirmMs
    private bool _resetArmed;
    private IVisualElementScheduledItem _resetDisarm;
    private const long ResetConfirmMs = 3000;
    private Button _btnHubResetProgress;
    // Сброс всего прогресса — так же, вторым кликом в течение ResetConfirmMs
    private bool _resetProgressArmed;
    private IVisualElementScheduledItem _resetProgressDisarm;
    private Button _btnHubLanguage;

    private VisualElement _exitConfirm;
    private Button _btnExitConfirm;
    private Button _btnExitCancel;

    private VisualElement _summary;
    private Label _summaryTitle;
    private Label _summaryStats;
    private VisualElement _summaryRunStats;
    private ScrollView _summaryLoot;
    private Label _summaryLootEmpty;
    private Label _summaryLost;
    private ScrollView _summaryAchievements;
    private Label _summaryAchievementsEmpty;
    private Button _btnToHub;

    public event Action OnStartRunRequested;
    public event Action OnExitConfirmed;
    public event Action OnReturnToHubRequested;
    /// <summary>Кнопка Achievements в лагере — панель показывает CodeEditorManager.</summary>
    public event Action OnAchievementsRequested;
    /// <summary>Кнопка Hints в лагере — переключить подсказки (сохраняет TutorialController).</summary>
    public event Action OnHintsToggleRequested;
    /// <summary>Reset tutorial в лагере, подтверждён вторым кликом.</summary>
    public event Action OnResetTutorialRequested;
    /// <summary>Reset progress в лагере, подтверждён вторым кликом (сброс и перезагрузку делает RunController).</summary>
    public event Action OnResetProgressRequested;
    /// <summary>Кнопка языка в лагере — сменить язык (сохранение и перезагрузку делает CodeEditorManager).</summary>
    public event Action OnLanguageToggleRequested;

    public RunScreens(VisualTreeAsset template)
    {
        VisualElement root = template.Instantiate();
        // Статические тексты UXML (включая вложенные шаблоны: лагерь, достижения, попап, тосты, туториал)
        LocUxml.Translate(root);
        QueryElements(root);
        ValidateReferences();

        // Корень растянут на весь экран — пока экраны скрыты, клики проходят в игру
        pickingMode = PickingMode.Ignore;
        root.pickingMode = PickingMode.Ignore;
        root.style.flexGrow = 1;
        style.position = Position.Absolute;
        style.left = 0;
        style.top = 0;
        style.right = 0;
        style.bottom = 0;

        // Лагерь не закрывает карту: мышь ловят только его панели, клики по сетке доходят до размещения
        _hubScreen.pickingMode = PickingMode.Ignore;

        // UI управляется только мышью — кнопки не должны забирать фокус
        foreach (Button button in new[]
                 {
                     _btnStartRun, _btnHubAchievements, _btnHubHints, _btnHubResetTutorial,
                     _btnHubResetProgress, _btnHubLanguage,
                     _btnExitConfirm, _btnExitCancel, _btnToHub
                 })
            button.focusable = false;

        _btnHubHints.clicked += () => OnHintsToggleRequested?.Invoke();
        _btnHubResetTutorial.clicked += HandleResetTutorialClicked;
        _btnHubResetProgress.clicked += HandleResetProgressClicked;
        _btnHubLanguage.clicked += () => OnLanguageToggleRequested?.Invoke();
        // Подпись — на языке, на который переключит кнопка (её поймёт тот, кто этот язык знает)
        _btnHubLanguage.text = Loc.IsRussian ? "Language: English" : "Язык: Русский";

        _btnStartRun.clicked += () => OnStartRunRequested?.Invoke();
        _btnExitCancel.clicked += HideExitConfirm;
        _btnExitConfirm.clicked += HandleExitConfirmed;
        _btnToHub.clicked += () => OnReturnToHubRequested?.Invoke();
        _btnHubAchievements.clicked += () => OnAchievementsRequested?.Invoke();

        Add(root);
    }

    private void QueryElements(VisualElement root)
    {
        _hubScreen = root.Q<VisualElement>("hub-screen");
        _hubStats = root.Q<Label>("hub-stats");
        _hubMaterials = root.Q<ScrollView>("hub-materials");
        _hubMaterialsEmpty = root.Q<Label>("hub-materials-empty");
        _btnStartRun = root.Q<Button>("btn-start-run");
        _btnHubAchievements = root.Q<Button>("btn-hub-achievements");
        _btnHubHints = root.Q<Button>("btn-hub-hints");
        _btnHubResetTutorial = root.Q<Button>("btn-hub-reset-tutorial");
        _btnHubResetProgress = root.Q<Button>("btn-hub-reset-progress");
        _btnHubLanguage = root.Q<Button>("btn-hub-language");

        _exitConfirm = root.Q<VisualElement>("exit-confirm");
        _btnExitConfirm = root.Q<Button>("btn-exit-confirm");
        _btnExitCancel = root.Q<Button>("btn-exit-cancel");

        _summary = root.Q<VisualElement>("run-summary");
        _summaryTitle = root.Q<Label>("summary-title");
        _summaryStats = root.Q<Label>("summary-stats");
        _summaryRunStats = root.Q<VisualElement>("summary-run-stats");
        _summaryLoot = root.Q<ScrollView>("summary-loot");
        _summaryLootEmpty = root.Q<Label>("summary-loot-empty");
        _summaryLost = root.Q<Label>("summary-lost");
        _summaryAchievements = root.Q<ScrollView>("summary-achievements");
        _summaryAchievementsEmpty = root.Q<Label>("summary-achievements-empty");
        _btnToHub = root.Q<Button>("btn-to-hub");
    }

    private void ValidateReferences()
    {
        Debug.Assert(_hubScreen != null && _hubStats != null && _hubMaterials != null
                     && _hubMaterialsEmpty != null && _btnStartRun != null && _btnHubAchievements != null,
            "RunScreens: hub elements not found");
        Debug.Assert(_btnHubHints != null && _btnHubResetTutorial != null && _btnHubResetProgress != null
                     && _btnHubLanguage != null,
            "RunScreens: hub tutorial settings not found");
        Debug.Assert(_exitConfirm != null && _btnExitConfirm != null && _btnExitCancel != null,
            "RunScreens: exit confirmation elements not found");
        Debug.Assert(_summary != null && _summaryTitle != null && _summaryStats != null && _summaryLoot != null
                     && _summaryLootEmpty != null && _summaryLost != null && _btnToHub != null
                     && _summaryAchievements != null && _summaryAchievementsEmpty != null
                     && _summaryRunStats != null,
            "RunScreens: summary elements not found");
    }

    // ─── Public API ───────────────────────────────────────────

    public bool IsExitConfirmOpen => !_exitConfirm.ClassListContains(HiddenClass);
    public bool IsHubShown => !_hubScreen.ClassListContains(HiddenClass);
    public bool IsSummaryShown => !_summary.ClassListContains(HiddenClass);

    /// <summary>Текст кнопки Hints в лагере.</summary>
    public void SetHintsEnabled(bool enabled) => _btnHubHints.text = Loc.T(enabled ? "Hints: On" : "Hints: Off");

    /// <summary>Элемент экрана по имени (якорь подсказки туториала).</summary>
    public VisualElement FindElement(string name) => this.Q<VisualElement>(name);

    /// <summary>
    /// Хост шаблона поверх экранов (achievements-overlay, unlock-overlay, toast-overlay в RunScreens.uxml):
    /// растянут на весь экран и не перехватывает мышь — её ловят только видимые элементы внутри.
    /// </summary>
    public VisualElement GetOverlay(string name)
    {
        VisualElement host = this.Q<VisualElement>(name);
        Debug.Assert(host != null, $"RunScreens: overlay '{name}' not found");
        if (host == null) return null;

        host.pickingMode = PickingMode.Ignore;
        host.style.position = Position.Absolute;
        host.style.left = 0;
        host.style.top = 0;
        host.style.right = 0;
        host.style.bottom = 0;
        return host;
    }

    /// <summary>Корень экрана лагеря (экземпляр HubPanel.uxml) — для HubBuildPanel.</summary>
    public VisualElement HubRoot => _hubScreen;

    /// <summary>
    /// Иконка материала склада по ключу (MetaProgress.GetItemKey). Склад хранит только ключ и имя —
    /// предметы знает CodeEditorManager (стоимости построек). null — склад без иконок.
    /// </summary>
    public Func<string, Sprite> HubIconResolver { get; set; }

    public void ShowHub(MetaProgress progress)
    {
        RefreshHub(progress);
        _hubScreen.RemoveFromClassList(HiddenClass);
    }

    /// <summary>Перерисовать статистику и склад лагеря (после постройки склад уменьшился).</summary>
    public void RefreshHub(MetaProgress progress)
    {
        if (progress == null) return;

        _hubStats.text = progress.RunsFinished == 0
            ? Loc.T("No runs yet")
            : Loc.F("Runs finished: {0} (defeats: {1})", progress.RunsFinished, progress.Defeats);

        _hubMaterials.Clear();
        foreach (StoredMaterial stored in progress.Materials)
        {
            if (stored.count <= 0) continue;
            Sprite icon = HubIconResolver?.Invoke(stored.itemId);
            _hubMaterials.Add(CreateItemRow(icon, Loc.T(stored.displayName ?? stored.itemId), stored.count));
        }
        _hubMaterialsEmpty.EnableInClassList(HiddenClass, _hubMaterials.childCount > 0);
    }

    public void ShowExitConfirm() => _exitConfirm.RemoveFromClassList(HiddenClass);

    public void HideExitConfirm() => _exitConfirm.AddToClassList(HiddenClass);

    public void ShowSummary(RunSummary summary)
    {
        HideExitConfirm();

        _summaryTitle.text = Loc.T(summary.Defeated ? "Defeat" : "Run complete");
        _summaryTitle.EnableInClassList("screen-title--defeat", summary.Defeated);
        _summaryStats.text = summary.Defeated
            ? Loc.F("Your health ran out on day {0}. Everything gathered in this run is lost.", summary.Days)
            : Loc.F("Days in the run: {0}", summary.Days);

        // Статистика забега — показывается и при поражении
        _summaryRunStats.Clear();
        _summaryRunStats.Add(CreateStatRow(Loc.T("Code runs"), summary.CodeRuns.ToString()));
        _summaryRunStats.Add(CreateStatRow(Loc.T("Energy spent"), FormatEnergy(summary.EnergySpent)));
        _summaryRunStats.Add(CreateStatRow(Loc.T("Code errors"), summary.CodeErrors.ToString()));
        _summaryRunStats.Add(CreateStatRow(Loc.T("Enemies defeated"), summary.EnemiesKilled.ToString()));
        _summaryRunStats.Add(CreateStatRow(Loc.T("Puzzles solved"), summary.PuzzlesSolved.ToString()));
        _summaryRunStats.Add(CreateStatRow(Loc.T("Items crafted"), summary.ItemsCrafted.ToString()));

        _summaryLoot.Clear();
        foreach (ItemStack stack in summary.Carried)
            _summaryLoot.Add(CreateItemRow(stack.item.Icon, Loc.T(stack.item.DisplayName), stack.amount));
        _summaryLootEmpty.EnableInClassList(HiddenClass, summary.Carried.Count > 0);

        _summaryLost.text = summary.Left.Count > 0 ? Loc.F("Stays in the run: {0}", FormatStacks(summary.Left)) : string.Empty;
        _summaryLost.EnableInClassList(HiddenClass, summary.Left.Count == 0);

        // Достижения забега — прогресс и награды сохраняются и при поражении
        _summaryAchievements.Clear();
        foreach (AchievementDefinition achievement in summary.Achievements)
            _summaryAchievements.Add(CreateAchievementRow(achievement));
        _summaryAchievementsEmpty.EnableInClassList(HiddenClass, summary.Achievements.Count > 0);

        _summary.RemoveFromClassList(HiddenClass);
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Первый клик — «Click again to reset» на несколько секунд, второй — сброс (подтверждение внутри UI).
    /// </summary>
    private void HandleResetTutorialClicked()
    {
        if (!_resetArmed)
        {
            _resetArmed = true;
            _btnHubResetTutorial.text = Loc.T("Click again to reset");
            _btnHubResetTutorial.AddToClassList("screen-btn--confirm");
            _resetDisarm?.Pause();
            _resetDisarm = _btnHubResetTutorial.schedule.Execute(DisarmReset).StartingIn(ResetConfirmMs);
            return;
        }

        DisarmReset();
        OnResetTutorialRequested?.Invoke();
    }

    private void DisarmReset()
    {
        _resetArmed = false;
        _resetDisarm?.Pause();
        _resetDisarm = null;
        _btnHubResetTutorial.text = Loc.T("Reset tutorial");
        _btnHubResetTutorial.RemoveFromClassList("screen-btn--confirm");
    }

    /// <summary>
    /// Первый клик — «Click again to reset ALL» на несколько секунд, второй — сброс всего прогресса.
    /// </summary>
    private void HandleResetProgressClicked()
    {
        if (!_resetProgressArmed)
        {
            _resetProgressArmed = true;
            _btnHubResetProgress.text = Loc.T("Click again to reset ALL");
            _btnHubResetProgress.AddToClassList("screen-btn--confirm");
            _resetProgressDisarm?.Pause();
            _resetProgressDisarm = _btnHubResetProgress.schedule.Execute(DisarmResetProgress).StartingIn(ResetConfirmMs);
            return;
        }

        DisarmResetProgress();
        OnResetProgressRequested?.Invoke();
    }

    private void DisarmResetProgress()
    {
        _resetProgressArmed = false;
        _resetProgressDisarm?.Pause();
        _resetProgressDisarm = null;
        _btnHubResetProgress.text = Loc.T("Reset progress");
        _btnHubResetProgress.RemoveFromClassList("screen-btn--confirm");
    }

    private void HandleExitConfirmed()
    {
        HideExitConfirm();
        OnExitConfirmed?.Invoke();
    }

    private static VisualElement CreateItemRow(Sprite icon, string name, int count)
    {
        var row = new VisualElement();
        row.AddToClassList("item-row");

        if (icon != null)
        {
            var image = new VisualElement();
            image.AddToClassList("item-icon");
            image.style.backgroundImage = new StyleBackground(icon);
            row.Add(image);
        }

        var nameLabel = new Label(name);
        nameLabel.AddToClassList("item-name");
        row.Add(nameLabel);

        var countLabel = new Label($"×{count}");
        countLabel.AddToClassList("item-count");
        row.Add(countLabel);

        return row;
    }

    private static VisualElement CreateStatRow(string name, string value)
    {
        var row = new VisualElement();
        row.AddToClassList("stat-row");

        var nameLabel = new Label(name) { enableRichText = false };
        nameLabel.AddToClassList("stat-name");
        row.Add(nameLabel);

        var valueLabel = new Label(value) { enableRichText = false };
        valueLabel.AddToClassList("stat-value");
        row.Add(valueLabel);

        return row;
    }

    /// <summary>Энергия дробная (if — 0.5): целое без дробной части, иначе один знак.</summary>
    private static string FormatEnergy(float energy)
    {
        float rounded = Mathf.Round(energy * 10f) / 10f;
        return rounded.ToString(Mathf.Approximately(rounded, Mathf.Round(rounded)) ? "0" : "0.0",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static VisualElement CreateAchievementRow(AchievementDefinition achievement)
    {
        var row = new VisualElement();
        row.AddToClassList("summary-achievement");

        var title = new Label(Loc.T(achievement.Title)) { enableRichText = false };
        title.AddToClassList("summary-achievement-title");
        row.Add(title);

        string rewards = AchievementText.FormatRewards(achievement);
        if (!string.IsNullOrEmpty(rewards))
        {
            var rewardsLabel = new Label(Loc.F("Rewards: {0}", rewards)) { enableRichText = false };
            rewardsLabel.AddToClassList("summary-achievement-rewards");
            row.Add(rewardsLabel);
        }

        return row;
    }

    private static string FormatStacks(List<ItemStack> stacks)
    {
        var parts = new List<string>(stacks.Count);
        foreach (ItemStack stack in stacks)
            parts.Add($"{Loc.T(stack.item.DisplayName)} ×{stack.amount}");
        return string.Join(", ", parts);
    }
}
