using System.Text;
using ResolutionX.Core.Models;

namespace ResolutionX.Core.Services;

/// <summary>Leitura do formato EDID (VESA E-EDID 1.3/1.4 e extensões CTA-861).</summary>
public static class EdidParser
{
    public const int BlockSize = 128;
    public const int DescriptorSize = 18;
    public const byte CtaExtensionTag = 0x02;

    /// <summary>Posições dos quatro descritores de 18 bytes do bloco base.</summary>
    public static readonly int[] BaseDescriptorOffsets = [54, 72, 90, 108];

    private static readonly byte[] Header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    /// <summary>Tamanho coerente, cabeçalho correto e checksum válido em todos os blocos.</summary>
    public static bool IsValid(byte[]? edid)
    {
        if (edid is null || edid.Length < BlockSize || edid.Length % BlockSize != 0)
            return false;
        if (!edid.AsSpan(0, Header.Length).SequenceEqual(Header))
            return false;
        if (edid.Length != BlockSize * (1 + edid[126]))
            return false;

        for (var offset = 0; offset < edid.Length; offset += BlockSize)
        {
            if (!HasValidChecksum(edid.AsSpan(offset, BlockSize)))
                return false;
        }

        return true;
    }

    public static bool HasValidChecksum(ReadOnlySpan<byte> block)
    {
        var sum = 0;
        foreach (var b in block)
            sum += b;
        return sum % 256 == 0;
    }

    public static EdidInfo? Parse(byte[]? edid)
    {
        if (edid is null || edid.Length < BlockSize || !edid.AsSpan(0, Header.Length).SequenceEqual(Header))
            return null;

        string? name = null;
        string? serialText = null;
        int? rangeMaxRefresh = null;
        var timings = new List<VideoTiming>();
        DisplayMode? native = null;

        foreach (var offset in BaseDescriptorOffsets)
        {
            var descriptor = edid.AsSpan(offset, DescriptorSize);
            if (ParseDetailedTiming(descriptor) is { } timing)
            {
                timings.Add(timing);
                if (offset == BaseDescriptorOffsets[0] && !timing.Interlaced)
                    native = timing.ToMode();
                continue;
            }

            switch (descriptor[3])
            {
                case 0xFC: name = ReadText(descriptor); break;
                case 0xFF: serialText = ReadText(descriptor); break;
                case 0xFD:
                    // No EDID 1.4 o bit 1 do byte 4 soma 255 à taxa vertical máxima.
                    rangeMaxRefresh = descriptor[6] + ((descriptor[4] & 0x02) != 0 ? 255 : 0);
                    break;
            }
        }

        for (var block = BlockSize; block + BlockSize <= edid.Length; block += BlockSize)
            timings.AddRange(ReadCtaDetailedTimings(edid.AsSpan(block, BlockSize)));

        var numericSerial = BitConverter.ToUInt32(edid, 12);
        var year = edid[17] == 0 ? (int?)null : 1990 + edid[17];
        var maxFromTimings = timings.Count > 0 ? (int?)timings.Max(t => (int)Math.Round(t.RefreshRate)) : null;

        return new EdidInfo
        {
            ManufacturerId = DecodeManufacturer(edid[8], edid[9]),
            ProductCode = (edid[10] | (edid[11] << 8)).ToString("X4"),
            MonitorName = name,
            SerialNumber = !string.IsNullOrEmpty(serialText) ? serialText
                : numericSerial != 0 ? numericSerial.ToString()
                : null,
            ManufactureYear = year,
            Version = $"{edid[18]}.{edid[19]}",
            NativeMode = native,
            MaxRefreshRate = rangeMaxRefresh is > 0 ? rangeMaxRefresh : maxFromTimings,
            DetailedTimings = timings,
            ExtensionCount = edid[126]
        };
    }

    /// <summary>Lê um descritor de 18 bytes como "Detailed Timing"; null se for outro tipo de descritor.</summary>
    public static VideoTiming? ParseDetailedTiming(ReadOnlySpan<byte> d)
    {
        var pixelClock = d[0] | (d[1] << 8);
        if (pixelClock == 0)
            return null;

        var hActive = d[2] | ((d[4] & 0xF0) << 4);
        var hBlank = d[3] | ((d[4] & 0x0F) << 8);
        var vActive = d[5] | ((d[7] & 0xF0) << 4);
        var vBlank = d[6] | ((d[7] & 0x0F) << 8);
        var hFront = d[8] | ((d[11] & 0xC0) << 2);
        var hSync = d[9] | ((d[11] & 0x30) << 4);
        var vFront = (d[10] >> 4) | ((d[11] & 0x0C) << 2);
        var vSync = (d[10] & 0x0F) | ((d[11] & 0x03) << 4);
        var flags = d[17];

        return new VideoTiming(
            hActive, hFront, hSync, hBlank - hFront - hSync,
            vActive, vFront, vSync, vBlank - vFront - vSync,
            pixelClock * 10_000L,
            HSyncPositive: (flags & 0x02) != 0,
            VSyncPositive: (flags & 0x04) != 0,
            Interlaced: (flags & 0x80) != 0);
    }

    /// <summary>
    /// Num bloco CTA-861 o byte 2 diz onde começam os "Detailed Timings";
    /// eles seguem em sequência até o primeiro descritor zerado.
    /// </summary>
    public static int FindCtaFreeTimingOffset(ReadOnlySpan<byte> block)
    {
        if (block[0] != CtaExtensionTag)
            return -1;

        int start = block[2];
        if (start < 4)
            return -1;

        for (var p = start; p + DescriptorSize <= BlockSize - 1; p += DescriptorSize)
        {
            if (block[p] == 0 && block[p + 1] == 0)
                return p;
        }

        return -1;
    }

    private static IEnumerable<VideoTiming> ReadCtaDetailedTimings(ReadOnlySpan<byte> block)
    {
        var result = new List<VideoTiming>();
        if (block[0] != CtaExtensionTag || block[2] < 4)
            return result;

        for (int p = block[2]; p + DescriptorSize <= BlockSize - 1; p += DescriptorSize)
        {
            if (ParseDetailedTiming(block.Slice(p, DescriptorSize)) is not { } timing)
                break;
            result.Add(timing);
        }

        return result;
    }

    private static string ReadText(ReadOnlySpan<byte> descriptor)
    {
        var text = descriptor.Slice(5, 13);
        var end = text.IndexOf((byte)0x0A);
        if (end >= 0)
            text = text[..end];
        return Encoding.ASCII.GetString(text).Trim('\0', ' ');
    }

    /// <summary>Três letras de 5 bits, em big-endian.</summary>
    private static string DecodeManufacturer(byte high, byte low)
    {
        var value = (high << 8) | low;
        return new string(
        [
            (char)('A' - 1 + ((value >> 10) & 0x1F)),
            (char)('A' - 1 + ((value >> 5) & 0x1F)),
            (char)('A' - 1 + (value & 0x1F))
        ]);
    }
}
