# Изменения контрактов API

Документ для клиентов (Android). Описывает изменения, сделанные по фидбеку
о том, что новый чат приходит без `companionId`, а онлайн-статус нельзя
запросить принудительно.

**Статус:** breaking. Публичного релиза не было, обратная совместимость не поддерживается.

---

## Краткая сводка

| # | Что | Тип изменения |
|---|-----|---------------|
| 1 | `ChatListItemDto` — новое поле `companionUsername` | additive |
| 2 | `POST /api/chats/private/{userId}` — тело ответа теперь `ChatListItemDto` | **breaking** |
| 3 | SignalR `ChatCreated` — один аргумент `ChatListItemDto` вместо `(chatId, dto)` | **breaking** |
| 4 | `GET /api/chats/{chatId}/item` — новая ручка | new |
| 5 | `GET /api/users/{userId}/status` — новая ручка | new |
| 6 | `POST /api/users/status` — новая ручка (батч) | new |
| 7 | `GET /api/chats/search` — теперь отдаёт `companionId`/`companionUsername` | bugfix |

---

## 1. `ChatListItemDto`: новое поле `companionUsername`

Единый тип для списка чатов, поиска чатов, `ChatCreated`, ответа на создание чата
и новой ручки `/item`.

```json
{
  "chatId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "type": "private",
  "title": null,
  "companionId": "0b2c...",
  "companionName": "Alice",
  "companionUsername": "alice",
  "lastMessage": null,
  "unreadCount": 0,
  "lastActivityAt": "2026-08-31T10:12:00Z"
}
```

- `companionId` / `companionName` / `companionUsername` заполнены только для `type: "private"`,
  для групп — `null`.
- Companion — всегда «второй участник с точки зрения того, кому отдаётся объект».
- `lastMessage` — `null`, если в чате ещё нет сообщений.

---

## 2. `POST /api/chats/private/{userId}`

**Было** — только id, клиенту нечего положить в список чатов:

```json
{ "chatId": "3fa85f64-..." }
```

**Стало** — полный `ChatListItemDto` (и для `201 Created`, и для `200 OK`, когда чат уже существовал):

```json
{
  "chatId": "3fa85f64-...",
  "type": "private",
  "title": null,
  "companionId": "0b2c...",
  "companionName": "Alice",
  "companionUsername": "alice",
  "lastMessage": null,
  "unreadCount": 0,
  "lastActivityAt": "2026-08-31T10:12:00Z"
}
```

Поле `chatId` осталось на месте и с тем же смыслом, так что старый парсинг «достать `chatId`»
продолжает работать. Companion в ответе — это **тот, с кем создали чат** (`{userId}` из пути).

---

## 3. SignalR `ChatCreated`

**Было** — два аргумента, и в `companionName` лежало имя *самого получателя* (баг):

```
ChatCreated(chatId: Guid, dto: ChatCreatedEventDto)
```

**Стало** — один аргумент, готовый элемент списка:

```
ChatCreated(item: ChatListItemDto)
```

- `chatId` теперь внутри тела (`item.chatId`).
- Payload собирается **отдельно для каждого получателя**: в `companionId`/`companionName`/
  `companionUsername` лежит создатель чата, а не сам получатель.
- Тип `ChatCreatedEventDto` удалён.

Клиенту достаточно вставить `item` в список чатов как есть и вызвать `JoinChat(item.chatId)` —
дополнительный запрос не нужен.

---

## 4. `GET /api/chats/{chatId}/item` (новая)

Возвращает один чат в форме элемента списка — тот же `ChatListItemDto`, что отдаёт `GET /api/chats`,
собранный для вызывающего.

- `200` — тело `ChatListItemDto`
- `403 NOT_A_MEMBER` — вызывающий не участник чата
- `404 CHAT_NOT_FOUND` — чата нет

Когда использовать: клиент знает только `chatId` (пуш, deep link, событие `ChatListUpdated`)
и хочет отрисовать/обновить ровно одну строку, не перезапрашивая весь список.

Существующая `GET /api/chats/{chatId}` не изменилась — она по-прежнему отдаёт `ChatDetailDto`
со списком участников. Нужен список участников — берите её, нужна карточка для списка — `/item`.

---

## 5. `GET /api/users/{userId}/status` (новая)

Онлайн-статус одного пользователя.

```json
{ "userId": "0b2c...", "isOnline": true }
```

- Оффлайн отдаётся **явным** `"isOnline": false`, а не отсутствием записи.
- `404 USER_NOT_FOUND` — если у вас нет общего чата с этим пользователем.
  Тот же код возвращается и для несуществующего аккаунта: по ручке нельзя проверить,
  зарегистрирован ли кто-то.
- Свой собственный id запрашивать можно.

Когда использовать: вход на экран чата. Событие `UserOnlineChanged` могло сработать,
пока приложение было закрыто, поэтому текущее состояние нужно спросить самому.

---

## 6. `POST /api/users/status` (новая, батч)

```
POST /api/users/status
{ "userIds": ["0b2c...", "7f1a..."] }
```

```json
{
  "items": [
    { "userId": "0b2c...", "isOnline": true },
    { "userId": "7f1a...", "isOnline": false }
  ]
}
```

- Отдаёт запись на каждого запрошенного пользователя, **включая оффлайн**.
- Пользователи, с которыми у вас нет общего чата, молча выпадают из ответа.
- Максимум 200 id за запрос (`400 TOO_MANY_IDS`), пустой список — `400 INVALID_REQUEST`.
  Дубликаты схлопываются.

Когда использовать: вход на экран списка чатов — отправить `companionId` видимых приватных
чатов и получить их статусы одним запросом.

### Отличие от существующей `GET /api/users/status`

`GET /api/users/status` не изменилась: она возвращает **только онлайн** участников
всех ваших чатов, без возможности задать список. Отсутствие пользователя в её ответе
означает «оффлайн». Новые ручки нужны, когда важен явный `false` и точечный набор id.

---

## 7. Bugfix: `GET /api/chats/search`

Поиск чатов терял `companionId` и `companionUsername` при маппинге — приватные чаты
из результатов поиска приходили без id собеседника. Починено; маппинг списка чатов
и поиска теперь общий, так что расхождение не вернётся.

---

## Что осталось на клиенте

Сервер по-прежнему **не** пушит снапшот онлайна при подключении к хабу, и в
`ChatListItemDto` **нет** поля `isOnline`. После реконнекта/холодного старта клиент
обязан сам запросить статусы (п. 5 и 6) — иначе собеседники будут показаны оффлайн
до первого события `UserOnlineChanged`.
