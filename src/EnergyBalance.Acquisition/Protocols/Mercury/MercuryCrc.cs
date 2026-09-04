namespace EnergyBalance.Acquisition.Protocols.Mercury;

/// <summary>
/// Контрольная сумма CRC-16 (полином 0xA001, начальное значение 0xFFFF), используемая
/// протоколом «Меркурий» (Меркурий 230/234) для проверки целостности кадров запроса/ответа.
/// Совпадает по алгоритму с CRC-16/MODBUS.
/// </summary>
public static class MercuryCrc
{
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                var lsbSet = (crc & 0x0001) != 0;
                crc >>= 1;
                if (lsbSet)
                {
                    crc ^= 0xA001;
                }
            }
        }

        return crc;
    }

    /// <summary>Добавляет к кадру два байта CRC (младший байт первым — little-endian).</summary>
    public static byte[] AppendCrc(byte[] frame)
    {
        var crc = Compute(frame);
        var result = new byte[frame.Length + 2];
        Array.Copy(frame, result, frame.Length);
        result[^2] = (byte)(crc & 0xFF);
        result[^1] = (byte)(crc >> 8);
        return result;
    }

    /// <summary>Проверяет корректность CRC в принятом кадре (последние 2 байта — контрольная сумма).</summary>
    public static bool Validate(ReadOnlySpan<byte> frameWithCrc)
    {
        if (frameWithCrc.Length < 3)
        {
            return false;
        }

        var payload = frameWithCrc[..^2];
        var expected = Compute(payload);
        var actual = (ushort)(frameWithCrc[^2] | (frameWithCrc[^1] << 8));
        return expected == actual;
    }
}
