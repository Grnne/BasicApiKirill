# BasicChatApi

Бэкенд self-hosted мессенджера для команд: личные чаты и группы, файлы, реакции, черновики,
статусы прочтения, папки, поиск, push. REST + SignalR, .NET 10, Postgres. Фронт — Vue SPA в
`BasicWebClient`, отдаётся тем же сервером.

## Быстрый старт

Нужен Docker.

```bash
docker-compose up -d
```

Порт по умолчанию — 8080; другой — через `HOST_PORT` (`HOST_PORT=9090 docker-compose up -d`).
Остановка — `docker-compose down`.

## Проверки

```powershell
./scripts/test.ps1          # сборка, тесты клиента, юнит- и интеграционные тесты, аудит пакетов (нужны Docker и Node.js)
./scripts/test.ps1 -Image   # плюс сборка прод-образа
./scripts/e2e-ui.ps1        # сценарии пользователя в браузере против локального прод-стека (docs/deploy.md)
```

CI (GitHub Actions) запускает `test.ps1` на каждый push. Деплой ручной — по
[docs/deploy.md](docs/deploy.md); после деплоя — `./scripts/e2e.ps1 -BaseUrl https://<домен>` и
[чек-лист ручной проверки](docs/manual-checklist.md).

## Документация

| Ресурс | Где | Что |
|---|---|---|
| REST API | `/swagger` (в проде — с `Swagger:Enabled`) | ручки, модели, коды ответов |
| SignalR | `/signalr-docs` (там же, где `/swagger`), эндпоинт `/hubs/chat` | методы хаба и события |
| Архитектура | [docs/architecture.md](docs/architecture.md) | устройство проекта, правила зависимостей, ключевые решения |
| Веб-клиент | [BasicWebClient/README.md](BasicWebClient/README.md) | запуск в разработке, структура, синхронизация, сессии, push |
| Изменения контракта | [docs/api-contract-changes.md](docs/api-contract-changes.md) | что поменялось для клиентов, справочник кодов ошибок |
| Деплой | [docs/deploy.md](docs/deploy.md) | бэкап, запуск, проверка, откат, push-ключи |
| Ручная проверка | [docs/manual-checklist.md](docs/manual-checklist.md) | чек-лист клиента, известные проблемы, журнал прогонов |
| Пропускная способность | [docs/capacity-and-limits.md](docs/capacity-and-limits.md) | что упирается первым, по замерам |
| Нагрузочный прогон | [docs/load-testing.md](docs/load-testing.md) | как запустить `tools/BasicApi.LoadTest`, базовые цифры |
