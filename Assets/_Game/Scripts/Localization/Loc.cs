using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Язык интерфейса.</summary>
public enum GameLanguage
{
    English,
    Russian
}

/// <summary>
/// Локализация интерфейса (RU / EN). Исходный язык — английский: тексты остаются в коде, UXML и ассетах
/// как есть, а русский перевод — таблица «английский текст → русский» (gettext-подход), собранная из
/// разделов LocTablesRu.*. Loc.T("Run") на русском вернёт «Запуск», на английском — исходный текст.
/// Нет перевода — возвращается английский (в редакторе один раз пишется предупреждение).
/// Строки Lua-API (ошибки, возвращаемые командами, имена типов, поля таблиц) НЕ переводятся:
/// игрок сравнивает их в коде, и код должен работать одинаково на обоих языках.
/// Язык хранится в MetaProgress.Language; смена — через SetLanguage + перезагрузка сцены (GameSession).
/// </summary>
public static class Loc
{
    public const string EnglishCode = "en";
    public const string RussianCode = "ru";

    /// <summary>Язык сменился (UI, живущий между сценами, может обновиться; сцена обычно перезагружается).</summary>
    public static event Action OnLanguageChanged;

    public static GameLanguage Language { get; private set; } = GameLanguage.English;
    public static bool IsRussian => Language == GameLanguage.Russian;

    private static Dictionary<string, string> _russian;
    private static HashSet<string> _reportedMissing;

    // ─── Public API ───────────────────────────────────────────

    /// <summary>Перевод английского текста на текущий язык.</summary>
    public static string T(string english)
    {
        if (string.IsNullOrEmpty(english) || Language == GameLanguage.English) return english;

        if (Russian.TryGetValue(english, out string russian)) return russian;

        ReportMissing(english);
        return english;
    }

    /// <summary>Перевод шаблона и string.Format: Loc.F("Day {0}", day). Шаблон в таблице — с {0}, {1}…</summary>
    public static string F(string englishFormat, params object[] args)
    {
        string format = T(englishFormat);
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            // Битый перевод не должен ронять UI — откатываемся на английский шаблон
            Debug.LogWarning($"Loc: bad format in translation of \"{englishFormat}\"");
            return string.Format(englishFormat, args);
        }
    }

    /// <summary>Есть ли перевод (для тестов и проверки покрытия).</summary>
    public static bool HasTranslation(string english) =>
        !string.IsNullOrEmpty(english) && Russian.ContainsKey(english);

    /// <summary>Все английские ключи русской таблицы (для тестов).</summary>
    public static IEnumerable<string> RussianKeys => Russian.Keys;

    public static void SetLanguage(GameLanguage language)
    {
        if (Language == language) return;
        Language = language;
        OnLanguageChanged?.Invoke();
    }

    /// <summary>Язык из сохранения; пустое значение — по языку системы (первый запуск).</summary>
    public static void LoadFrom(MetaProgress progress)
    {
        string code = progress != null ? progress.Language : "";
        if (string.IsNullOrEmpty(code))
            code = Application.systemLanguage == SystemLanguage.Russian ? RussianCode : EnglishCode;
        SetLanguage(FromCode(code));
    }

    /// <summary>Сохранить выбор (сам Save делает вызывающий).</summary>
    public static void SaveTo(MetaProgress progress)
    {
        if (progress != null) progress.Language = ToCode(Language);
    }

    public static string ToCode(GameLanguage language) =>
        language == GameLanguage.Russian ? RussianCode : EnglishCode;

    public static GameLanguage FromCode(string code) =>
        string.Equals(code, RussianCode, StringComparison.OrdinalIgnoreCase)
            ? GameLanguage.Russian
            : GameLanguage.English;

    // ─── Таблица ──────────────────────────────────────────────

    private static Dictionary<string, string> Russian => _russian ??= BuildRussian();

    /// <summary>
    /// Собирает таблицу из разделов. Новый раздел — статический класс с полем
    /// <c>public static readonly (string En, string Ru)[] Entries</c>, добавить его сюда.
    /// Дубли ключей с разным переводом — предупреждение, побеждает первый.
    /// </summary>
    private static Dictionary<string, string> BuildRussian()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string En, string Ru)[] section in LocTablesRu.Sections)
        {
            if (section == null) continue;
            foreach ((string en, string ru) in section)
            {
                if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(ru)) continue;
                if (table.TryGetValue(en, out string existing))
                {
                    if (existing != ru) Debug.LogWarning($"Loc: duplicate key with different translation: \"{en}\"");
                    continue;
                }
                table[en] = ru;
            }
        }
        return table;
    }

    private static void ReportMissing(string english)
    {
#if UNITY_EDITOR
        _reportedMissing ??= new HashSet<string>(StringComparer.Ordinal);
        if (_reportedMissing.Add(english))
            Debug.LogWarning($"Loc: no Russian translation for \"{english}\"");
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnLanguageChanged = null;
        _reportedMissing = null;
        // Язык и таблица переживают перезагрузку сцены; сбрасываются только при входе в Play Mode
        Language = GameLanguage.English;
        _russian = null;
    }
}
