# Задачи для участников

Каждая задача — отдельный файл `Txx-*.md` в этой папке: что сделать, какие файлы можно трогать,
критерии готовности и как проверить. Задачи подобраны так, чтобы их можно было делать **параллельно**
друг с другом и с основной разработкой (Claude + автор), почти не пересекаясь по файлам.

Перед началом прочитайте [CLAUDE.md](../../CLAUDE.md) (архитектура и правила кода) и, по желанию,
[GameDesign.md](../GameDesign.md) — замысел игры.

## Как взять задачу

1. Выберите задачу со статусом **free** в таблице ниже и проверьте поле «Не брать одновременно с».
2. Создайте issue на GitHub по шаблону **Task** (`.github/ISSUE_TEMPLATE/task.md`): заголовок `T07: Полоса здоровья над врагом`,
   назначьте себя. Issue — это «бронь»: одна задача — один человек.
3. В том же PR, где начинаете работу (или отдельным маленьким PR), поменяйте статус задачи в таблице на
   `taken (@ник)`. Так бронь видна прямо в репозитории.
4. Ветка: `task/T07-enemy-hp-bar` от свежего `main`. Один PR на задачу, в описании — `Closes #<issue>`.
5. Готово → PR в `main`, статус `review`; после слияния — `done`.

Нашли ошибку или хотите предложить задачу — issue без шаблона, ничего не бронирует.

## Зоны файлов

**Зона Claude / автора — не менять без договорённости в issue** (здесь идёт основная работа, конфликты гарантированы):

- `Assets/_Game/Scripts/Lua/LuaSandbox.cs`, `EnergyInstrumenter.cs`, `EnergyMeter.cs`, `EnergyCounter.cs`,
  `LuaConstructGuard.cs`, `UnlockRegistry.cs`, `LuaTokenizer.cs`
- Числа баланса: `Assets/_Game/ScriptableObjects/Default*Config.asset`, `Enemies/EnemyConfig.asset`,
  `Achievements/*.asset` (кроме иконок — задача T11), `Recipes/RecipeBook.asset`
- `Docs/GameDesign.md`, `Docs/Roadmap.md`, `CLAUDE.md` (отметки в Roadmap ставит автор после слияния)

**«Горячие» общие файлы — можно, но минимально и с пометкой в PR** («трогаю Level.cs: +1 поле, +1 Initialize»):

| Файл | Почему горячий | Правило |
|---|---|---|
| `Scripts/Core/Level.cs` | точка сборки всех систем | только добавить поле/assert/`Initialize` для своей системы, не переставлять существующее |
| `Scenes/Game/SampleScene.unity` | YAML сцены плохо сливается | менять только если без этого нельзя (привязать ссылку на `Level`); одна правка — один PR; лучше через префаб |
| `Scripts/Core/GameEventBus.cs` | `enum GameEventType` сериализуется числом | новые значения только **в конец** enum |
| `Scripts/Core/MetaProgress.cs` | формат сохранения | только новые поля с безопасным значением по умолчанию, ничего не переименовывать |
| `Scripts/UI/CodeEditorManager.cs`, `UI/GUI/RunScreens.uxml` | сюда подключается весь UI | добавить свой элемент/вызов, не переписывать |
| `Scripts/Localization/LocTablesRu.cs` | список разделов перевода | добавить одну строку со своим разделом (см. ниже) |
| `Scripts/Lua/Commands/FindObjectsCommand.cs` (`TypeMap`) | все типы объектов | одна строка на новый тип |

**Слияние сцен и префабов.** В `.gitattributes` для `*.unity`, `*.prefab`, `*.asset` уже указан драйвер
`merge=unityyamlmerge`. Чтобы он работал, один раз добавьте в свой `~/.gitconfig`:

```
[merge "unityyamlmerge"]
    name = Unity SmartMerge
    driver = '<путь к Unity>/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A
    recursive = binary
```

Пути из зоны основной разработки и горячие файлы перечислены в `.github/CODEOWNERS` — PR с ними автоматически ждёт ревью автора.

## Обязательные правила (кратко из CLAUDE.md)

- Код в `Assets/_Game`, `Assets/Tiny Swords` не менять. Классы в глобальном namespace.
- Файлы `.cs` — **UTF-8 с BOM**, комментарии на русском, стиль: `[SerializeField] private`, `_camelCase`,
  `ValidateReferences()` с `Debug.Assert`, разделители `// ─── Public API ───`.
- Системы не ищут друг друга (`FindObjectOfType` в игровом коде нельзя) — зависимости передаются через `Initialize(...)` из `Level`.
- Подписки на статические шины (`PlayerEventBus`, `UIEventBus`, `GameEventBus`) — именованными методами, отписка в `OnDestroy`/`Dispose`.
- UI только на UI Toolkit (UXML/USS), без uGUI; UI мышиный — не давать коду захватывать фокус клавиатуры (см. CLAUDE.md, раздел UI).
- Любая новая Lua-команда стоит энергию > 0 (это проверяется при регистрации).
- **Тексты для игрока — через `Loc.T("English text")` / `Loc.F(...)`.** Чтобы не конфликтовать в общих таблицах,
  заведите **свой раздел** `Scripts/Localization/LocRuTxx.cs` (класс `LocRuTxx` с полем
  `public static readonly (string En, string Ru)[] Entries`) и добавьте `LocRuTxx.Entries,` в `LocTablesRu.Sections`.
  Строки, которые возвращаются в Lua (ошибки команд, имена типов, поля таблиц), **не переводятся**.
- Новый ассет / префаб создавайте в Unity (GUID выдаёт Unity), не руками в YAML. Бинарные файлы (png, wav…) — через Git LFS (уже настроен).
- Звуки/картинки со стороны — только с лицензией, разрешающей использование (CC0 / собственные); источник укажите в PR.

## Проверка перед PR

1. Unity **6000.4.10f1**, проект открывается без ошибок в консоли.
2. `Window → General → Test Runner → EditMode → Run All` — всё зелёное (сейчас 489 тестов).
3. Play Mode: лагерь → Start Run → то, что затрагивает задача, работает; в консоли нет ошибок и предупреждений `Loc: no Russian translation`.
4. Проверено на обоих языках (переключатель в лагере).
5. В PR: что сделано, какие горячие файлы тронуты, скриншот/гиф для визуальных задач.

## Задачи

Размер: **S** — до дня, **M** — 2–4 дня.

| ID | Задача | Область | Размер | Не брать одновременно с | Статус |
|---|---|---|---|---|---|
| [T01](T01-inventory-tooltip.md) | Подсказка над слотом инвентаря | UI | S | — | done |
| [T02](T02-editor-hotkeys.md) | Горячие клавиши редактора кода | UI | S | T04 | done |
| [T03](T03-help-search.md) | Поиск в справке по командам | UI | S | — | done |
| [T04](T04-runlog-goto-line.md) | Клик по ошибке в логе → переход к строке | UI | S | T02 | done |
| [T05](T05-run-summary-stats.md) | Статистика на экране итогов забега | UI / мета | S | — | done |
| [T06](T06-energy-floaters.md) | Всплывающие «−5» у полосы энергии | UI | S | — | done |
| [T07](T07-enemy-hp-bar.md) | Полоса здоровья над врагом | Визуал | S | T08, T13 | done |
| [T08](T08-damage-numbers.md) | Цифры урона над врагом и игроком | Визуал | S | T07 | done |
| [T09](T09-sound-feedback.md) | Звуки действий | Аудио | M | — | free |
| [T10](T10-mushroom-food.md) | Грибы — еда на карте | Контент | S | T12 | done |
| [T11](T11-achievement-icons.md) | Иконки достижений | Контент | S | — | done |
| [T12](T12-sheep-wander.md) | Овца бродит, пока идёт код | Геймплей | S | T10 | done |
| [T13](T13-archer-enemy.md) | Враг-лучник | Геймплей | M | T07 | free |
| [T14](T14-hub-building-levels.md) | Уровни построек хаба | Хаб | M | T15 | done |
| [T15](T15-hub-adjacency.md) | Бонусы за соседство построек | Хаб | M | T14 | done |
| [T16](T16-pressure-plates-puzzle.md) | Головоломка «плиты по порядку» | Головоломки | M | — | free |
| [T17](T17-debug-window.md) | Окно отладки в редакторе (выдать предметы, открыть всё, телепорт) | Инструменты | S | — | done |
| [T18](T18-combat-tests.md) | EditMode-тесты боя | Тесты | S | — | done |
| [T19](T19-run-modifiers-tests.md) | EditMode-тесты эффектов построек на забег | Тесты | S | T14, T15 | done |
| [T20](T20-readme.md) | README репозитория | Документация | S | — | done |
| [T21](T21-ru-proofreading.md) | Вычитка русского перевода | Локализация | S | — | done |
| [T22](T22-code-library-clipboard.md) | Библиотека кода: копировать / вставить через буфер обмена | UI | S | — | done |
| [T23](T23-tutorial-energy-warning.md) | Шаг туториала: предупреждение о нехватке энергии | Туториал | S | — | done |

Задачи, которые остаются за основной разработкой (не брать): баланс среза.
(Сделано 2026-10-24: перевод текста задач обелиска, подсветка выполняемой строки, точки останова, шаги, переменные на паузе.)
