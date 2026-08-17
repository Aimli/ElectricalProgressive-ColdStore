using System.Text;
using ElectricalProgressive.Content.Block;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ElectricalProgressiveColdStore.Content;

/// <summary>
/// 冷库冷风机方块。
/// 在物品栏提示和生存手册中显示额定电气参数。
/// </summary>
public sealed class BlockAirCooler : BlockEBase
{
    public override void GetHeldItemInfo(
        ItemSlot inSlot,
        StringBuilder dsc,
        IWorldAccessor world,
        bool withDebugInfo)
    {
        base.GetHeldItemInfo(
            inSlot,
            dsc,
            world,
            withDebugInfo
        );

        int voltage =
            Attributes?["voltage"]
                .AsInt(128)
            ?? 128;

        float maxCurrent =
            Attributes?["maxCurrent"]
                .AsFloat(22f)
            ?? 22f;

        int maxConsumption =
            Attributes?["maxConsumption"]
                .AsInt(2744)
            ?? 2744;

        int wattsPerInteriorBlock =
            Attributes?["wattsPerInteriorBlock"]
                .AsInt(1)
            ?? 1;

        float minimumOperatingPowerRatio =
            Attributes?["minimumOperatingPowerRatio"]
                .AsFloat(0.9f)
            ?? 0.9f;

        dsc.AppendLine();

        dsc.AppendLine(
            Lang.Get(
                "electricalprogressivecoldstore:aircooler-specs-title"
            )
        );

        dsc.AppendLine(
            Lang.Get(
                "electricalprogressivecoldstore:aircooler-spec-voltage",
                voltage,
                Lang.Get("electricalprogressivebasics:V")
            )
        );

        dsc.AppendLine(
            Lang.Get(
                "electricalprogressivecoldstore:aircooler-spec-max-power",
                maxConsumption,
                Lang.Get("electricalprogressivebasics:W")
            )
        );

        dsc.AppendLine(
            Lang.Get(
                "electricalprogressivecoldstore:aircooler-spec-max-current",
                maxCurrent,
                Lang.Get("electricalprogressivebasics:A")
            )
        );

        dsc.AppendLine(
            Lang.Get(
                "electricalprogressivecoldstore:aircooler-spec-volume-power",
                wattsPerInteriorBlock,
                Lang.Get("electricalprogressivebasics:W")
            )
        );

        dsc.AppendLine(
            Lang.Get(
                "electricalprogressivecoldstore:aircooler-spec-minimum-power",
                minimumOperatingPowerRatio * 100f
            )
        );
    }
}