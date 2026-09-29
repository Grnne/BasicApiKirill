# BasicAPI - Docker Deployment

## Что это?
Web API с Swagger документацией и SignalR чатом для тестирования.

## Быстрый старт

### 1. Установите Docker
- Windows: https://docs.docker.com/desktop/install/windows-install/
- Mac: https://docs.docker.com/desktop/install/mac-install/
- Linux: `sudo apt install docker.io`

### 2. Клонируйте репозиторий

### 3. Запустите API
Откройте терминал в этой папке и выполните:
docker-compose up -d

если необходимо поменять порт, задайте порт вручную через env, например:
для powershell:
$env:HOST_PORT=9090; docker-compose up -d
для bash:
HOST_PORT=9090 docker-compose up -d

Для остановки выполните:
docker-compose down

## Проверки перед мёржем

CI пока нет — перед каждым мёржем полный прогон вручную (нужен запущенный Docker
для интеграционных тестов):

```powershell
./scripts/test.ps1              # сборка, юнит- и интеграционные тесты, аудит пакетов
./scripts/test.ps1 -Image       # плюс сборка docker-образа (перед деплоем)
```

Строку-итог из конца вывода — в описание PR или коммита. Деплой — вручную по
[docs/deploy.md](docs/deploy.md). После деплоя — сквозные тесты против стека и ручная
проверка клиента:

```powershell
./scripts/e2e.ps1 -BaseUrl https://<домен>   # без параметра — локальный прод-стек
```

Чек-лист ручной проверки — [docs/manual-checklist.md](docs/manual-checklist.md).

## Документация

| Ресурс | URL | Описание |
|--------|-----|---------|
| REST API (Swagger) | `/swagger` | REST эндпоинты (чаты, сообщения, пользователи, аутентификация) |
| SignalR Hub | `/signalr-docs` | Документация по SignalR хабу (методы и события) |
| SignalR endpoint | `/hubs/chat` | WebSocket endpoint для подключения к чату |
| Изменения контрактов | [docs/api-contract-changes.md](docs/api-contract-changes.md) | Что поменялось для клиентов: новые поля, ручки, события, коды ошибок |
| Пропускная способность | [docs/capacity-and-limits.md](docs/capacity-and-limits.md) | Сколько выдерживает сервер, что упирается первым (по замерам) |
| Нагрузочный прогон | [docs/load-testing.md](docs/load-testing.md) | Как запустить `tools/BasicApi.LoadTest`, базовые цифры |
| Деплой | [docs/deploy.md](docs/deploy.md) | Ручной деплой в прод: бэкап, запуск, проверка, откат |
| Ручная проверка клиента | [docs/manual-checklist.md](docs/manual-checklist.md) | Чек-лист, известные проблемы клиента, журнал прогонов |
| Ревью и архитектура бэкенда | [docs/backend-roadmap.md](docs/backend-roadmap.md) | Ревью, позиционирование, ключевые решения |
| План 1: рефакторинг | [docs/plan-1-refactoring.md](docs/plan-1-refactoring.md) | Фундамент, дыры, ядро архитектуры, стабилизация |
| План 2: функционал | [docs/plan-2-features.md](docs/plan-2-features.md) | Базовый набор функций мессенджера |

## SignalR Hub (`/hubs/chat`)

Подключение через WebSocket с JWT в query string:
```
wss://host/hubs/chat?access_token={jwt}
```

Хаб — канал событий; команды есть и в REST. Подробно — `/signalr-docs`.

### Client → Server (вызываемые методы)
- `JoinChat(chatId)` — подписаться на `MessageCreated` чата
- `LeaveChat(chatId)` — отписаться
- `SendMessage(chatId, text)` — отправить сообщение; то же в REST —
  `POST /api/chats/{chatId}/messages`, там же идемпотентность по `clientMessageId`
- `Typing(chatId, isTyping)` — «печатает»; то же в REST — `POST /api/chats/{chatId}/typing`

Ошибки методов — `HubException` с кодом (`NOT_A_MEMBER`, `MESSAGE_EMPTY`,
`MESSAGE_TOO_LONG`, `RATE_LIMITED`).

### Server → Client (события)
- `MessageCreated` — новое сообщение (подписчики чата); в `MessageDto` есть `seq` —
  номер сообщения в чате
- `ChatListUpdated` — превью для списка чатов (все участники, включая отправителя)
- `ChatCreated` — новый чат (когда вас добавили); payload — готовый `ChatListItemDto`
- `UserOnlineChanged` — онлайн/офлайн
- `TypingChanged` — «печатает»

События о сообщениях и чатах доставляются «хотя бы один раз» через outbox и
пишутся в журнал пользователя: пропущенное за время разрыва — `GET /api/sync?since={pts}`,
снимок — `GET /api/sync/state`.

## TODO / возможные оптимизации

- **System.Text.Json Source Generators** вместо рефлексии для сериализации DTO.
  Сейчас `AddControllers()` ([ServiceExtensions.cs](BasicApi/Extensions/ServiceExtensions.cs))
  и ручная сериализация в [ExceptionHandlingMiddleware.cs](BasicApi/Middleware/ExceptionHandlingMiddleware.cs)
  используют дефолтный reflection-based `System.Text.Json`. Нужно завести
  partial `JsonSerializerContext` с `[JsonSerializable(typeof(...))]` под DTO
  из `Models/Dto/**`, подключить его как `TypeInfoResolver`, и переиспользовать
  один `JsonSerializerOptions` в 429-обработчике rate limiter'а (сейчас там
  создаётся новый на каждый reject). Небольшой, но бесплатный выигрыш по CPU
  на каждый запрос — не приоритет, пока не станет узким местом (см. обсуждение
  про то, что текущие боттлнеки — Postgres round-trip и BCrypt, а не GC).
