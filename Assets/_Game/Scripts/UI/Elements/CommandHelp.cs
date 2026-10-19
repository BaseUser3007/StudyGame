using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;

/// <summary>
/// Справка по командам (кнопка Help на верхней панели). Содержимое строится из реестра открытых
/// команд и EnergyConfig, поэтому новые команды и цены появляются в ней автоматически.
/// </summary>
public class CommandHelp
{
    /// <summary>Справку открыли (условие шага туториала).</summary>
    public event System.Action OnOpened;

    private readonly Button _toggleButton;
    private readonly VisualElement _popup;
    private readonly ScrollView _content;

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

        List<ILuaCommand> all = commands.OrderBy(c => c.Name).ToList();

        // Заголовки и пояснения — английский исходник, перевод через Loc; описания команд — из реестра (Loc.T)
        AddSection(Loc.T("Commands"),
            Loc.T("The cost is charged on every call. Mining and crafting also spend energy for the resource and the recipe."));
        foreach (ILuaCommand command in all.Where(c => !c.IsReadyMade))
            _content.Add(CreateCommandEntry(command));

        // Массив возвращают только findObjects и check — без них раздел бесполезен
        if (all.Any(c => c is FindObjectsCommand || c is CheckCommand))
        {
            AddSection(Loc.T("findObjects list"),
                Loc.F("Methods are called with a colon: list:getNear(). where, sortBy and take return a new list, " +
                      "so they can be chained. Object fields: {0}. Harvested objects are skipped.", Loc.T(LuaObjectList.Fields)));
            foreach (var method in LuaObjectList.Methods)
                _content.Add(CreateEntry(method.Signature, LuaObjectList.MethodCost, Loc.T(method.Description)));
        }

        List<ILuaCommand> readyMade = all.Where(c => c.IsReadyMade).ToList();
        if (readyMade.Count > 0)
        {
            AddSection(Loc.T("Ready-made commands"),
                Loc.T("They do what a few basic commands do, but cost more: your own code saves energy."));
            foreach (ILuaCommand command in readyMade)
                _content.Add(CreateCommandEntry(command));
        }

        BuildConstructs(energyConfig, unlocks);
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
                AddKeyword("local", config?.localCost, "per execution", locked);
                break;
            case LuaConstructs.If:
                AddKeyword("if / elseif", config?.ifCost, "per condition check", locked);
                AddKeyword("else", config?.elseCost, "per branch entry", locked);
                break;
            case LuaConstructs.For:
                AddKeyword("for", config?.forCost, "per iteration", locked);
                break;
            case LuaConstructs.While:
                AddKeyword("while", config?.whileCost, "per condition check", locked);
                break;
            case LuaConstructs.Repeat:
                AddKeyword("repeat … until", config?.repeatCost, "per condition check", locked);
                break;
            case LuaConstructs.Function:
                AddKeyword("function", config?.functionCost, "per call", locked);
                break;
            case LuaConstructs.Classes:
                // Доп. окно — глобальная функция с именем заголовка: платится как вызов функции
                AddKeyword(Loc.T("class windows") + " (+)", config?.functionCost, "per window call", locked);
                break;
            default:
                // Новая конструкция без описанной цены — показываем хотя бы имя
                AddKeyword(Loc.T(info.DisplayName), null, string.Empty, locked);
                break;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void SetOpen(bool open)
    {
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
    }

    private static VisualElement CreateCommandEntry(ILuaCommand command)
    {
        return CreateEntry(
            string.IsNullOrEmpty(command.Signature) ? command.Name : command.Signature,
            command.EnergyCost,
            Loc.T(command.Description));
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
    private void AddKeyword(string keyword, float? cost, string when, bool locked)
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

        _content.Add(row);
    }

    private static string Format(float value)
    {
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
