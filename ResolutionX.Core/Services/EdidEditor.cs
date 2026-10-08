using ResolutionX.Core.Models;

namespace ResolutionX.Core.Services;

/// <summary>
/// Acrescenta resoluções a um EDID sem tocar no que o monitor já anuncia: o primeiro
/// "Detailed Timing" (resolução nativa/preferida) e os demais modos permanecem como estão.
/// </summary>
public static class EdidEditor
{
    private const int BlockSize = EdidParser.BlockSize;
    private const int DescriptorSize = EdidParser.DescriptorSize;

    /// <summary>Limites dos campos de um "Detailed Timing" de 18 bytes.</summary>
    public static bool FitsDetailedTiming(VideoTiming t)
        => t is { HActive: > 0 and <= 4095, VActive: > 0 and <= 4095 }
           && t.HBlank is > 0 and <= 4095
           && t.VBlank is > 0 and <= 4095
           && t.HFrontPorch is >= 0 and <= 1023
           && t.HSyncWidth is > 0 and <= 1023
           && t.VFrontPorch is >= 0 and <= 63
           && t.VSyncWidth is > 0 and <= 63
           && t.PixelClockHz is >= 10_000 and <= 655_350_000;

    /// <summary>
    /// Devolve uma cópia do EDID com a temporização incluída, ou null se não houver onde colocá-la.
    /// Ordem de preferência: espaço vazio no bloco base, espaço livre numa extensão CTA-861,
    /// nova extensão CTA-861 e, por último, o lugar de um texto dispensável do bloco base.
    /// </summary>
    public static byte[]? AddDetailedTiming(byte[] edid, VideoTiming timing)
    {
        if (!EdidParser.IsValid(edid) || !FitsDetailedTiming(timing))
            return null;

        var (hSizeMm, vSizeMm) = ReadImageSize(edid);
        var descriptor = EncodeDetailedTiming(timing, hSizeMm, vSizeMm);
        var result = (byte[])edid.Clone();

        var slot = FindBaseSlot(result, tag: 0x10);
        if (slot >= 0)
            return WriteAt(result, slot, descriptor);

        for (var block = BlockSize; block < result.Length; block += BlockSize)
        {
            var free = EdidParser.FindCtaFreeTimingOffset(result.AsSpan(block, BlockSize));
            if (free >= 0)
                return WriteAt(result, block + free, descriptor);
        }

        if (result[126] == 0)
            return AppendCtaBlock(result, descriptor);

        // 0xFE = texto livre, 0xFF = número de série em texto: nenhum dos dois define modos de vídeo.
        slot = FindBaseSlot(result, tag: 0xFE);
        if (slot < 0)
            slot = FindBaseSlot(result, tag: 0xFF);
        return slot >= 0 ? WriteAt(result, slot, descriptor) : null;
    }

    public static byte[] EncodeDetailedTiming(VideoTiming t, int hSizeMm, int vSizeMm)
    {
        var d = new byte[DescriptorSize];
        var clock = (int)(t.PixelClockHz / 10_000);

        d[0] = (byte)(clock & 0xFF);
        d[1] = (byte)(clock >> 8);
        d[2] = (byte)(t.HActive & 0xFF);
        d[3] = (byte)(t.HBlank & 0xFF);
        d[4] = (byte)(((t.HActive >> 8) << 4) | (t.HBlank >> 8));
        d[5] = (byte)(t.VActive & 0xFF);
        d[6] = (byte)(t.VBlank & 0xFF);
        d[7] = (byte)(((t.VActive >> 8) << 4) | (t.VBlank >> 8));
        d[8] = (byte)(t.HFrontPorch & 0xFF);
        d[9] = (byte)(t.HSyncWidth & 0xFF);
        d[10] = (byte)(((t.VFrontPorch & 0x0F) << 4) | (t.VSyncWidth & 0x0F));
        d[11] = (byte)(((t.HFrontPorch >> 8) << 6) | ((t.HSyncWidth >> 8) << 4) |
                       ((t.VFrontPorch >> 4) << 2) | (t.VSyncWidth >> 4));
        d[12] = (byte)(hSizeMm & 0xFF);
        d[13] = (byte)(vSizeMm & 0xFF);
        d[14] = (byte)((((hSizeMm >> 8) & 0x0F) << 4) | ((vSizeMm >> 8) & 0x0F));
        d[15] = 0;
        d[16] = 0;
        // 0x18 = sincronismo digital separado; bit 2 = vertical positivo; bit 1 = horizontal positivo.
        d[17] = (byte)(0x18 | (t.VSyncPositive ? 0x04 : 0) | (t.HSyncPositive ? 0x02 : 0) |
                       (t.Interlaced ? 0x80 : 0));
        return d;
    }

    public static void FixChecksum(Span<byte> block)
    {
        var sum = 0;
        for (var i = 0; i < BlockSize - 1; i++)
            sum += block[i];
        block[BlockSize - 1] = (byte)((256 - sum % 256) % 256);
    }

    /// <summary>Tamanho físico da imagem em mm: do modo nativo, senão do cabeçalho (em cm).</summary>
    private static (int H, int V) ReadImageSize(byte[] edid)
    {
        var first = edid.AsSpan(EdidParser.BaseDescriptorOffsets[0], DescriptorSize);
        if (EdidParser.ParseDetailedTiming(first) is not null)
        {
            var h = first[12] | ((first[14] & 0xF0) << 4);
            var v = first[13] | ((first[14] & 0x0F) << 8);
            if (h > 0 && v > 0)
                return (h, v);
        }

        return (edid[21] * 10, edid[22] * 10);
    }

    /// <summary>Procura no bloco base um descritor que não seja de vídeo com o tipo indicado.</summary>
    private static int FindBaseSlot(byte[] edid, byte tag)
    {
        // O primeiro descritor é a resolução preferida e nunca é substituído.
        foreach (var offset in EdidParser.BaseDescriptorOffsets.Skip(1))
        {
            if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 2] == 0 && edid[offset + 3] == tag)
                return offset;
        }

        return -1;
    }

    private static byte[] WriteAt(byte[] edid, int offset, byte[] descriptor)
    {
        descriptor.CopyTo(edid, offset);
        var blockStart = offset / BlockSize * BlockSize;
        FixChecksum(edid.AsSpan(blockStart, BlockSize));
        return edid;
    }

    private static byte[] AppendCtaBlock(byte[] edid, byte[] descriptor)
    {
        var result = new byte[edid.Length + BlockSize];
        edid.CopyTo(result, 0);

        result[126] = 1;
        FixChecksum(result.AsSpan(0, BlockSize));

        var block = result.AsSpan(edid.Length, BlockSize);
        block[0] = EdidParser.CtaExtensionTag;
        block[1] = 0x03; // revisão 3
        block[2] = 0x04; // sem blocos de dados: os timings começam logo após o cabeçalho
        block[3] = 0x00;
        descriptor.CopyTo(block[4..]);
        FixChecksum(block);
        return result;
    }
}
