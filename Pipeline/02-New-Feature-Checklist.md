# 02 — New Feature Checklist

Перед началом работы над новой фичей.

## 1. Создай progress-файл

```bash
cp Pipeline/progress-template.md progress-my-feature.md
```

Заполни: Feature Name, Story ссылка, Owner, Dates.

## 2. Определи scope

Ответь на вопросы:

- **Где изменения?**
  - [ ] Gateway.API (middleware, bootstrap)
  - [ ] Gateway.Consul (новые параметры конфига)
  - [ ] Gateway.Logger (формат/поток логирования)
  - [ ] Gateway.Tests (тесты)
  - [ ] Helm/CI/CD (деплой, values.yaml)

- **Нужны ли новые роуты YARP?**
  - [ ] Да → открой `09-New-Route.md`
  - [ ] Нет

- **Нужны ли новые настройки в Consul?**
  - [ ] Да → обнови `config/consul-config.{env}.json` в тестах
  - [ ] Нет

- **Нужны ли новые метрики?**
  - [ ] Да → открой `14-Metrics.md`
  - [ ] Нет

- **Нужны ли изменения в Helm?**
  - [ ] Да → обнови `helm/chart/values.yaml` (все 3 env)
  - [ ] Нет

## 3. Создай тестовый файл

Не добавляй тесты в `Gateway.Tests/Tests.cs`. Создай отдельный файл:

```
Gateway.Tests/{FeatureName}Tests.cs
```

## 4. Начни TDD цикл

```
RED → GREEN → REFACTOR
```

→ `03-TDD-RED.md`
→ `04-TDD-GREEN.md`
→ `05-TDD-REFACTOR.md`
