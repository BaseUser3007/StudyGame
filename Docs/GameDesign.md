# CodeRogue — дизайн-документ (целевая версия)

Документ фиксирует задуманную итоговую игру. Источник — диалог с автором 2026-10-13.
Сверяйтесь с ним перед проектированием новых систем. Чек-лист задач — в [Roadmap.md](Roadmap.md).
Пункты с пометкой **(допущение)** ещё не подтверждены автором.

## 1. Суть

Игра-рогалик, в которой игрок не управляет персонажем напрямую, а пишет Lua-код. Каждая конструкция
и каждое действие стоят энергии — это дневной бюджет. Игра одновременно учит программированию и
поощряет писать свой, более эффективный код вместо готовых дорогих команд.

**Аудитория — любой уровень.** Даже человек, никогда не программировавший, должен разобраться, как
играть (туториал + объяснения в подсказках). Опытный игрок может отключить подсказки и сразу играть.

## 2. Игровой цикл

```
Хаб ──(начать забег)──► Забег на новой карте ──(выход / поражение)──► Хаб
 ▲   тратим ресурсы на постройки-бонусы           собираем ресурсы, выполняем достижения      │
 └─────────────────────────────── ресурсы, открытые команды, события ◄────────────────────────┘
```

- **Победы нет.** Цель — глобальный список **достижений**; за них открываются команды, конструкции
  языка, рецепты, постройки и события на карте.
- Задач «на забег» нет: игрок сам решает маршрут и что делать сейчас. Достижения глобальные и
  выполняются в любом забеге.

## 3. Забег

- **Карта каждый раз новая** (случайный seed; генерация биомов/объектов уже есть).
- **Количество дней не ограничено.** Ограничитель — **мировая угроза**: начиная с дня N
  (по умолчанию **3**, постройки хаба могут отодвинуть) на карте появляются враги и патрулируют.
  Игрок выбирает: вернуться в хаб с добычей или рисковать дальше.
- Игрок может **выйти из забега в любой момент** (возврат в хаб).
- Дневной цикл и энергия — как сейчас (`DayCycle`: 3 запуска в день, штраф/бонус к лимиту).

### Здоровье и еда
- У игрока есть **здоровье** (отдельно от энергии). Враги наносят **урон**.
- Здоровье 0 → **поражение**: забег заканчивается, игрок возвращается в хаб и **теряет все
  материалы** этого забега (переносится только то, что вынесено добровольным выходом).
  Открытые команды, конструкции и достижения сохраняются.
- Голода нет. Еда — только восстановление, и она **двух видов**:
  - еда для **энергии**;
  - отдельная еда для **здоровья**.

### Враги и бой
- **Статические враги** с самого начала стоят в окружении (охраняют лагеря/ценные места) —
  игрок видит их заранее, встреча не неожиданность.
- После наступления угрозы враги **появляются и патрулируют** карту.
- Контакт с врагом/его атака — **урон** здоровью.
- Игрок **может сражаться**: команда атаки, оружие (крафтится, как инструменты).
- Враги видны через `findObjects`/`check`, их можно обходить или атаковать кодом.
- **Время в мире идёт только пока выполняется код**: между запусками враги стоят. Числа боя — §10.1.

## 4. Хаб и мета-прогрессия

Между забегами сохраняются:
- **открытые команды** и конструкции языка;
- **материалы** из забега (дерево, камень, кремень, уникальные ресурсы головоломок…).
  **Инструменты, оружие и прочие крафтовые предметы в хаб не переносятся** — каждый забег их
  нужно делать заново;
- **открытые события** (новые типы сооружений/головоломок на картах).
- Библиотека кода (`CodeLibrary`) уже сохраняется глобально.

**Хаб — как лагерь в Loop Hero:** отдельная локация, в которую игрок **ставит сооружения мышью на
сетку** (без кода), оплачивая их принесёнными материалами. Каждое сооружение даёт эффект на следующие забеги.
Примеры эффектов:
- + к лимиту энергии;
- + дни до мировой угрозы;
- + слоты инвентаря / стартовые предметы;
- + запуски кода в день.

## 5. Обучение

### Готовые команды → свой код («декомпиляция»)
- В начале игроку даются **готовые команды** (`obtain`, `autoExplore` и т.п.): они дорогие по энергии,
  зато простые в использовании.
- «Декомпиляция» — **метафора, а не отдельная механика**: по мере прогресса вместо дорогой команды,
  которая делает много действий, игрок получает **простые базовые команды**. Собрав из них свой
  алгоритм, он тратит меньше энергии. При открытии объясняется, из чего состояла готовая команда
  и почему свой код дешевле.
- Готовая команда после открытия базовых **остаётся доступной** (и дорогой): игрок сам выбирает
  удобство или экономию, уже написанный код не ломается.
- Правило уже заложено в архитектуре: готовая команда дороже, чем эквивалентный короткий фрагмент кода.

### Открытие конструкций языка
- Часть конструкций (`for`, `while`, функции, окна-«классы», таблицы…) открывается не сразу.
- Темп: **новая конструкция примерно каждые 1–2 забега**, не медленнее.
- При открытии обязательно **объясняется, зачем она нужна** — на примере задачи, которую ею проще решить.

### Туториал
- **UI-гайд** с подсвечиванием элементов интерфейса + **всплывающие подсказки**.
- Понятия программирования (переменная, условие, цикл, функция) объясняются **прямо во всплывающих
  подсказках** обучения.
- Подсказки можно **отключить** (для опытных игроков).

## 6. Достижения

Разнообразные, глобальные. Примеры:
- скрафтить топор; добыть определённый ресурс;
- построить здание (в забеге или в хабе);
- собрать A ресурсов, потратив меньше N энергии (эффективность кода);
- впервые использовать новую конструкцию.

Награда за достижение — новая команда / конструкция / рецепт / постройка / событие на карте.

## 6.1. Достижения (срез)

Спецификация набора для среза 2026-10-26 (2–3 забега). По ней заводятся ассеты `AchievementDefinition`.
Прогресс счётчиков глобальный (между забегами, в том числе после поражения). UI игры на английском.

### Модель условий и наград

| Тип условия | Параметры | Смысл |
|---|---|---|
| `Counter` | событие, фильтр (пусто = любое), порог | счётчик игрового события, копится между забегами |
| `FirstUse` | конструкция или команда | синоним `Counter` (`ConstructUsed`/`CommandUsed`) с порогом 1 |
| `EfficientGather` | itemId, A, N | за **один запуск кода** получить A предметов itemId, потратив за этот запуск ≤ N энергии (всё: конструкции + команды + добыча) |

События: `Harvested`, `Mined`, `ItemObtained`, `Crafted`, `Built`, `DayPassed`, `ConstructUsed`,
`CommandUsed` — работают уже на этапе 2; `ThreatReached`, `EnemyFound`, `EnemyKilled` — этап 3;
`HubBuildingBuilt` — этап 4; `PuzzleSolved` — этап 6; `FoodEaten` — этап 7.
Награды: `Command`, `Construct`, `Recipe` (по itemId результата), `HubBuilding` (этап 4). `MapEvent` (этап 6) —
тип награды остаётся в коде, но в срезе события выдаются **не достижениями, а числом выполненных достижений** (ниже).

### События на карте — по числу достижений (решение 2026-10-26)

Тип головоломки (событие карты) открывается, когда выполнено ≥ K достижений из каталога (18), любых.
Открытие в забеге действует **со следующего забега**: головоломки генерируются при старте забега.

| Событие | K | Порог набирается обычно (на карте — со следующего забега) | Что нужно уметь | Вместе открывается |
|---|---|---|---|---|
| `code_task` (обелиски) | 6 | забег 1, дни 1–2 | `for`, `if` (к 6 достижениям почти всегда открыты: `first_harvest` → `if`, `copy_paste` → `for`) | команды `puzzle`, `solve` |
| `maze` | 11 | забег 2 | `while` + `if` (`check`/`move`) | — |
| `plates` (плиты) | 15 | забег 3 | таблицы + обход по порядку (сортировка номеров) | — |

Почему так:
- **Без зависимости от хаба.** Раньше `code_task` давал `hub_builder`, а постройка после забега 1 часто не по
  карману → обелиски могли не появиться до забега 3. Порог 6 даёт их в начале забега 2 почти всегда.
- **Команды вместе с событием.** `puzzle`/`solve` раньше шли от `hub_builder` отдельно от события — можно было
  получить обелиски без команд. Теперь они открываются тем же порогом.
- **`maze` = 11** — тот же темп, что был через `code_solver` (забег 2–3), но без обязательного обелиска.
- **`plates` = 15** — за пределами среза 2–3 забегов: нужна тема таблиц, её пока не учим.
- **Достижимость.** Без достижений, зависящих от головоломок (`code_solver`, `maze_runner`), доступно 16 —
  все пороги (≤ 15) выполнимы, тупика нет.
- Задачи программистам — §9 «К внедрению».

Уточнения семантики (нужны для чисел ниже):
- `Harvested` поднимается в `ResourceHarvester` (подбор), поэтому считается и `obtain`, а не только `harvest`.
- `CommandUsed` — на каждый вызов команды (там же, где списывается её цена).
- `ConstructUsed` — **один раз за запуск** на каждую конструкцию, найденную в коде запуска
  (`LuaConstructGuard` и так сканирует токены). Не на каждую итерацию — иначе «5 раз» выполняется одним циклом.
- `EfficientGather` проверяется в момент, когда набрано A предметов: потрачено с начала запуска ≤ N → выполнено.
  Не считаются предметы с объектов, выброшенных игроком (`drop`), и стартовые предметы забега.

### Набор достижений

Колонка «Этап» — когда достижение реально сможет выполниться (ассет можно завести сразу).
Таблица — **версия 2** (2026-10-20, обоснование — §6.2): изменены строки 1, 4, 5, 8, добавлена 18,
из стартового набора убран `if`. Было (v1): №1 без `if`; №4 порог 10; №5 без `while`; №8 N = 50 и награда
`while` + `repeat`; №18 не было.

| # | id | Название / описание (UI) | Условие | Параметры | Награды | Этап |
|---|---|---|---|---|---|---|
| 1 | `first_harvest` | **First Haul** — Gather 3 resources. | Counter `Harvested` | фильтр — пусто, порог 3 | Command `findObjects`, `moveTo`, `harvest` («декомпиляция» `obtain`), **Construct `if`** (v2) | 2 |
| 2 | `first_axe` | **Sharp Idea** — Craft an axe. | Counter `Crafted` | `axe`, 1 | Command `mine` | 2 |
| 3 | `workbench_built` | **Workshop** — Build a workbench. | Counter `Built` | `workbench`, 1 | Recipe `bed`, Recipe `pickaxe` | 2 |
| 4 | `copy_paste` | **Déjà Vu** — Call harvest() 6 times. | Counter `CommandUsed` | `harvest`, **6** (v2, было 10) | Construct `for` | 2 |
| 5 | `explorer` | **Wanderer** — Use autoExplore() twice. | Counter `CommandUsed` | `autoExplore`, 2 | Command `move`, `check` («декомпиляция» `autoExplore`), **Construct `while`** (v2) | 2 |
| 6 | `lumberjack` | **Lumberjack** — Chop down 5 trees. | Counter `Mined` | `tree`, 5 | HubBuilding `storage` | 2 (награда — 4) |
| 7 | `sweet_dreams` | **Sweet Dreams** — Build a bed. | Counter `Built` | `bed`, 1 | HubBuilding `shrine` | 2 (награда — 4) |
| 8 | `efficient_logger` | **Clean Code** — Chop trees and pick up 4 of their logs in one code run, spending ≤ 55 energy. | EfficientGather | `log`, A = 4, N = **55** (v2, было 50) | Construct `repeat` (v2: `while` перенесён в №5) | 2 |
| 9 | `loop_lover` | **In the Loop** — Use a for loop in 5 code runs. | Counter `ConstructUsed` | `for`, 5 | Construct `function` | 2 |
| 10 | `function_fan` | **Abstraction** — Use functions in 3 code runs. | Counter `ConstructUsed` | `function`, 3 | Construct `classes` (доп. окна + библиотека) | 2 |
| 11 | `threat_survivor` | **Brace Yourself** — Survive until the threat day. | Counter `ThreatReached` | пусто, 1 | Recipe `sword` | 3 |
| 12 | `enemy_spotted` | **Know Your Enemy** — Spot an enemy. | Counter `EnemyFound` | пусто, 1 | Command `attack` | 3 |
| 13 | `first_blood` | **First Blood** — Defeat an enemy. | Counter `EnemyKilled` | пусто, 1 | Recipe `campfire`, Recipe `cooked_meat` | 3 (рецепты — 7) |
| 14 | `hub_builder` | **Home Sweet Home** — Build a structure in the hub. | Counter `HubBuildingBuilt` | пусто, 1 | — (только счёт; было: MapEvent `code_task`, `puzzle`, `solve` — 2026-10-26 события перенесены на число достижений, ниже) | 4 |
| 15 | `code_solver` | **Hello, World** — Solve a code puzzle. | Counter `PuzzleSolved` | `code_task`, 1 | — (только счёт; было: MapEvent `maze`) | 6 |
| 16 | `maze_runner` | **Pathfinder** — Escape a maze. | Counter `PuzzleSolved` | `maze`, 1 | HubBuilding `watchtower` | 6 |
| 17 | `first_meal` | **Bon Appétit** — Eat something. | Counter `FoodEaten` | пусто, 1 | HubBuilding `healer_hut` | 7 |
| 18 | `first_bug` (v2) | **Bug Report** — Get your first error in a code run. | Counter `RunFinished` | `error` (Tag, §6.2.7), 1 | Command `print` | 2 (нужен код §6.2.8) |

Без этапов 3–7 выполнимы №1–10 и 18: это 11 достижений и все 6 открываемых конструкций (`if` теперь тоже открывается).

### Почему так (темп)

Версия 2 (§6.2.1); v1 давала в забеге 1 только `for`.

| Забег | Ожидаемые достижения | Конструкции и инструменты |
|---|---|---|
| 1 | день 1: `first_harvest`, `first_bug`, `explorer`, `first_axe`; день 2: `copy_paste`, `workbench_built`; дни 2–3: часто `efficient_logger`, `lumberjack`, `sweet_dreams`, `loop_lover`; `threat_survivor` на 3-й день | `if`, `while`, `for` (+ `print`); часто `repeat`, `function` |
| 2 | `efficient_logger`, `loop_lover` (если не в забеге 1), `enemy_spotted`, `first_blood`, `hub_builder` (между забегами), `function_fan` к концу; на карте обелиски (6 достижений набрано в забеге 1), часто `code_solver` | `repeat`, `function`, часто `classes`; `puzzle`, `solve` |
| 3 | `function_fan` (если не в забеге 2), `code_solver`, `maze_runner` (лабиринт — 11 достижений к концу забега 2), `first_meal` | `classes` |

Цепочка до верстака занимает ~110 энергии, т.е. дни 1–2 забега 1: 3 × `obtain` (24) → свой сбор
2 палок + 2 камней (~20) → `craft` кремня и топора (5) → 2 дерева через `mine` (~34) → подбор 4 брёвен
и 4 палок (~10–15) → верстак (`craft` 4 + `build` 3).

### Расчёт порога `efficient_logger`

Цены: `obtain` 8, `findObjects` 1, метод списка (`getNear`) 1, `moveTo` 2, `harvest` 1, `mine` 1 + добыча;
дерево роняет 2 бревна (прочность 5) + 2 палки (1) → добыча 12, итого 13 за дерево. Итерация `for` 1.
`obtain` деревья не рубит — он только подбирает брёвна с земли, рубить в любом случае нужно своим кодом.

- **20 брёвен невозможно за один запуск ни при каком коде:** 10 деревьев × 13 = 130 > 100 (дневной лимит,
  максимум 125 с бонусом за ночёвки). Поэтому A = 4 (2 дерева, физика 26).

| Вариант (A = 4 бревна) | Деревья | Подбор 4 брёвен | Итого | N = 55 (v2) |
|---|---|---|---|---|
| Рубка своим кодом + `obtain("log")` | 2 × (2 + 2 + 13) = 34 | 4 × 8 = 32 | **66** | не проходит (−11) |
| То же, но через `for` или `while` (у обоих 1 за проход) | 34 + 2 | 32 + 4 | **72** | не проходит |
| Свой код «в лоб» + брёвна откатились (`moveTo` к каждому) | 34 | 4 × (2 + 2 + 1) + 8 = 28 | **62** | не проходит |
| Свой код «в лоб»: `findObjects():getNear()` перед каждым действием | 34 | 4 × (2 + 2 + 1) = 20 | **54** | проходит впритык |
| То же, но брёвна откатились и к каждому нужен `moveTo` (аккуратный код) | 33 | 9 + 4 × 2 = 17 | **50** | проходит (+5) |
| Аккуратный свой код: один `findObjects` на список + цикл `for` | 1 + 2 × (1 + 2 + 13) = 33 | 1 + 4 × (1 + 1) = 9 | **42** | проходит (+13) |
| Без цикла, строками подряд по одному списку | 1 + 2 × 15 = 31 | 1 + 4 = 5 | **36** | проходит (+19) |

**N = 55** (v1 — 50: аккуратный код с откатившимися брёвнами проходил ровно на пределе, разброс лута
ломал честное решение). `obtain` проигрывает на 11 при любом цикле. Урок: своё вместо готового, искать один
раз и переиспользовать список. Экономию дают только **меньше вызовов** (`obtain`, повторные `findObjects`,
лишние `moveTo`), а замена `for` на `while` ничего не даёт — итерация и проверка стоят одинаково (1).

### Закрытые на старте рецепты

| Рецепт (itemId) | Статус | Чем открывается |
|---|---|---|
| `flint`, `axe`, `workbench` | открыты | — (нужны для первой цепочки) |
| `bed` | закрыт | `workbench_built` |
| `pickaxe` | закрыт | `workbench_built` |
| `sword` | закрыт, предмета ещё нет (этап 3) | `threat_survivor` |
| `campfire`, `cooked_meat` | закрыты, предметов ещё нет (этапы 3/7) | `first_blood` |

### Идентификаторы для других систем

- Постройки хаба (этап 4): `storage` (+2 слота инвентаря), `shrine` (+10 к лимиту энергии),
  `watchtower` (+1 день до угрозы), `healer_hut` (+25 к максимуму здоровья) — §10.3. На старте не открыта ни одна;
  первые две открываются в забеге 1, так что `hub_builder` выполним с забега 2.
- События на карте (этап 6): `code_task`, `maze`, `plates` — открываются числом выполненных достижений
  (6 / 11 / 15), а не наградой (см. «События на карте — по числу достижений»).
- Стартовый набор `UnlockConfig` (v2): команды `obtain`, `autoExplore`, `craft`, `build`, `drop`,
  `getInventory`, `getStatus`, `getPosition`, `eat`; конструкция `local` (`if` — за `first_harvest`, §6.2).
- Команды `puzzle`, `solve` открываются вместе с событием `code_task` (порог 6 достижений; раньше — `hub_builder`, §10.4).

## 6.2. Прогрессия v2 (по мотивам TFWR)

Решено 2026-10-20 автономно по отчёту [Research/FarmerWasReplaced.md](Research/FarmerWasReplaced.md) (§4) и
прохождениям §10.6 №10–12. Таблица §6.1 уже приведена к v2.

**Что взяли из TFWR и что нет.**

| Приём TFWR | У нас | Решение |
|---|---|---|
| Первое открытие — цикл, за секунды (5 × `harvest`) | `for` за 10 вызовов `harvest`, день 2 | порог 6; плюс `while` за `explorer` и `if` за `first_harvest` — три конструкции в забеге 1 |
| Улучшение мира создаёт боль и сразу даёт инструмент (Speed → `if`, Expand → `for`) | декомпиляция давала только команды | каждая декомпиляция несёт конструкцию, которая лечит её боль (таблица 6.2.1) |
| Справка: что болело → почему старое плохо → что открыто → пример | попап объяснял конструкцию | новая подача 6.2.4, тексты 6.2.5 |
| Debug / Timing — открытия | `print` не выводится в UI, замера нет | `print` за `first_bug`, `getStatus().spent` и итог запуска — сразу (6.2.6) |
| Вызов функции бесплатный | 0,5 | оставить 0,5 (6.2.3) |
| Сенсоры после действий | `check`/`findObjects` вместе с действиями | без изменений: без `findObjects` декомпиляция `obtain` невозможна |
| Переменные поздно | `local` со старта | без изменений: бесплатен и нужен для списков `findObjects` |
| Покупка открытий, уровни апгрейдов ×4 | достижения, постройки хаба по одной | без изменений в срезе; уровни построек — после среза (Roadmap этап 4) |

### 6.2.1. Темп и «боль → конструкция»

Правило v2: **каждое открытие лечит боль, которую игрок только что почувствовал**, и приходит в том же
окне, что и источник боли.

| Достижение | Боль (что игрок только что сделал) | Открывается | Почему именно это |
|---|---|---|---|
| `first_harvest` (3 подбора, день 1) | 3 × `obtain` = 24 энергии | декомпиляция `obtain` + **`if`** | свой код без `obtain` падает на `nil`, когда ничего не видно: `if` нужен в ту же минуту (аналог Speed → `if`) |
| `first_bug` (первая ошибка, день 1) | запуск упал с ошибкой | `print` | увидеть, что видит код (аналог Debug) |
| `explorer` (2 × `autoExplore`) | 20 энергии на разведку | декомпиляция `autoExplore` + **`while`** | идти самому «пока не упрёшься» — число шагов заранее неизвестно (аналог Expand → `move`) |
| `first_axe` | топор есть, а `obtain`/`harvest` дерево не берут | `mine` | без изменений |
| `copy_paste` (6 × `harvest`) | одно дерево роняет 4 предмета — 4 одинаковых блока подбора | **`for`** | обход списка лута (аналог Loops за 5 Hay); порог 6 = подбор лута первого дерева |
| `efficient_logger` | уложился в бюджет, но цикл проверяет условие до первого шага | `repeat` | «сначала сделай, потом проверь» |
| `loop_lover` (`for` в 5 запусках) | один и тот же блок в каждом запуске | `function` | без изменений |
| `function_fan` (`function` в 3 запусках) | новый остров — пустой редактор, функции набираются заново | `classes` + библиотека | рогалик-боль: код — единственное, что переносится, кроме материалов |

Где боль от мира ещё применима (после среза): угроза → патрули (`while` с проверкой врага уже есть —
только пример в туториале шаг 17); лабиринт (§10.4) использует `while`, открытый раньше, — так и задумано.

**Ожидаемый забег 1 (v2):** день 1 — `if`, `print`, обычно `mine` и `while`; день 2 — `for`, верстак;
дни 2–3 — часто `repeat`, `function`. Минимум (если игрок не трогал `autoExplore` и не ошибался): `if` + `for`
— требование «≥ 2 языковых новшества в забеге 1» выполнено в худшем случае. Забег 2 — `repeat`/`function`,
`classes` к концу; забег 3 — `classes`, если не раньше. Темп «конструкция каждые 1–2 забега» соблюдён с запасом.

**Почему `if` можно убрать из старта.** До `first_harvest` у игрока только `obtain`/`autoExplore`/`craft`/`build`:
им условия почти не нужны (`obtain` сам ничего не делает, если нечего брать). Туториал (§10.5 шаги 3–7) `if`
не использует. Игроки со старым сохранением `if` не теряют: `first_harvest` у них выполнено, а
`GrantRewardsOfCompleted()` повторно выдаёт награды выполненных достижений.

### 6.2.2. Пороги (v2)

| id | v1 | v2 | Обоснование |
|---|---|---|---|
| `copy_paste` | 10 | **6** | день 1 даёт ~4 своих `harvest` (2 палки + 2 камня, §10.6 №12); 6 набирается на подборе лута первого дерева (4 предмета) — ровно когда блоки подбора копируются. При 10 открытие приходило после 8 скопированных блоков |
| `efficient_logger` | N = 50 | **N = 55** | расчёт §6.1: аккуратный код с откатившимися брёвнами (50) проходил на пределе; `obtain` (66) по-прежнему не проходит |
| `explorer` | 2 | 2 | 20 энергии — заметно, но окупается декомпиляцией: свой шаг разведки 2–2,5 против 10 |
| `first_bug` | — | 1 ошибка | первая ошибка почти всегда в день 1; если игрок не ошибается, `print` ему и не горит |

### 6.2.3. Цена вызова функции — оставить 0,5

| Вариант | Плюсы | Минусы |
|---|---|---|
| **0,5 (оставить, решение)** | уже в коде; рекурсия через `and` (`return n > 0 and f(n - 1)`) стоит 0,5 за шаг — не бесплатный цикл; `code_task` (§10.4) нельзя решить даром | «налог» на декомпозицию: вызов помощника `collect()` = 0,5 при теле ≈ 5,5 (≈ 9 %) |
| 0 + лимит глубины стека | бесплатный «красивый код», как в TFWR | хвостовой вызов Lua (`return f(n - 1)`) не растёт стек — лимит не срабатывает; watchdog ловит только зависание без `await` |
| 0 + цена только у рекурсивного вызова | платит лишь рекурсия | инструментер видит только прямой вызов `f` внутри `function f`; псевдоним `local g = f`, взаимная рекурсия, окна-«классы» обходят; сложный код в `EnergyInstrumenter` |
| 0 + платно после N вызовов за запуск | | непрозрачно для новичка, противоречит «цена строки в гуттере» |

**Почему 0,5 не наказывает красивый код у нас.** В TFWR вызов стоил 1 тик при действии 200 — их разрыв
200 : 1, и бесплатность вызовов была косметикой. У нас мир платный и так: типичный помощник (`collect`)
стоит ≈ 6 за вызов, из них 0,5 — вызов; `obtain` за то же — 8. Функция выигрывает у готовой команды,
а у копипасты проигрывает только 0,5 за вызов — мотивация к `function` идёт от повторного использования
и библиотеки (`classes`), а не от энергии. Рекурсия на `and` остаётся самым дешёвым циклом (0,5 против 1) —
это допустимая находка для опытного игрока, не дыра. Пересмотреть после прохождения среза, если игроки
избегают функций из-за цены.

### 6.2.4. Подача попапов «Unlocked» / «Decompiled»

Порядок блоков в `UnlockPopup` (и для конструкций, и для декомпиляций, и для команд с текстом):

| # | Блок | Стиль | Содержание |
|---|---|---|---|
| 1 | Заголовок | как сейчас | «New language construct: For» / «Decompiled: obtain()» / «New command: print» |
| 2 | **Why** (боль) | новый класс `unlock-pain`, выделен | одна-две фразы: что игрок только что сделал и чем это плохо |
| 3 | What | `unlock-text` | что открыто, одним абзацем |
| 4 | Example | `unlock-section` + код | 3–7 строк с реальными командами, решающих ту самую боль |
| 5 | Energy | `unlock-cost` | цена конструкции (как сейчас из `EnergyConfig`) + статичная заметка-сравнение; у декомпиляции — строки «obtain() 8 / Your code 5.5» (уже есть) |

Награды одного достижения идут в порядке ассета: сначала окно декомпиляции, затем окно конструкции
(`first_harvest`: `obtain()` → `if`; `explorer`: `autoExplore()` → `while`).

### 6.2.5. Тексты (английский, для `LuaConstructGuide` / `LuaDecompileGuide` / команд)

Поля: **Pain** (новое), **Explanation** (заменяет текущее), **Example**, **EnergyNote** (новое, статично).
Цены в заметках посчитаны по §10.6 и должны совпадать с подсчётом `UnlockPopup` по токенам примера.

**`if`** (за `first_harvest`)
- Pain: "obtain() never complained: when nothing was in sight, it simply did nothing. Your own code is less forgiving — findObjects(\"stick\"):getNear() returns nil when there is no stick, and stick.x on nil stops the run with an error."
- Explanation: "if runs a block only when its condition is true. nil and false count as \"no\", anything else as \"yes\". elseif and else add alternatives."
- EnergyNote: "Each check costs 0.5. An error ends the run and wastes one of today's runs — checking first is cheaper."

```lua
local stick = findObjects("stick"):getNear()
if stick then
  moveTo(stick.x, stick.y)
  harvest(stick)
else
  autoExplore()
end
```

**`for`** (за `copy_paste`)
- Pain: "You have called harvest() 6 times — line after line of the same code. One tree drops 4 items, and each of them needed its own lines."
- Explanation: "for repeats a block a set number of times. Its variable (i) counts the passes: 1, 2, 3… Use count to go through a whole list."
- EnergyNote: "A loop saves lines, not energy: every pass costs 1 plus everything inside. Search once before the loop — a findObjects() inside it is paid on every pass. Here: 1 + 4 per item, instead of 5 per item with a new search each time."

```lua
local logs = findObjects("log")
for i = 1, logs.count do
  local log = logs[i]
  moveTo(log.x, log.y)
  harvest(log)
end
```

**`while`** (за `explorer`)
- Pain: "You paid 10 energy for every autoExplore(). Walking yourself is cheaper — but you never know how far the land goes, so how many move() lines should you write?"
- Explanation: "while repeats a block as long as its condition is true. Use it when you don't know the number of passes in advance: walk until something stops you, chop while trees are in sight."
- EnergyNote: "Each check of the condition costs 1: here 2 energy per 6 tiles, against 10 for autoExplore(). Make sure the condition becomes false — an endless loop drains the whole day."

```lua
-- walk east until water or an obstacle stops you
local steps = move("east", 6)
while steps > 0 do
  steps = move("east", 6)
end
```

**`repeat`** (за `efficient_logger`)
- Pain: "Clean code — 4 logs on a tight budget. But while checks before the first pass, and sometimes you have to act first and only then see whether it was enough."
- Explanation: "repeat … until runs the body first and checks the condition after it, so the body always runs at least once. The loop stops when the until condition becomes true. Locals of the body are visible in until."
- EnergyNote: "Each until check costs 1, like a while check. Here: 3 energy per 3 tiles walked."

```lua
-- step north until a tree comes into view (or the way is blocked)
repeat
  local moved = move("north", 3)
  local trees = findObjects("tree")
until trees.count > 0 or moved == 0
```

**`function`** (за `loop_lover`)
- Pain: "You have written a for loop in 5 runs — probably with the same \"find, walk, pick up\" lines inside every time. A copied block has to be fixed in every copy."
- Explanation: "function gives a name to a piece of code; call it by that name like a command. Parameters (kind) receive the arguments of the call, and return hands a value back."
- EnergyNote: "Defining a function is free; each call costs 0.5 and its body is paid as usual. collect() below costs 6 per call — still less than obtain() at 8."

```lua
function collect(kind)
  local obj = findObjects(kind):getNear()
  if obj then
    moveTo(obj.x, obj.y)
    harvest(obj)
  end
  return obj
end

collect("stick")
collect("stone")
```

**`classes`** (за `function_fan`)
- Pain: "Every new island starts with an empty editor: the functions you wrote last run are gone, and you type them again."
- Explanation: текущий текст `LuaConstructGuide` без изменений ("Extra code windows: press + …").
- EnergyNote: "Calling a window costs the same as calling a function (0.5). Saved windows are kept between runs — besides materials, your code is the only thing you take home."
- Example: текущий (окно `Gather` + `Main`).

**Декомпиляция `obtain()`** (за `first_harvest`) — Explanation и Example без изменений.
- Pain: "Every obtain() cost you 8 energy — three of them, 24."

**Декомпиляция `autoExplore()`** (за `explorer`) — Explanation и Example без изменений (пример: 2,5 против 10).
- Pain: "Every autoExplore() cost you 10 energy, and it only walks a few tiles."

**Команды** (`UnlockPopup.BuildCommand`; сейчас показывает русский `ILuaCommand.Description` — нужен английский текст):

| Команда | Pain | Explanation | EnergyNote |
|---|---|---|---|
| `mine` | "obtain() and harvest() only pick things up from the ground. A tree has to be chopped first — and now you have an axe." | "mine(obj) chops or breaks an object with the right tool (tree — axe, boulder — pickaxe). The loot falls on the ground around it: pick it up with harvest()." | "1 per call plus the loot: a tree costs 1 + 12. The axe wears out after 20 uses." |
| `print` | "Your code stopped with an error. The quickest way to find out why is to look at what your code actually sees." | "print(...) writes values to the log under the main window: numbers, text, nil, and tables with their fields." | "0.5 per call. Remove debug prints once the code works — inside a loop they are paid on every pass." |
| `attack` | "Goblins stand between you and the gold. You can sneak around them — or fight." | "attack(enemy) hits an enemy within 1.5 tiles: 5 damage bare-handed, 15 with a sword. It does not walk up to the enemy — use moveTo first, or wait for it to come." | "2 per hit. The real price is health: fight with a sword." |

Пример для `print`:

```lua
local stick = findObjects("stick"):getNear()
print(stick)               -- {type="stick", x=3.4, y=-1.2, ...} or nil
print(getStatus().spent)   -- energy spent in this run so far
```

### 6.2.6. Отладка и замер

| Инструмент | Нужен? | Когда | Почему |
|---|---|---|---|
| `print(...)` | да, **команда** 0,5 | за `first_bug` (день 1) | сейчас стандартный `print` пишет в консоль Unity — игрок его не видит. Бесплатным быть не может (принцип «ни одна команда не бесплатна»), 0,5 — как `if`, чтобы отладка не била по бюджету |
| `getStatus().spent` | да, поле | сразу (`getStatus` в старте) | замер расхода за текущий запуск — инструмент для `efficient_logger`; цена — вызов `getStatus` (1) |
| Итог запуска «Spent N energy» | да, UI | сразу | бесплатный для игрока замер без кода; данные уже есть (`CodeRunReport.EnergySpent`) |
| Точки останова, пошагово | нет | после среза | дорого в реализации; подсветка выполняемой строки — **сделана 2026-10-24** (`LuaLineTracker`); точки останова и шаги — **сделаны 2026-10-24** (`LuaDebugger`, клик по номеру строки); просмотр переменных на паузе — **сделан 2026-10-24** (`DebugVariablesPanel`); после среза |

### 6.2.7. Новые события и достижения (для программистов)

| Что | Где | Точное поведение |
|---|---|---|
| Tag у `RunFinished` | `LuaSandbox.ExecuteAsync` → `GameEventBus.PublishRunFinished` | Id без изменений (`success`/`failed`); Tag: `ok`, `error` (ошибка компиляции или выполнения Lua, включая ошибки команд, дошедшие до верха), `energy` (кончилась энергия), `stopped` (Stop/watchdog), `locked` (отказ `LuaConstructGuard`) |
| Достижение `first_bug` | новый ассет | Counter, `gameEvent` = `RunFinished`, filter `error` (сравнивается с Tag), amount 1, reward Command `print` |

Других новых событий не нужно: остальные изменения v2 — пороги и награды существующих ассетов.

### 6.2.8. К внедрению

| Исполнитель | Система / ассет | Что поменять |
|---|---|---|
| asset-editor | `DefaultUnlockConfig` | `startingConstructs`: убрать `if` (остаётся `local`) |
| asset-editor | `first_harvest.asset` | rewards: + Construct (kind 1) `if` **после** трёх команд |
| asset-editor | `copy_paste.asset` | `condition.amount` 10 → 6; description "Call harvest() 6 times." |
| asset-editor | `explorer.asset` | rewards: + Construct `while` после `move`, `check` |
| asset-editor | `efficient_logger.asset` | `condition.maxEnergy` 50 → 55; rewards: убрать `while`, оставить `repeat`; description "Chop trees and pick up 4 of their logs in one code run, spending ≤ 55 energy." |
| asset-editor | `first_bug.asset` (новый) + `AchievementCatalog` | id `first_bug`, title "Bug Report", description "Get your first error in a code run.", Counter / `RunFinished` / filter `error` / 1, reward Command `print`; в каталоге — после `first_harvest` |
| программист | `LuaSandbox`, `GameEventBus` | Tag у `RunFinished` (6.2.7) — **сделано** (`GameEventBus.RunTag*`, `CodeRunReport.Outcome`; watchdog → `stopped`, вызов закрытой команды → `locked`; тесты `LuaDebugToolsTests`) |
| программист | новая `PrintCommand` | `print(...)` 0,5; заменяет стандартный `print` в `RestrictStandardLibraries`; вывод в лог главного окна (через существующий неиспользуемый `LuaSandbox.OnLogMessage` → `UIManager`); таблицы — один уровень `{k=v, …}`, `LuaObjectList` — `count` + первые 3 элемента, строка до ~200 символов; регистрация в `RegisterCommands`; убрать `print` из встроенных `LuaCompletionProvider` (придёт из реестра) — **сделано** (вывод через `OnLogMessage(string, LogEntryType.Info)`, строка до 200 символов; подписка UI — за ui-programmer) |
| программист | `GetStatusCommand` | поле `spent` — энергия, потраченная с начала текущего запуска (то же, что `CodeRunReport.EnergySpent`) — **сделано** (`LuaSandbox.EnergySpentThisRun`) |
| программист | `CodeEditorManager.OnRun` | уведомление после запуска с расходом: "Execution completed — spent N energy" (и для stop/error) |
| программист | `LuaConstructGuide`, `LuaDecompileGuide` | поля `Pain`, `EnergyNote`; тексты 6.2.5 |
| программист | `UnlockPopup` | блоки по 6.2.4: Pain первым (класс `unlock-pain` в USS), EnergyNote после цены; для команд — английские Pain/Explanation/EnergyNote из нового `LuaCommandGuide` (`mine`, `print`, `attack`), иначе как сейчас |
| программист | `TutorialSteps` | концепт «Condition»: триггер только при открытом `if` (`IsConstructUnlocked`), не по `IsStepDone("variable")`; шаг 7 (§10.5): "Gather 3 items to unlock cheaper commands and conditions." |
| программист (тесты) | `DataIntegrityTests.AchievementCatalog_MatchesDesignIds` | + `first_bug`; `LuaSandboxUnlockTests` и др. задают стартовый набор сами — проверить, что не опираются на `if` из `DefaultUnlockConfig` |
| вручную | прохождение от нового сохранения | проверить ожидания 6.2.1: `if` и `print` в день 1, `for` не позже дня 2, `efficient_logger` проходится аккуратным кодом |

## 7. Сооружения-головоломки (события на карте)

- Это часть **генерации мира**: игрок находит их, исследуя карту.
- Новые типы открываются через мета-прогресс: числом выполненных достижений (`code_task` 6, `maze` 11,
  `plates` 15 — §6.1); открытый в забеге тип появляется на карте со следующего забега.
- Типы — любые: **лабиринт**, **перемещение предметов** (сокобан), **задачи на код** (вход → выход:
  сумма, сортировка, поиск), и т.д.
- **Награда зависит от типа:**
  - задача на код → открытие новой команды или бонус на текущий забег;
  - пространственная головоломка (лабиринт, перемещение) → уникальные ресурсы.

## 8. Ориентир по срокам

**К 2026-10-26 — контент на 2–3 забега** (вертикальный срез: хаб, забеги с угрозой, достижения с
открытием команд, туториал, 1–2 типа головоломок). Предварительная прогрессия — в Roadmap.md.

## 9. Открытые вопросы

Числа для этапов 3–7 (враги, бой, еда, хаб, головоломки, туториал, проверка баланса) — **§10**.

Решено 2026-10-13: здоровье 0 → конец забега с потерей всех материалов; готовые команды
остаются доступными; сооружения хаба ставятся мышью на сетку.

### Решено (2026-10-19, автономно)

Приняты рекомендации документа (автор разрешил решать самостоятельно):

| # | Вопрос | Решение |
|---|---|---|
| 1 | За что `for` | За `copy_paste` (10 вызовов `harvest`), как в §6.1 |
| 2 | Функции и окна-«классы» | За использование `for` (5 запусков) → `function`, `function` (3 запуска) → `classes`, как в §6.1 |
| 3 | Цена `autoExplore` | **10** (вместо 25). Аналог из базовых — `move(dir, 5)` = 1 + выбор направления ≈ 1–2; наценка ×5–10, сопоставимо с `obtain` (8 против 5), но не наказание. `explorer` теперь стоит 20 энергии вместо 50 |
| 4 | Порог `first_harvest` | **3** (уже так в ассете) |
| 5 | Постройки хаба | `storage`, `shrine`, `watchtower`, `healer_hut`; на старте ни одной; `watchtower` остаётся за `maze_runner` (у `threat_survivor` уже есть награда — меч). Числа — §10.3 |
| 6 | Враги видны в Lua | Да: тип `enemy`/`goblin` в `findObjects` и `check` (было допущение §3) |
| 7 | Как идёт время в мире | Только пока выполняется код; между запусками враги стоят (§10.1) |
| 8 | Команда `eat` | В стартовом наборе `UnlockConfig` (выживание, не учебная тема) |
| 9 | Команды головоломки `puzzle`/`solve` | Открываются вместе с событием `code_task`. Пересмотрено 2026-10-26 (решение автора): события — по числу достижений (`code_task` 6, `maze` 11, `plates` 15, §6.1), `puzzle`/`solve` — вместе с `code_task`; у `hub_builder` и `code_solver` наград нет |
| 10 | Восстановление здоровья без еды | Ночь у спального места (`bed`): +15 здоровья (§10.1), чтобы срез не зависел целиком от этапа 7 |
| 11 | Цена вызова функции | 0 → **0,5**: иначе рекурсия с `and` (`return n > 0 and f(n - 1)`) — бесплатный цикл, задачи на код решались бы даром. Обоснование — §10.6. Пересмотрено 2026-10-20 по TFWR — **остаётся 0,5** (§6.2.3) |
| 12 | Что делает `autoExplore` | Не случайные 5 шагов, а разведка к неизвестному (2026-10-20): из 8 направлений — то, где путь до 6 тайлов по прямой (вода останавливает) откроет больше всего ещё не виденной суши; если вокруг всё открыто — к ближайшей границе тумана. Ничья — хеш от seed и позиции. Возвращает число открытых тайлов суши. `autoExplore(type)` — до 3 шагов, пока объект/враг/головоломка типа не появится в поле зрения, вернёт ближайший или `nil, "not found"`; 10 за вызов + 10 за каждый шаг после первого. «Декомпиляция» — `check` + `move` (идти в одну сторону, пока впереди суша) |
| 13 | Прогрессия открытий v2 (по мотивам TFWR, 2026-10-20) | `if` из старта → `first_harvest`; `while` → `explorer`; `copy_paste` 10 → 6; `efficient_logger` N 50 → 55, награда только `repeat`; новое `first_bug` → команда `print` (0,5); `getStatus().spent`; попапы «боль → что открыто → пример → энергия». Подробно — §6.2 |

### К внедрению (задачи программистам / asset-editor)

| Система | Что поменять | Почему |
|---|---|---|
| `AutoExploreCommand.EnergyCost` | 25 → 10, `IsReadyMade` — **сделано** | решение 3 |
| `DefaultEnergyConfig.functionCost` | 0 → 0,5 — **сделано** (и дефолт в коде, `CLAUDE.md`) | решение 11 |
| `EnergyConfig.apiCallCost`, `variableCost` | удалены — **сделано** | мёртвые поля путают баланс |
| `DefaultUnlockConfig` | + команда `eat` в стартовый набор — **сделано** | решение 8 |
| `hub_builder.asset` | + награды Command `puzzle`, Command `solve` — сделано, **отменено 2026-10-26**: убрать все награды (MapEvent `code_task`, `puzzle`, `solve`) | решение 9 (пересмотр) |
| `code_solver.asset` | убрать награду MapEvent `maze` | решение 9 (пересмотр) |
| программист: `UnlockRegistry` / `AchievementTracker` | правило «событие по числу достижений»: таблица {событие, порог, доп. команды} (`code_task` 6 + `puzzle`, `solve`; `maze` 11; `plates` 15) — в конфиге (`UnlockConfig` или `AchievementCatalog`), не в коде; после каждого `CompleteAchievement` при `CompletedCount` ≥ порога → `UnlockMapEvent` + `UnlockCommand` (тот же путь, что награды: `OnUnlocked` → `UnlockPopup`); при загрузке — догнать идемпотентно, как `GrantRewardsOfCompleted` (старые сохранения) | решение 9 (пересмотр) |
| программист: `HubBuildPanel` / `AchievementsPanel` / тесты | подпись открытия события «N achievements» вместо имени достижения (если где-то показывается); `DataIntegrityTests`: нет наград MapEvent у достижений, пороги ≤ числа достижений без головоломок (17); строки `Loc` | решение 9 (пересмотр) |
| ассеты достижений, `DefaultUnlockConfig`, `PrintCommand`, `UnlockPopup`… | список v2 — §6.2.8 | решение 13 |
| `HubBuildingDefinition` `storage` (этап 4) | +2 слота (в §6.1 было +1, исправлено) | §10.3 |
| `DayCycle` | ночь у `isSleepingPlace` → `PlayerHealth.Heal(15)` | решение 10 |
| `RabbitObject` | заменить на `sheep` (у Tiny Swords есть спрайты овцы, у кролика нет) — **сделано в коде** (`SheepObject`, ассеты — asset-editor) | §10.2 |
| `FoodResource` | превратить в `FoodItem` (+ `healthRestore`) — **сделано** | §10.2 |

### Открыто (2026-10-26, хаб T14/T15, §10.3)

| # | Вопрос | Варианты | Рекомендация |
|---|---|---|---|
| 14 | Перенос построек в лагере | (а) как сейчас — без сноса и переноса; (б) бесплатный перенос мышью (материалы не возвращаются — их и не тратим); (в) снос с возвратом части материалов | **(б)**: соседство — головоломка, а без переноса ранняя ошибка расстановки навсегда; задача программистам — `HubService.TryMove(def, cell)` + режим переноса в `HubPlacementController` |
| 15 | Кристаллов мало (14 на всё, 2 за забег) | (а) оставить — хвост после среза; (б) сундук лабиринта ×3; (в) второй лабиринт на карте после среза | **(а)** до ручного прохождения; если ур. 2 `shrine`/`watchtower` не берут к забегу 4 — (б) |
| 16 | Награды `hub_builder` и `code_solver` (с 2026-10-26 без наград, только счёт к порогам событий §6.1) | (а) оставить без наград — тост показывает прогресс «N / 11 до лабиринта»; (б) `hub_builder` → рецепт (новый предмет, например сундук-хранилище в забеге); (в) `code_solver` → новая постройка хаба (например, «обсерватория»: +1 обелиск на карту) или рецепт с `crystal`; (г) `code_solver` → готовая команда для задач, например `sum(list)` за 15 против ≈ 12–13 у своего `for` по 12 числам — «декомпиляция» наоборот: готовое дороже своего | **(а)** для среза; после — (г) для `code_solver` (учебная и по принципу «готовое дороже») и (б) для `hub_builder`. Решение автора |

Всё, что выше, можно пересмотреть после ручного прохождения среза (этап 8).
Проверить в прохождении (v2, §6.2): не слишком ли быстро (`if`, `while`, `for` + `print` в забеге 1 —
новичка не перегружает ли); не избегают ли игроки функций из-за цены 0,5 (§6.2.3); проходится ли
`efficient_logger` аккуратным кодом при разбросе лута.

## 10. Числа для среза (спецификация ассетов)

Базовые величины из ассетов (2026-10-19): лимит энергии 100/день (до 125 с ночёвками), 3 запуска в день,
здоровье 100 (`PlayerConfig.maxHealth`, по умолчанию), скорость игрока 5 тайлов/с (`moveSpeed` 0,2 с/тайл),
обзор 6 тайлов (было 4, см. §10.6 №11), досягаемость 1,5 тайла, карта 100×100 (~7500 тайлов суши). Расстояния — в тайлах, время —
в секундах **игрового времени, которое идёт только пока выполняется код**.

### 10.1. Этап 3: угроза, враги, бой

**Время.** Враги, патрули, перезарядки обновляются только при `LuaSandbox.IsRunning`. Между запусками всё
стоит. В начале каждого запуска 0,5 с «форы»: враги в погоне не бьют (игрок успевает уйти, если запуск
закончился рядом с врагом).

**Угроза.**

| Параметр | Значение | Зачем |
|---|---|---|
| `threatDay` | 3 (`RunModifiers.ThreatDay`, `watchtower` +1) | 2 спокойных дня хватает на цепочку до верстака (~110 энергии) |
| Предупреждение | в начале дня `threatDay − 1`: тост «Goblins will start patrolling tomorrow.» | игрок решает: уходить или готовиться |
| `TopBar` | «Threat in N days» → «Threat!» | |
| `getStatus()` | `threatDay`, `daysToThreat` (0 = угроза идёт), `threat` (bool) | |
| `ThreatReached` | один раз, в начале дня `threatDay` | достижение `threat_survivor` |

**Враги.** В срезе один тип; второй — после среза.

| Параметр | `goblin` (★) | `goblin_guard` (после среза) |
|---|---|---|
| Спрайты | Tiny Swords `Units/Red Units/Warrior` | `Units/Black Units/Warrior` |
| Здоровье | 40 | 80 |
| Урон за удар | 10 | 15 |
| Радиус атаки | 1,0 (центр–центр) | 1,0 |
| Замах перед первым ударом | 0,4 с | 0,6 с |
| Перезарядка | 1,5 с | 2,0 с |
| Радиус агрессии | 3 (< обзора 6: игрок всегда видит врага раньше, чем тот его) | 3 |
| Сброс погони | игрок дальше 6 или враг дальше 8 от поста | то же |
| Скорость: погоня / патруль | 3 / 2 тайла/с (игрок 5 — убежать можно всегда) | 2,5 / — |
| Лут (на землю, `ResourceVariant.loot`) | `gold` ×2 | `gold` ×4 |

**Размещение.**

| Что | Сколько | Правила |
|---|---|---|
| Лагеря статических врагов | 3 на карту, по 1, 2 и 3 гоблина | центр лагеря ≥ 15 / 20 / 25 тайлов от спавна (малый ближе всех), ≥ 20 между лагерями; гоблины в радиусе 2 от центра; стоят с дня 1 |
| Ценность лагеря | в лагерях на 2 и 3 гоблина — `gold_ore` в центре | «охраняют ценное место» |
| Патрули | в начале каждого дня ≥ `threatDay`: новых `min(1 + (day − threatDay), 4)`, по 1 гоблину; старые остаются | день 3: 1, день 4: +2 (3 всего), день 5: +3 (6) — давление растёт, уходить выгодно на 4–5 день |
| Спавн патруля | суша в тумане, 12–20 тайлов от игрока | |
| Маршрут патруля | случайная точка суши ≤ 8 от места спавна, идёт по прямой, стоит 1 с, повторяет | движение — как `MoveTowardsAsync` |

**Новые предметы и объекты.**

| id | Класс | Параметры | Спрайт |
|---|---|---|---|
| `gold` | `MaterialResource` | durability 3, stack 20, `worldPrefab` — подбираемый объект | `Pawn and Resources/Gold/Gold Resource` |
| `gold_ore` | `ExtractableResource`, инструмент — кирка | loot `gold` ×3 → `mine` = 1 + 9 = 10 энергии | `Gold/Gold Stones/Gold Stone 1–6` |
| `sword` | оружие (`ToolItem` с новым `toolType` Sword или `WeaponItem`) | урон 15, maxDurability 20, износ 1 за попадание, stack 1, не материал | Tiny Swords иконка меча / любая |

**Рецепт меча:** `sword` ← `stick` ×2 + `stone` ×3 + `flint` ×1, станция — верстак, `energyCost` 3,
закрыт (`threat_survivor`). Сбор ингредиентов своим кодом ≈ 25–30 энергии (3 камня ~12, кремень = 2 камня
+ `craft` ~9, 2 палки ~6) — треть дня, как топор + верстак.

**Бой.**

| Параметр | Значение | Обоснование |
|---|---|---|
| `attack(obj \| x, y)` | **2** энергии за вызов | как `moveTo`: действие в мире. Бой дешёв по энергии — его цена в здоровье |
| Радиус удара игрока | 1,5 (= `interactionRadius`) | сам к врагу не подходит: учит `moveTo` / ждать, пока враг подойдёт |
| Длительность удара | 0,5 с (команда ждёт анимацию) | не больше 2 ударов в секунду |
| Урон кулаком | 5 | |
| Урон мечом | 15, износ 1 за попадание | 20 ударов ≈ все 6 гоблинов лагерей (18 ударов) |
| `findObjects` / `check` | тип `enemy` (все враги) и `goblin`; поля `health`, `maxHealth`, `state` (`idle`/`patrol`/`chase`), `durability` = 0 | `harvest`/`mine` по врагу → «use attack()» |
| `EnemyFound` | когда враг впервые за забег оказывается на видимом тайле (Id = тип) | `enemy_spotted` |
| `EnemyKilled` | смерть врага (Id = тип) | `first_blood` |
| Лечение | ночь у спального места +15 здоровья; еда — §10.2 | |

**Проверка боя** (гоблин 40 HP; удары гоблина на 0,4 / 1,9 / 3,4 / 4,9 … с момента входа в радиус; игрок
бьёт каждые 0,5 с; несколько гоблинов бьют одновременно, игрок убивает по одному):

| Бой | Ударов игрока / время | Урон игроку | Энергия на `attack` | Итог |
|---|---|---|---|---|
| 1 гоблин, меч | 3 / 1,5 с | 10 | 6 | легко |
| 1 гоблин, кулак | 8 / 4,0 с | 30 | 16 | выигрываемо, но дорого |
| 2 гоблина, меч | 6 / 3,0 с | 10 + 20 = 30 | 12 | нормально |
| 2 гоблина, кулак | 16 / 8,0 с | 30 + 60 = 90 | 32 | почти смерть |
| 3 гоблина, меч | 9 / 4,5 с | 10 + 20 + 30 = 60 | 18 | тяжело, нужна еда/ночь |
| 3 гоблина, кулак | 24 / 12 с | 30 + 60 + 80 = 170 | 48 | смерть |

**Обход.** Агрессия 3 < обзор 6 → враг виден в `findObjects` до того, как заметит игрока. Путь в ≥ 5 тайлах
от центра лагеря безопасен. Если враг погнался, `moveTo` прочь (5 > 3 тайлов/с) уводит за 6 тайлов за ~3 с.

### 10.2. Этап 7: еда

| id | Класс | Энергия | Здоровье | stack | Откуда |
|---|---|---|---|---|---|
| `berries` | `FoodItem` | +6 | 0 | 20 | `berry_bush` |
| `raw_meat` | `FoodItem` | 0 | +5 | 10 | `sheep` |
| `cooked_meat` | `FoodItem` | 0 | +25 | 10 | рецепт у костра |

| Объект | Класс | Добыча | Плотность (на 100 тайлов биома) | Спрайт |
|---|---|---|---|---|
| `berry_bush` | `HarvestableResource` (`BerryBushObject`), исчезает | `harvest` → `berries` ×2 | Луг 0,4, Лес 0,3, Скалы 0 (~20 на карту) | `Terrain/Decorations/Bushes` |
| `sheep` (вместо `rabbit`) | `ExtractableResource`, инструмент — топор | `mine` → loot `raw_meat` ×2 (durability 3) = 1 + 6 = 7 энергии | Луг 0,3, Лес 0,1 (~12 на карту) | `Pawn and Resources/Meat/Sheep` |

| Рецепт | Ингредиенты | Станция | `energyCost` | Закрыт до |
|---|---|---|---|---|
| `campfire` (`StructureItem`, станция Campfire — новое значение `CraftingStation`) | `log` ×1, `stick` ×2, `stone` ×2 | нет | 2 | `first_blood` |
| `cooked_meat` | `raw_meat` ×1 | `campfire` | 1 | `first_blood` |

Костёр: спрайт `Particle FX/Fire_01` (анимация) на камнях, не спальное место.

**`eat("name" | slot)`** — 1 энергия; восстанавливает не выше максимума; если всё, что даёт еда, уже на
максимуме — ошибка «already full», предмет не тратится (вызов оплачен). `FoodEaten` (Id = itemId).

**Проверка.**
- Куст: `getNear` 1 + `moveTo` 2 + `harvest` 1 + 2 × `eat` 1 = 6 потрачено, 12 получено → **+6 чистыми**.
  ~20 кустов на карте ≈ +120 за забег (чуть больше дня), если обойти все — бонус, а не замена дневного лимита.
- Овца → 2 жареных мяса: `getNear` 1 + `moveTo` 2 + `mine` 7 + 2 `harvest` + 2 `craft` + 2 `eat` = 16 энергии
  за +50 здоровья ≈ лечение после 1–2 боёв с мечом. Сырое мясо (+5) почти бесполезно — повод строить костёр.

### 10.3. Этап 4: хаб

**Сетка:** 8 × 6 клеток (48), постройки ставятся мышью, каждая — один раз (в срезе), без сноса и возврата
материалов. 4 постройки занимают 14 клеток — место под будущие.

| id | Размер | Стоимость | Эффект в `RunModifiers` | Открывается | Спрайт |
|---|---|---|---|---|---|
| `storage` | 2×2 | `log` 6, `stick` 4 | `InventorySlots` +2 (10 → 12) | `lumberjack` | `Buildings/*/House1` |
| `shrine` | 2×2 | `log` 4, `stone` 6 | `BaseEnergyLimit` +10 (100 → 110) | `sweet_dreams` | `Buildings/*/Monastery` |
| `watchtower` | 1×2 | `log` 4, `stone` 4, `crystal` 2 | `ThreatDay` +1 (3 → 4) | `maze_runner` | `Buildings/*/Tower` |
| `healer_hut` | 2×2 | `log` 4, `stick` 4, `gold` 4 | `MaxHealth` +25 (100 → 125) | `first_meal` | `Buildings/*/House3` |

**Что приносят из забега 1** (≈ 300 энергии за 3 дня): ~110 уходит на цепочку до верстака, ~20 на кровать,
~20 на `explorer`, остальное (~150) — сбор: дерево своим кодом ≈ 18–20 энергии за 2 бревна + 2 палки.

| Материал | Ожидаемо к концу забега 1 | Ожидаемо за забег 2 |
|---|---|---|
| `log` | 8–12 | 12–16 |
| `stick` | 8–12 | 10–14 |
| `stone` | 3–6 | 8–12 (кирка, валуны) |
| `flint` | 0–2 | 2–4 |
| `gold` | 0–2 (малый лагерь, если есть меч) | 4–9 (лагеря + `gold_ore`) |
| `crystal` | 0 | 0 (лабиринт — забег 3) |

Итог: после забега 1 хватает на **одну** постройку (`storage` из дерева или `shrine`, если собирал камни) →
`hub_builder` между забегами 1 и 2; вторая — после забега 2; `watchtower`/`healer_hut` — после забега 3.
`+10` энергии у `shrine` = ещё одно дерево в день; `+1` день угрозы = целый спокойный день (~100 энергии).

#### Уровни и соседство (T14/T15)

Проверено по ассетам `ScriptableObjects/Hub/*.asset` 2026-10-26. Эффект уровня — **прибавка** к предыдущим
(`GetEffects` складывает 1..level). Уровень 1 = таблица выше.

| Постройка | Ур. 2: стоимость → прибавка | Ур. 3: стоимость → прибавка | Итог на макс. уровне | Всего материалов (ур. 1–макс.) |
|---|---|---|---|---|
| `storage` | `log` 10, `stone` 6 → +2 слота | `log` 14, `stone` 8, `gold` 4 → +2 слота | 10 → 16 слотов | log 30, stick 4, stone 14, gold 4 |
| `shrine` | `log` 6, `stone` 10, `crystal` 2 → +10 энергии | `stone` 14, `crystal` 4, `gold` 4 → **+10** (было +15) | 100 → 130 | log 10, stone 30, crystal 6, gold 4 |
| `watchtower` | `log` 8, `stone` 8, `crystal` 3 → +1 день | **нет** (было: `stone` 12, `crystal` 5, `gold` 6 → +1 день) | угроза 3 → 5 | log 12, stone 12, crystal 5 |
| `healer_hut` | `log` 8, `stick` 6, `gold` 6 → +15 здоровья | `log` 10, `crystal` 3, `gold` 8 → +20 здоровья | 100 → 160 | log 22, stick 10, crystal 3, gold 18 |

Соседство (касание стороной; каждая запись — один раз; от уровня не зависит):

| Владелец | Сосед | Бонус |
|---|---|---|
| `healer_hut` | `shrine` | +10 к макс. здоровью |
| `shrine` | `healer_hut` | +5 к лимиту энергии |
| `storage` | `watchtower` | +1 слот |
| `watchtower` | `shrine` | +5 к лимиту энергии |

Максимум хаба: энергия 100 → **140** (130 + 2 × 5), здоровье → 170, слоты → 17, угроза → день 5.

- **Темп уровней.** Доход: забег 1 — бревна 8–12, камни 3–6; забег 2 — бревна 12–16, камни 8–12, золото 4–9;
  кристаллы — только лабиринт (2 за карту, с забега 3). `storage` ур. 2 (10 бревен + 6 камней) — после забега 2–3,
  ур. 3 — забег 3–4. `healer_hut` ур. 2 — забег 4–5 (открывается в забеге 3, золото 4 + 6). Срез (2–3 забега) =
  2–3 постройки ур. 1 и максимум один ур. 2; ур. 3 — хвост после среза. Всё до максимума ≈ 9–10 забегов (упирается в кристаллы: лабиринт с забега 3, по 2 за карту).
- **Угроза.** У `watchtower` было 3 уровня × +1 день: угроза 3 → 6, а с обелиском `count` — 7. Игрок уходит
  на 4–5-й день (§10.1), т.е. угрозы бы не было вовсе — пропадает главный ограничитель забега (§3). Поэтому
  **ур. 3 убран**: максимум — день 5, патрули ещё застают игрока.
- **Энергия.** Энергия — главный учебный ресурс: свой код экономит её. Бонус хаба держим в пределах +40%:
  со `shrine` ур. 3 = +15 выходило 145 (+45%), с +10 — 140, и шаг ровный (+10 за уровень = одно дерево в день).
  Здоровье (+70) не трогаем: 7 лишних ударов гоблина, лагерь из 3 гоблинов без меча всё так же опасен (§10.6 №13б).
- **Соседство — головоломка, но не обязательная.** Все 4 пары собираются одновременно: ряд
  `healer_hut`–`shrine`–`watchtower`–`storage` = 2 + 2 + 1 + 2 = 7 клеток из 8 по ширине. Весь бонус
  (+10 энергии, +10 здоровья, +1 слот) ≈ одному уровню постройки — заметно, но без него играть можно.
  Риск: сноса нет, ранняя неудачная расстановка (например, `storage` в углу) теряет бонус навсегда —
  открытый вопрос §9.
- **Дефицит.** Узкое место — `crystal` (14 на всё = 7 лабиринтов) и `gold` (26, 4–9 за забег, лагеря с
  гоблинами). Это намеренно: кристаллы — повод решать лабиринт каждый забег, золото — повод драться. Дерево и
  камень (бревна 74, камни 56) идут с обычного сбора своим кодом.

**К внедрению** (asset-editor):

| Ассет | Что поменять | Почему |
|---|---|---|
| `ScriptableObjects/Hub/Watchtower.asset` | удалить второй элемент `levels` (ур. 3: `stone` 12, `crystal` 5, `gold` 6 → ThreatDays +1); `MaxLevel` станет 2 | угроза не дальше дня 5 |
| `ScriptableObjects/Hub/Shrine.asset` | `levels[1].effects[0].amount` 15 → 10 | бонус энергии хаба ≤ +40%, ровный шаг |

### 10.4. Этап 6: головоломки

**Генерация** (только открытые типы, сид карты; в тумане; не ближе 8 к лагерю врагов):

| Тип | Сколько на карту | Расстояние от спавна | Между собой |
|---|---|---|---|
| `code_task` (обелиск) | 2, разные задачи | ≥ 12 | ≥ 15 |
| `maze` | 1 | ≥ 20 | — |

**Открытие типов:** по числу выполненных достижений — `code_task` ≥ 6, `maze` ≥ 11, `plates` ≥ 15 (§6.1);
генерируются только типы, открытые к старту забега.

**`code_task`.** Команды (открываются вместе с событием `code_task`, порог 6 достижений), обе требуют игрока в 1,5 тайла от обелиска:

| Команда | Цена | Что делает |
|---|---|---|
| `puzzle(obj)` | 1 | таблица `{kind, text, input, limit, solved}`; `input` — массив чисел |
| `solve(obj, answer)` | 2 | `true` или `nil, "wrong answer"` (как у остальных ошибок команд); **одна попытка на обелиск за запуск кода** — после неверного ответа до конца запуска `nil, "...try again in the next run"` (вход в новом запуске другой, так что перебор растягивается на много запусков, а их 3 в день) |

`input` генерируется заново в начале **каждого запуска кода** — ответ нужно вычислить кодом в том же
запуске (подсмотреть и вписать вручную не выйдет). Решённый обелиск: `solved = true`, `PuzzleSolved` (Id = `code_task`).

| kind | text (UI) | input | Ответ | Нужно | Энергия решения | Награда (до конца забега) |
|---|---|---|---|---|---|---|
| `sum` | Return the sum of all numbers in input. | 12 целых 1–50 | число | `for` | 1 + 12 + 2 = 15 | `BaseEnergyLimit` +10 на этот забег и +10 энергии сейчас |
| `max` | Return the largest number in input. | 12 целых 1–99 | число | `for`, `if` | 1 + 12 + 6 + 2 = 21 | здоровье до максимума и `MaxHealth` +20 на этот забег |
| `count` | Return how many numbers in input are greater than limit. | 12 целых 1–99, `limit` случайный 10–89, ответ равномерно 0–12 (перебор закрыт правилом одной попытки за запуск) | число | `for`, `if` | 1 + 12 + 6 + 2 = 21 | `ThreatDay` +1, если угроза ещё не началась; иначе как `sum` |

Окупается: `sum` за 15 энергии даёт +10 в каждый оставшийся день.

**`maze`.**

| Параметр | Значение |
|---|---|
| Размер | 7×7 тайлов (идеальный лабиринт 3×3 клетки, стены — тайлы-препятствия с коллайдером: камни/кусты) |
| Вход | один, в середине случайной стороны |
| Цель | центральный тайл, на нём `maze_chest`; игрок встал на центр → `PuzzleSolved` (Id = `maze`), сундук роняет `crystal` ×2 |
| Видимость внутри | «густая изгородь»: туман внутри лабиринта снимается только в радиусе 1 → `check` работает лишь по соседним тайлам, путь не подсмотреть |
| Команды | `move`, `check` (есть с забега 1), `while`/`repeat` |
| Энергия | правило правой руки: на шаг `check` 1–2 + `if` 0,5–1 + `move` 1 + `while` 1 ≈ 4–5; путь с тупиками 8–14 шагов → **35–70** (один день). Позиция сохраняется между запусками — алгоритм без состояния просто продолжает |
| `crystal` | `MaterialResource`, durability 5, stack 20; нужен для `watchtower` |

### 10.5. Этап 5: туториал первого забега

Шаги идут по очереди; шаг с условием «Next» закрывается кнопкой. Все шаги пропускаются кнопкой «Skip tutorial»,
флаги — в `MetaProgress`. Понятия (переменная, условие, цикл, функция) объясняет `UnlockPopup` при открытии,
туториал только отсылает к нему.

| # | Элемент UI | Текст | Переход |
|---|---|---|---|
| 1 | экран хаба, Start Run | Welcome to camp. Start a run to explore a new island. | забег начат |
| 2 | главное окно кода | You can't move the hero directly. Write Lua code here — the hero does what the code says. | Next |
| 3 | главное окно кода | Type `obtain()` — this is a command call. It walks to the nearest item and picks it up. | в коде есть `obtain(` |
| 4 | колонка цены строк | This number is the energy cost of the line. Ready-made commands like `obtain` are convenient but expensive. | Next |
| 5 | кнопка Run | Press Run to execute your code. | `RunStarted` |
| 6 | полоса энергии (TopBar) | Energy is your daily budget. Every command spends it, even failed ones. | `RunFinished` |
| 7 | InventoryBar | Gathered items go here. Gather 3 items to unlock cheaper commands and conditions. (v2, §6.2) | `first_harvest` выполнено |
| 8 | главное окно кода | Arguments go inside the brackets: `obtain("stick")` picks up only sticks. | Next |
| 9 | счётчик запусков/дня (TopBar) | You have 3 runs per day. After the last one the day ends and energy refills. | Next |
| 10 | кнопка Help | Help lists every command you have and its cost. | Help открыта |
| 11 | главное окно кода (после попапа `first_harvest`) | `local items = findObjects("stick")` stores a list in a variable. Reuse it instead of searching again — it saves energy. | запуск с `local` |
| 12 | кнопка CraftingPanel | Recipes are listed here. Crafting is done in code: `craft("flint")`, then `craft("axe")`. | `Crafted` axe |
| 13 | главное окно кода (после попапа `first_axe`) | Trees need an axe: `mine(tree)` chops it and drops logs on the ground. Pick them up with `harvest`. | `Mined` |
| 14 | кнопка End Day | End the day early to refill energy. Ending with less than 1 energy lowers tomorrow's limit. | Next |
| 15 | кнопка Achievements | Achievements unlock new commands and language features. | панель открыта |
| 16 | главное окно кода (с дня 2; шаг `portal_exit`) | To return to camp, stand next to the portal and call `enterPortal()` — `findObjects("portal")` shows where it is. Only materials are kept — and nothing if you die. | Next |
| 17 | TopBar, угроза (в начале дня `threatDay − 1`) | Goblins will patrol from tomorrow. Use `findObjects("enemy")` to see them before they see you. | Next |
| 18 | кнопка «+» / вкладка библиотеки (после попапа `classes`) | Extra windows become functions named after the title. Drag a window onto the library to save it. | Next |

Каждый шаг: подсветка рамкой + стрелка к элементу, не блокирует ввод. Шаги 11–13, 17, 18 ждут своего события
и не показываются, если оно уже случилось (игрок с прошлого забега).

### 10.6. Проверка баланса энергии (по ассетам на 2026-10-19)

**Цены.**

| Что | Цена | Источник |
|---|---|---|
| `findObjects`, `harvest`, `move`, `check`, `craft`, `drop`, `getInventory`, `getStatus`, `getPosition`, `mine` | 1 | `Commands/*.cs` |
| `moveTo` | 2 | |
| `build` | 3 | |
| `obtain` | 8 | аналог `findObjects():getNear()` + `moveTo` + `harvest` = 5 → ×1,6 |
| `autoExplore` | 25 → **10** (+10 за каждый шаг `autoExplore(type)` после первого) | аналог `check` + `move(dir, 6)` ≈ 2–3; сама выбирает направление к неоткрытой суше (§9, решение 12) |
| метод списка (`getNear`, `where`…) | 1 | `LuaObjectList.MethodCost` |
| итерация `for` / проверка `while`, `until` | 1 | `DefaultEnergyConfig` |
| `if` / `elseif` | 0,5 | |
| `else`, `local`, `return`, вызов функции | 0 (функция → **0,5**) | |
| добыча `mine` | Σ durability × amount лута: дерево 2 × 5 + 2 × 1 = 12, валун 3 × 4 + 1 × 1 = 13 | `Tree_Common`, `Boulder_Common`, `Log`, `Stone`, `Stick`, `Flint` |
| рецепты | `flint` 1 (2 камня), `axe` 2, `workbench` 3, `pickaxe` 3, `bed` 3 | `RecipeBook` |
| износ | топор и кирка — 20 использований | `Axe`, `Pickaxe` |

**Выводы.**

| # | Наблюдение | Решение |
|---|---|---|
| 1 | Все команды > 0, готовые дороже базового аналога | соблюдено |
| 2 | Расчёт `efficient_logger` (§6.1) совпадает с ассетами: дерево = 1 + 12 | без изменений |
| 3 | `autoExplore` ×12–25 к аналогу | 10 (§9) |
| 4 | Вызов функции 0 + `and`/`return` 0 → рекурсия без `if` — бесплатный цикл (`return n > 0 and f(n - 1)`) | `functionCost` 0,5: рекурсия ≈ цикл; обычные функции почти не дорожают (вызов 10 раз = 5) |
| 5 | `moveTo` стоит 2 при любой дальности | оставить: дальний путь «платит» временем — во время хода идут патрули |
| 6 | `apiCallCost`, `variableCost` в `EnergyConfig` не используются | удалить/задокументировать |
| 7 | Камни с земли (`harvest` 1 + `moveTo` 2 + `getNear` 1 ≈ 4 за штуку) против валуна (`mine` 14 → 3 камня + кремень) — паритет, кирка выгодна только серией | без изменений |
| 8 | Бой: меч окупается в первом же бою (6 против 16 энергии и 10 против 30 здоровья) | без изменений |
| 9 | Еда для энергии: +6 чистыми за куст, ~+120 за забег | не ломает дневной лимит |
| 10 | Скриптовое прохождение забега 1: палок ~1,5 на 100 тайлов суши, обзор 4 тайла — у спавна в среднем < 1 палки; за день 1 и начало дня 2 (≈85 энергии) собрано 2 камня, 0 палок, ближайшие палка/дерево в 7 тайлах. Цепочка «кремень → топор → верстак» (§6.1) за дни 1–2 недостижима, туториал ломается | гарантированный стартовый набор в забеге: `MapGenerationConfig.starterResources` (палки 4, камни 4, деревья 2) досыпается `ObjectGenerator` в кольце 3–5 тайлов от спавна — всё видно сразу |
| 11 | Повторное прохождение со стартовым набором: при обзоре 4 стоит дойти до одного предмета набора — остальные (по другую сторону спавна) пропадают из вида; `obtain`/`findObjects` их не видят, день 1 кончился без топора (кремень есть, 13 энергии осталось). `autoExplore` случайным направлением тратил 10 впустую | обзор 4 → 6 (`PlayerConfig.visionRadius`), кольцо набора до 5; `autoExplore` идёт к неоткрытому (решение 12) |
| 12 | Прохождение после №10–11 (обзор 6, кольцо 3–5, новый `autoExplore`): день 1 — 3 × `obtain` + кремень (27), свой сбор по одному `findObjects` + топор (23), рубка 2 деревьев (энергия кончилась в запуске → штраф, день 2 = 85); день 2, запуск 1 — подбор брёвен и палок, верстак + `build` (37). `first_harvest` → `first_axe` → `workbench_built` за 4 запуска, ≈135 энергии | темп §6.1 (≈110 за дни 1–2) подтверждён; урок «кончилась энергия → штраф» срабатывает на 1-м дне — туториалу стоит предупредить заранее (шаг про энергию) |
| 13 | Сквозное прохождение в Play Mode через `LuaSandbox` (2026-10-24, с нового сохранения, 3 забега): прогрессия работает целиком — 14 из 18 достижений получены вживую с наградами (`first_harvest`, `first_bug`, `explorer`, `first_axe`, `copy_paste`, `workbench_built`, `threat_survivor`, `enemy_spotted`, `hub_builder`, `code_solver`, `first_blood`, `maze_runner`, `loop_lover`, `first_meal`); выход переносит только материалы; `storage` даёт 12 слотов; обелиск `sum` → +10 к лимиту; лабиринт → `crystal` ×2; ягоды +6. Не проверены вживую: `efficient_logger`, `lumberjack`, `sweet_dreams`, `function_fan`. Замечания: (а) `enemy_spotted` бывает уже в 1-й день — `autoExplore` выводит к лагерю; (б) кулаком против лагеря: 8 ударов на гоблина, здоровье 100 → 10 (подтянулись соседи); (в) лабиринт: обход «левой рукой» с `getPosition` и перебором 4 направлений ≈ 9 энергии на шаг, путь 11 шагов не помещается в день — проходится за 2 дня, т. к. позиция сохраняется (оценка §10.4 — 4–5 на шаг, при экономном коде); (г) текст задачи `puzzle().text` английский и на русском языке (по правилу «Lua-API не переводится») | (а), (в) — оставить; (б) — проверить вручную, не слишком ли больно (задумано: без меча бой невыгоден); (г) — решено 2026-10-24: `text` переводится (§11) |

## 11. Локализация

| Решение | Как |
|---|---|
| Языки | английский и русский |
| Исходник | английский: тексты в коде, UXML и ассетах; русский — таблицы «английский → русский» (`Loc.T` / `Loc.F`, `Scripts/Localization`) |
| Lua-API не переводится | имена команд, типов, поля таблиц, строки ошибок, которые возвращают команды, — одинаковы на обоих языках (игрок сравнивает их в коде). Переводятся справка, описания, подсказки, достижения, открытия |
| Выбор языка | в лагере (настройка), сохраняется в `MetaProgress`, применяется перезагрузкой сцены |
| Первый запуск | язык системы (русский → RU, иначе EN) |
| Исключение | `puzzle().text` — описание задачи для человека, в коде его не сравнивают → переводится (`Loc.T` в `LuaPuzzles`, 2026-10-24); `kind`, `type`, `input`, `limit` — нет |
