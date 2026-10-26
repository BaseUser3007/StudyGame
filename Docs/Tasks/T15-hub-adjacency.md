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
- [x] Бонус появляется только у касающихся сторонами построек, учитывается в забеге.
- [ ] Призрак показывает будущий бонус до клика. (код готов; проверить в Play Mode)
- [x] Тесты: соседство по сторонам/диагонали, несколько соседей, отсутствие каталога.

Правило: каждая запись `adjacencyBonuses` считается один раз, сколько бы подходящих соседей ни было
(в срезе каждая постройка стоит в одном экземпляре); от уровня постройки бонус не зависит.

Предложенные пары (в ассетах, числа — на утверждение автора):

| Постройка (владелец) | Сосед | Бонус |
|---|---|---|
| `healer_hut` | `shrine` | +10 к макс. здоровью |
| `shrine` | `healer_hut` | +5 к запасу энергии |
| `storage` | `watchtower` | +1 слот инвентаря |
| `watchtower` | `shrine` | +5 к запасу энергии |
