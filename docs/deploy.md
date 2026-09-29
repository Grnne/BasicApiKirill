# Ручной деплой

CI и автоматического деплоя нет до последнего этапа [плана 2](plan-2-features.md).
До тех пор прод обновляется вручную по этой инструкции. Сервер — Ubuntu с Docker,
стек описан в `docker-compose.prod.yml`, секреты — в `.env.prod` (шаблон
`.env.prod.example`). Снаружи открыт только Caddy (80/443, TLS от Let's Encrypt,
конфиг — `deploy/Caddyfile`); API и Postgres доступны лишь во внутренних
docker-сетях.

Во всех командах ниже:

```bash
COMPOSE="docker compose --env-file .env.prod -f docker-compose.prod.yml"
```

## 0. Перед деплоем (на машине разработчика)

1. Коммит, который едет в прод, прошёл полный прогон:
   ```powershell
   ./scripts/test.ps1 -Image
   ```
   Строка-итог скрипта есть в описании PR или коммита.
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
сразу к шагу 5. Если старт упал с `Invalid configuration` — в `.env.prod`
не задан или слабый секрет (например, `JWT_KEY`); приложение не стартует
с ключом короче 32 байт или с заглушкой `CHANGE_ME`.

## 4. Проверка

1. Контейнер `basicapi` в статусе `healthy`, `/health/ready` отвечает `Healthy`.
2. Ручная проверка клиента по сокращённому чек-листу: вход, список чатов,
   отправка сообщения между двумя браузерами, получение в реальном времени,
   перезагрузка страницы. Полный чек-лист — [manual-checklist.md](manual-checklist.md).
   Известное поведение текущего клиента: вкладки, которые дольше срока
   access-токена не делали REST-запросов, после рестарта API остаются «нет связи»
   до F5 (подробнее — в чек-листе).
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

## Прод-стек локально (перед деплоем)

Тот же `docker-compose.prod.yml` (Caddy, закрытые сети, read-only контейнер),
но с `DOMAIN=localhost`: Caddy выпускает для него сертификат своим локальным
центром, без Let's Encrypt. Порт 80 на Windows часто занят системой, поэтому
Caddy публикуется на 8081/8443 через override-файл:

```yaml
# docker-compose.local.yml (не коммитить вместе с секретами)
services:
  caddy:
    ports: !override
      - "8081:80"
      - "8443:443"
```

`.env.local` — как `.env.prod.example`, с `DOMAIN=localhost` и любыми
сгенерированными секретами. Затем:

```powershell
docker compose --env-file .env.local -f docker-compose.prod.yml -f docker-compose.local.yml up -d --build
./scripts/e2e.ps1                      # по умолчанию https://localhost:8443
docker compose --env-file .env.local -f docker-compose.prod.yml -f docker-compose.local.yml down -v
```

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
