-- Данный файл НЕ является заменой EF Core миграций (см. src/EnergyBalance.DataAccess) —
-- миграции генерируются командой `dotnet ef migrations add InitialCreate` после первого
-- восстановления пакетов (`dotnet restore`) и применяются `dotnet ef database update`.
--
-- Скрипт ниже документирует ОБЯЗАТЕЛЬНУЮ пост-настройку СУБД для эксплуатации на объёме
-- ~1500 точек учёта с 5-летней историей профиля нагрузки (см. п. 7.5 ТЗ).

-- 1. Расширение TimescaleDB (если используется образ timescale/timescaledb — см. docker-compose.yml).
CREATE EXTENSION IF NOT EXISTS timescaledb;

-- 2. Преобразование таблицы профиля нагрузки в hypertable (после применения миграций EF Core).
--    Секционирование по времени резко ускоряет выборки и очистку старых партиций.
SELECT create_hypertable('load_profile_readings', 'interval_start_utc', if_not_exists => TRUE);

-- 3. Аналогично для таблицы контрольных срезов показаний.
SELECT create_hypertable('readings', 'timestamp_utc', if_not_exists => TRUE);

-- 4. Политика сжатия старых данных (старше 90 суток) для экономии места на диске.
ALTER TABLE load_profile_readings SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'meter_id'
);
SELECT add_compression_policy('load_profile_readings', INTERVAL '90 days');

-- 5. Политика хранения (опционально) — автоматическое удаление данных старше срока хранения,
--    установленного регламентом заказчика (по умолчанию в ТЗ — не менее 5 лет, поэтому
--    политика ниже закомментирована и приведена как пример):
-- SELECT add_retention_policy('load_profile_readings', INTERVAL '5 years');

-- Если TimescaleDB недоступна в контуре заказчика (изолированная сеть без стороннего
-- расширения), эквивалентного эффекта секционирования можно добиться штатным
-- PostgreSQL declarative partitioning (PARTITION BY RANGE (interval_start_utc)) —
-- в этом случае секции создаются и подключаются ежемесячным регламентным заданием.
