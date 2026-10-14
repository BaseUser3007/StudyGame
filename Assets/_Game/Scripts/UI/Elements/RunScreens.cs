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
}

/// <summary>
/// Полноэкранные экраны режима игры: лагерь (хаб), подтверждение выхода из забега, итоги забега.
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

    private VisualElement _exitConfirm;
    private Button _btnExitConfirm;
    private Button _btnExitCancel;

    private VisualElement _summary;
    private Label _summaryTitle;
    private Label _summaryStats;
    private ScrollView _summaryLoot;
    private Label _summaryLootEmpty;
    private Label _summaryLost;
    private Button _btnToHub;

    public event Action OnStartRunRequested;
    public event Action OnExitConfirmed;
    public event Action OnReturnToHubRequested;

    public RunScreens(VisualTreeAsset template)
    {
        VisualElement root = template.Instantiate();
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

        // UI управляется только мышью — кнопки не должны забирать фокус
        foreach (Button button in new[] { _btnStartRun, _btnExitConfirm, _btnExitCancel, _btnToHub })
            button.focusable = false;

        _btnStartRun.clicked += () => OnStartRunRequested?.Invoke();
        _btnExitCancel.clicked += HideExitConfirm;
        _btnExitConfirm.clicked += HandleExitConfirmed;
        _btnToHub.clicked += () => OnReturnToHubRequested?.Invoke();

        Add(root);
    }

    private void QueryElements(VisualElement root)
    {
        _hubScreen = root.Q<VisualElement>("hub-screen");
        _hubStats = root.Q<Label>("hub-stats");
        _hubMaterials = root.Q<ScrollView>("hub-materials");
        _hubMaterialsEmpty = root.Q<Label>("hub-materials-empty");
        _btnStartRun = root.Q<Button>("btn-start-run");

        _exitConfirm = root.Q<VisualElement>("exit-confirm");
        _btnExitConfirm = root.Q<Button>("btn-exit-confirm");
        _btnExitCancel = root.Q<Button>("btn-exit-cancel");

        _summary = root.Q<VisualElement>("run-summary");
        _summaryTitle = root.Q<Label>("summary-title");
        _summaryStats = root.Q<Label>("summary-stats");
        _summaryLoot = root.Q<ScrollView>("summary-loot");
        _summaryLootEmpty = root.Q<Label>("summary-loot-empty");
        _summaryLost = root.Q<Label>("summary-lost");
        _btnToHub = root.Q<Button>("btn-to-hub");
    }

    private void ValidateReferences()
    {
        Debug.Assert(_hubScreen != null && _hubStats != null && _hubMaterials != null
                     && _hubMaterialsEmpty != null && _btnStartRun != null,
            "RunScreens: hub elements not found");
        Debug.Assert(_exitConfirm != null && _btnExitConfirm != null && _btnExitCancel != null,
            "RunScreens: exit confirmation elements not found");
        Debug.Assert(_summary != null && _summaryTitle != null && _summaryStats != null && _summaryLoot != null
                     && _summaryLootEmpty != null && _summaryLost != null && _btnToHub != null,
            "RunScreens: summary elements not found");
    }

    // ─── Public API ───────────────────────────────────────────

    public bool IsExitConfirmOpen => !_exitConfirm.ClassListContains(HiddenClass);

    public void ShowHub(MetaProgress progress)
    {
        _hubStats.text = progress.RunsFinished == 0
            ? "Забегов ещё не было"
            : $"Завершено забегов: {progress.RunsFinished} (поражений: {progress.Defeats})";

        _hubMaterials.Clear();
        foreach (StoredMaterial stored in progress.Materials)
            _hubMaterials.Add(CreateItemRow(null, stored.displayName ?? stored.itemId, stored.count));
        _hubMaterialsEmpty.EnableInClassList(HiddenClass, progress.Materials.Count > 0);

        _hubScreen.RemoveFromClassList(HiddenClass);
    }

    public void ShowExitConfirm() => _exitConfirm.RemoveFromClassList(HiddenClass);

    public void HideExitConfirm() => _exitConfirm.AddToClassList(HiddenClass);

    public void ShowSummary(RunSummary summary)
    {
        HideExitConfirm();

        _summaryTitle.text = summary.Defeated ? "Поражение" : "Забег завершён";
        _summaryTitle.EnableInClassList("screen-title--defeat", summary.Defeated);
        _summaryStats.text = summary.Defeated
            ? $"Здоровье кончилось на {summary.Days}-й день. Всё добытое в этом забеге потеряно."
            : $"Дней в забеге: {summary.Days}";

        _summaryLoot.Clear();
        foreach (ItemStack stack in summary.Carried)
            _summaryLoot.Add(CreateItemRow(stack.item.Icon, stack.item.DisplayName, stack.amount));
        _summaryLootEmpty.EnableInClassList(HiddenClass, summary.Carried.Count > 0);

        _summaryLost.text = summary.Left.Count > 0 ? $"Остаётся в забеге: {FormatStacks(summary.Left)}" : string.Empty;
        _summaryLost.EnableInClassList(HiddenClass, summary.Left.Count == 0);

        _summary.RemoveFromClassList(HiddenClass);
    }

    // ─── Helpers ──────────────────────────────────────────────

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

    private static string FormatStacks(List<ItemStack> stacks)
    {
        var parts = new List<string>(stacks.Count);
        foreach (ItemStack stack in stacks)
            parts.Add($"{stack.item.DisplayName} ×{stack.amount}");
        return string.Join(", ", parts);
    }
}
