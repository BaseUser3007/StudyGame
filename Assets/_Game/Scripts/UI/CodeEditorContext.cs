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

    public CodeEditorContext(
        CodeEditorTheme theme,
        LuaSyntaxHighlighter highlighter,
        LuaCompletionProvider completion,
        Func<string, Dictionary<int, float>> lineCosts)
    {
        Theme = theme;
        Highlighter = highlighter;
        Completion = completion;
        LineCosts = lineCosts;
    }
}
