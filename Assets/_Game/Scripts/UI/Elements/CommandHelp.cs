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

        AddSection("Команды",
            "Цена списывается при каждом вызове. Добыча и крафт дополнительно тратят энергию по ресурсу и рецепту.");
        foreach (ILuaCommand command in all.Where(c => !c.IsReadyMade))
            _content.Add(CreateCommandEntry(command));

        // Массив возвращают только findObjects и check — без них раздел бесполезен
        if (all.Any(c => c is FindObjectsCommand || c is CheckCommand))
        {
            AddSection("Массив findObjects",
                "Методы вызываются через двоеточие: list:getNear(). where, sortBy и take возвращают новый массив — " +
                $"их можно выстраивать в цепочку. Поля объекта: {LuaObjectList.Fields}. Собранные объекты пропускаются.");
            foreach (var method in LuaObjectList.Methods)
                _content.Add(CreateEntry(method.Signature, LuaObjectList.MethodCost, method.Description));
        }

        List<ILuaCommand> readyMade = all.Where(c => c.IsReadyMade).ToList();
        if (readyMade.Count > 0)
        {
            AddSection("Готовые команды",
                "Делают то же, что несколько базовых команд, но стоят дороже — свой код экономит энергию.");
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
        AddSection("Конструкции языка",
            "Списывается каждый раз при выполнении: условие — при каждой проверке, цикл — на каждой итерации. " +
            "Закрытые (locked) конструкции открываются по ходу игры — код с ними не запустится.");

        foreach (LuaConstructs.Info info in LuaConstructs.All)
        {
            bool locked = unlocks != null && !unlocks.IsConstructUnlocked(info.Id);
            AddConstructRows(info, energyConfig, locked);
        }

        AddKeyword("return", energyConfig != null ? energyConfig.returnCost : (float?)null, "за выполнение", false);
    }

    private void AddConstructRows(LuaConstructs.Info info, EnergyConfig config, bool locked)
    {
        switch (info.Id)
        {
            case LuaConstructs.Local:
                AddKeyword("local", config?.localCost, "за выполнение", locked);
                break;
            case LuaConstructs.If:
                AddKeyword("if / elseif", config?.ifCost, "за проверку условия", locked);
                AddKeyword("else", config?.elseCost, "за вход в ветку", locked);
                break;
            case LuaConstructs.For:
                AddKeyword("for", config?.forCost, "за итерацию", locked);
                break;
            case LuaConstructs.While:
                AddKeyword("while", config?.whileCost, "за проверку условия", locked);
                break;
            case LuaConstructs.Repeat:
                AddKeyword("repeat … until", config?.repeatCost, "за проверку условия", locked);
                break;
            case LuaConstructs.Function:
                AddKeyword("function", config?.functionCost, "за вызов", locked);
                break;
            case LuaConstructs.Classes:
                // Доп. окно — глобальная функция с именем заголовка: платится как вызов функции
                AddKeyword("окна-классы (+)", config?.functionCost, "за вызов окна", locked);
                break;
            default:
                // Новая конструкция без описанной цены — показываем хотя бы имя
                AddKeyword(info.DisplayName, null, string.Empty, locked);
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
            command.Description);
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

        Label cost = new Label($"{Format(energyCost)} эн.");
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

    private void AddKeyword(string keyword, float? cost, string when, bool locked)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("help-keyword-row");
        row.EnableInClassList("help-keyword-row--locked", locked);

        Label name = new Label(keyword) { enableRichText = false };
        name.AddToClassList("help-keyword");
        row.Add(name);

        string priceText = locked
            ? "locked"
            : cost.HasValue ? $"{Format(cost.Value)} эн. {when}" : when;
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
