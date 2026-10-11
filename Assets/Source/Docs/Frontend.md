# Frontend — главное меню, лобби, сохранения рейсов

Заменяет Fusion Sample Menu собственным флоу. Код: `Assets/Source/Scripts/Frontend/`,
редактор: `Assets/Source/Scripts/Editor/Frontend/FrontendSetup.cs`, сохранения:
`Assets/Source/Scripts/Gameplay/World/Saves/VoyageSave.cs`.

## Сборка

`RumOverboard ▸ Frontend ▸ Build Menu Scenes` — создаёт `Assets/Scenes/MainMenu.unity` и
`Assets/Scenes/Lobby.unity` (с запечённым scene-NetworkObject `NetworkLobby`), переключает
`GameBootstrap.menuScene` в `Boot` на `MainMenu` и выставляет порядок Build Settings:
`Boot → MainMenu → Lobby → WorldMapScene → остальное`. Если `WorldMapScene` ещё нет — собирает и её.

## Флоу

```
Boot ─► MainMenu ─┬─ Новый рейс ──► настройка (имя рейса, seed, лобби, экипаж 1…GameConfig.MaxCrew, открытое/по коду)
                  ├─ Загрузить рейс ► список сохранений ► настройка лобби
                  │        └─ «Создать лобби» ─► Lobby (хост) ─ все готовы ─► «Отплыть» ─► WorldMapScene
                  │        └─ «Одиночный рейс (оффлайн)» ─► WorldMapScene (GameMode.Single, без сети)
                  └─ Присоединиться ─► код (6 символов) или список публичных лобби ─► Lobby / сразу в море
```

- **`GameSession`** — DontDestroyOnLoad-синглтон (создаётся `MainMenuEntry`), владеет `NetworkRunner`
  и фазами `Menu / Connecting / Lobby / Sailing / Leaving`. Хост стартует `GameMode.Host` сразу со
  сценой `Lobby`; «Отплыть» = `runner.LoadScene(WorldMapScene)`, клиенты переезжают вместе с хостом.
- **`NetworkLobby`** (сцена Lobby) — реплицируемый список экипажа: имя (из connection token), флаг
  готовности, капитан. «Отплыть» доступно капитану, когда готовы все остальные.
- **Поздний вход**: лобби остаётся открытым во время рейса; клиент по коду попадает прямо в текущую
  сцену хоста (мир строится по реплицированному seed, `ConnectionManager` спавнит игрока).
- **`ConnectionManager`** в игровой сцене не создаёт свой раннер, если сессию ведёт меню
  (`ConnectionManager.ExternalSessionActive`), а подхватывает существующий.
- **Пауза**: `Esc` в море — код лобби (копировать), «Продолжить / Покинуть рейс / Выйти из игры».
  Выход хоста сохраняет рейс. Пауза не останавливает симуляцию (это кооператив).
- **Имя игрока** — поле на главном экране (`PlayerPrefs`, ключ `ro.profile.name`).

### Отключения

- Хост вышел из **лобби** → клиенты возвращаются в меню («Капитан распустил лобби»).
- Хост вышел во время **рейса** → Fusion host migration (см. `SETUP_MULTIPLAYER.md` §6.4), экипаж
  продолжает плыть; новый хост файл сохранения не пишет (он есть только у исходного капитана).
- Нет раннера дольше 20 с вне меню → возврат в меню с сообщением.
- Photon недоступен → сообщение и кнопка «Играть оффлайн (одиночный рейс)».

## Лобби: Photon сейчас, Steam потом

Всё, что касается поиска/объявления лобби, — за интерфейсом **`ILobbyDirectory`**
(`StartBrowsingAsync`, `Listings`, `ResolveAsync(code)`, `SessionProperties`, `Publish`, `Read`).
Текущая реализация `PhotonLobbyDirectory`: отдельный раннер-браузер в `SessionLobby.ClientServer`,
код лобби = имя Fusion-сессии, свойства сессии `n` (название), `h` (капитан), `d` (рейс),
`s` (0 — лобби, 1 — в море), `v` (`Application.version`; несовпадающие версии в списке недоступны).
Приватное лобби — `IsVisible = false`, вход только по коду.

Для Steam: реализовать `ILobbyDirectory` поверх Steam Lobbies (код → lobby id / join через
оверлей), подменить создание в `GameSession.Awake`; Fusion-транспорт при этом остаётся.

## Сохранения рейсов

- Папка: `Application.persistentDataPath/Voyages/<id>.json` (JsonUtility, атомарная запись).
- Поля: имя, seed, ветер, `Reached`, число чекпоинтов, `Completed`, даты.
- Пишет только хост (`NetworkWorld.HostSaveVoyage`): при первой генерации, при каждом прогрессе
  (новый чекпоинт, смена seed/ветра) и при выходе из рейса.
- Загрузка: тот же seed и ветер → тот же мир; корабль появляется пришвартованным у **последнего
  достигнутого** чекпоинта (`NetworkWorld.StartCheckpoint`), туман открыт по прогрессу.
- Передача рейса из меню в мир — `VoyageLaunch.Active`.

## Автотесты (batch)

```
# соло: меню, браузер лобби (Photon), коды, приватное лобби → Отплыть → мир с seed рейса,
# сохранение, пауза, автосейв прогресса, выход; затем офлайн-загрузка: тот же мир, старт у чекпоинта 2
Unity -batchmode -projectPath . -executeMethod RumOverboard.EditorTools.Frontend.FrontendSetup.RunMenuSmoke -menuSmoke

# два процесса: собрать Win64-плеер, хост в плеере, клиент в редакторе (или второй плеер)
Unity -batchmode -projectPath . -executeMethod RumOverboard.EditorTools.Frontend.FrontendSetup.BuildTestPlayer -menuOut Builds/MenuSmoke
Builds/MenuSmoke/RumOverboard.exe -batchmode -nographics -menuSmokeHost ABC234 1 -profileName Host -logFile host.log
Unity -batchmode -projectPath . -executeMethod RumOverboard.EditorTools.Frontend.FrontendSetup.RunMenuSmoke -menuSmokeClient ABC234 -profileName "Smoke Client"
```

`-menuSmokeHost CODE N` ждёт клиента в лобби (имя + готовность), отплывает, при `N > 1` ждёт ещё
и позднего входа; `-menuSmokeClient CODE` входит, жмёт «Готов», плывёт/входит поздно и сверяет
контрольную сумму мира. Код возврата 0 = всё прошло (`[MenuSmoke] RESULT: ALL PASSED`).
Сохранения тестов пишутся во временную папку.
