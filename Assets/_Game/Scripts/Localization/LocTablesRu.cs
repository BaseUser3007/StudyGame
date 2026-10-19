/// <summary>
/// Разделы русской таблицы Loc. Каждый раздел — отдельный файл со статическим классом и полем Entries
/// (пары «английский исходник → русский»), чтобы тексты разных частей игры правились независимо.
/// Ключ — английский текст ровно как в коде/UXML/ассете (с {0}-плейсхолдерами для Loc.F).
/// </summary>
public static class LocTablesRu
{
    public static readonly (string En, string Ru)[][] Sections =
    {
        LocRuUi.Entries,
        LocRuCommands.Entries,
        LocRuContent.Entries,
    };
}
