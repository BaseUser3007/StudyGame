using UnityEngine.UIElements;

/// <summary>
/// Перевод статических текстов экземпляра UXML-шаблона: text у Label/Button/…, tooltip, label у полей.
/// Исходник — английский текст из UXML, перевод — Loc.T. Вызывать сразу после Instantiate().
/// Не трогает: поля ввода (код, заголовок окна), заголовки окон кода (имена Lua-функций, класс
/// <see cref="SkipClass"/> и window-title), тексты без букв (числа, «×», «‹»), однобуквенные («X»).
/// Динамические тексты C# ставит сам — через Loc.T/Loc.F.
/// </summary>
public static class LocUxml
{
    /// <summary>Элемент с этим классом (и всё внутри) не переводится.</summary>
    public const string SkipClass = "loc-skip";

    private const string WindowTitleClass = "window-title";

    // ─── Public API ───────────────────────────────────────────

    public static void Translate(VisualElement root)
    {
        if (root == null) return;
        Visit(root);
    }

    /// <summary>Нужно ли переводить статический текст (то же правило использует тест покрытия UXML).</summary>
    public static bool IsTranslatable(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        string trimmed = text.Trim();
        if (trimmed.Length < 2) return false;

        foreach (char c in trimmed)
            if (char.IsLetter(c)) return true;
        return false;
    }

    // ─── Обход ────────────────────────────────────────────────

    private static void Visit(VisualElement element)
    {
        if (element.ClassListContains(SkipClass)) return;

        if (!string.IsNullOrEmpty(element.tooltip))
            element.tooltip = TranslateText(element.tooltip);

        // Поле ввода: переводится только подпись, содержимое (код, заголовок окна) — никогда
        if (element is TextField textField)
        {
            if (IsTranslatable(textField.label))
                textField.label = Loc.T(textField.label);
            return;
        }

        if (element is TextElement textElement && !element.ClassListContains(WindowTitleClass))
            textElement.text = TranslateText(textElement.text);

        foreach (VisualElement child in element.Children())
            Visit(child);
    }

    private static string TranslateText(string text) => IsTranslatable(text) ? Loc.T(text) : text;
}
