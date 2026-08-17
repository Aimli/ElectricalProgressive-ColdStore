using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressiveColdStore;

public sealed class ColdStoreState
{
    public required string Key { get; init; }
    public required BlockPos AirCoolerPos { get; init; }
    public required Room Room { get; init; }
    public required float Temperature { get; init; }
    public required float PerishRate { get; init; }
    public required long LastRefreshMilliseconds { get; init; }
}
