/// <summary>Русская таблица задачи T15 (см. Docs/Tasks): бонусы за соседство построек лагеря.</summary>
public static class LocRuT15
{
    public static readonly (string En, string Ru)[] Entries =
    {
        // HubAdjacencyMatch.Describe / карточка HubBuildPanel: «+5 energy limit (next to Shrine)».
        // «сосед:» — чтобы не склонять название постройки после «рядом с»
        ("{0} (next to {1})", "{0} (сосед: {1})"),
        // HubBuildPanel — карточка постройки
        ("Neighbour bonuses:", "Бонусы за соседство:"),
        ("{0} · active", "{0} · действует"),
        // HubBuildPanel — подсказка при размещении
        ("No neighbour bonus here", "Здесь нет бонуса за соседство"),
    };
}
