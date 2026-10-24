# T16. Головоломка «плиты по порядку»

**Область:** Головоломки · **Размер:** M · **Не брать одновременно с:** —

## Идея
Площадка 5×5, на ней 4–5 плит с номерами, но номера видны **только** через `check()` на соседнем тайле
(`check(x, y).plate` → число). Нужно наступить на плиты по возрастанию номеров. Неправильная плита — все сбрасываются.
Учит: собрать данные в таблицу, отсортировать (`table.sort` или свой цикл), обойти по порядку.

## Что сделать
- Тип `PuzzleTypes.Plates` = `"plates"` (добавить в `PuzzleTypes.All` — тогда `UnlockRegistry` его узнает как событие карты).
- `PlatesPuzzle : PuzzleStructure` — генерация расположения из seed; реакция на `PlayerEventBus.OnPlayerMoved`; визуал плит
  (нажата / не нажата), сброс.
- `PuzzleGenerator`: размещение по тем же правилам, что обелиски (суша с отступом, в тумане, далеко от лагерей), только если тип открыт.
- Lua: `check` возвращает поле `plate` на тайле плиты; `findObjects("plates")` / `"puzzle"` — запись головоломки;
  `puzzle(obj)` → `{kind = "plates", text = "...", solved}`. Хелперы — в `Scripts/Lua/LuaPuzzles.cs`.
- Награда: предложите (например, `crystal` ×1 или +день до угрозы) — утвердит автор; достижение и открытие — автор добавит после.
- `PuzzleConfig`: префаб плиты, количество (по умолчанию 0 — без открытия на карте ничего не меняется).

## Файлы
- `Scripts/Puzzles/*` (новый `PlatesPuzzle.cs`, `PuzzleTypes.cs`, `PuzzleGenerator.cs`, `PuzzleConfig.cs`, `PuzzleManager.cs`)
- `Scripts/Lua/LuaPuzzles.cs`, `Scripts/Lua/Commands/CheckCommand.cs` (поле `plate`), `PuzzleCommand.cs`
- префаб плиты (Tiny Swords — плитки), `Tests/Editor/PuzzleTests.cs`, `PuzzleCommandTests.cs`; `LocRuT16.cs`

## Готово, когда
- [ ] Включив тип (`unlockedEvents` с `plates`), на карте есть площадка; решается Lua-кодом из примера в PR.
- [ ] Неверная плита сбрасывает прогресс; решённая головоломка не решается повторно.
- [ ] Тесты генерации (детерминированность по seed, номера уникальны) и Lua-полей.
