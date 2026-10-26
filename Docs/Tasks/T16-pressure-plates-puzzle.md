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
- [x] Неверная плита сбрасывает прогресс; решённая головоломка не решается повторно.
- [x] Тесты генерации (детерминированность по seed, номера уникальны) и Lua-полей.

## Реализация (Claude, 2026-10-26)
- `PlatesLayout` (5×5, 4–5 плит, номера 1–30 без повторов, плиты не касаются даже углами, в каждой строке и
  столбце — не больше одной), `PlatesSequence` (порядок, сброс), `PlatesPuzzle`, `PuzzleConfig.platePrefab` /
  `platesCount` (0) / `platesMinDistanceFromSpawn` (15) / `platesCrystalAmount` (1).
- Lua: `check(x, y)` на плите — `type`/`structure` = `"plate"`, `plate` (номер), `pressed`; элемент `findObjects`
  (`"plates"`, `"puzzle"`) — `minX`/`minY`/`maxX`/`maxY` площадки; `puzzle(obj)` → `kind = "plates"`, `text`,
  `plates` (сколько), `pressedCount` (сколько нажато), `solved`; `solve()` на плитах — `nil, "plates are solved by stepping…"`.
- Награда (предложение, ждёт утверждения): `crystal` ×1 в инвентарь, не влезло — на землю в центре площадки.

Пример решения (стоимость ≈ 25 `check` + движение):
```lua
local p = findObjects("plates")[1]
moveTo(p.minX - 1, p.minY)  -- подойти к левому нижнему углу (путь не должен идти через площадку)
local plates = {}
for x = p.minX, p.maxX do
  for y = p.minY, p.maxY do
    local t = check(x, y)
    if t and t.plate then plates[#plates + 1] = { x = x, y = y, n = t.plate } end
  end
end
table.sort(plates, function(a, b) return a.n < b.n end)
local y = p.minY
for _, pl in ipairs(plates) do
  moveTo(p.minX - 1, y)     -- по строке прошлой плиты назад к краю
  moveTo(p.minX - 1, pl.y)  -- вдоль края площадки (снаружи)
  moveTo(pl.x, pl.y)        -- по строке плиты: других плит в ней нет
  y = pl.y
end
```
