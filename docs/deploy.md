# Ручной деплой

CI и автоматического деплоя нет до последнего этапа [плана 2](plan-2-features.md).
До тех пор прод обновляется вручную по этой инструкции. Сервер — Ubuntu с Docker,
стек описан в `docker-compose.prod.yml`, секреты — в `.env.prod` (шаблон
`.env.prod.example`).

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
```

Миграции применяются при старте приложения. Если старт упал на миграции —
сразу к шагу 5.

## 4. Проверка

1. Контейнер `basicapi` в статусе `healthy`.
2. Ручная проверка клиента по сокращённому чек-листу: вход, список чатов,
   отправка сообщения между двумя браузерами, получение в реальном времени,
   перезагрузка страницы. Полный чек-лист — в пункте 3.1
   [плана 1](plan-1-refactoring.md).
3. Записать деплой в журнал на сервере:
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

## Бэкапы

Старые бэкапы чистятся вручную; держать минимум последние 5:

```bash
ls -1t ~/backups/chat-*.dump | tail -n +6 | xargs -r rm
```
