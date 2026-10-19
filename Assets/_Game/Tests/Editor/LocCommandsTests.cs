using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Локализация текстов Lua-API: описания всех команд, методов и полей списков — английский исходник
/// без кириллицы, и у каждого есть русский перевод в таблице Loc (LocRuCommands).
/// </summary>
public class LocCommandsTests
{
    private static readonly Regex Cyrillic = new Regex("[Ѐ-ӿ]");

    private TestHelpers.TempSaveFolder _saves;
    private UnlockConfig _config;
    private GameObject _host;
    private LuaSandbox _sandbox;

    [SetUp]
    public void SetUp()
    {
        _saves = new TestHelpers.TempSaveFolder();
        _config = ScriptableObject.CreateInstance<UnlockConfig>();
        _config.unlockAll = true;
        var unlocks = new UnlockRegistry(_config, _saves.Progress());

        // Песочница без мира: команды создаются со всеми зависимостями null, описания от них не зависят
        _host = new GameObject("LocCommandsTest");
        _sandbox = _host.AddComponent<LuaSandbox>();
        _sandbox.Initialize(null, null, null, TestHelpers.ProjectEnergyConfig(), null, new PlayerEnergy(100f), null, null,
            null, null, 1f, null, unlocks, null, null);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_host);
        Object.DestroyImmediate(_config);
        _saves.Dispose();
    }

    [Test]
    public void AllCommands_DescriptionIsEnglishAndTranslated()
    {
        int count = 0;
        foreach (ILuaCommand command in _sandbox.AllCommands)
        {
            count++;
            Assert.IsFalse(string.IsNullOrEmpty(command.Description), $"command '{command.Name}' has no description");
            Assert.IsFalse(Cyrillic.IsMatch(command.Description),
                $"description of '{command.Name}' must be English (source language): {command.Description}");
            Assert.IsTrue(Loc.HasTranslation(command.Description),
                $"no Russian translation (LocRuCommands) for the description of '{command.Name}': {command.Description}");
        }
        Assert.Greater(count, 0, "sandbox has no commands");
    }

    [Test]
    public void ObjectListMethods_DescriptionIsEnglishAndTranslated()
    {
        var texts = new List<string> { LuaObjectList.Fields };
        foreach (var method in LuaObjectList.Methods) texts.Add(method.Description);
        AssertEnglishAndTranslated(texts);
    }

    [Test]
    public void CompletionTexts_AreEnglishAndTranslated()
    {
        var texts = new List<string>(LuaCompletionProvider.FieldDescriptions)
        {
            LuaCompletionProvider.WindowFunctionDescription
        };
        AssertEnglishAndTranslated(texts);
    }

    [Test]
    public void Russian_TranslatesCommandDescription()
    {
        ILuaCommand move = null;
        foreach (ILuaCommand command in _sandbox.AllCommands)
            if (command.Name == "move") move = command;
        Assert.IsNotNull(move);

        GameLanguage previous = Loc.Language;
        try
        {
            Loc.SetLanguage(GameLanguage.Russian);
            StringAssert.StartsWith("Идти в направлении", Loc.T(move.Description));
            Loc.SetLanguage(GameLanguage.English);
            Assert.AreEqual(move.Description, Loc.T(move.Description));
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    private static void AssertEnglishAndTranslated(IEnumerable<string> texts)
    {
        foreach (string text in texts)
        {
            Assert.IsFalse(Cyrillic.IsMatch(text), $"text must be English (source language): {text}");
            Assert.IsTrue(Loc.HasTranslation(text), $"no Russian translation (LocRuCommands) for: {text}");
        }
    }
}
