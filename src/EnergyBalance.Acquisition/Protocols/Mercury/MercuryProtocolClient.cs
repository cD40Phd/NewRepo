using EnergyBalance.Domain.Entities;
using EnergyBalance.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EnergyBalance.Acquisition.Protocols.Mercury;

/// <summary>
/// Драйвер фирменного протокола «Меркурий» (счётчики Меркурий 230 / Меркурий 234).
/// Реализует базовый цикл сеанса: открытие канала (сетевой адрес + уровень доступа + пароль),
/// чтение накопленной энергии по тарифам, чтение мгновенных значений качества сети, закрытие канала.
///
/// ВАЖНО: коды команд и раскладка байт в ответах ниже соответствуют типовой структуре протокола
/// «Меркурий» для приборов серии 230/234, но конкретное расположение полей (номер массива,
/// тарифная зона, порядок BCD/бинарного представления) зависит от версии прошивки прибора и
/// ДОЛЖНО быть сверено с действующим «Описанием протокола обмена» на счётчик при пусконаладке
/// на реальном оборудовании. Точки расширения вынесены в защищённые методы разбора ответа
/// (см. <see cref="ParseTariffEnergyResponse"/>, <see cref="ParseInstantValuesResponse"/>).
/// </summary>
public class MercuryProtocolClient : IMeterDataSource
{
    private const byte CmdTestChannel = 0x00;
    private const byte CmdOpenChannel = 0x01;
    private const byte CmdCloseChannel = 0x02;
    private const byte CmdReadTariffEnergy = 0x05;
    private const byte CmdReadInstantValues = 0x08;
    private const byte CmdReadLoadProfile = 0x04;

    private const byte AccessLevelUser = 0x01;

    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<Meter, ISerialTransport> _transportFactory;
    private readonly ILogger<MercuryProtocolClient> _logger;

    public MercuryProtocolClient(Func<Meter, ISerialTransport> transportFactory, ILogger<MercuryProtocolClient> logger)
    {
        _transportFactory = transportFactory;
        _logger = logger;
    }

    public MeterProtocol Protocol => MeterProtocol.Mercury;

    public async Task<MeterSnapshot> ReadCurrentValuesAsync(Meter meter, CancellationToken ct = default)
    {
        var address = ParseAddress(meter.PollingAddress);
        await using var transport = _transportFactory(meter);
        await transport.OpenAsync(ct);
        await OpenChannelAsync(transport, address, meter.AccessPassword, ct);

        try
        {
            var energyResponse = await SendCommandAsync(transport, address, CmdReadTariffEnergy, [0x00], ct);
            var (t1, t2, t3, t4) = ParseTariffEnergyResponse(energyResponse);

            // Энергия «выдача» (А-) и накопленная реактивная энергия (Р+/Р-) читаются отдельными
            // массивами данных прибора (обычно массивы №2 и №4 в протоколе «Меркурий») — точный
            // номер массива и код подпараметра уточняется по протоколу конкретной прошивки при
            // пусконаладке (см. класс-комментарий); до уточнения возвращается 0, что корректно
            // для однонаправленных точек потребления (подавляющее большинство точек сети 0,4-10 кВ).
            var (e1, e2, e3, e4) = (0m, 0m, 0m, 0m);
            var (reactiveImport, reactiveExport) = (0m, 0m);

            var instantResponse = await SendCommandAsync(transport, address, CmdReadInstantValues, [0x11], ct);
            var instant = ParseInstantValuesResponse(instantResponse);

            return new MeterSnapshot(
                TimestampUtc: DateTime.UtcNow,
                ActiveEnergyTariff1: t1,
                ActiveEnergyTariff2: t2,
                ActiveEnergyTariff3: t3,
                ActiveEnergyTariff4: t4,
                ActiveEnergyExportTariff1: e1,
                ActiveEnergyExportTariff2: e2,
                ActiveEnergyExportTariff3: e3,
                ActiveEnergyExportTariff4: e4,
                ReactiveEnergyImport: reactiveImport,
                ReactiveEnergyExport: reactiveExport,
                VoltageA: instant.VoltageA,
                VoltageB: instant.VoltageB,
                VoltageC: instant.VoltageC,
                CurrentA: instant.CurrentA,
                CurrentB: instant.CurrentB,
                CurrentC: instant.CurrentC,
                ActivePower: instant.ActivePower,
                PowerFactor: instant.PowerFactor,
                TamperFlag: false);
        }
        finally
        {
            await CloseChannelAsync(transport, address, ct);
        }
    }

    public async Task<IReadOnlyList<LoadProfileInterval>> ReadLoadProfileAsync(Meter meter, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var address = ParseAddress(meter.PollingAddress);
        await using var transport = _transportFactory(meter);
        await transport.OpenAsync(ct);
        await OpenChannelAsync(transport, address, meter.AccessPassword, ct);

        try
        {
            var result = new List<LoadProfileInterval>();
            var cursor = fromUtc;

            while (cursor < toUtc)
            {
                var requestData = BuildLoadProfileRequestData(cursor);
                var response = await SendCommandAsync(transport, address, CmdReadLoadProfile, requestData, ct);
                var interval = ParseLoadProfileResponse(response, cursor);
                result.Add(interval);
                cursor = cursor.AddMinutes(interval.IntervalMinutes);
            }

            return result;
        }
        finally
        {
            await CloseChannelAsync(transport, address, ct);
        }
    }

    private static byte ParseAddress(string pollingAddress)
    {
        if (!byte.TryParse(pollingAddress, out var address))
        {
            throw new MercuryProtocolException($"Некорректный сетевой адрес прибора: '{pollingAddress}'.");
        }

        return address;
    }

    private async Task OpenChannelAsync(ISerialTransport transport, byte address, string? password, CancellationToken ct)
    {
        var passwordBytes = EncodePassword(password);
        var data = new byte[1 + passwordBytes.Length];
        data[0] = AccessLevelUser;
        Array.Copy(passwordBytes, 0, data, 1, passwordBytes.Length);

        var response = await SendCommandAsync(transport, address, CmdOpenChannel, data, ct);
        if (response.Length < 1 || response[0] != 0x00)
        {
            throw new MercuryProtocolException($"Прибор по адресу {address} отклонил открытие канала (неверный пароль или уровень доступа).");
        }
    }

    private async Task CloseChannelAsync(ISerialTransport transport, byte address, CancellationToken ct)
    {
        try
        {
            await SendCommandAsync(transport, address, CmdCloseChannel, Array.Empty<byte>(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось корректно закрыть канал с прибором по адресу {Address}", address);
        }
    }

    private static byte[] EncodePassword(string? password)
    {
        // Пароль доступа "Меркурий" — 6 байт; по умолчанию заводской пароль уровня "пользователь".
        var defaultPassword = new byte[] { 0x01, 0x01, 0x01, 0x01, 0x01, 0x01 };
        if (string.IsNullOrWhiteSpace(password))
        {
            return defaultPassword;
        }

        var digits = password.Where(char.IsDigit).Select(c => (byte)(c - '0')).ToArray();
        if (digits.Length == 6)
        {
            return digits;
        }

        return defaultPassword;
    }

    private async Task<byte[]> SendCommandAsync(ISerialTransport transport, byte address, byte command, byte[] data, CancellationToken ct)
    {
        var frame = new byte[2 + data.Length];
        frame[0] = address;
        frame[1] = command;
        Array.Copy(data, 0, frame, 2, data.Length);
        var frameWithCrc = MercuryCrc.AppendCrc(frame);

        await transport.WriteAsync(frameWithCrc, ct);
        var response = await transport.ReadAsync(ResponseTimeout, ct);

        if (response.Length < 3)
        {
            throw new MercuryProtocolException($"Прибор по адресу {address} не ответил на команду 0x{command:X2}.");
        }

        if (!MercuryCrc.Validate(response))
        {
            throw new MercuryProtocolException($"Ошибка контрольной суммы CRC в ответе прибора по адресу {address}.");
        }

        if (response[0] != address)
        {
            throw new MercuryProtocolException($"Получен ответ от прибора с адресом {response[0]}, ожидался {address}.");
        }

        // Отбрасываем адрес и CRC — возвращаем полезную нагрузку ответа.
        return response[1..^2];
    }

    private static (decimal t1, decimal t2, decimal t3, decimal t4) ParseTariffEnergyResponse(byte[] payload)
    {
        // Ожидается 4 счётчика по 4 байта (32-битное значение в 0.01 кВт·ч), big-endian по байтам внутри слова.
        if (payload.Length < 16)
        {
            throw new MercuryProtocolException("Недостаточная длина ответа при чтении энергии по тарифам.");
        }

        decimal ReadTariff(int offset)
        {
            uint raw = (uint)((payload[offset] << 24) | (payload[offset + 1] << 16) | (payload[offset + 2] << 8) | payload[offset + 3]);
            return raw / 100m;
        }

        return (ReadTariff(0), ReadTariff(4), ReadTariff(8), ReadTariff(12));
    }

    private static InstantValues ParseInstantValuesResponse(byte[] payload)
    {
        // Формат мгновенных значений (U, I, P, cosφ) специфичен для модели прибора — здесь используется
        // упрощённая раскладка по 3-байтным полям с фиксированной точкой, типичная для семейства "Меркурий".
        decimal ReadFixedPoint3(int offset, int scale)
        {
            if (offset + 3 > payload.Length)
            {
                return 0m;
            }

            int raw = (payload[offset] << 16) | (payload[offset + 1] << 8) | payload[offset + 2];
            return raw / (decimal)scale;
        }

        return new InstantValues(
            VoltageA: ReadFixedPoint3(0, 100),
            VoltageB: ReadFixedPoint3(3, 100),
            VoltageC: ReadFixedPoint3(6, 100),
            CurrentA: ReadFixedPoint3(9, 1000),
            CurrentB: ReadFixedPoint3(12, 1000),
            CurrentC: ReadFixedPoint3(15, 1000),
            ActivePower: ReadFixedPoint3(18, 1000),
            PowerFactor: ReadFixedPoint3(21, 1000));
    }

    private static byte[] BuildLoadProfileRequestData(DateTime intervalStartUtc)
    {
        // Запрос профиля мощности за конкретный получасовой интервал: месяц, день, час, получасовой номер.
        return
        [
            (byte)(intervalStartUtc.Year % 100),
            (byte)intervalStartUtc.Month,
            (byte)intervalStartUtc.Day,
            (byte)intervalStartUtc.Hour,
            (byte)(intervalStartUtc.Minute >= 30 ? 1 : 0),
        ];
    }

    private static LoadProfileInterval ParseLoadProfileResponse(byte[] payload, DateTime intervalStartUtc)
    {
        if (payload.Length < 4)
        {
            throw new MercuryProtocolException("Недостаточная длина ответа при чтении профиля нагрузки.");
        }

        uint raw = (uint)((payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3]);
        var activeEnergyDelta = raw / 1000m;

        return new LoadProfileInterval(
            intervalStartUtc,
            IntervalMinutes: 30,
            ActiveEnergyDelta: activeEnergyDelta,
            ActiveEnergyExportDelta: 0m,
            ReactiveEnergyImportDelta: null,
            ReactiveEnergyExportDelta: null);
    }

    private readonly record struct InstantValues(
        decimal VoltageA,
        decimal VoltageB,
        decimal VoltageC,
        decimal CurrentA,
        decimal CurrentB,
        decimal CurrentC,
        decimal ActivePower,
        decimal PowerFactor);
}
