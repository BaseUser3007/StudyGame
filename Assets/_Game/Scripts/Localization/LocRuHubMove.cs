/// <summary>Русская таблица: портал лагеря (постоянная постройка) и перенос построек.</summary>
public static class LocRuHubMove
{
    public static readonly (string En, string Ru)[] Entries =
    {
        // HubBuildPanel — карточка построенной и подсказка переноса
        ("Move", "Перенести"),
        ("Moving: {0}", "Перенос: {0}"),
        ("Click a cell to move · Right click / Esc to cancel", "Клик по клетке — перенести · ПКМ / Esc — отмена"),
        ("{0} moved", "{0}: перенесено"),
        // HubService.Describe(HubMoveResult / HubBuildResult.Permanent)
        ("Ready to move", "Можно перенести"),
        ("Always in the camp — it can only be moved", "Всегда стоит в лагере — его можно только перенести"),
        // Ассет постройки портала (ScriptableObjects/Hub/Portal.asset): название и описание
        ("Portal", "Портал"),
        ("Portal — the way from the camp into a new run. It is always in the camp: it can be moved, but not built or upgraded.",
            "Портал — путь из лагеря в новый забег. Всегда стоит в лагере: его можно перенести, но не построить и не улучшить."),
    };
}
