namespace ResolutionX.Core.Models;

/// <summary>
/// Temporização completa de um sinal de vídeo: além da área visível, os intervalos de
/// sincronismo que o monitor precisa receber. É o que um "Detailed Timing" do EDID descreve.
/// </summary>
public sealed record VideoTiming(
    int HActive, int HFrontPorch, int HSyncWidth, int HBackPorch,
    int VActive, int VFrontPorch, int VSyncWidth, int VBackPorch,
    long PixelClockHz,
    bool HSyncPositive,
    bool VSyncPositive,
    bool Interlaced = false)
{
    public int HBlank => HFrontPorch + HSyncWidth + HBackPorch;
    public int VBlank => VFrontPorch + VSyncWidth + VBackPorch;
    public int HTotal => HActive + HBlank;
    public int VTotal => VActive + VBlank;

    public double RefreshRate => HTotal > 0 && VTotal > 0 ? PixelClockHz / ((double)HTotal * VTotal) : 0;
    public double PixelClockMHz => PixelClockHz / 1_000_000.0;

    public DisplayMode ToMode() => new(HActive, VActive, (int)Math.Round(RefreshRate));
}
