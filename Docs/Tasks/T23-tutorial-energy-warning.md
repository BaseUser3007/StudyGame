# T23. Шаг туториала: предупреждение о нехватке энергии

**Область:** Туториал · **Размер:** S · **Не брать одновременно с:** —

## Зачем
По итогам прохождений (GameDesign §9, строки 12–13): в первый день игрок часто тратит энергию до нуля посреди запуска
и получает штраф к лимиту на следующий день, не понимая почему. Нужно предупредить заранее.

## Что сделать
Новый шаг в `TutorialSteps.Steps` (порядок в списке = приоритет):
- условие показа (`Requires`): идёт забег, энергии осталось < 25% от максимума, шаг ещё не показан;
- якорь — полоса энергии в `TopBar` (если в `TutorialAnchor` нет подходящего — добавить значение **в конец** enum
  и обработку в `CodeEditorManager.FindTutorialAnchor`);
- текст (EN, перевод в `LocRuT23.cs`): «Energy is almost gone. If a day ends with less than 1 energy, tomorrow's limit is lower.
  Press End Day to rest while you still have some.»;
- закрывается кнопкой Next или нажатием End Day.

Для условия по энергии в `TutorialState` / `TutorialFacts` может понадобиться текущая энергия — брать из `UIEventBus.OnPlayerEnergyChanged`.

## Файлы
- `Scripts/UI/TutorialSteps.cs`, `TutorialController.cs` (факт энергии), при необходимости `CodeEditorManager.cs` (горячий — якорь)
- `Scripts/Localization/LocRuT23.cs`

## Готово, когда
- [x] Шаг появляется один раз, когда энергии < 25%, и не появляется при выключенных подсказках (Hints off).
- [x] Флаг `step:<id>` сохраняется; Reset tutorial в лагере его сбрасывает.
