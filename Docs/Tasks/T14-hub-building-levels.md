# T14. Уровни построек хаба

**Область:** Хаб · **Размер:** M · **Не брать одновременно с:** T15, T19

## Что сделать
Построенную постройку можно улучшить до уровня 2 и 3 за материалы со склада.
- `HubBuildingDefinition`: список уровней `levels` (стоимость + эффекты уровня; уровень 1 = текущие `cost`/`effects`,
  старые ассеты без `levels` остаются одноуровневыми).
- `PlacedHubBuilding` в `MetaProgress`: новое поле `level` (по умолчанию 1 — старые сохранения читаются без изменений).
- `HubService`: `CanUpgrade`, `TryUpgrade(def)` (списать материалы, `Save`, событие `OnUpgraded`, `GameEventBus` — новое значение
  `HubBuildingUpgraded` в **конец** enum).
- `RunModifiers.Create(config, progress, catalog)` — эффекты берутся с текущего уровня.
- UI: на карточке построенной постройки в `HubBuildPanel` кнопка **Upgrade** (стоимость следующего уровня чипами, как у постройки);
  на сетке — значок уровня у постройки.
- Числа уровней для 4 построек предложите в PR (таблица), автор их утвердит.

## Файлы
- `Scripts/Hub/HubBuildingDefinition.cs`, `HubService.cs`, `HubGrid.cs` (значок), `Scripts/Core/RunModifiers.cs`
- `Scripts/Core/MetaProgress.cs` (горячий — только поле), `Scripts/Core/GameEventBus.cs` (горячий — значение в конец)
- `Scripts/UI/Elements/HubBuildPanel.cs` + USS; ассеты `ScriptableObjects/Hub/*.asset`
- `Tests/Editor/HubServiceTests.cs` — новые тесты; `Scripts/Localization/LocRuT14.cs`

## Готово, когда
- [x] Старое сохранение с постройками загружается, всё уровня 1.
- [ ] Улучшение списывает материалы, переживает перезапуск, эффект нового уровня виден в следующем забеге.
- [ ] На максимальном уровне кнопки нет; нехватка материалов — понятная причина (как у постройки).
- [x] Тесты: стоимость, недостаток материалов, сохранение, эффекты уровня в `RunModifiers`.
