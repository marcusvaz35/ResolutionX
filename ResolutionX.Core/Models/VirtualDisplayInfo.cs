namespace ResolutionX.Core.Models;

public sealed record VirtualDisplayInfo(string Id, int Width, int Height, int RefreshRate, bool Enabled);

/// <summary>Situação do driver de monitor virtual (IDD) nesta máquina.</summary>
public sealed record VirtualDisplayStatus(bool DriverInstalled, string Message);
