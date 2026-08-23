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

## Документация

| Ресурс | URL | Описание |
|--------|-----|---------|
| REST API (Swagger) | `/swagger` | REST эндпоинты (чаты, сообщения, пользователи, аутентификация) |
| SignalR Hub | `/signalr-docs` | Документация по SignalR хабу (методы и события) |
| SignalR endpoint | `/hubs/chat` | WebSocket endpoint для подключения к чату |

## SignalR Hub (`/hubs/chat`)

Подключение через WebSocket с JWT в query string:
```
wss://host/hubs/chat?access_token={jwt}
```

### Client → Server (вызываемые методы)
- `JoinChat(chatId)` — подписаться на события чата
- `LeaveChat(chatId)` — отписаться от событий чата
- `SendMessage(chatId, text)` — отправить сообщение
- `Typing(chatId, isTyping)` — статус печатания

### Server → Client (события)
- `MessageCreated` — новое сообщение (получают подписчики чата)
- `ChatListUpdated` — превью сообщения для списка чатов (все участники)
- `ChatCreated` — новый чат (когда вас добавили)
- `UserOnlineChanged` — онлайн/офлайн статус
- `TypingChanged` — статус печатания собеседника

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
