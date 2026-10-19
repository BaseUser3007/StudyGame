using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Модальное окно «Unlocked: X» по <see cref="UnlockRegistry.OnUnlocked"/>. Команды, конструкции и
/// декомпиляции строятся по схеме Why (боль, класс unlock-pain) → What → Example → Energy (GameDesign §6.2.4):
///   • команда — Pain/Explanation/Example/EnergyNote из <see cref="LuaCommandGuide"/> (без записи —
///     Description), Signature и цена из ILuaCommand (LuaSandbox.AllCommands);
///   • конструкция — тексты и пример из <see cref="LuaConstructGuide"/>, цена из EnergyConfig;
///   • рецепт — иконка результата (ResourceItem.Icon), ингредиенты, станция, энергия;
///   • постройка хаба / событие на карте — короткая строка;
///   • «декомпиляция» — если в очереди команды из одной записи <see cref="LuaDecompileGuide"/> и все её
///     базовые команды уже открыты, они показываются одним окном «Decompiled: X()»: из чего состояла
///     готовая команда, новые команды, пример и сравнение цены (реестр команд + EnergyConfig).
/// Пока выполняется код, окно не показывается (SetSuspended), открытия копятся в очереди и идут после
/// окончания запуска — к этому моменту песочница уже применила их (OnUnlocksApplied).
/// Несколько открытий — по очереди, кнопка Next (N) / OK. Кнопка не фокусируемая, управление только мышью.
/// Элементы — экземпляр шаблона UnlockPopup.uxml (хост выдаёт RunScreens.GetOverlay).
/// Тексты — английский исходник через Loc (SetHeader переводит подпись сам, AddText — нет: перевод
/// на месте вызова); код примеров и сигнатуры не переводятся.
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
    private readonly LuaTokenizer _tokenizer = new LuaTokenizer();

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

        _content.Clear();
        _icon.AddToClassList(HiddenClass);

        // Команды одной декомпиляции — одним окном, остальные открытия — по одному
        List<int> group = CollectDecompileGroup(0, out LuaDecompileGuide.Entry decompile);
        if (group != null)
        {
            var commands = new List<string>(group.Count);
            foreach (int index in group) commands.Add(_queue[index].Id);
            for (int i = group.Count - 1; i >= 0; i--) _queue.RemoveAt(group[i]);
            BuildDecompile(decompile, commands);
        }
        else
        {
            PendingUnlock next = _queue[0];
            _queue.RemoveAt(0);
            Build(next.Kind, next.Id);
        }

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
        int pages = CountPages();
        _btnOk.text = pages > 0 && !_suspended ? Loc.F("Next ({0})", pages) : Loc.T("OK");
    }

    // ─── Decompile grouping ───────────────────────────────────

    /// <summary>
    /// Индексы (по возрастанию) открытий команд одной декомпиляции, начиная с <paramref name="start"/>,
    /// или null, если открытие под этим индексом — не команда декомпиляции. Группа образуется, только
    /// когда готовая команда есть в игре и все базовые команды записи уже открыты (иначе — обычные окна).
    /// </summary>
    private List<int> CollectDecompileGroup(int start, out LuaDecompileGuide.Entry entry)
    {
        entry = default;
        PendingUnlock first = _queue[start];
        if (first.Kind != UnlockKind.Command) return null;
        if (!LuaDecompileGuide.TryGetByBasic(first.Id, out LuaDecompileGuide.Entry candidate)) return null;
        if (FindCommand(candidate.ReadyMade) == null) return null;

        UnlockRegistry unlocks = _sandbox.Unlocks;
        if (unlocks != null)
            foreach (string basic in candidate.Basics)
                if (!unlocks.IsCommandUnlocked(basic)) return null;

        var group = new List<int>();
        for (int i = start; i < _queue.Count; i++)
            if (_queue[i].Kind == UnlockKind.Command && candidate.Contains(_queue[i].Id))
                group.Add(i);

        entry = candidate;
        return group;
    }

    /// <summary>Сколько окон ещё покажет очередь (команды одной декомпиляции — одно окно).</summary>
    private int CountPages()
    {
        int pages = 0;
        var consumed = new bool[_queue.Count];
        for (int i = 0; i < _queue.Count; i++)
        {
            if (consumed[i]) continue;
            pages++;

            List<int> group = CollectDecompileGroup(i, out _);
            if (group == null) continue;
            foreach (int index in group) consumed[index] = true;
        }
        return pages;
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
                AddText(Loc.T("It can now be built in the camp."), "unlock-text");
                break;
            case UnlockKind.MapEvent:
                SetHeader("New map event", id);
                AddText(Loc.T("It can now appear on the map during runs."), "unlock-text");
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
            AddText(Loc.T("This command is now available in your code."), "unlock-text");
            return;
        }

        // Why → What → Example → Energy; без текста в LuaCommandGuide — описание из реестра команд
        bool hasGuide = LuaCommandGuide.TryGet(name, out LuaCommandGuide.Entry guide);
        if (hasGuide) AddText(Loc.T(guide.Pain), "unlock-pain");

        AddCode(command.Signature);
        AddText(Loc.T(hasGuide && !string.IsNullOrEmpty(guide.Explanation) ? guide.Explanation : command.Description),
            "unlock-text");

        if (hasGuide && !string.IsNullOrEmpty(guide.Example))
        {
            AddText(Loc.T("Example"), "unlock-section");
            AddCode(guide.Example);
        }

        if (hasGuide) AddText(Loc.T("Energy"), "unlock-section");
        AddText(Loc.F("Costs {0} energy per call.", Format(command.EnergyCost)), "unlock-cost");
        if (hasGuide) AddText(Loc.T(guide.EnergyNote), "unlock-note");
        if (command.IsReadyMade)
            AddText(Loc.T("Ready-made command: it does what a few basic commands do, but costs more — " +
                          "your own code saves energy."), "unlock-note");
        AddText(Loc.T("It is now listed in Help and suggested by autocompletion."), "unlock-note");
    }

    private void BuildConstruct(string id)
    {
        string displayName = Loc.T(LuaConstructs.GetDisplayName(id));
        SetHeader("New language construct", Capitalize(displayName));

        // Why → What → Example → Energy (GameDesign §6.2.4)
        LuaConstructGuide.TryGet(id, out LuaConstructGuide.Entry entry);
        AddText(Loc.T(entry.Pain), "unlock-pain");
        AddText(Loc.T(entry.Explanation), "unlock-text");

        if (!string.IsNullOrEmpty(entry.Example))
        {
            AddText(Loc.T("Example"), "unlock-section");
            AddCode(entry.Example);
        }

        string cost = FormatConstructCost(id, _sandbox.EnergyConfig);
        if (cost != null || !string.IsNullOrEmpty(entry.EnergyNote))
            AddText(Loc.T("Energy"), "unlock-section");
        AddText(cost, "unlock-cost");
        AddText(Loc.T(entry.EnergyNote), "unlock-note");
    }

    /// <summary>
    /// «Decompiled: obtain()»: из чего состояла готовая команда, новые базовые команды, пример на них
    /// и сравнение цены: вызов готовой команды против одного прохода примера.
    /// </summary>
    private void BuildDecompile(LuaDecompileGuide.Entry entry, List<string> newCommands)
    {
        ILuaCommand readyMade = FindCommand(entry.ReadyMade);
        SetHeader("Decompiled", $"{entry.ReadyMade}()");

        // Why → What → Example → Energy (GameDesign §6.2.4)
        AddText(Loc.T(entry.Pain), "unlock-pain");
        AddText(Loc.T(entry.Explanation), "unlock-text");

        AddText(Loc.T(newCommands.Count == 1 ? "New command" : "New commands"), "unlock-section");
        foreach (string basic in entry.Basics)
        {
            if (!newCommands.Contains(basic)) continue;

            ILuaCommand command = FindCommand(basic);
            if (command == null) continue;

            AddCode(command.Signature);
            AddText(Loc.T(command.Description), "unlock-note");
            AddText(Loc.F("Costs {0} energy per call.", Format(command.EnergyCost)), "unlock-cost");
        }

        AddText(Loc.T("The same, with your own code"), "unlock-section");
        AddCode(entry.Example);

        AddText(Loc.T("Energy"), "unlock-section");
        _content.Add(CreateCostRow($"{entry.ReadyMade}()", Format(readyMade.EnergyCost), "unlock-cost-ready"));

        float ownCost = CalculateCost(entry.Example, out List<(string Name, float Cost)> parts);
        string breakdown = string.Join(" + ", parts.ConvertAll(FormatPart));
        string total = parts.Count > 1 ? $"{breakdown} = {Format(ownCost)}" : Format(ownCost);
        _content.Add(CreateCostRow(Loc.T("Your code"), total, "unlock-cost-own"));

        float saved = readyMade.EnergyCost - ownCost;
        if (saved > 0f)
            AddText(Loc.F("Your own code saves {0} energy on every pass — and you decide " +
                          "which steps you actually need.", Format(saved)), "unlock-cost");
        AddText(Loc.T(entry.EnergyNote), "unlock-note");

        AddText(Loc.F("{0}() stays available: handy, but expensive.", entry.ReadyMade), "unlock-note");
        AddText(Loc.T("The new commands are listed in Help and suggested by autocompletion."), "unlock-note");
    }

    private static string FormatPart((string Name, float Cost) part) => $"{part.Name} {Format(part.Cost)}";

    private static VisualElement CreateCostRow(string name, string value, string valueClass)
    {
        var row = new VisualElement();
        row.AddToClassList("unlock-cost-row");

        var nameLabel = new Label(name) { enableRichText = false };
        nameLabel.AddToClassList("unlock-cost-name");
        row.Add(nameLabel);

        var valueLabel = new Label(value) { enableRichText = false };
        valueLabel.AddToClassList("unlock-cost-value");
        valueLabel.AddToClassList(valueClass);
        row.Add(valueLabel);

        return row;
    }

    /// <summary>
    /// Цена одного прохода кода по тем же правилам, что EnergyCounter (команды, методы массива findObjects,
    /// ключевые слова), но команды берутся из всего реестра — независимо от того, что открыто.
    /// parts — платные элементы по порядку появления, повторы сложены.
    /// </summary>
    private float CalculateCost(string code, out List<(string Name, float Cost)> parts)
    {
        parts = new List<(string Name, float Cost)>();
        float total = 0f;
        if (string.IsNullOrEmpty(code)) return total;

        foreach (LuaTokenizer.Token token in _tokenizer.Tokenize(code))
        {
            float cost = GetTokenCost(token);
            if (cost <= 0f) continue;

            total += cost;
            int index = parts.FindIndex(p => p.Name == token.Value);
            if (index >= 0) parts[index] = (token.Value, parts[index].Cost + cost);
            else parts.Add((token.Value, cost));
        }
        return total;
    }

    private float GetTokenCost(LuaTokenizer.Token token)
    {
        if (token.Type == LuaTokenizer.TokenType.Keyword)
            return GetKeywordCost(token.Value, _sandbox.EnergyConfig);

        if (token.Type != LuaTokenizer.TokenType.Identifier) return 0f;

        ILuaCommand command = FindCommand(token.Value);
        if (command != null) return command.EnergyCost;
        return LuaObjectList.IsMethod(token.Value) ? LuaObjectList.MethodCost : 0f;
    }

    /// <summary>Те же поля EnergyConfig, что в EnergyCounter.</summary>
    private static float GetKeywordCost(string keyword, EnergyConfig config)
    {
        if (config == null) return 0f;

        switch (keyword)
        {
            case "if": return config.ifCost;
            case "elseif": return config.elseifCost;
            case "else": return config.elseCost;
            case "for": return config.forCost;
            case "while": return config.whileCost;
            case "repeat": return config.repeatCost;
            case "function": return config.functionCost;
            case "local": return config.localCost;
            case "return": return config.returnCost;
            default: return 0f;
        }
    }

    private void BuildRecipe(string key)
    {
        CraftRecipe recipe = FindRecipe(key);
        if (recipe == null || recipe.result == null)
        {
            SetHeader("New recipe", key);
            AddText(Loc.T("It now shows up in the crafting panel."), "unlock-text");
            return;
        }

        SetHeader("New recipe", Loc.T(recipe.DisplayName));
        Sprite icon = recipe.result.Icon;
        if (icon != null)
        {
            _icon.style.backgroundImage = new StyleBackground(icon);
            _icon.RemoveFromClassList(HiddenClass);
        }

        AddText(Loc.T("Craft it with:"), "unlock-text");
        // Код — Lua-API: имя предмета в craft() не переводится
        AddCode($"craft(\"{recipe.DisplayName}\")");

        AddText(Loc.T("Ingredients"), "unlock-section");
        foreach (RecipeIngredient ingredient in recipe.GetRequirements())
            _content.Add(CreateItemRow(ingredient.item.Icon, Loc.T(ingredient.item.DisplayName), ingredient.amount));

        if (recipe.NeedsStation)
            AddText(Loc.F("Needs a {0} nearby.", Loc.T(recipe.station.DisplayName())), "unlock-note");
        AddText(Loc.F("Crafting costs {0} energy.", Format(recipe.energyCost)), "unlock-cost");
        AddText(Loc.T("It now shows up in the crafting panel."), "unlock-note");
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <param name="caption">Английский исходник — переводится здесь.</param>
    private void SetHeader(string caption, string title)
    {
        _caption.text = Loc.T(caption);
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
            case LuaConstructs.Local: return Loc.F("Costs {0} energy each time it runs.", Format(config.localCost));
            case LuaConstructs.If:
                return Loc.F("Costs {0} energy per condition check, else — {1}.",
                    Format(config.ifCost), Format(config.elseCost));
            case LuaConstructs.For: return Loc.F("Costs {0} energy per iteration.", Format(config.forCost));
            case LuaConstructs.While: return Loc.F("Costs {0} energy per condition check.", Format(config.whileCost));
            case LuaConstructs.Repeat: return Loc.F("Costs {0} energy per condition check.", Format(config.repeatCost));
            case LuaConstructs.Function: return Loc.F("Costs {0} energy per call.", Format(config.functionCost));
            case LuaConstructs.Classes:
                return Loc.F("Calling a window costs {0} energy, like a function call.", Format(config.functionCost));
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
