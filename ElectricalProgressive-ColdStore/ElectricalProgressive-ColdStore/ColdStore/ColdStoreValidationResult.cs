using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressiveColdStore;

public sealed class ColdStoreValidationResult
{
    public bool IsValid { get; init; }

    public string FailureCode { get; init; } =
        "unknown";

    public Room? Room { get; init; }

    public int BoundaryFaceCount { get; init; }

    public int InsulatedFaceCount { get; init; }

    public int DoorCount { get; init; }

    /// <summary>
    /// 当前有效冷库的实际内部空气格数量。
    /// 同时作为冷风机的需求功率。
    /// </summary>
    public int InteriorVolume { get; init; }

    /// <summary>
    /// 导致验证失败的具体方块位置。
    /// 目前主要用于缺少保温层提示。
    /// </summary>
    public BlockPos? FailurePosition { get; init; }

    /// <summary>
    /// 导致验证失败的具体方块表面。
    /// </summary>
    public BlockFacing? FailureFace { get; init; }

    public static ColdStoreValidationResult Invalid(
        string failureCode,
        Room? room = null,
        BlockPos? failurePosition = null,
        BlockFacing? failureFace = null)
    {
        return new ColdStoreValidationResult
        {
            IsValid = false,
            FailureCode = failureCode,
            Room = room,

            FailurePosition =
                failurePosition?.Copy(),

            FailureFace =
                failureFace
        };
    }
}