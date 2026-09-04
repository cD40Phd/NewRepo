# EnergyBalance — АИС технического и коммерческого учёта электроэнергии

Автоматизированная информационная система сбора данных со счётчиков электроэнергии
(Меркурий 230/234, Сириус 2-Л) и расчёта многоуровневого баланса отпуска/потребления
электроэнергии для электросетевой компании. Разработана как открытая альтернатива/дополнение
к АИС OMNI-US, «Пирамида-Сети» и АСКУЭ АльфаЦЕНТР, интегрируемая с существующей
инфраструктурой **MasterOPC Insat + MasterSCADA 3.12**.

## Документация

- [Техническое задание](docs/TZ.md)
- [Архитектура системы](docs/ARCHITECTURE.md)

## Структура решения

```
src/
  EnergyBalance.Domain/        — модель предметной области
  EnergyBalance.DataAccess/    — EF Core, PostgreSQL/TimescaleDB
  EnergyBalance.Acquisition/   — OPC UA клиент к MasterOPC/MasterSCADA, драйверы Меркурий и Modbus (Сириус 2-Л)
  EnergyBalance.BalanceEngine/ — расчёт баланса и потерь (чистая логика, без внешних зависимостей)
  EnergyBalance.Application/   — оркестрация расчёта (связывает DataAccess и BalanceEngine)
  EnergyBalance.Api/           — ASP.NET Core Web API
  EnergyBalance.Worker/        — фоновые службы: опрос приборов по расписанию, регламентный расчёт баланса
tests/
  EnergyBalance.Tests/         — юнит-тесты расчётного ядра и протокольных утилит
sql/
  schema_notes.sql             — пост-настройка PostgreSQL/TimescaleDB после применения EF-миграций
```

## Требования

- .NET 8 SDK
- PostgreSQL 15+ (рекомендуется образ `timescale/timescaledb` — см. `docker-compose.yml`)
- Доступ к OPC UA эндпоинту MasterOPC/MasterSCADA (или его DA-UA Wrapper) для основного канала сбора данных
- COM-порты/Ethernet-шлюзы RS-485 — для резервного/верификационного прямого опроса приборов

## Сборка и запуск (локально)

```bash
dotnet restore EnergyBalance.sln
dotnet build EnergyBalance.sln

# Первая миграция БД (после настройки строки подключения в appsettings.json):
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate --project src/EnergyBalance.DataAccess --startup-project src/EnergyBalance.Api
dotnet ef database update --project src/EnergyBalance.DataAccess --startup-project src/EnergyBalance.Api

# Пост-настройка TimescaleDB (партиционирование архивных таблиц) — см. sql/schema_notes.sql

dotnet run --project src/EnergyBalance.Api
dotnet run --project src/EnergyBalance.Worker
```

## Тесты

```bash
dotnet test tests/EnergyBalance.Tests/EnergyBalance.Tests.csproj
```

## Запуск в Docker

```bash
docker compose up --build
```

## Важные допущения, требующие уточнения на объекте заказчика

Раскладка регистров/команд протоколов «Меркурий» (`src/EnergyBalance.Acquisition/Protocols/Mercury`)
и Modbus «Сириус 2-Л» (`src/EnergyBalance.Acquisition/Protocols/Modbus/SiriusRegisterMap.cs`), а также
адрес узла OPC UA / DA-UA Wrapper MasterSCADA (`src/EnergyBalance.Acquisition/OpcIntegration`), заданы
по типовым для данных семейств приборов схемам и **должны быть сверены с действующей документацией
на конкретные модели и версии прошивок приборов** на этапе пусконаладки (см. раздел 11 `docs/TZ.md`).
