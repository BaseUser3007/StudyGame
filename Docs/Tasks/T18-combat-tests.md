# T18. EditMode-тесты боя

**Область:** Тесты · **Размер:** S · **Не брать одновременно с:** —

## Что сделать
У боя (`CombatService`, `Enemy`, `WeaponItem`, `PlayerInventory.FindWeaponSlot`) нет EditMode-тестов. Написать `Tests/Editor/CombatTests.cs`:
- урон кулаком 5 / мечом 15, радиус атаки 1,5, ошибка «enemy is too far»;
- выбор оружия: самое сильное, при равенстве — самое изношенное; износ за удар; меч ломается на 0 и пропадает;
- `Enemy.TakeDamage` до 0 → `OnKilled` один раз, повторные удары по мёртвому — ошибка/игнор;
- `LuaObjectList.TryGetEnemyId` / смещение `EnemyIdOffset` — id не пересекаются с объектами.

Объекты создавать в тесте (`new GameObject` + `AddComponent`, ScriptableObject через `ScriptableObject.CreateInstance`),
удалять в `TearDown`. Посмотрите, как сделаны `FoodTests`, `PuzzleTests` и `TestHelpers.cs`.

Если какой-то тест выявит ошибку в коде боя — не чинить молча, а описать в PR (или отдельным issue).

## Файлы
- новый `Tests/Editor/CombatTests.cs`, при необходимости — хелперы в `Tests/Editor/TestHelpers.cs` (только добавление)

## Готово, когда
- [ ] ≥ 10 тестов, все зелёные, не зависят от сцены и порядка запуска.
