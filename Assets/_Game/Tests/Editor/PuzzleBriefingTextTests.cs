using NUnit.Framework;

/// <summary>
/// Тексты UI начатой головоломки (T25, часть C): индикатор в верхней панели по состоянию и строка лога.
/// </summary>
public class PuzzleBriefingTextTests
{
    [SetUp]
    public void UseEnglish() => Loc.SetLanguage(GameLanguage.English);

    [TearDown]
    public void RestoreLanguage() => Loc.SetLanguage(GameLanguage.English);

    [Test]
    public void FormatStatus_ShownOnlyWhileStarted()
    {
        Assert.AreEqual("Puzzle: the next run is your attempt", PuzzleBriefingPopup.FormatStatus(PuzzleState.Armed));
        Assert.AreEqual("Puzzle attempt in progress", PuzzleBriefingPopup.FormatStatus(PuzzleState.Attempt));
        Assert.IsNull(PuzzleBriefingPopup.FormatStatus(PuzzleState.Waiting));
        Assert.IsNull(PuzzleBriefingPopup.FormatStatus(PuzzleState.Solved));
        Assert.IsNull(PuzzleBriefingPopup.FormatStatus(PuzzleState.Failed));
    }

    [Test]
    public void FormatLogLine_JoinsTitleAndRule()
    {
        Assert.AreEqual(
            "Puzzle started: Obelisk. " + PuzzleManager.AttemptRuleText,
            PuzzleBriefingPopup.FormatLogLine("Obelisk", PuzzleManager.AttemptRuleText));
    }

    [Test]
    public void Texts_AreTranslatedToRussian()
    {
        Loc.SetLanguage(GameLanguage.Russian);
        Assert.AreEqual("Головоломка: попытка — следующий запуск", PuzzleBriefingPopup.FormatStatus(PuzzleState.Armed));
        Assert.AreEqual("Идёт попытка решить головоломку", PuzzleBriefingPopup.FormatStatus(PuzzleState.Attempt));
        StringAssert.StartsWith("Головоломка начата: Обелиск. ",
            PuzzleBriefingPopup.FormatLogLine("Обелиск", PuzzleManager.AttemptRuleText));
    }
}
