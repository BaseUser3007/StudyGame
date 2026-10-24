# T15. Бонусы за соседство построек

**Область:** Хаб · **Размер:** M · **Не брать одновременно с:** T14, T19

## Зачем
Как в Loop Hero: расстановка в лагере — маленькая головоломка. Сейчас клетка не важна.

## Что сделать
- `HubBuildingDefinition`: список `adjacencyBonuses` { `neighbourId`, эффект (`HubEffectType` + amount) } —
  бонус действует, если у постройки есть сосед с этим id (футпринты касаются стороной; по диагонали — нет).
- `HubService.GetAdjacencyBonuses()` (чистая логика, тестируемая) и учёт в `RunModifiers.Create(config, progress, catalog)`.
- UI: при размещении (призрак за мышью, `HubPlacementController` / `HubGrid.ShowGhost`) — подсветка соседей, которые дадут
  бонус, и подпись «+1 energy limit (next to Shrine)»; в карточке постройки — список возможных бонусов.
- Предложите 3–4 пары для существующих построек (`storage`, `shrine`, `watchtower`, `healer_hut`) таблицей в PR; числа утвердит автор.

## Файлы
- `Scripts/Hub/HubBuildingDefinition.cs`, `HubService.cs`, `HubGrid.cs`, `HubPlacementController.cs`, `Scripts/Core/RunModifiers.cs`
- `Scripts/UI/Elements/HubBuildPanel.cs`; ассеты `ScriptableObjects/Hub/*.asset`
- `Tests/Editor/HubServiceTests.cs`; `Scripts/Localization/LocRuT15.cs`

## Готово, когда
- [ ] Бонус появляется только у касающихся сторонами построек, учитывается в забеге.
- [ ] Призрак показывает будущий бонус до клика.
- [ ] Тесты: соседство по сторонам/диагонали, несколько соседей, отсутствие каталога.
