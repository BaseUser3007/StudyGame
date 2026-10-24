# T10. Грибы — еда на карте

**Область:** Контент · **Размер:** S · **Не брать одновременно с:** T12

## Зачем
Класс `MushroomObject` (подбирается без инструмента) и тип `mushroom` в `findObjects` уже есть, но на карте грибов нет:
нет предмета, префаба и записи в биомах. Нужен второй источник энергии, кроме ягод, в другом биоме.

## Что сделать (в Unity, ассетами)
1. `FoodItem` `Mushroom` (`itemId` = `mushroom`, `energyRestore` 4, `healthRestore` 2, durability 1, не материал) — в `ScriptableObjects/Items/`.
2. `ResourceVariant` `Mushroom_Common` (drop = Mushroom ×1) и префаб `Prefabs/Objects/Mushroom.prefab` (компонент `MushroomObject`,
   спрайт из Tiny Swords или CC0, пивот у основания, коллайдер — как у `Stick`).
3. Строка в `DefaultBiomeConfig` у одного-двух биомов без ягод (плотность ~1 на 100 тайлов). *Исключение из зоны баланса — согласовано.*
4. Перевод названия — `Scripts/Localization/LocRuT10.cs`.

## Готово, когда
- [ ] Грибы появляются на новой карте, `findObjects("mushroom")` их видит, `harvest` подбирает, `eat("mushroom")` восстанавливает.
- [ ] `getInventory()` у гриба показывает `food = true`, `energy`, `health`.
- [ ] `DataIntegrityTests` проходят (проверяют ссылки в ассетах).
