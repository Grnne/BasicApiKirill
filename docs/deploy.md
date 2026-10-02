# Ручной деплой

Проверки гоняет CI (GitHub Actions, `.github/workflows/ci.yml`) на каждый push; прод
обновляется вручную по этой инструкции — автоматического деплоя нет, у CI нет доступа к серверу. Сервер — Ubuntu с Docker,
стек описан в `docker-compose.prod.yml`, секреты — в `.env.prod` (шаблон
`.env.prod.example`). Снаружи открыт только Caddy (80/443, TLS от Let's Encrypt,
конфиг — `deploy/Caddyfile`); API и Postgres доступны лишь во внутренних
docker-сетях.

Во всех командах ниже:

```bash
COMPOSE="docker compose --env-file .env.prod -f docker-compose.prod.yml"
```

## 0. Перед деплоем (на машине разработчика)

1. Коммит, который едет в прод, зелёный в CI: на GitHub у коммита в master — зелёная галочка
   (вкладка Actions, прогон `CI`: задачи «Build, tests, package audit» и «Docker image»). Строка-итог
   прогона — на странице прогона (Summary). Если CI недоступен — то же локально:
   ```powershell
   ./scripts/test.ps1 -Image
   ```
2. Если в коммите есть новые миграции — прочитать их ещё раз: миграции
   необратимы на практике, откат после них — только восстановлением бэкапа (шаг 5).
3. Изменения контракта для клиентов записаны в
   [api-contract-changes.md](api-contract-changes.md).

## 1. Бэкап базы

```bash
cd ~/BasicChatApi
set -a; . ./.env.prod; set +a
mkdir -p ~/backups
BACKUP=~/backups/chat-$(date +%Y%m%d-%H%M).dump
$COMPOSE exec -T postgres pg_dump -U "$DB_USER" -d "$DB_NAME" -Fc > "$BACKUP"
ls -lh "$BACKUP"    # размер не нулевой
```

**Файлы** лежат в томе `basicchat_media_data` хранилища SeaweedFS. Их
бэкап — копия тома; на время копирования хранилище останавливается, чтобы данные тома были
согласованы (сообщения при этом работают, не грузятся только файлы):

```bash
$COMPOSE stop seaweedfs
docker run --rm -v basicchat_media_data:/data:ro -v ~/backups:/backup alpine   tar czf /backup/media-$(date +%Y%m%d-%H%M).tgz -C /data .
$COMPOSE start seaweedfs
```

Сколько хранятся файлы, задают настройки `Media__UnusedFileHours` (неиспользуемые файлы, 24 ч)
и `Media__RetentionDays` (оригиналы старше удаляются, превью остаются; 0 — хранить всегда) в
окружении `basicapi`. Файлы только добавляются и удаляются, миграции их не меняют, поэтому при откате (шаг 5)
том не восстанавливают — он нужен при потере сервера или диска.

## 2. Код и образ

```bash
git fetch --all --tags
git log --oneline -1                   # что стоит сейчас — записать
docker tag basicapi:latest basicapi:previous   # образ для отката
git checkout <commit-или-тег>
$COMPOSE build basicapi
```

## 3. Запуск

```bash
$COMPOSE up -d
$COMPOSE ps                            # basicapi и postgres — healthy
$COMPOSE logs --since 5m basicapi      # миграции применились, нет ошибок
curl -fsS "https://$DOMAIN/health/ready"   # Healthy (через Caddy и TLS)
```

Миграции применяются при старте приложения. Если старт упал на миграции —
сразу к шагу 5. Команда миграции может идти до часа: таймаут запросов из строки подключения
(30 с) к миграциям не относится, а индекс на большой таблице строится минуты — API в это время
ещё не отвечает.

**Нормальные предупреждения при старте** — на них не реагировать:

- `No XML encryptor configured. Key … may be persisted to storage in unencrypted form` —
  ключи Data Protection живут в tmpfs контейнера и пересоздаются при рестарте; для
  JWT-авторизации они не используются.
- `Overriding HTTP_PORTS '8080' and HTTPS_PORTS ''. Binding to values defined by URLS instead`
  — порт задан через `ASPNETCORE_URLS`, это и есть 8080.
- `Cannot load library libgssapi_krb5.so.2` и следом `Error: libgssapi_krb5.so.2: cannot open
  shared object file` — это Npgsql ищет Kerberos, который не используется; строка не из лога
  приложения, хоть и начинается с `Error`.

Всё остальное с `"LogLevel":"Error"` при старте — повод разбираться. Если старт упал с `Invalid configuration` — в `.env.prod`
не задан или слабый секрет (например, `JWT_KEY`, `STORAGE_SECRET_KEY`); приложение не
стартует с JWT-ключом короче 32 байт или с заглушкой `CHANGE_ME`. При обновлении с версии
без файлов в `.env.prod` нужно добавить `STORAGE_ACCESS_KEY` и
`STORAGE_SECRET_KEY` (см. `.env.prod.example`).

## Push-уведомления

Сервер умеет слать WebPush. Без ключей push выключен (клиенту
`GET /api/push/config` отвечает `enabled: false`), остальное работает как раньше. Включить:

```bash
$COMPOSE --env-file .env.prod run --rm --no-deps basicapi --generate-vapid-keys
```

Команда печатает две строки `PUSH_VAPID_PUBLIC_KEY=…` и `PUSH_VAPID_PRIVATE_KEY=…` — вписать их
в `.env.prod` и перезапустить `basicapi`. `PUSH_SUBJECT` — как push-сервисы свяжутся с
владельцем сервера (`mailto:…` или `https://…`, по умолчанию `https://DOMAIN`).

- **Ключи не менять.** Подписки браузеров привязаны к публичному ключу: с новой парой все
  подписки перестают работать, пользователям придётся подписаться заново.
- Приватный ключ — секрет наравне с `JWT_KEY`: с ним можно слать уведомления подписчикам от
  имени сервера. Несовпадающая пара или половина пары — приложение не стартует
  (`Invalid configuration`).
- API ходит к push-сервисам (Google, Mozilla, Microsoft, Apple) по https через сеть `edge`;
  если на сервере исходящий трафик закрыт фаерволом, открыть 443 наружу.
- В логе `basicapi`: `Push service … refused a notification` — push-сервис отказал (в тексте код
  и ответ), `… is unreachable` — нет связи с ним, `Push queue is full` — уведомления
  не успевают уходить.

## 4. Проверка

1. Контейнер `basicapi` в статусе `healthy`, `/health/ready` отвечает `Healthy`.
2. Ручная проверка клиента по сокращённому чек-листу: вход, список чатов,
   отправка сообщения между двумя браузерами, получение в реальном времени,
   перезагрузка страницы. Полный чек-лист — [manual-checklist.md](manual-checklist.md).
   После рестарта API открытые вкладки переподключаются сами, F5 не нужен.
3. Сквозные тесты против прода (с машины разработчика; создают двух
   пользователей `E2E_Alice_…`/`e2e_bob_…`):
   ```powershell
   ./scripts/e2e.ps1 -BaseUrl https://<DOMAIN>
   ```
4. Записать деплой в журнал на сервере:
   ```bash
   echo "$(date -Is) $(git rev-parse --short HEAD) backup=$BACKUP" >> ~/deploy.log
   ```

## 5. Откат

**Без новых миграций** — вернуть предыдущий образ:

```bash
docker tag basicapi:previous basicapi:latest
git checkout <предыдущий коммит из шага 2>
$COMPOSE up -d --no-build basicapi
```

**С новыми миграциями** — дополнительно восстановить базу из бэкапа шага 1.
Всё, что пользователи записали после деплоя, будет потеряно — поэтому решение
об откате принимается быстро, пока таких данных мало.

```bash
$COMPOSE stop basicapi
$COMPOSE exec -T postgres pg_restore -U "$DB_USER" -d "$DB_NAME" --clean --if-exists < "$BACKUP"
docker tag basicapi:previous basicapi:latest
git checkout <предыдущий коммит из шага 2>
$COMPOSE up -d --no-build basicapi
```

После отката — пройти шаг 4 и записать откат в `~/deploy.log`.

## Первый запуск на новом сервере

1. DNS-запись `DOMAIN` указывает на сервер; порты 80 и 443 открыты в фаерволе.
2. `cp .env.prod.example .env.prod`, заполнить все значения (`DOMAIN`,
   `ACME_EMAIL`, секреты).
3. `$COMPOSE up -d --build`, затем `$COMPOSE logs caddy` — сертификат выпущен
   (`certificate obtained successfully`).
4. Том `basicchat_caddy_data` хранит сертификаты — не удалять: при частых
   перевыпусках Let's Encrypt временно блокирует домен.
5. Память: лимиты контейнеров по умолчанию в сумме 1 ГБ — на ~100 клиентов онлайн
   ([capacity-and-limits.md](capacity-and-limits.md#малый-сервер--настройки-по-умолчанию)); для
   большего — раздел «Memory» в `.env.prod`. На сервере с 2 ГБ нужен swap хотя бы на 1–2 ГБ: сборка
   образа (`--build`: .NET и клиент на node) сама занимает больше гигабайта, и без swap её убьёт
   нехватка памяти.
6. Регистрация (`REGISTRATION_MODE`): по умолчанию открыта всем, кто дойдёт до сервера. Сервер в
   интернете — завести первые аккаунты и переключить на `invite` (участники приглашают из
   настроек одноразовой ссылкой) или `closed`, затем `$COMPOSE up -d basicapi`.

## Прод-стек локально (перед деплоем)

Тот же `docker-compose.prod.yml` (Caddy, закрытые сети, read-only контейнер),
но с `DOMAIN=localhost`: Caddy выпускает для него сертификат своим локальным
центром, без Let's Encrypt. Порт 80 на Windows часто занят системой, поэтому
Caddy публикуется на 8081/8443 через override-файл:

```yaml
# docker-compose.local.yml (не коммитить вместе с секретами)
services:
  caddy:
    # Только на loopback: стенд не виден из сети.
    ports: !override
      - "127.0.0.1:8081:80"
      - "127.0.0.1:8443:443"
  basicapi:
    # Сквозные тесты интерфейса регистрируют десятки пользователей с одного IP.
    environment:
      RateLimiting__AuthPerMinute: "1000"
      RateLimiting__PerIpPerMinute: "3000"
```

`.env.local` — как `.env.prod.example`, с `DOMAIN=localhost`,
`PUBLIC_URL=https://localhost:8443` (ссылки на файлы подписываются для адреса с портом) и
любыми сгенерированными секретами. Затем:

```powershell
docker compose --env-file .env.local -f docker-compose.prod.yml -f docker-compose.local.yml up -d --build
./scripts/e2e.ps1                      # по умолчанию https://localhost:8443
./scripts/e2e-ui.ps1 -Restart          # сценарии пользователя в браузере, с рестартами API и базы
docker compose --env-file .env.local -f docker-compose.prod.yml -f docker-compose.local.yml down -v
```

`e2e-ui.ps1` гоняет Playwright в установленном Edge (`-Browser chrome` — в Chrome), браузеры не
скачивает; отчёт — `BasicWebClient/e2e-report/index.html`. Порты — именно на `127.0.0.1`: на
Windows с WSL опубликованный на всех адресах порт после рестарта Caddy перехватывает на `[::1]`
`wslrelay`, и `https://localhost:8443` зависает у браузера и тестов (они пробуют IPv6 первым).

`down -v` удаляет и тома стека (база, сертификаты Caddy) — для локального
прогона это то, что нужно; на сервере `-v` не использовать.

## Миграции с предусловиями

Некоторые миграции останавливаются, если данные им противоречат, — чтобы не
решать за человека. Проверьте заранее на проде (шаг 1 — после бэкапа):

**Миграция 7 (логин и email без учёта регистра).** Не должно быть пользователей,
чьи логины или почты отличаются только регистром:

```bash
$COMPOSE exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" -c "
  SELECT 'username' AS field, lower(username) AS value, COUNT(*) FROM users GROUP BY 2 HAVING COUNT(*) > 1
  UNION ALL
  SELECT 'email', lower(email), COUNT(*) FROM users GROUP BY 2 HAVING COUNT(*) > 1;"
```

Пусто — можно деплоить. Иначе — переименовать лишние аккаунты (`UPDATE users
SET username = ... WHERE id = ...`) и проверить снова. Если деплой всё же упал
на этой миграции, в логе `basicapi` будет список конфликтов; база не изменена,
откат — без восстановления бэкапа.

## Долгие миграции

Эти миграции переписывают таблицы и на время работы блокируют их целиком: API
в это время не отвечает на запросы к ним. Время растёт с размером таблиц.
Оценить заранее — размер таблиц:

```bash
$COMPOSE exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" -c "
  SELECT relname, pg_size_pretty(pg_total_relation_size(oid)) FROM pg_class
  WHERE relname IN ('messages', 'users', 'chats', 'chat_members');"
```

До сотен мегабайт — секунды, можно деплоить в любое время. Больше — деплоить
в тихие часы.

**Миграция 8 (время в timestamptz).** Меняет тип временных колонок в `users`,
`chats`, `chat_members`, `messages`. Существующие значения считаются UTC.

**Миграция 9 (номера сообщений).** Нумерует все сообщения (`UPDATE` каждой строки
`messages`) и строит два уникальных индекса. Удаляет `chat_members.last_read_message_id`
(указатель переезжает в `last_read_seq`), поэтому откат на версию до неё — только
с восстановлением бэкапа.

**Миграция 12 (поиск с русским словарём).** Добавляет в `messages` вычисляемую
колонку `search_vector` (переписывает таблицу) и строит по ней GIN-индекс.

**Миграция 24 (вкладка ссылок в галерее).** Добавляет в `messages` вычисляемую колонку
`has_links` (переписывает таблицу) и частичный индекс по ней.

## Очередь событий (outbox)

События о сообщениях и новых чатах сначала сохраняются в таблицу `outbox`, потом
рассылаются. Если клиенты перестали получать сообщения в реальном времени, а
в истории они есть, — посмотреть очередь:

```bash
$COMPOSE exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" -c "
  SELECT COUNT(*) AS pending, MIN(created_at) AS oldest, MAX(attempts) AS max_attempts
  FROM outbox WHERE processed_at IS NULL;"
```

`pending` растёт — рассылка стоит, причина — в логе `basicapi` (`Outbox ...`).
Событие, которое не удалось разослать 10 раз, снимается с очереди с ошибкой в логе.
`Outbox__DispatcherEnabled=false` отключает рассылку (события копятся и уйдут после
включения) — только для отладки.

## Бэкапы

Старые бэкапы чистятся вручную; держать минимум последние 5:

```bash
ls -1t ~/backups/chat-*.dump | tail -n +6 | xargs -r rm
```
