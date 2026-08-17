using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

[assembly: ModDependency("game", "1.22.0")]
[assembly: ModDependency("survival", "1.22.0")]
[assembly: ModDependency("electricalprogressivecore", "3.3.0")]
[assembly: ModDependency("electricalprogressivebasics", "3.3.0")]
[assembly: ModInfo(
    "Electrical Progressive: Cold Store",
    "electricalprogressivecoldstore",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Powered, insulated walk-in cold stores.",
    Version = "0.1.0",
    Authors = ["Cold Store contributors"]
)]

namespace ElectricalProgressiveColdStore;

public sealed class ElectricalProgressiveColdStoreMod : ModSystem
{
    private const string HarmonyId =
        "electricalprogressivecoldstore.perishrate";

    [ThreadStatic]
    internal static int ClimateOverrideSuppressionDepth;
    private Harmony? harmony;
    private ICoreAPI? api;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        this.api = api;

        api.RegisterBlockClass("ColdStoreInsulationLayer", typeof(Content.BlockInsulationLayer));
        api.RegisterBlockClass(
            "ColdStoreRefrigerantPipe",
            typeof(Content.BlockRefrigerantPipe)
        );

        api.RegisterBlockClass(
            "ColdStoreAirCoolerBlock",
            typeof(Content.BlockAirCooler)
        );

        api.RegisterBlockEntityClass(
            "ColdStoreAirCooler",
            typeof(Content.BlockEntityAirCooler)
        );
        api.RegisterBlockEntityClass("ColdStoreCondensingUnit", typeof(Content.BlockEntityCondensingUnit));
        api.RegisterBlockEntityBehaviorClass("ColdStoreAirCoolerElectric", typeof(Content.BEBehaviorAirCoolerElectric));

        ColdStoreRuntime.SetManager(
            api.Side,
            new ColdStoreManager(api)
        );

        api.Event.OnGetClimate += OnGetClimate;

        harmony = new Harmony(HarmonyId);
        harmony.PatchAll(typeof(ElectricalProgressiveColdStoreMod).Assembly);

        api.Logger.Notification("Electrical Progressive: Cold Store 0.1.0 initialized ({0}).", api.Side);
    }

    private void OnGetClimate(
        ref ClimateCondition climate,
        BlockPos pos,
        EnumGetClimateMode mode,
        double totalDays)
    {
        if (api == null)
        {
            return;
        }

        // 世界生成使用原始气候值，不能被冷库改变。
        if (mode == EnumGetClimateMode.WorldGenValues)
        {
            return;
        }

        // 冷风机查询户外温度时，不允许读到冷库温度。
        if (ClimateOverrideSuppressionDepth > 0)
        {
            return;
        }

        ColdStoreManager? manager =
            ColdStoreRuntime.GetManager(api.Side);

        if (manager?.TryGetTemperature(
                pos,
                out float coldStoreTemperature
            ) != true)
        {
            return;
        }

        // 冷库只能降低环境温度，不作为加热器使用。
        climate.Temperature = Math.Min(
            climate.Temperature,
            coldStoreTemperature
        );
    }

    public override void Dispose()
    {
        if (api != null)
        {
            api.Event.OnGetClimate -= OnGetClimate;
            ColdStoreRuntime.ClearManager(api.Side);
        }

        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        api = null;

        base.Dispose();
    }
}
