# CodeRogue (StudyGame)

Рогалик, в котором персонажем управляют **кодом на Lua**. Игрок пишет программу в окне редактора, а она двигает
героя, ищет и добывает ресурсы, крафтит, строит и сражается. Каждая конструкция языка и каждое действие стоят
**энергии** — это дневной бюджет, поэтому свой аккуратный код выгоднее готовых дорогих команд. Между забегами —
лагерь: постройки из принесённых материалов дают бонусы следующим забегам, а достижения открывают новые команды
и конструкции языка (`if`, циклы, функции…). Игра рассчитана и на тех, кто никогда не программировал: туториал,
подсказки к ошибкам, подсветка выполняемой строки, точки останова и просмотр переменных.

Интерфейс — на русском и английском (переключатель в лагере).

## Установка

1. **Git LFS обязателен** — картинки, DLL и прочие бинарные файлы лежат в LFS. До клонирования:
   ```
   git lfs install
   git clone https://github.com/BaseUser3007/StudyGame.git
   ```
   Без LFS вместо файлов придут текстовые заглушки, и проект не скомпилируется.
2. **Unity 6000.4.10f1** (Unity Hub → Add project from disk).
3. Первое открытие: Unity скачает пакеты из `Packages/manifest.json` (UniTask, NuGetForUnity, связка Lua для Unity,
   MCP for Unity — последний нужен только для работы с Claude Code). Lua-CSharp 0.5.5 уже лежит в `Assets/Packages`
   (его же восстанавливает NuGetForUnity по `Assets/packages.config`).
4. Открыть сцену `Assets/_Game/Scenes/Game/SampleScene.unity` и нажать Play. Игра стартует в лагере → **Start Run**.

## Проверка

- Тесты: `Window → General → Test Runner → EditMode → Run All`.
- Быстрая проверка компиляции без Unity (csproj генерирует Unity, новый файл попадёт в него после открытия редактора):
  ```
  dotnet build Assembly-CSharp.csproj -nologo -v q
  ```
- Сброс прогресса: меню **CodeRogue → Reset Meta Progress** или кнопка Reset progress в лагере.
  Сохранение — `meta_progress.json` и библиотека кода `code_library.json` в `Application.persistentDataPath`
  (на Windows: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\StudyGame`).

## Структура `Assets/_Game`

| Папка | Что внутри |
|---|---|
| `Scripts/Core` | `Level` — точка сборки всех систем, шины событий, день и угроза, переход лагерь ↔ забег, мета-прогресс |
| `Scripts/Lua` | песочница Lua, команды (`Lua/Commands`), учёт энергии, открытие команд и конструкций, отладчик |
| `Scripts/UI` | интерфейс на UI Toolkit: окна кода и редактор, верхняя панель, инвентарь, крафт, лагерь, туториал |
| `Scripts/Map`, `Objects`, `Player` | генерация карты, туман, объекты и добыча, движение и инвентарь игрока |
| `Scripts/Crafting`, `Structures` | рецепты и постройки в забеге |
| `Scripts/Enemies`, `Puzzles`, `Hub`, `Achievements` | враги и бой, головоломки, постройки лагеря, достижения |
| `Scripts/Localization` | русские таблицы перевода (`Loc.T` / `Loc.F`) |
| `ScriptableObjects` | настройки и контент: конфиги, предметы, рецепты, достижения, враги, постройки |
| `Prefabs`, `Tiles`, `Animations` | префабы объектов, тайлы, анимации |
| `Tests/Editor` | EditMode-тесты NUnit |

Графика — пак `Assets/Tiny Swords` (сторонний, не менять).

## Документы

- [Docs/GameDesign.md](Docs/GameDesign.md) — замысел игры, баланс, решения.
- [Docs/Roadmap.md](Docs/Roadmap.md) — этапы и что уже сделано.
- [Docs/Tasks/README.md](Docs/Tasks/README.md) — **задачи для участников**: как взять задачу, какие файлы можно трогать.
- [Docs/PlaytestChecklist.md](Docs/PlaytestChecklist.md) — ручная проверка среза.
- [CLAUDE.md](CLAUDE.md) — подробная архитектура и правила кода.
