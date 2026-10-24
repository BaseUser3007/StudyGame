using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Справка по командам (кнопка Help на верхней панели). Содержимое строится из реестра открытых
/// команд и EnergyConfig, поэтому новые команды и цены появляются в ней автоматически.
/// Поле поиска сверху фильтрует записи по имени, сигнатуре и описанию на текущем языке.
/// Как и поля кода, оно принимает фокус только после клика мышью (иначе клавиши камеры
/// могли бы перенести в него фокус); пока в нём печатают, камера выключена через PlayerEventBus.
/// </summary>
public class CommandHelp
{
    /// <summary>Справку открыли (условие шага туториала).</summary>
    public event Action OnOpened;

    private const string SearchFieldName = "help-search";

    /// <summary>Раздел справки: заголовок с пояснением и его записи — скрывается, если ничего не подошло.</summary>
    private class Section
    {
        public VisualElement Header;
        public VisualElement Note;
        public readonly List<Item> Items = new List<Item>();
    }

    /// <summary>Запись справки и текст, по которому она ищется (уже в нижнем регистре).</summary>
    private class Item
    {
        public VisualElement Element;
        public string SearchText;
    }

    private readonly Button _toggleButton;
    private readonly VisualElement _popup;
    private readonly ScrollView _content;
    private readonly TextField _searchField;
    private readonly List<Section> _sections = new List<Section>();
    private Label _nothingFound;

    private bool _focusedByMouse;
    private bool _focusPublished;

    public CommandHelp(Button toggleButton, VisualElement popup, Button closeButton, ScrollView content)
    {
        _toggleButton = toggleButton;
        _popup = popup;
        _content = content;

        // UI управляется только мышью — кнопки не должны забирать фокус
        _toggleButton.focusable = false;
        closeButton.focusable = false;

        _toggleButton.clicked += Toggle;
        closeButton.clicked += Close;

        _searchField = popup.Q<TextField>(SearchFieldName) ?? CreateSearchField();
        SetupSearchField();
    }

    // ─── Public API ───────────────────────────────────────────

    public void Toggle()
    {
        SetOpen(_popup.ClassListContains("hidden"));
    }

    public void Close()
    {
        SetOpen(false);
    }

    /// <summary>
    /// Перестраивает справку. commands — только открытые команды (закрытые в справке не показываются);
    /// конструкции языка показываются все: закрытые — приглушённо с пометкой locked.
    /// unlocks == null — всё открыто.
    /// </summary>
    public void Build(IEnumerable<ILuaCommand> commands, EnergyConfig energyConfig, UnlockRegistry unlocks)
    {
        _content.Clear();
        _sections.Clear();
        _searchField.textEdition.placeholder = Loc.T("Search commands…");

        List<ILuaCommand> all = commands.OrderBy(c => c.Name).ToList();

        // Заголовки и пояснения — английский исходник, перевод через Loc; описания команд — из реестра (Loc.T)
        AddSection(Loc.T("Commands"),
            Loc.T("The cost is charged on every call. Mining and crafting also spend energy for the resource and the recipe."));
        foreach (ILuaCommand command in all.Where(c => !c.IsReadyMade))
            AddCommandEntry(command);

        // Массив возвращают только findObjects и check — без них раздел бесполезен
        if (all.Any(c => c is FindObjectsCommand || c is CheckCommand))
        {
            AddSection(Loc.T("findObjects list"),
                Loc.F("Methods are called with a colon: list:getNear(). where, sortBy and take return a new list, " +
                      "so they can be chained. Object fields: {0}. Harvested objects are skipped.", Loc.T(LuaObjectList.Fields)));
            foreach (var method in LuaObjectList.Methods)
            {
                string description = Loc.T(method.Description);
                AddItem(CreateEntry(method.Signature, LuaObjectList.MethodCost, description),
                    method.Name, method.Signature, description);
            }
        }

        List<ILuaCommand> readyMade = all.Where(c => c.IsReadyMade).ToList();
        if (readyMade.Count > 0)
        {
            AddSection(Loc.T("Ready-made commands"),
                Loc.T("They do what a few basic commands do, but cost more: your own code saves energy."));
            foreach (ILuaCommand command in readyMade)
                AddCommandEntry(command);
        }

        BuildConstructs(energyConfig, unlocks);

        _nothingFound = new Label(Loc.T("Nothing found"));
        _nothingFound.AddToClassList("help-nothing-found");
        _content.Add(_nothingFound);

        // Справку перестраивают и при введённом запросе (открытие команд) — фильтр сохраняется
        ApplyFilter();
    }

    // ─── Конструкции языка ────────────────────────────────────

    /// <summary>
    /// Все конструкции из LuaConstructs.All в порядке прогрессии. Закрытые — приглушённо, вместо цены «locked»:
    /// игрок видит, что ещё можно открыть. return не закрывается никогда.
    /// </summary>
    private void BuildConstructs(EnergyConfig energyConfig, UnlockRegistry unlocks)
    {
        AddSection(Loc.T("Language constructs"),
            Loc.T("Charged every time they execute: a condition on every check, a loop on every iteration. " +
                  "Locked constructs are unlocked during the game: code that uses them won't run."));

        foreach (LuaConstructs.Info info in LuaConstructs.All)
        {
            bool locked = unlocks != null && !unlocks.IsConstructUnlocked(info.Id);
            AddConstructRows(info, energyConfig, locked);
        }

        AddKeyword("return", energyConfig != null ? energyConfig.returnCost : (float?)null, "per execution", false);
    }

    private void AddConstructRows(LuaConstructs.Info info, EnergyConfig config, bool locked)
    {
        switch (info.Id)
        {
            case LuaConstructs.Local:
                AddKeyword("local", config?.localCost, "per execution", locked, info);
                break;
            case LuaConstructs.If:
                AddKeyword("if / elseif", config?.ifCost, "per condition check", locked, info);
                AddKeyword("else", config?.elseCost, "per branch entry", locked, info);
                break;
            case LuaConstructs.For:
                AddKeyword("for", config?.forCost, "per iteration", locked, info);
                break;
            case LuaConstructs.While:
                AddKeyword("while", config?.whileCost, "per condition check", locked, info);
                break;
            case LuaConstructs.Repeat:
                AddKeyword("repeat … until", config?.repeatCost, "per condition check", locked, info);
                break;
            case LuaConstructs.Function:
                AddKeyword("function", config?.functionCost, "per call", locked, info);
                break;
            case LuaConstructs.Classes:
                // Доп. окно — глобальная функция с именем заголовка: платится как вызов функции
                AddKeyword(Loc.T("class windows") + " (+)", config?.functionCost, "per window call", locked, info);
                break;
            default:
                // Новая конструкция без описанной цены — показываем хотя бы имя
                AddKeyword(Loc.T(info.DisplayName), null, string.Empty, locked, info);
                break;
        }
    }

    // ─── Поиск ────────────────────────────────────────────────

    /// <summary>Запасной вариант, если в TopBar.uxml нет поля поиска: создаём его над списком.</summary>
    private TextField CreateSearchField()
    {
        TextField field = new TextField { name = SearchFieldName };
        field.AddToClassList("help-search");
        _popup.Insert(Mathf.Max(0, _popup.IndexOf(_content)), field);
        return field;
    }

    private void SetupSearchField()
    {
        _searchField.isDelayed = false;
        _searchField.textEdition.placeholder = Loc.T("Search commands…");
        _searchField.textEdition.hidePlaceholderOnFocus = false;

        _searchField.RegisterValueChangedCallback(HandleSearchChanged);

        // PointerDown приходит до смены фокуса — отмечаем, что фокус получен мышью (как в CodeEditor)
        _searchField.RegisterCallback<PointerDownEvent>(HandleSearchPointerDown, TrickleDown.TrickleDown);
        _searchField.RegisterCallback<FocusInEvent>(HandleSearchFocusIn);
        _searchField.RegisterCallback<FocusOutEvent>(HandleSearchFocusOut);
        _searchField.RegisterCallback<KeyDownEvent>(HandleSearchKeyDown, TrickleDown.TrickleDown);
    }

    private void HandleSearchPointerDown(PointerDownEvent evt)
    {
        _focusedByMouse = true;
    }

    private void HandleSearchFocusIn(FocusInEvent evt)
    {
        if (!_focusedByMouse)
        {
            // Фокус пришёл не от мыши (клавиатурная навигация) — не принимаем его.
            // Blur внутри FocusIn может не сработать, поэтому повторяем в следующем кадре.
            _searchField.Blur();
            _searchField.schedule.Execute(BlurIfNotByMouse);
            return;
        }

        if (_focusPublished) return;
        _focusPublished = true;
        PlayerEventBus.PublishUIFocused();
    }

    private void HandleSearchFocusOut(FocusOutEvent evt)
    {
        _focusedByMouse = false;

        // Снимаем «фокус UI», только если сами его выставили — отклонённый фокус ничего не публиковал
        if (!_focusPublished) return;
        _focusPublished = false;
        PlayerEventBus.PublishUIUnfocused();
    }

    private void BlurIfNotByMouse()
    {
        if (!_focusedByMouse) _searchField.Blur();
    }

    private void HandleSearchKeyDown(KeyDownEvent evt)
    {
        // Esc и Enter заканчивают ввод: фокус снимается, камера снова слушает клавиши
        if (evt.keyCode == KeyCode.Escape ||
            evt.keyCode == KeyCode.Return ||
            evt.keyCode == KeyCode.KeypadEnter)
        {
            _searchField.Blur();
            evt.StopPropagation();
        }
    }

    private void HandleSearchChanged(ChangeEvent<string> evt)
    {
        ApplyFilter();
        _content.scrollOffset = Vector2.zero;
    }

    /// <summary>
    /// Показывает записи, в тексте которых есть все слова запроса (без учёта регистра).
    /// Раздел без подходящих записей скрывается целиком; пустой запрос — показывается всё.
    /// </summary>
    private void ApplyFilter()
    {
        string[] words = (_searchField.value ?? string.Empty)
            .ToLowerInvariant()
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        bool filtering = words.Length > 0;
        int visibleTotal = 0;

        foreach (Section section in _sections)
        {
            int visible = 0;
            foreach (Item item in section.Items)
            {
                bool match = !filtering || words.All(w => item.SearchText.Contains(w));
                SetShown(item.Element, match);
                if (match) visible++;
            }

            // Пояснение раздела при поиске только мешает — оставляем заголовок
            SetShown(section.Header, visible > 0);
            SetShown(section.Note, !filtering && visible > 0);
            visibleTotal += visible;
        }

        if (_nothingFound != null)
            SetShown(_nothingFound, filtering && visibleTotal == 0);
    }

    private static void SetShown(VisualElement element, bool shown)
    {
        element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void SetOpen(bool open)
    {
        // Закрытие справки снимает фокус с поля поиска — иначе камера осталась бы выключенной
        if (!open) _searchField.Blur();

        _popup.EnableInClassList("hidden", !open);
        _toggleButton.EnableInClassList("btn-help--active", open);
        if (open) OnOpened?.Invoke();
    }

    private void AddSection(string title, string note)
    {
        Label header = new Label(title);
        header.AddToClassList("help-section");
        _content.Add(header);

        Label hint = new Label(note);
        hint.AddToClassList("help-section-note");
        _content.Add(hint);

        _sections.Add(new Section { Header = header, Note = hint });
    }

    /// <summary>Добавляет запись в текущий (последний) раздел и запоминает текст для поиска.</summary>
    private void AddItem(VisualElement element, params string[] searchParts)
    {
        _content.Add(element);
        if (_sections.Count == 0) return;

        string text = string.Join("\n", searchParts.Where(p => !string.IsNullOrEmpty(p)));
        _sections[_sections.Count - 1].Items.Add(new Item
        {
            Element = element,
            SearchText = text.ToLowerInvariant()
        });
    }

    private void AddCommandEntry(ILuaCommand command)
    {
        string signature = string.IsNullOrEmpty(command.Signature) ? command.Name : command.Signature;
        string description = Loc.T(command.Description);
        AddItem(CreateEntry(signature, command.EnergyCost, description),
            command.Name, signature, description);
    }

    private static VisualElement CreateEntry(string signatureText, float energyCost, string descriptionText)
    {
        VisualElement entry = new VisualElement();
        entry.AddToClassList("help-entry");

        VisualElement header = new VisualElement();
        header.AddToClassList("help-entry-header");

        Label signature = new Label(signatureText);
        signature.AddToClassList("help-signature");
        signature.enableRichText = false;
        header.Add(signature);

        Label cost = new Label(Loc.F("{0} en.", Format(energyCost)));
        cost.AddToClassList("help-cost");
        header.Add(cost);

        entry.Add(header);

        if (!string.IsNullOrEmpty(descriptionText))
        {
            Label description = new Label(descriptionText);
            description.AddToClassList("help-description");
            description.enableRichText = false;
            entry.Add(description);
        }

        return entry;
    }

    /// <param name="when">Английский исходник («per call»…) — переводится здесь.</param>
    /// <param name="info">Конструкция языка: её название тоже участвует в поиске («цикл»…).</param>
    private void AddKeyword(string keyword, float? cost, string when, bool locked,
        LuaConstructs.Info? info = null)
    {
        when = Loc.T(when);
        VisualElement row = new VisualElement();
        row.AddToClassList("help-keyword-row");
        row.EnableInClassList("help-keyword-row--locked", locked);

        Label name = new Label(keyword) { enableRichText = false };
        name.AddToClassList("help-keyword");
        row.Add(name);

        string priceText = locked
            ? Loc.T("locked")
            : cost.HasValue ? Loc.F("{0} en. {1}", Format(cost.Value), when) : when;
        Label price = new Label(priceText);
        price.AddToClassList("help-cost");
        row.Add(price);

        AddItem(row, keyword,
            info.HasValue ? info.Value.Id : null,
            info.HasValue ? Loc.T(info.Value.DisplayName) : null,
            priceText);
    }

    private static string Format(float value)
    {
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
