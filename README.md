# gelark-bot

.NET 8 CLI: берёт прокси из [FloppyData](https://floppydata.com/docs) и создаёт облачные профили (cloud phones) в [GeeLark](https://open.geelark.com/api). Без установки приложений и без автоматизаций внутри телефона.

Почта для создания профиля **не нужна**. `--emails` — только чтобы сразу привязать аккаунт к профилю на потом: логин в заметку GeeLark, логин/пароль/TOTP-секрет — в локальный JSON. Коды 2FA при создании не считаем и в GeeLark не отправляем.

## Что нужно

- .NET 8 SDK
- токен GeeLark (настройки клиента → OpenAPI token)
- ключ FloppyData Client API: https://app.floppydata.com/api-keys

```bash
cp .env.example .env
# заполнить GEELARK_TOKEN и FLOPPYDATA_API_KEY
```

## Сборка и запуск

Нужен .NET 8 SDK. Ключи кладутся в `.env` в корне репозитория.

```bash
cp .env.example .env
# GEELARK_TOKEN и FLOPPYDATA_API_KEY

dotnet build
dotnet run --project src/GelarkBot.Cli -- create --count 3 --dry-run
dotnet run --project src/GelarkBot.Cli -- create --count 3 --group qa
dotnet run --project src/GelarkBot.Cli -- create --emails accounts.txt --group qa
```

После `dotnet build` бинарник: `src/GelarkBot.Cli/bin/Debug/net8.0/gelark-bot`.

`--dry-run` ходит в FloppyData, но не создаёт профили в GeeLark. Боевой `create` создаёт cloud phone, вешает прокси и пишет маппинг в `data/created-profiles.json`. Приложения не ставит, 2FA-коды не считает. В консоли строки `OK`/`FAIL`; код выхода `0` если все создались, `1` если были ошибки.

## Команды

| Команда | Назначение |
| --- | --- |
| `create` | Взять прокси и создать профили |
| `check` | Проверить FloppyData и чекер GeeLark, не создавая телефоны |
| `proxies` | Показать static-инвентарь FloppyData |
| `phones` | Показать уже существующие профили GeeLark |

Полезные флаги `create`:

- `--count N` — сколько профилей. Если не задан, берётся размер `--emails`
- `--emails file` — пул `логин:пароль:totp`, не обязателен
- `--proxy-mode rotating|static` — по умолчанию rotating: бот сам собирает sticky-сессии из баланса FloppyData
- `--proxy-type mobile|residential|datacenter` — по умолчанию mobile
- `--country US` — гео для rotating / фильтр static
- `--dry-run` — только план и JSON, без `phone/addNew`
- `--group`, `--mobile-type`, `--region`, `--output`

По умолчанию Basic-план GeeLark: создание по одному (`--batch-size 1`). На Pro можно больше; если API вернёт `44001`, клиент сам уйдёт в поштучное создание.

## Прокси

По умолчанию бот **сам создаёт** sticky mobile-прокси из баланса FloppyData (`POST /v2/proxy/rotating/connections`, `type=mobile`, `rotation=0`, уникальный `session` на профиль).

Перед `phone/addNew` бот:

1. проверяет прокси у FloppyData
2. шлёт в GeeLark **структурированные** поля (`server`/`port`/`username`/`password`), не сырой `user:pass@host` URL
3. если чекер GeeLark падает на hostname — резолвит IPv4 `geo.g-w.info` и пробует ещё раз
4. если падает IP-API — переключает канал на IP2Location
5. при успехе добавляет прокси в GeeLark и создаёт телефон через `proxyNumber`

`check proxy failed` на HTTP `geo.g-w.info:10080` **и** на SOCKS5 `:10800` значит, что облако GeeLark не достучалось до шлюза FloppyData. Это не лечится `--protocol http`. Проверь ту же связку в GeeLark → Proxies → Check proxy. Если UI тоже красный — нужен провайдер из Dynamic proxy GeeLark или другой хост, который их чекер видит.

```bash
dotnet run --project src/GelarkBot.Cli -- check --count 1 --proxy-mode rotating --proxy-type mobile --country US --protocol http
```

`Need N static FloppyData proxies in US, found 0` значит, что в `.env` всё ещё `PROXY_MODE=static`: бот ищет уже купленные dedicated IP и баланс не трогает. Поставь `PROXY_MODE=rotating` и `PROXY_TYPE=mobile` или:

```bash
dotnet run --project src/GelarkBot.Cli -- create --count 3 --proxy-mode rotating --proxy-type mobile --country US
```

Команда `proxies` показывает только static-инвентарь, не rotating-баланс.

В GeeLark прокси уходит как сохранённый `proxyNumber` или как `proxyInformation` в формате `http://host:port:user:pass`.

## Файл аккаунтов

Формат одной строки: `логин:пароль:токен_аутентификатора`

```
alice@example.com:secret1:JBSWY3DPEHPK3PXP
```

Логин может быть почтой или ником. Если в пароле есть `:`, токен всё равно берётся из последнего поля.

В заметку GeeLark уходит только логин. Пароль и TOTP-секрет остаются в `data/created-profiles.json`. 2fa.live и OTP-библиотека не подключены: для создания профиля они не нужны.

## Тесты

```bash
dotnet test
```
