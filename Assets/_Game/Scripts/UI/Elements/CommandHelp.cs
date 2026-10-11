using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;

/// <summary>
/// Справка по командам (кнопка Help на верхней панели). Содержимое строится из реестра
/// команд и EnergyConfig, поэтому новые команды и цены появляются в ней автоматически.
/// </summary>
public class CommandHelp
{
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

    public void Build(IEnumerable<ILuaCommand> commands, EnergyConfig energyConfig)
    {
        _content.Clear();

        List<ILuaCommand> all = commands.OrderBy(c => c.Name).ToList();

        AddSection("Команды",
            "Цена списывается при каждом вызове. Добыча и крафт дополнительно тратят энергию по ресурсу и рецепту.");
        foreach (ILuaCommand command in all.Where(c => !c.IsReadyMade))
            _content.Add(CreateCommandEntry(command));

        AddSection("Массив findObjects",
            "Методы вызываются через двоеточие: list:getNear(). where, sortBy и take возвращают новый массив — " +
            $"их можно выстраивать в цепочку. Поля объекта: {LuaObjectList.Fields}. Собранные объекты пропускаются.");
        foreach (var method in LuaObjectList.Methods)
            _content.Add(CreateEntry(method.Signature, LuaObjectList.MethodCost, method.Description));

        List<ILuaCommand> readyMade = all.Where(c => c.IsReadyMade).ToList();
        if (readyMade.Count > 0)
        {
            AddSection("Готовые команды",
                "Делают то же, что несколько базовых команд, но стоят дороже — свой код экономит энергию.");
            foreach (ILuaCommand command in readyMade)
                _content.Add(CreateCommandEntry(command));
        }

        if (energyConfig != null)
        {
            AddSection("Конструкции языка",
                "Списывается каждый раз при выполнении: условие — при каждой проверке, цикл — на каждой итерации.");
            AddKeyword("if / elseif", energyConfig.ifCost, "за проверку условия");
            AddKeyword("else", energyConfig.elseCost, "за вход в ветку");
            AddKeyword("for", energyConfig.forCost, "за итерацию");
            AddKeyword("while", energyConfig.whileCost, "за проверку условия");
            AddKeyword("repeat … until", energyConfig.repeatCost, "за проверку условия");
            AddKeyword("function", energyConfig.functionCost, "за вызов");
            AddKeyword("local", energyConfig.localCost, "за выполнение");
            AddKeyword("return", energyConfig.returnCost, "за выполнение");
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void SetOpen(bool open)
    {
        _popup.EnableInClassList("hidden", !open);
        _toggleButton.EnableInClassList("btn-help--active", open);
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

    private void AddKeyword(string keyword, float cost, string when)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("help-keyword-row");

        Label name = new Label(keyword);
        name.AddToClassList("help-keyword");
        row.Add(name);

        Label price = new Label($"{Format(cost)} эн. {when}");
        price.AddToClassList("help-cost");
        row.Add(price);

        _content.Add(row);
    }

    private static string Format(float value)
    {
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
