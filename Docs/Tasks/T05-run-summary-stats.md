# T05. Статистика на экране итогов забега

**Область:** UI / мета · **Размер:** S · **Не брать одновременно с:** —

## Что сделать
Экран итогов (`RunScreens`, данные — `RunSummary`) сейчас показывает дни, перенесённое/оставленное и достижения.
Добавить блок статистики забега: запусков кода, энергии потрачено всего, ошибок в коде, врагов побеждено,
головоломок решено, предметов скрафчено.

Считать по `GameEventBus` (`RunFinished` с тегом, `EnemyKilled`, `PuzzleSolved`, `Crafted`) и
`CodeRunReport.EnergySpent` — отдельным маленьким классом-счётчиком (например `RunStats`, plain class, `IDisposable`,
подписка/отписка именованными методами), который `RunController` читает при сборке `RunSummary`.

## Файлы
- новый `Scripts/Core/RunStats.cs`
- `Scripts/Core/RunController.cs`, `Scripts/UI/Elements/RunScreens.cs` (класс `RunSummary` + отрисовка), `UI/GUI/RunScreens.uss`
- `Scripts/Core/Level.cs` (горячий) — создать `RunStats` и передать в `RunController`, `Dispose` в `OnDestroy`
- `LocRuT05.cs`

## Готово, когда
- [ ] Цифры верные после забега с ошибками, боем и крафтом (сверить вручную).
- [ ] В поражении статистика тоже показывается.
- [ ] Нет утечек подписок при перезагрузке сцены (статические шины!).
- [ ] EditMode-тест на `RunStats`: события → счётчики.
