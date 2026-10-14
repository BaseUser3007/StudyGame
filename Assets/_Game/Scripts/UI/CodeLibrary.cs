using System;
using System.Collections.Generic;

/// <summary>
/// Сохранённый фрагмент кода: окно, перетащенное в библиотеку.
/// </summary>
[Serializable]
public class CodeSnippet
{
    public string title;
    public string code;
    public string savedAt;
}

/// <summary>
/// Библиотека сохранённого кода. Хранится в JSON-файле в Application.persistentDataPath
/// и переживает перезапуск игры.
/// </summary>
public class CodeLibrary
{
    private const string FileName = "code_library.json";

    [Serializable]
    private class LibraryData
    {
        public List<CodeSnippet> snippets = new List<CodeSnippet>();
    }

    private readonly string _filePath;
    private LibraryData _data = new LibraryData();

    public event Action OnChanged;

    public IReadOnlyList<CodeSnippet> Snippets => _data.snippets;
    public string FilePath => _filePath;

    public CodeLibrary()
    {
        _filePath = JsonFileStore.GetPath(FileName);
        Load();
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Сохраняет код под названием окна. Фрагмент с тем же названием перезаписывается.
    /// Возвращает true, если фрагмент обновлён, false — если добавлен новый.
    /// </summary>
    public bool Save(string title, string code)
    {
        CodeSnippet existing = Find(title);
        bool updated = existing != null;

        if (!updated)
        {
            existing = new CodeSnippet { title = title };
            _data.snippets.Add(existing);
        }

        existing.code = code;
        existing.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        WriteFile();
        OnChanged?.Invoke();
        return updated;
    }

    public void Remove(CodeSnippet snippet)
    {
        if (!_data.snippets.Remove(snippet)) return;
        WriteFile();
        OnChanged?.Invoke();
    }

    public CodeSnippet Find(string title)
    {
        foreach (CodeSnippet snippet in _data.snippets)
            if (snippet.title == title) return snippet;
        return null;
    }

    // ─── File ─────────────────────────────────────────────────

    private void Load()
    {
        if (!JsonFileStore.TryLoad(_filePath, out LibraryData data) || data.snippets == null) return;

        _data = data;
        _data.snippets.RemoveAll(s => s == null || string.IsNullOrEmpty(s.title));
    }

    private void WriteFile() => JsonFileStore.Write(_filePath, _data);
}
