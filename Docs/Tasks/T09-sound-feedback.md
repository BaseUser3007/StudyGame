# T09. Звуки действий

**Область:** Аудио · **Размер:** M · **Не брать одновременно с:** —

## Зачем
В игре нет звука. Короткие звуки на действия помогают понять, что делает код, не глядя на мир.

## Что сделать
1. `AudioConfig` (ScriptableObject, меню `CodeRogue/Audio/AudioConfig`): клипы и громкость на события.
2. `AudioFeedback` (plain class + один-два `AudioSource` на runtime-объекте), подписан на `GameEventBus.OnGameEvent`,
   `OnRunStarted` / `OnRunFinished`: подбор (`Harvested`), добыча (`Mined`), крафт (`Crafted`), постройка (`Built`,
   `HubBuildingBuilt`), победа над врагом (`EnemyKilled`), урон игроку (`EnemyManager.OnPlayerAttacked`), еда (`FoodEaten`),
   конец запуска (`RunFinished`: успех / ошибка / нет энергии — разные звуки), достижение (`AchievementTracker.OnAchievementCompleted`).
3. Один и тот же звук — не чаще N раз в секунду (цикл из 20 `harvest` не должен трещать).
4. Кнопка «Звук вкл/выкл» (или ползунок) в лагере; значение — новое поле в `MetaProgress` со значением по умолчанию.

Звуки — CC0 (kenney.nl, freesound CC0 и т. п.); источник и лицензия — в `Assets/_Game/Audio/LICENSES.md`.

## Файлы
- новые `Scripts/Audio/*.cs`, `Assets/_Game/Audio/**` (LFS), ассет `ScriptableObjects/AudioConfig.asset`
- `Scripts/Core/Level.cs` (горячий) — поле `audioConfig`, создание, `Dispose`; `SampleScene.unity` — привязать ассет
- `Scripts/Core/MetaProgress.cs` (горячий) — поле громкости; `UI/GUI/HubPanel.uxml` + `RunScreens.cs` — переключатель
- `Scripts/Localization/LocRuT09.cs`

## Готово, когда
- [ ] Звуки на все события выше; без `AudioConfig` на `Level` игра работает молча (warning, как у других конфигов).
- [ ] Настройка звука переживает перезапуск игры.
- [ ] Нет исключений и двойных звуков после перехода лагерь ↔ забег (отписки от статических шин).
