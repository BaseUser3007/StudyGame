/// <summary>Русская таблица задачи T14 (см. Docs/Tasks): уровни построек лагеря.</summary>
public static class LocRuT14
{
    public static readonly (string En, string Ru)[] Entries =
    {
        // HubBuildPanel — карточка построенной постройки
        ("Upgrade", "Улучшить"),
        ("Level {0}/{1}", "Уровень {0}/{1}"),
        ("Up to level {0}", "До уровня {0}"),
        ("Level {0} adds:", "Уровень {0} даёт:"),
        ("Built · maximum level", "Построено · максимальный уровень"),
        ("{0} upgraded to level {1}", "{0}: улучшено до уровня {1}"),
        // HubService.Describe(HubUpgradeResult)
        ("Ready to upgrade", "Можно улучшить"),
        ("Build it first", "Сначала постройте"),
        ("Maximum level", "Максимальный уровень"),
        ("Not enough materials to upgrade", "Не хватает материалов для улучшения"),
    };
}
