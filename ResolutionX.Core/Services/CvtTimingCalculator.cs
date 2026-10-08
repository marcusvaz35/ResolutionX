using ResolutionX.Core.Models;

namespace ResolutionX.Core.Services;

/// <summary>
/// Calcula a temporização de uma resolução pelo padrão VESA CVT com "Reduced Blanking" (CVT-RB v1),
/// o mesmo usado por monitores LCD e pela maioria das ferramentas de resolução personalizada.
/// </summary>
public static class CvtTimingCalculator
{
    private const int HBlank = 160;
    private const int HFrontPorch = 48;
    private const int HSyncWidth = 32;
    private const int VFrontPorch = 3;
    private const int MinVBackPorch = 6;
    private const double MinVBlankMicroseconds = 460;

    public static VideoTiming ReducedBlanking(int width, int height, int refreshRate)
    {
        var vSync = VSyncWidthFor(width, height);

        // Quantas linhas cabem no tempo mínimo de apagamento vertical exigido pelo padrão.
        var hPeriodEstimate = (1_000_000.0 / refreshRate - MinVBlankMicroseconds) / height;
        var vBlank = Math.Max(
            (int)Math.Floor(MinVBlankMicroseconds / hPeriodEstimate) + 1,
            VFrontPorch + vSync + MinVBackPorch);

        var hTotal = width + HBlank;
        var vTotal = height + vBlank;

        // O padrão arredonda o clock para baixo em passos de 0,25 MHz, o que daria ~59,9 Hz e faria o
        // Windows listar o modo como 59 Hz. O EDID aceita passos de 10 kHz; usar o mais próximo
        // mantém a taxa pedida.
        var pixelClock = (long)Math.Round((double)refreshRate * hTotal * vTotal / 10_000) * 10_000;

        return new VideoTiming(
            width, HFrontPorch, HSyncWidth, HBlank - HFrontPorch - HSyncWidth,
            height, VFrontPorch, vSync, vBlank - VFrontPorch - vSync,
            pixelClock,
            HSyncPositive: true,
            VSyncPositive: false);
    }

    /// <summary>No CVT a largura do pulso vertical identifica a proporção da imagem.</summary>
    private static int VSyncWidthFor(int width, int height)
    {
        var ratio = (double)width / height;
        if (IsRatio(ratio, 4, 3)) return 4;
        if (IsRatio(ratio, 16, 9)) return 5;
        if (IsRatio(ratio, 16, 10)) return 6;
        if (IsRatio(ratio, 5, 4)) return 7;
        if (IsRatio(ratio, 15, 9)) return 7;
        return 10;
    }

    private static bool IsRatio(double ratio, int w, int h) => Math.Abs(ratio - (double)w / h) < 0.01;
}
