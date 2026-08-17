using System;
using System.Text;
using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressiveColdStore.Content;

public sealed class BEBehaviorAirCoolerElectric
    : BlockEntityBehavior, IElectricConsumer
{
    private const string PowerKey =
        "electricalprogressivecoldstore:powersetting";

    private const string RequiredConsumptionKey =
        "electricalprogressivecoldstore:requiredconsumption";

    private const int DefaultMaximumConsumption = 3000;

    private int maxConsumption =
        DefaultMaximumConsumption;

    private int requiredConsumption;

    private float minimumOperatingPowerRatio =
        0.9f;

    public BEBehaviorAirCoolerElectric(
        BlockEntity blockEntity)
        : base(blockEntity)
    {
    }

    /// <summary>
    /// 电网实际提供的功率。
    /// </summary>
    public int PowerSetting { get; private set; }

    public float AvgConsumeCoeff { get; set; }

    /// <summary>
    /// 当前冷库根据实际容积需要的功率。
    /// </summary>
    public int RequiredConsumption =>
        requiredConsumption;

    /// <summary>
    /// 设备额定最大功率。
    /// </summary>
    public int MaxConsumption =>
        maxConsumption;

    /// <summary>
    /// 必须获得完整的容积需求功率才允许制冷。
    /// </summary>
    /// <summary>
    /// 当前实际供电占需求功率的比例。
    /// </summary>
    public float ReceivedPowerRatio
    {
        get
        {
            if (requiredConsumption <= 0)
            {
                return 0f;
            }

            return Math.Clamp(
                PowerSetting
                    / (float)requiredConsumption,
                0f,
                1f
            );
        }
    }

    /// <summary>
    /// 允许少量线路输电损耗。
    /// 默认至少获得需求功率的 90% 才启动。
    /// </summary>
    /// <summary>
    /// 输入功率必须高于最低运行比例。
    ///
    /// 当最低比例为 90% 时，恰好 90% 的供电按照
    /// 制冷速度公式等于 0%，因此也不应进入工作状态。
    /// </summary>
    public bool HasEnoughPower =>
        requiredConsumption > 0
        && ReceivedPowerRatio
            > minimumOperatingPowerRatio;

    public override void Initialize(
        ICoreAPI api,
        JsonObject properties)
    {
        base.Initialize(api, properties);

        maxConsumption =
            Block.Attributes?[
                "maxConsumption"
            ].AsInt(
                DefaultMaximumConsumption
            )
            ?? DefaultMaximumConsumption;

        maxConsumption = Math.Max(
            1,
            maxConsumption
        );

        requiredConsumption = Math.Clamp(
            requiredConsumption,
            0,
            maxConsumption
        );

        minimumOperatingPowerRatio =
            Block.Attributes?[
                "minimumOperatingPowerRatio"
            ].AsFloat(0.9f)
            ?? 0.9f;

        minimumOperatingPowerRatio =
            Math.Clamp(
                minimumOperatingPowerRatio,
                0.1f,
                1f
            );
    }

    /// <summary>
    /// 由冷风机方块实体在每次房间扫描后设置。
    /// </summary>
    public void SetRequiredConsumption(
        int watts)
    {
        int newRequiredConsumption =
            Math.Clamp(
                watts,
                0,
                maxConsumption
            );

        if (requiredConsumption
            == newRequiredConsumption)
        {
            return;
        }

        requiredConsumption =
            newRequiredConsumption;

        /*
         * 房间缩小或结构失效时，
         * 立即清除超过当前需求的旧功率显示。
         */
        if (PowerSetting > requiredConsumption)
        {
            PowerSetting =
                requiredConsumption;
        }

        Blockentity.MarkDirty(true);
    }

    public float Consume_request()
    {
        return requiredConsumption;
    }

    public void Consume_receive(
        float amount)
    {
        int received = Math.Max(
            0,
            (int)Math.Round(
                amount,
                MidpointRounding.AwayFromZero
            )
        );

        /*
         * 电网不应该提供超过设备当前请求的功率。
         * 这里仍进行一次限制，避免显示异常。
         */
        received = Math.Min(
            received,
            requiredConsumption
        );

        if (PowerSetting == received)
        {
            return;
        }

        PowerSetting = received;
        Blockentity.MarkDirty(true);
    }

    public float getPowerReceive()
    {
        return PowerSetting;
    }

    public float getPowerRequest()
    {
        return requiredConsumption;
    }

    public void Update()
    {
        /*
         * Electrical Progressive 会调用这个方法。
         * 电气过载和烧毁仍由其自身行为处理。
         */
    }

    public override void GetBlockInfo(
        IPlayer forPlayer,
        StringBuilder stringBuilder)
    {
        base.GetBlockInfo(
            forPlayer,
            stringBuilder
        );

        float percentage =
            requiredConsumption > 0
                ? PowerSetting
                  * 100f
                  / requiredConsumption
                : 0f;

        percentage = Math.Clamp(
            percentage,
            0f,
            100f
        );

        stringBuilder.AppendLine(
            StringHelper.Progressbar(
                percentage
            )
        );

        stringBuilder.AppendLine(
            "└ "
            + Lang.Get(
                "electricalprogressivebasics:Consumption"
            )
            + ": "
            + PowerSetting
            + "/"
            + requiredConsumption
            + " "
            + Lang.Get(
                "electricalprogressivebasics:W"
            )
        );

        stringBuilder.AppendLine();
    }

    public override void ToTreeAttributes(
        ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetInt(
            PowerKey,
            PowerSetting
        );

        tree.SetInt(
            RequiredConsumptionKey,
            requiredConsumption
        );
    }

    public override void FromTreeAttributes(
        ITreeAttribute tree,
        IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(
            tree,
            worldAccessForResolve
        );

        PowerSetting =
            tree.GetInt(PowerKey);

        requiredConsumption = Math.Max(
            0,
            tree.GetInt(
                RequiredConsumptionKey
            )
        );
    }
}