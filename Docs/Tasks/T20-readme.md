# T20. README репозитория

**Область:** Документация · **Размер:** S · **Не брать одновременно с:** —

## Что сделать
В корне нет `README.md`. Написать его (на русском, кратко, с одним-двумя скриншотами в `Docs/Images/`):
- что за игра — 3–4 предложения (персонажем управляют кодом на Lua, энергия за каждую конструкцию, забеги ↔ лагерь);
- установка: Unity **6000.4.10f1**, **Git LFS** обязателен (`git lfs install` до клона, иначе битые png/dll),
  NuGetForUnity восстанавливает Lua-CSharp при первом открытии; сцена `Assets/_Game/Scenes/Game/SampleScene.unity`;
- как запустить тесты (Test Runner → EditMode) и быструю проверку компиляции (`dotnet build Assembly-CSharp.csproj`, см. CLAUDE.md);
- структура папок `Assets/_Game` — по одной строке;
- ссылки: `Docs/GameDesign.md`, `Docs/Roadmap.md`, `Docs/Tasks/README.md` (как взять задачу), `CLAUDE.md` (архитектура);
- сброс прогресса: меню `CodeRogue/Reset Meta Progress`; где лежит сохранение (`Application.persistentDataPath`).

Проверить шаги установки на чистом клоне.

## Файлы
- новый `README.md`, `Docs/Images/*` (LFS)

## Готово, когда
- [ ] Человек без контекста по README клонирует, открывает и запускает игру.
