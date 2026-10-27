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
        // Разделы задач Docs/Tasks — каждая задача добавляет свои строки только в свой файл
        LocRuT01.Entries,
        LocRuT03.Entries,
        LocRuT06.Entries,
        LocRuT02.Entries,
        LocRuT05.Entries,
        LocRuT10.Entries,
        LocRuT22.Entries,
        LocRuT04.Entries,
        LocRuT14.Entries,
        LocRuT23.Entries,
        LocRuT15.Entries,
        LocRuT09.Entries,
        LocRuT13.Entries,
        LocRuT16.Entries,
        LocRuEvents.Entries,
        LocRuHubMove.Entries,
        LocRuT24.Entries,
        LocRuT25.Entries,
    };
}
