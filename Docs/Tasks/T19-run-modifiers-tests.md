# T19. EditMode-тесты эффектов построек на забег

**Область:** Тесты · **Размер:** S · **Не брать одновременно с:** T14, T15 (меняют `RunModifiers`)

## Что сделать
`Tests/Editor/RunModifiersTests.cs` для `RunModifiers.Create(config, progress, catalog)`:
- без каталога / без построек — базовые значения из `PlayerConfig`;
- каждая из `HubEffectType` (EnergyLimit, ThreatDays, InventorySlots, MaxHealth, RunsPerDay) складывается правильно, несколько построек — сумма;
- постройка в сохранении, которой нет в каталоге, — игнорируется без исключения;
- `AddEnergyLimit` / `AddMaxHealth` / `DelayThreat` (награды головоломок) — действуют до конца забега;
- стартовые предметы — из `PlayerConfig.startingItems`.

Образцы — `HubServiceTests.cs`, `MetaProgressTests.cs` (как создать `MetaProgress` в памяти без файла).

## Файлы
- новый `Tests/Editor/RunModifiersTests.cs`

## Готово, когда
- [ ] Все случаи выше покрыты, тесты зелёные.
