using System;
using System.Collections.Generic;

namespace MktsScanner;

/// <summary>Результат опроса одного сетевого адреса.</summary>
public enum ProbeStatus
{
    /// <summary>Тишина в линии — прибора с таким адресом нет.</summary>
    NoAnswer,
    /// <summary>Нормальный ответ Modbus.</summary>
    Answer,
    /// <summary>Ответ-исключение Modbus: прибор есть, но запрошенный регистр/функцию не поддерживает.</summary>
    Exception,
    /// <summary>В линии что-то есть, но кадр битый (CRC, чужой адрес) — часто признак неверной скорости/чётности.</summary>
    Garbage
}

public sealed record ProbeResult(ProbeStatus Status, byte[] Frame, string Details);

/// <summary>Сборка и разбор кадров Modbus RTU.</summary>
public static class ModbusRtu
{
    public const byte ReadHoldingRegisters = 0x03;
    public const byte ReadInputRegisters = 0x04;
    public const byte ReportSlaveId = 0x11;

    public static ushort Crc16(byte[] data, int length)
    {
        ushort crc = 0xFFFF;
        for (int n = 0; n < length; n++)
        {
            crc ^= data[n];
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }
        return crc;
    }

    public static bool CheckCrc(byte[] frame)
    {
        if (frame.Length < 4) return false;
        ushort crc = Crc16(frame, frame.Length - 2);
        return frame[frame.Length - 2] == (byte)(crc & 0xFF) && frame[frame.Length - 1] == (byte)(crc >> 8);
    }

    public static byte[] BuildRequest(byte address, byte function, ushort register, ushort count)
    {
        byte[] pdu = function == ReportSlaveId
            ? new[] { address, function }
            : new[] { address, function, (byte)(register >> 8), (byte)register, (byte)(count >> 8), (byte)count };

        ushort crc = Crc16(pdu, pdu.Length);
        return [.. pdu, (byte)(crc & 0xFF), (byte)(crc >> 8)];
    }

    /// <summary>
    /// Полная длина ответа по уже принятому заголовку (адрес, функция, байт длины/кода исключения).
    /// Возвращает null, если заголовок не похож на ответ на наш запрос.
    /// </summary>
    public static int? ExpectedLength(IList<byte> header, byte function)
    {
        if (header.Count < 3) return null;
        if (header[1] == (function | 0x80)) return 5;
        if (header[1] != function) return null;
        // Для функций 0x03, 0x04 и 0x11 третий байт — число байт данных.
        return 3 + header[2] + 2;
    }

    public static ProbeResult Parse(byte[] frame, byte address, byte function)
    {
        if (frame.Length == 0)
            return new ProbeResult(ProbeStatus.NoAnswer, frame, "");

        if (!CheckCrc(frame))
            return new ProbeResult(ProbeStatus.Garbage, frame, "ошибка CRC");

        if (frame[0] != address)
            return new ProbeResult(ProbeStatus.Garbage, frame, $"ответил адрес {frame[0]}");

        if (frame[1] == (function | 0x80))
            return new ProbeResult(ProbeStatus.Exception, frame, $"исключение {frame[2]:X2} ({ExceptionName(frame[2])})");

        if (frame[1] != function)
            return new ProbeResult(ProbeStatus.Garbage, frame, $"неожиданная функция {frame[1]:X2}");

        var data = new ArraySegment<byte>(frame, 3, frame[2]);
        string details = function == ReportSlaveId
            ? $"Slave ID: {Hex(data)} \"{Printable(data)}\""
            : $"регистры: {Registers(data)}";
        return new ProbeResult(ProbeStatus.Answer, frame, details);
    }

    public static string Hex(IEnumerable<byte> data)
    {
        var sb = new System.Text.StringBuilder();
        foreach (byte b in data) sb.Append(b.ToString("X2"));
        return sb.ToString();
    }

    private static string Registers(IList<byte> data)
    {
        var values = new List<string>();
        for (int i = 0; i + 1 < data.Count; i += 2)
        {
            values.Add(((data[i] << 8) | data[i + 1]).ToString());
        }
        return string.Join(" ", values);
    }

    private static string Printable(IList<byte> data)
    {
        var chars = new char[data.Count];
        for (int i = 0; i < data.Count; i++)
        {
            chars[i] = data[i] is >= 0x20 and < 0x7F ? (char)data[i] : '.';
        }
        return new string(chars);
    }

    private static string ExceptionName(byte code) => code switch
    {
        0x01 => "функция не поддерживается",
        0x02 => "недопустимый адрес регистра",
        0x03 => "недопустимое значение",
        0x04 => "сбой прибора",
        0x06 => "прибор занят",
        _ => "неизвестно"
    };
}
