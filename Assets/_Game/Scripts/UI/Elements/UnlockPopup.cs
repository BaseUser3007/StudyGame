using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Модальное окно «Unlocked: X» по <see cref="UnlockRegistry.OnUnlocked"/>: зачем это нужно и пример.
///   • команда — Signature + Description + цена из ILuaCommand (LuaSandbox.AllCommands);
///   • конструкция — объяснение и пример кода из <see cref="LuaConstructGuide"/>, цена из EnergyConfig;
///   • рецепт — иконка результата (ResourceItem.Icon), ингредиенты, станция, энергия;
///   • постройка хаба / событие на карте — короткая строка.
/// Пока выполняется код, окно не показывается (SetSuspended), открытия копятся в очереди и идут после
/// окончания запуска — к этому моменту песочница уже применила их (OnUnlocksApplied).
/// Несколько открытий — по очереди, кнопка Next (N) / OK. Кнопка не фокусируемая, управление только мышью.
/// Элементы — экземпляр шаблона UnlockPopup.uxml (хост выдаёт RunScreens.GetOverlay).
/// </summary>
public class UnlockPopup
{
    private const string HiddenClass = "hidden";

    private readonly struct PendingUnlock
    {
        public readonly UnlockKind Kind;
        public readonly string Id;

        public PendingUnlock(UnlockKind kind, string id)
        {
            Kind = kind;
            Id = id;
        }
    }

    private readonly List<PendingUnlock> _queue = new List<PendingUnlock>();
    private readonly LuaSandbox _sandbox;
    private readonly RecipeBook _recipeBook;
    private readonly LuaSyntaxHighlighter _highlighter;

    private readonly VisualElement _backdrop;
    private readonly Label _caption;
    private readonly VisualElement _icon;
    private readonly Label _title;
    private readonly ScrollView _content;
    private readonly Button _btnOk;

    private bool _suspended;
    private bool _showScheduled;

    public bool IsOpen => !_backdrop.ClassListContains(HiddenClass);

    /// <param name="root">Хост шаблона UnlockPopup.uxml.</param>
    public UnlockPopup(VisualElement root, LuaSandbox sandbox, RecipeBook recipeBook, LuaSyntaxHighlighter highlighter)
    {
        _sandbox = sandbox;
        _recipeBook = recipeBook;
        _highlighter = highlighter;

        _backdrop = root.Q<VisualElement>("unlock-backdrop");
        _caption = root.Q<Label>("unlock-caption");
        _icon = root.Q<VisualElement>("unlock-icon");
        _title = root.Q<Label>("unlock-title");
        _content = root.Q<ScrollView>("unlock-content");
        _btnOk = root.Q<Button>("btn-unlock-ok");

        Debug.Assert(_backdrop != null && _caption != null && _icon != null && _title != null && _content != null
                     && _btnOk != null, "UnlockPopup: elements not found");
        Debug.Assert(_sandbox != null, "UnlockPopup: LuaSandbox is null");

        _title.enableRichText = false;

        // UI управляется только мышью — кнопка не должна забирать фокус
        _btnOk.focusable = false;
        _btnOk.clicked += HandleOkClicked;
    }

    // ─── Public API ───────────────────────────────────────────

    public void Enqueue(UnlockKind kind, string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        foreach (PendingUnlock pending in _queue)
            if (pending.Kind == kind && pending.Id == id) return;

        _queue.Add(new PendingUnlock(kind, id));

        if (IsOpen) RefreshOkButton();
        else ScheduleShow();
    }

    /// <summary>
    /// true — выполняется код: новые окна не показываются. После false окно появляется в следующем кадре —
    /// песочница к этому времени применит открытия (ApplyUnlocks идёт сразу после SetRunning(false)).
    /// Уже открытое окно не закрывается.
    /// </summary>
    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        if (!suspended) ScheduleShow();
    }

    // ─── Show / hide ──────────────────────────────────────────

    private void ScheduleShow()
    {
        if (_showScheduled || _suspended || IsOpen || _queue.Count == 0) return;

        _showScheduled = true;
        _backdrop.schedule.Execute(HandleScheduledShow);
    }

    private void HandleScheduledShow()
    {
        _showScheduled = false;
        if (!_suspended && !IsOpen) ShowNext();
    }

    private void ShowNext()
    {
        if (_queue.Count == 0)
        {
            _backdrop.AddToClassList(HiddenClass);
            return;
        }

        PendingUnlock next = _queue[0];
        _queue.RemoveAt(0);

        _content.Clear();
        _icon.AddToClassList(HiddenClass);
        Build(next.Kind, next.Id);

        RefreshOkButton();
        _backdrop.RemoveFromClassList(HiddenClass);
        _content.scrollOffset = Vector2.zero;
    }

    private void HandleOkClicked()
    {
        // Во время выполнения кода следующие ждут его окончания
        if (_queue.Count > 0 && !_suspended)
        {
            ShowNext();
            return;
        }

        _backdrop.AddToClassList(HiddenClass);
    }

    private void RefreshOkButton()
    {
        _btnOk.text = _queue.Count > 0 && !_suspended ? $"Next ({_queue.Count})" : "OK";
    }

    // ─── Content ──────────────────────────────────────────────

    private void Build(UnlockKind kind, string id)
    {
        switch (kind)
        {
            case UnlockKind.Command: BuildCommand(id); break;
            case UnlockKind.Construct: BuildConstruct(id); break;
            case UnlockKind.Recipe: BuildRecipe(id); break;
            case UnlockKind.HubBuilding:
                SetHeader("New camp building", id);
                AddText("It can now be built in the camp.", "unlock-text");
                break;
            case UnlockKind.MapEvent:
                SetHeader("New map event", id);
                AddText("It can now appear on the map during runs.", "unlock-text");
                break;
            default:
                SetHeader("Unlocked", id);
                break;
        }
    }

    private void BuildCommand(string name)
    {
        SetHeader("New command", name);

        ILuaCommand command = FindCommand(name);
        if (command == null)
        {
            AddText("This command is now available in your code.", "unlock-text");
            return;
        }

        AddCode(command.Signature);
        AddText(command.Description, "unlock-text");
        AddText($"Costs {Format(command.EnergyCost)} energy per call.", "unlock-cost");
        if (command.IsReadyMade)
            AddText("Ready-made command: it does what a few basic commands do, but costs more — " +
                    "your own code saves energy.", "unlock-note");
        AddText("It is now listed in Help and suggested by autocompletion.", "unlock-note");
    }

    private void BuildConstruct(string id)
    {
        string displayName = LuaConstructs.GetDisplayName(id);
        SetHeader("New language construct", Capitalize(displayName));

        if (LuaConstructGuide.TryGet(id, out LuaConstructGuide.Entry entry))
            AddText(entry.Explanation, "unlock-text");

        string cost = FormatConstructCost(id, _sandbox.EnergyConfig);
        if (cost != null)
            AddText(cost, "unlock-cost");

        if (!string.IsNullOrEmpty(entry.Example))
        {
            AddText("Example", "unlock-section");
            AddCode(entry.Example);
        }
    }

    private void BuildRecipe(string key)
    {
        CraftRecipe recipe = FindRecipe(key);
        if (recipe == null || recipe.result == null)
        {
            SetHeader("New recipe", key);
            AddText("It now shows up in the crafting panel.", "unlock-text");
            return;
        }

        SetHeader("New recipe", recipe.DisplayName);
        Sprite icon = recipe.result.Icon;
        if (icon != null)
        {
            _icon.style.backgroundImage = new StyleBackground(icon);
            _icon.RemoveFromClassList(HiddenClass);
        }

        AddText("Craft it with:", "unlock-text");
        AddCode($"craft(\"{recipe.DisplayName}\")");

        AddText("Ingredients", "unlock-section");
        foreach (RecipeIngredient ingredient in recipe.GetRequirements())
            _content.Add(CreateItemRow(ingredient.item.Icon, ingredient.item.DisplayName, ingredient.amount));

        if (recipe.NeedsStation)
            AddText($"Needs a {recipe.station} nearby.", "unlock-note");
        AddText($"Crafting costs {Format(recipe.energyCost)} energy.", "unlock-cost");
        AddText("It now shows up in the crafting panel.", "unlock-note");
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void SetHeader(string caption, string title)
    {
        _caption.text = caption;
        _title.text = title;
    }

    private void AddText(string text, string className)
    {
        if (string.IsNullOrEmpty(text)) return;

        var label = new Label(text) { enableRichText = false };
        label.AddToClassList(className);
        _content.Add(label);
    }

    /// <summary>
    /// Код с подсветкой редактора (rich text; подсветка экранирует '&lt;', текст совпадает с исходником).
    /// </summary>
    private void AddCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return;

        var label = new Label();
        label.AddToClassList("unlock-code");
        if (_highlighter != null)
        {
            label.enableRichText = true;
            label.text = _highlighter.Highlight(code);
        }
        else
        {
            label.enableRichText = false;
            label.text = code;
        }
        _content.Add(label);
    }

    private static VisualElement CreateItemRow(Sprite icon, string name, int count)
    {
        var row = new VisualElement();
        row.AddToClassList("unlock-item-row");

        var image = new VisualElement();
        image.AddToClassList("unlock-item-icon");
        if (icon != null) image.style.backgroundImage = new StyleBackground(icon);
        row.Add(image);

        var nameLabel = new Label(name) { enableRichText = false };
        nameLabel.AddToClassList("unlock-item-name");
        row.Add(nameLabel);

        var countLabel = new Label($"×{count}");
        countLabel.AddToClassList("unlock-item-count");
        row.Add(countLabel);

        return row;
    }

    private ILuaCommand FindCommand(string name)
    {
        IReadOnlyList<ILuaCommand> commands = _sandbox.AllCommands;
        if (commands == null) return null;

        foreach (ILuaCommand command in commands)
            if (command.Name == name) return command;
        return null;
    }

    private CraftRecipe FindRecipe(string key)
    {
        if (_recipeBook == null) return null;

        foreach (CraftRecipe recipe in _recipeBook.Recipes)
            if (recipe != null && recipe.Key == key) return recipe;
        return null;
    }

    /// <summary>Цена конструкции из EnergyConfig — те же поля, что в справке (CommandHelp).</summary>
    private static string FormatConstructCost(string id, EnergyConfig config)
    {
        if (config == null) return null;

        switch (id)
        {
            case LuaConstructs.Local: return $"Costs {Format(config.localCost)} energy each time it runs.";
            case LuaConstructs.If:
                return $"Costs {Format(config.ifCost)} energy per condition check, else — {Format(config.elseCost)}.";
            case LuaConstructs.For: return $"Costs {Format(config.forCost)} energy per iteration.";
            case LuaConstructs.While: return $"Costs {Format(config.whileCost)} energy per condition check.";
            case LuaConstructs.Repeat: return $"Costs {Format(config.repeatCost)} energy per condition check.";
            case LuaConstructs.Function: return $"Costs {Format(config.functionCost)} energy per call.";
            case LuaConstructs.Classes: return $"Calling a window costs {Format(config.functionCost)} energy, like a function call.";
            default: return null;
        }
    }

    private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Capitalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
