# gelark-bot

.NET 8 CLI: берёт прокси из [FloppyData](https://floppydata.com/docs) и создаёт облачные профили (cloud phones) в [GeeLark](https://open.geelark.com/api). Без установки приложений и без автоматизаций внутри телефона.

Email для создания профиля **не нужен**. Если есть пул — каждый email привязывается к профилю: в заметку GeeLark и в локальный JSON, чтобы потом кормить своё приложение.

## Что нужно

- .NET 8 SDK
- токен GeeLark (настройки клиента → OpenAPI token)
- ключ FloppyData Client API: https://app.floppydata.com/api-keys

```bash
cp .env.example .env
# заполнить GEELARK_TOKEN и FLOPPYDATA_API_KEY
```

## Сборка и запуск

```bash
dotnet build
dotnet run --project src/GelarkBot.Cli -- proxies
dotnet run --project src/GelarkBot.Cli -- create --count 3 --dry-run
dotnet run --project src/GelarkBot.Cli -- create --emails examples/emails.txt --group qa
```

После `dotnet build` бинарник: `src/GelarkBot.Cli/bin/Debug/net8.0/gelark-bot`.

## Команды

| Команда | Назначение |
| --- | --- |
| `create` | Взять прокси и создать профили |
| `proxies` | Показать static-инвентарь FloppyData |
| `phones` | Показать уже существующие профили GeeLark |

Полезные флаги `create`:

- `--count N` — сколько профилей. Если не задан, берётся размер `--emails`
- `--emails file` — пул `email` / `email:password` / `email,password`
- `--proxy-mode static|rotating` — static IP с аккаунта или sticky rotating-сессии
- `--country US` — фильтр static / гео rotating
- `--dry-run` — только план и JSON, без `phone/addNew`
- `--group`, `--mobile-type`, `--region`, `--output`

По умолчанию Basic-план GeeLark: создание по одному (`--batch-size 1`). На Pro можно больше; если API вернёт `44001`, клиент сам уйдёт в поштучное создание.

## Прокси

- **static** — `GET /v2/proxy/static`, в профиль уходит `connection.connectionString`
- **rotating** — `POST /v2/proxy/rotating/connections` с уникальным `session` и `rotation=0` (липкая сессия на профиль)

В GeeLark прокси передаётся как `proxyInformation` в `POST /open/v1/phone/addNew`.

## Email-файл

См. `examples/emails.txt`. Пароль в заметку GeeLark не пишется, только в `data/created-profiles.json`.

## Тесты

```bash
dotnet test
```
