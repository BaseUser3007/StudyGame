using System;
using System.Collections.Generic;

/// <summary>
/// Общие для всех окон редактора сервисы: тема, подсветка,
/// автодополнение и подсчёт стоимости строк.
/// </summary>
public class CodeEditorContext
{
    public CodeEditorTheme Theme { get; }
    public LuaSyntaxHighlighter Highlighter { get; }
    public LuaCompletionProvider Completion { get; }
    public Func<string, Dictionary<int, float>> LineCosts { get; }
    /// <summary>
    /// Ключевое слово относится к закрытой конструкции языка — его не предлагает автодополнение.
    /// Читается на каждом запросе, поэтому открытия применяются сразу.
    /// </summary>
    public Func<string, bool> IsKeywordLocked { get; }

    public CodeEditorContext(
        CodeEditorTheme theme,
        LuaSyntaxHighlighter highlighter,
        LuaCompletionProvider completion,
        Func<string, Dictionary<int, float>> lineCosts,
        Func<string, bool> isKeywordLocked)
    {
        Theme = theme;
        Highlighter = highlighter;
        Completion = completion;
        LineCosts = lineCosts;
        IsKeywordLocked = isKeywordLocked;
    }
}
