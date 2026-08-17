using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressiveColdStore.ColdStore;

public static class InsulationHelper
{
    public const string ModId = "electricalprogressivecoldstore";

    /// <summary>
    /// 判断一个 Decor 方块是否为冷库保温层。
    /// 优先检查 JSON 属性，方块代码只作为兼容性回退。
    /// </summary>
    public static bool IsColdStoreInsulation(Block? decor)
    {
        if (decor?.Code == null)
        {
            return false;
        }

        if (decor.Attributes?["coldStoreInsulation"].AsBool(false) == true)
        {
            return true;
        }

        return decor.Code.Domain == ModId
            && decor.Code.Path.StartsWith(
                "coldstore-insulation",
                StringComparison.OrdinalIgnoreCase
            );
    }

    /// <summary>
    /// 检查宿主方块指定面是否贴有冷库保温层。
    /// </summary>
    public static bool HasInsulation(
        IBlockAccessor blockAccessor,
        BlockPos hostBlockPos,
        BlockFacing hostBlockFace
    )
    {
        int decorIndex = new DecorBits(hostBlockFace);

        Block? decor = blockAccessor.GetDecor(
            hostBlockPos,
            decorIndex
        );

        return IsColdStoreInsulation(decor);
    }

    /// <summary>
    /// 调试用：取得指定面的 Decor。
    /// </summary>
    public static Block? GetDecor(
        IBlockAccessor blockAccessor,
        BlockPos hostBlockPos,
        BlockFacing hostBlockFace
    )
    {
        int decorIndex = new DecorBits(hostBlockFace);

        return blockAccessor.GetDecor(
            hostBlockPos,
            decorIndex
        );
    }
}