using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

/// <summary>
/// Русская таблица Loc: ключи раздела UI уникальны и непусты, шаблоны Loc.F совпадают по {N},
/// статические тексты UXML, гайды открытий и туториал переведены, разделы не спорят о переводе.
/// </summary>
public class LocalizationTests
{
    private const string GuiFolder = "Assets/_Game/Scripts/UI/GUI";
    private static readonly Regex Placeholder = new Regex(@"\{\d+\}");

    [TearDown]
    public void RestoreLanguage() => Loc.SetLanguage(GameLanguage.English);

    // ─── Таблица ──────────────────────────────────────────────

    [Test]
    public void UiKeys_AreUniqueAndNotEmpty()
    {
        var seen = new HashSet<string>();
        foreach ((string en, string ru) in LocRuUi.Entries)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(en), "empty key in LocRuUi");
            Assert.IsFalse(string.IsNullOrWhiteSpace(ru), $"empty translation for \"{en}\"");
            Assert.IsTrue(seen.Add(en), $"duplicate key in LocRuUi: \"{en}\"");
        }
    }

    [Test]
    public void Translations_KeepFormatPlaceholders()
    {
        foreach ((string En, string Ru)[] section in LocTablesRu.Sections)
        {
            foreach ((string en, string ru) in section)
            {
                if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(ru)) continue;
                CollectionAssert.AreEquivalent(Placeholders(en), Placeholders(ru),
                    $"placeholders differ: \"{en}\" → \"{ru}\"");
                if (en.Contains("{runs}"))
                    StringAssert.Contains("{runs}", ru, $"{{runs}} lost in \"{ru}\"");
            }
        }
    }

    [Test]
    public void Sections_AgreeOnSharedKeys()
    {
        var table = new Dictionary<string, string>();
        foreach ((string En, string Ru)[] section in LocTablesRu.Sections)
        {
            foreach ((string en, string ru) in section)
            {
                if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(ru)) continue;
                if (table.TryGetValue(en, out string existing))
                    Assert.AreEqual(existing, ru, $"sections translate \"{en}\" differently");
                else
                    table[en] = ru;
            }
        }
    }

    [Test]
    public void Format_WorksInRussian()
    {
        Loc.SetLanguage(GameLanguage.Russian);
        Assert.AreEqual("День 2 · Запуски 1/3", Loc.F("Day {0} · Runs {1}/{2}", 2, 1, 3));
        // Нет перевода — английский текст как есть
        Assert.AreEqual("no such key 42", Loc.F("no such key {0}", 42));
    }

    // ─── UXML ─────────────────────────────────────────────────

    [Test]
    public void UxmlStaticTexts_HaveTranslations()
    {
        string[] files = Directory.GetFiles(GuiFolder, "*.uxml");
        Assert.IsNotEmpty(files, "no UXML found");

        var missing = new List<string>();
        foreach (string file in files)
            Collect(XDocument.Load(file).Root, Path.GetFileName(file), false, missing);

        Assert.IsEmpty(missing, "no Russian translation:\n" + string.Join("\n", missing));
    }

    /// <summary>Те же правила, что у LocUxml.Translate: поля ввода, заголовки окон и loc-skip не переводятся.</summary>
    private static void Collect(XElement element, string file, bool skip, List<string> missing)
    {
        string[] classes = ((string)element.Attribute("class") ?? string.Empty).Split(' ');
        skip = skip || classes.Contains(LocUxml.SkipClass);
        string tag = element.Name.LocalName;

        if (!skip)
        {
            Check((string)element.Attribute("tooltip"), file, missing);
            if (tag == "TextField")
                Check((string)element.Attribute("label"), file, missing);
            else if (!classes.Contains("window-title"))
                Check((string)element.Attribute("text"), file, missing);
        }

        foreach (XElement child in element.Elements())
            Collect(child, file, skip, missing);
    }

    private static void Check(string text, string file, List<string> missing)
    {
        if (LocUxml.IsTranslatable(text) && !Loc.HasTranslation(text))
            missing.Add($"{file}: \"{text}\"");
    }

    // ─── Тексты C# ────────────────────────────────────────────

    [Test]
    public void UnlockGuides_HaveTranslations()
    {
        var texts = new List<string>();
        foreach (LuaConstructs.Info info in LuaConstructs.All)
        {
            texts.Add(info.DisplayName);
            if (!LuaConstructGuide.TryGet(info.Id, out LuaConstructGuide.Entry entry)) continue;
            texts.Add(entry.Pain);
            texts.Add(entry.Explanation);
            texts.Add(entry.EnergyNote);
        }

        foreach (string command in new[] { "mine", "print", "attack" })
        {
            Assert.IsTrue(LuaCommandGuide.TryGet(command, out LuaCommandGuide.Entry entry), command);
            texts.Add(entry.Pain);
            texts.Add(entry.Explanation);
            texts.Add(entry.EnergyNote);
        }

        foreach (string readyMade in new[] { "obtain", "autoExplore" })
        {
            Assert.IsTrue(LuaDecompileGuide.TryGetByReadyMade(readyMade, out LuaDecompileGuide.Entry entry), readyMade);
            texts.Add(entry.Pain);
            texts.Add(entry.Explanation);
            texts.Add(entry.EnergyNote);
        }

        AssertTranslated(texts);
    }

    [Test]
    public void Tutorial_HasTranslations()
    {
        var texts = new List<string>();
        foreach (TutorialStep step in TutorialSteps.Steps)
        {
            texts.Add(step.Text);
            texts.Add(step.Waiting);
        }
        foreach (TutorialConcept concept in TutorialSteps.Concepts)
        {
            texts.Add(concept.Title);
            texts.Add(concept.Text);
        }

        AssertTranslated(texts);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static void AssertTranslated(IEnumerable<string> texts)
    {
        List<string> missing = texts
            .Where(t => !string.IsNullOrEmpty(t) && !Loc.HasTranslation(t))
            .Distinct()
            .ToList();
        Assert.IsEmpty(missing, "no Russian translation:\n" + string.Join("\n", missing));
    }

    private static List<string> Placeholders(string text) =>
        Placeholder.Matches(text).Cast<Match>().Select(m => m.Value).Distinct().ToList();
}
