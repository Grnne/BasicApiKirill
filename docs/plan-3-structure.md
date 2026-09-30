# План 3. Структура по функциям

Цель: код одной функции — в одной папке. Сейчас функция размазана по `Features/` (контроллер),
`Services/` (сервисы), `Models/Dto/` (DTO) и `Extensions/ServiceExtensions.cs` (регистрация):
push из плана 2 лёг в пять мест. После плана — `Features/<Функция>/` с контроллером, сервисами,
DTO и регистрацией; общее для нескольких функций остаётся в `Services/` и `Models/`.

Подход — feature slices + pragmatic clean (решение 2026-10-01): без отдельных проектов Domain
и Application, без MediatR, сервисы по функциям. Правила зависимостей — соглашение,
записанное в `docs/architecture.md` и `CLAUDE.md`; отдельного архитектурного теста нет, после
каждого шага — обычный полный прогон (решение 2026-10-01).

Попутно: лишние комментарии в коде убираются, остаются только критически важные, аннотации
классов — короче; устаревшая и дублирующая документация удаляется из репозитория.

---

## Рамки

**Меняется:** раскладка файлов в `BasicApi` и в тестовых проектах, namespace перенесённых
классов, регистрация сервисов, `ChatsController` делится на два, комментарии, состав документации.

**Не меняется:**
- поведение, API, SignalR, БД — ни одного изменения контракта и миграций;
- проект `BasicApi.Storage` — репозитории, сущности, миграции остаются как есть;
- сквозное техническое — `Hubs/`, `Middleware/`, `Extensions/` остаются на месте: отдельная
  папка для cross-cutting пока не нужна;
- фронт;
- XML-документация ручек контроллеров и полей DTO — это описание API в Swagger, её не режем.

---

## Целевая структура

```
BasicApi/
  Features/
    Auth/        AuthController, AuthService, SessionService, JwtService (+ интерфейсы), DTO входа
    Devices/     DevicesController, DeviceService, DTO
    Push/        PushController, PushService, PushSender, WebPushTransport, PushOptions/VapidKeys, DTO подписки
    Chats/       ChatsController (список, личные, «Избранное», закреп/архив/mute, карточка, поиск чатов),
                 ChatService, ChatStateService, DTO карточки и участников
    Messages/    MessagesController (история, отправка, пересылка, правка, удаление, реакции, черновики,
                 «печатает», прочтение, галерея, поиск в чате), SearchController (глобальный поиск),
                 MessageService, ReactionService, ReadStateService, DraftService, DTO запросов
    Groups/      GroupsController, GroupService, GroupOptions, DTO запросов групп
    Folders/     FoldersController, FolderService, DTO запросов папок
    Media/       MediaController, MediaService, ObjectStorage (S3), MediaSniffer, ImagePreviews,
                 MediaCleanup, Avatars, MediaOptions/StorageOptions, DTO загрузки
    Users/       UsersController, UserService, ProfileService, PrivacyService, DTO профиля и поиска
    Sync/        SyncController, SyncService, DTO снимка и журнала
    (в каждой)   <Функция>Feature.cs — AddXxxFeature(): регистрация её сервисов
  Services/      общее для нескольких функций (см. ниже)
    Events/      outbox, журнал, публикаторы событий, очередь push
  Models/        общие DTO: сообщение, карточка чата, полезные нагрузки событий, снимок
  Hubs/  Middleware/  Extensions/   — без изменений (Extensions худеет: регистрация уходит в функции)
BasicApi.Storage/                     — без изменений
BasicApi.Tests/
  Features/<Функция>/   юнит-тесты сервисов функции
  Services/  Hubs/  Middleware/  Extensions/  Models/  — тесты общего, как в коде
  TestDoubles/
BasicApi.IntegrationTests/
  Features/<Функция>/   тесты ручек и сценариев функции
  Platform/             сквозное: health, лимиты, заголовки, хаб, outbox, конфигурация, новое устройство
  Migrations/  Repositories/  E2E/  Infrastructure/
```

Namespace = папка: `BasicApi.Features.Messages` и т. д.; DTO функции лежат в её папке плоско
(`MessageRequests.cs`) в том же namespace — без подпапки `Dto`, чтобы не множить `using`.

---

## Правила зависимостей

| Кто | Может зависеть от | Не может |
|---|---|---|
| Функция `Features/X` | `Services/`, `Models/`, `BasicApi.Storage`, `Hubs/` (реестр соединений); другой функции — только от разрешённого списка ниже | внутренних классов других функций |
| Общее `Services/`, `Models/` | друг от друга, `BasicApi.Storage`, `Hubs/` | любой функции |
| Входные точки: контроллеры, `ChatHub` | своей функции и общего; хаб — любых функций (он общий транспорт команд) | репозиториев напрямую |
| Корень композиции: `Program.cs`, `Extensions/` | всего | — |

Разрешённые связи между функциями — по одной причине на каждую:
- **Messages → Media** (`IMediaService`): вложения сообщений — файлы медиа.
- **Sync → Chats, Folders, Users** (`IChatService`, `IFolderService`, `IPrivacyService`,
  `IUserService`): снимок собирает состояние всех функций по определению.
- **Auth, Devices → `Hubs`** (`HubConnectionRegistry`): выход разрывает соединения.

Новая связь между функциями — только осознанно: либо дописать сюда с причиной, либо вынести
общее в `Services/`. Правило «общее» — код попадает в `Services/`/`Models/`, только когда им
реально пользуются хотя бы две функции.

---

## Карта переноса

Составлена по фактическому использованию типов (кто на кого ссылается), 2026-10-01.

### Остаётся в `Services/` (общее)

| Файл | Кто пользуется |
|---|---|
| `ChatPolicy`, `MembershipService`, `GroupRights` | права: сообщения, группы, черновики, реакции, прочтение, присутствие, хаб |
| `MessageOptions` | политика и реакции |
| `PresenceService`, `IUserStatusService`, `UserStatusService` | чаты, группы, пользователи, приватность, хаб, push |
| `MessageMapper`, `MessageStatuses`, `SystemMessages` | сообщения, группы, реакции |
| `ChatListItemMapper`, `ChatStates` (только статические помощники) | чаты, папки, группы, сообщения |
| `MessageAttachments` (из `Services/Media/`) | мапперы сообщений и карточек |
| `PostgresHealthCheck` | health checks (сквозное, не выделяем) |
| `Events/*` + **очередь push** (`PushJob`, `PushQueue`, `PushNotifications` из `Services/Push/PushSender.cs`) | все функции пишут события; диспетчер outbox кладёт задания в очередь push |

### Остаётся в `Models/` (общие DTO)

- Сообщение: `MessageDto` (+ `MessageReplyDto`, `MessageForwardDto`, `MessageActionDto`),
  `MessageEntityDto`, `MessageDeletedDto`, `ReceiptDto`, `ReactionCountDto`, `MessageReactionsDto`,
  `AttachmentDto`, `CursorPaginatedResponse<T>`, `MessageEntities`, `MessageText`.
- Карточка чата: `ChatListItemDto`, `DraftDto`.
- Полезные нагрузки событий и снимка: `DraftUpdatedDto`, `ReadStateDto`, `PinnedChatsDto`,
  `ChatStateDto`, `FolderDto`, `FoldersDto`, `GroupMemberDto`, `GroupPermissionsDto`,
  `PermissionsPatchDto`, `MembersAddedDto`, `MemberRemovedDto`, `MemberUpdatedDto`,
  `ChatUpdatedDto`, `ChatDeletedDto`, `UserUpdatedDto`, `PrivacySettingsDto`,
  `BlockListChangedDto`, `OwnProfileResponseDto`, `PushNotificationDto`.
- Присутствие (возвращает общий `PresenceService`): `UserStatusResponseDto`,
  `UserStatusBatchRequestDto`, `TypingStatusResponseDto`.
- `SetAvatarDto` — аватар пользователя и группы.

### Переезжает в функции

| Функция | Сервисы | DTO | Контроллеры |
|---|---|---|---|
| Auth | `AuthService`, `SessionService`, `ISessionService`, `JwtService`, `IJwtService` | все из `Models/Dto/Auth` | `AuthController` |
| Devices | `DeviceService` | `DeviceDtos` | `DevicesController` |
| Push | `PushService`, `PushOptions` (+`VapidKeys`), `PushSender` (только класс отправщика), `WebPushTransport` | `PushConfigDto`, `PushSubscriptionDto`, `PushSubscriptionKeysDto` | `PushController` |
| Chats | `ChatService`, `IChatService` (+`PrivateChatResult`), `ChatStateService` + `IChatStateService` (из `ChatStates.cs`) | `ChatDetailDto`, `ChatParticipantDto`, `SearchChatsResponseDto`, `SetPinnedDto`, `SetArchivedDto`, `SetMutedDto` | `ChatsController` (часть) |
| Messages | `MessageService`, `ReactionService`, `ReadStateService`, `DraftService` | `SendMessageDto`, `EditMessageDto`, `ForwardMessagesDto` (+ответ), `MarkMessageReadDto`, `MarkUnreadDto`, `SaveDraftDto`, `SetReactionDto`, `TypingDto`, `SearchMessagesResponseDto`, `GlobalSearchDtos` | `MessagesController` (из `ChatsController`), `SearchController` |
| Groups | `GroupService`, `GroupOptions` | `CreateGroupDto`, `AddMembersDto`, `UpdateGroupDto`, `SetRoleDto`, `AuditEntryDto`, `AuditPageDto` | `GroupsController` |
| Folders | `FolderService` | `SaveFolderDto`, `FolderOrderDto` | `FoldersController` |
| Media | `MediaService`, `MediaOptions`/`StorageOptions`, `ObjectStorage`, `MediaSniffer`, `ImagePreviews`, `MediaCleanup`, `Avatars` | `CreateUploadDto`, `UploadTicketDto`, `MediaLinksRequestDto`, `MediaLinkDto`, `MediaLinksDto` | `MediaController` |
| Users | `UserService`, `ProfileService`, `PrivacyService` | `UpdateProfileDto`, `SearchUsersResponseDto`, `UserIdResponseDto`, `UserProfileResponseDto` | `UsersController` |
| Sync | `SyncService` | `SyncDtos` | `SyncController` |

Отдельной функции «Search» нет: глобальный поиск — метод `MessageService`, его контроллер
живёт в `Messages`. Состав смешанных файлов сверен по использованию типов; на шаге С1 его ещё раз проверит
компилятор.

### Делится `ChatsController` (660 строк)

- `Features/Chats/ChatsController` — `GET /api/chats`, `page`, закреп, архив, mute, личный чат,
  «Избранное», карточка (`{chatId}`, `{chatId}/item`), поиск чатов.
- `Features/Messages/MessagesController` — всё под `/api/chats/{chatId}/messages…`, `draft`,
  `typing`, `read`, `marked-unread`, `media`.

Маршруты, теги Swagger и коды ответов не меняются — это проверяет снимок OpenAPI (С0).

---

## Шаги

Каждый шаг — отдельный коммит, после каждого — `scripts/test.ps1` и снимок OpenAPI без изменений.
Переносы и правки кода — в разных коммитах, чтобы git распознавал переименования и история
файлов (`git log --follow`, blame) не рвалась.

| # | Что | Проверка |
|---|---|---|
| С0 | **Снимок OpenAPI.** Интеграционный тест сравнивает `/swagger/v1/swagger.json` с файлом в репозитории; обновить снимок — осознанно, переменной окружения. Остаётся навсегда: любое изменение контракта станет видно в диффе. | тест зелёный на текущем коде |
| С1 | **Развести смешанные файлы на месте**, без переносов: DTO-файлы, где рядом запросы одной функции и общие нагрузки событий (`ChatStateDtos`, `DraftDtos`, `FolderDtos`, `GroupDtos`, `ReadStateDto`, `ReactionDtos`, `MediaDtos`, `PushDtos`, `ProfileDtos`); `ChatStates.cs` → помощники и `ChatStateService`; очередь push → `Services/Events/PushQueue.cs`. | тесты, OpenAPI без изменений |
| С2 | **Перенос по функциям**: Auth, Devices, Push, Media, Users, Sync, Folders, Groups, Chats, Messages — файлы в `Features/X/`, namespace, `using`. Тесты — сразу по той же раскладке (структура выше). | тесты, OpenAPI; `git diff -M --stat` показывает переименования, а не удаление и создание |
| С3 | **Деление `ChatsController`** на `ChatsController` и `MessagesController` (одним файлом: сообщения, черновики и прочтение — близкие ответственности). | тесты, OpenAPI без изменений |
| С4 | **Регистрация по функциям**: `Features/X/XFeature.cs` с `AddXFeature()`; в `ServiceExtensions` остаются общие сервисы, хранилище, события, аутентификация, лимиты, SignalR. | тесты; приложение стартует с теми же сервисами |
| С5 | **Комментарии**: убрать пересказ кода и историю («план 1, пункт…»), оставить только критически важное — неочевидные инварианты, безопасность, конкуренцию, причины странных решений; аннотации классов — одна-две строки. Документация ручек и полей DTO (Swagger) не режется. | тесты; OpenAPI отличается только описаниями схем |
| С6 | **Документация**: `docs/architecture.md` (карта проекта, правила зависимостей, путь запроса, события, ключевые решения из ревью и планов 1–2), `CLAUDE.md` (правила работы и ссылка на карту), `readme.md` заново (короткий: что это, запуск, проверки, ссылки). Удалить устаревшее: завершённые планы 1–2 и ревью (`backend-roadmap.md`) — живые решения переезжают в `architecture.md`; `project-analysis.md` и промпт к нему, `workflow/`, `.continue/` (описывают слой Handler, которого нет); `BasicApi.http` (шаблон weatherforecast); ручные `test-*.ps1` в корне (заменены интеграционными тестами и `scripts/e2e.ps1`). | ссылки в оставшихся документах живые |

Оценка: около дня работы вместе с прогонами.

---

## Риски

- **Большой дифф.** ~100 файлов меняют папку и namespace. Смягчение: переносы отдельными
  коммитами без правок логики, снимок OpenAPI и полный прогон после каждого.
- **Категории логов меняются** вместе с namespace (`BasicApi.Services.Push.WebPushTransport` →
  `BasicApi.Features.Push.WebPushTransport`). В `appsettings` фильтров по нашим категориям нет,
  в `deploy.md` строки логов ищутся по тексту — ломаться нечему; если у вас есть фильтры или алерты
  по категориям во внешней системе логов — их надо поправить.
- **Сохранённые данные не зависят от имён типов**: события outbox и журнал хранят JSON без
  имён классов, Swagger берёт имена схем без namespace. Проверено по коду; снимок OpenAPI это
  подтвердит.
- **`Services/` может снова разрастись.** Правило «минимум две функции» в `CLAUDE.md`;
  при ревью новых файлов в `Services/` — спрашивать, чьи они.
- **Граница Chats / Messages размыта** (карточка содержит сообщение, счётчики на стыке) — решено
  явно: всё, что про содержимое и прочтение чата, — Messages; про чат как объект в списке —
  Chats; общее (карточка, мапперы) — в `Services/`/`Models/`.

---

## Решения по вопросам (2026-10-01)

1. **Тесты раскладываются по функциям сразу**, в том же шаге, что и код.
2. **Архитектурного теста нет**: правила зависимостей — соглашение в документации, проверка — обычный
   полный прогон после каждого шага.
3. **`MessagesController` — одним файлом**: сообщения, черновики и прочтение — близкие ответственности.
