# BasicAPI - Docker Deployment

## Что это?
Простое Web API с Swagger документацией для тестирования.

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
