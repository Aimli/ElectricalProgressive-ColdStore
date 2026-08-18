using System;
using System.Text;
using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressiveColdStore.Content;

public sealed class BlockEntityAirCooler : BlockEntityEBase
{
    private const string TemperatureKey = "electricalprogressivecoldstore:temperature";
    private const string InitializedKey = "electricalprogressivecoldstore:temperatureinitialized";
    private const string LastUpdateHoursKey = "electricalprogressivecoldstore:lastupdatehours";

    private long tickListenerId;
    private double lastUpdateHours;
    private bool temperatureInitialized;
    private string statusCode = "initializing";
    private ColdStoreValidationResult? lastValidation;
    private RefrigerantNetworkResult? lastNetwork;

    private BlockPos? missingInsulationPos;
    private string missingInsulationFaceCode = "";

    /// <summary>
    /// 当前是否处于一次连续制冷周期中。
    /// </summary>
    private bool coolingCycleActive;

    /// <summary>
    /// 本次制冷周期开始时的室温。
    /// </summary>
    private float coolingCycleStartTemperature;

    /// <summary>
    /// 本次制冷周期完成进度，范围为 0～1。
    /// </summary>
    private float coolingCycleProgress;

    public float CurrentTemperature { get; private set; } = 20f;
    public float CurrentPerishRate { get; private set; } = 1f;
    public bool IsWorking { get; private set; }

    private string StateKey => $"{Pos.dimension}:{Pos.X}:{Pos.Y}:{Pos.Z}";

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        // BEBehaviorElectricalProgressive registers the consumer during behavior
        // initialization, but it still needs LoadEProperties to populate its
        // electrical faces, voltage and current parameters. Calling this here also
        // repairs air coolers that were placed by an older build of the mod.
        InitializeElectricalProperties();

        if (lastUpdateHours <= 0)
            lastUpdateHours = api.World.Calendar.TotalHours;

        tickListenerId = RegisterGameTickListener(OnMachineTick, 2_000);
        MarkDirty(true);
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        InitializeElectricalProperties();
    }

    private void InitializeElectricalProperties()
    {
        if (Block == null) return;

        LoadEProperties.Load(Block, this);
        ElectricalProgressive?.Update(true);
    }

    private void OnMachineTick(float dt)
    {
        if (Api == null) return;

        double nowHours = Api.World.Calendar.TotalHours;
        double deltaHours = Math.Max(0, nowHours - lastUpdateHours);
        lastUpdateHours = nowHours;

        float ambient = GetAmbientTemperature();
        if (!temperatureInitialized)
        {
            CurrentTemperature = ambient;
            temperatureInitialized = true;
        }

        lastNetwork = RefrigerantNetwork.Scan(
            Api.World.BlockAccessor,
            Pos
        );

        lastValidation = ColdStoreScanner.Validate(
            Api,
            Pos,
            lastNetwork.Condensers,
            lastNetwork.IsConnected,
            lastNetwork.ReachedScanLimit
        );

        /*
         * 将扫描器发现的缺失保温层位置和表面保存到
         * 方块实体字段中。
         *
         * GetBlockInfo()、ToTreeAttributes() 和客户端同步
         * 都使用这两个字段。
         */
        if (lastValidation.FailureCode
            == "missing-insulation")
        {
            missingInsulationPos =
                lastValidation.FailurePosition?.Copy();

            missingInsulationFaceCode =
                lastValidation.FailureFace?.Code
                ?? "unknown";
        }
        else
        {
            /*
             * 验证结果改变后必须清除旧坐标，
             * 否则可能继续显示上一次缺失的位置。
             */
            missingInsulationPos = null;
            missingInsulationFaceCode = "";
        }

        BEBehaviorAirCoolerElectric? electric =
            GetBehavior<BEBehaviorAirCoolerElectric>();

        bool refrigerationStructureValid =
            lastNetwork.IsConnected
            && !lastNetwork.ReachedScanLimit
            && lastValidation.IsValid;

        /*
         * 每一个有效的冷库内部格需要 1W。
         *
         * 结构无效、门打开或管路断开时，
         * 功率需求立即变成 0W。
         */
        int requiredPower =
            refrigerationStructureValid
                ? lastValidation.InteriorVolume
                : 0;

        electric?.SetRequiredConsumption(
            requiredPower
        );

        bool powered =
            requiredPower > 0
            && electric?.HasEnoughPower == true;

        IsWorking =
            refrigerationStructureValid
            && powered;
        statusCode = DetermineStatus(
            powered,
            lastValidation
        );

        float suppliedPowerRatio =
            electric?.ReceivedPowerRatio ?? 0f;

        float elapsedCoolingSeconds =
            CalculateCoolingElapsedSeconds(
                dt,
                deltaHours
            );

        UpdateTemperature(
            elapsedCoolingSeconds,
            deltaHours,
            ambient,
            IsWorking,
            suppliedPowerRatio
        );

        CurrentPerishRate = CalculatePerishRate(
            CurrentTemperature,
            ambient
        );

        ColdStoreManager? manager = ColdStoreRuntime.GetManager(Api.Side);
        if (IsWorking && lastValidation.Room != null)
        {
            manager?.Upsert(new ColdStoreState
            {
                Key = StateKey,
                AirCoolerPos = Pos.Copy(),
                Room = lastValidation.Room,
                Temperature = CurrentTemperature,
                PerishRate = CurrentPerishRate,
                LastRefreshMilliseconds = Api.World.ElapsedMilliseconds
            });
        }
        else
        {
            manager?.Remove(StateKey);
        }

        MarkDirty(true);
    }

    private string DetermineStatus(
        bool powered,
        ColdStoreValidationResult validation)
    {
        /*
         * ColdStoreScanner 已经按照以下顺序验证：
         *
         * 1. 房间结构；
         * 2. 冷凝机和制冷管网；
         * 3. 保温层。
         *
         * 因此这里不能再次提前检查网络状态，
         * 否则会覆盖 no-room 等更优先的错误。
         */
        if (!validation.IsValid)
        {
            return validation.FailureCode;
        }

        /*
         * 房间、冷凝机和保温层全部有效后，
         * 最后才检查供电。
         */
        if (!powered)
        {
            return "no-power";
        }

        return "working";
    }

    private float CalculateCoolingElapsedSeconds(
    float realDeltaSeconds,
    double deltaHours)
    {
        /*
         * deltaHours 包含正常流逝的游戏时间，
         * 也包含时间指令造成的向前跳跃。
         */
        double elapsedCalendarSeconds =
            Math.Max(
                0d,
                deltaHours * 3600d
            );

        /*
         * SpeedOfTime 是游戏秒相对现实秒的速度。
         * CalendarSpeedMul 额外控制日历推进速度。
         */
        double effectiveCalendarRate =
            Api.World.Calendar.SpeedOfTime
            * Api.World.Calendar.CalendarSpeedMul;

        /*
         * 防止日历速度为 0 时发生除零。
         *
         * 即便日历暂停，真实 tick 时间仍会通过下面的
         * realDeltaSeconds 让冷风机继续工作。
         */
        effectiveCalendarRate =
            Math.Max(
                1d,
                effectiveCalendarRate
            );

        double calendarEquivalentSeconds =
            elapsedCalendarSeconds
            / effectiveCalendarRate;

        /*
         * 正常游戏时两个值大致相同。
         * 使用时间指令后，calendarEquivalentSeconds
         * 会明显大于普通 tick 的 dt。
         */
        double elapsedSeconds =
            Math.Max(
                Math.Max(
                    0f,
                    realDeltaSeconds
                ),
                calendarEquivalentSeconds
            );

        return (float)elapsedSeconds;
    }

    private void UpdateTemperature(
        float deltaSeconds,
        double deltaHours,
        float ambient,
        bool working,
        float suppliedPowerRatio)
    {
        if (deltaSeconds < 0f)
        {
            deltaSeconds = 0f;
        }

        if (deltaHours < 0d)
        {
            deltaHours = 0d;
        }

        float minimumTemperature =
            Block.Attributes?[
                "minimumTemperature"
            ].AsFloat(-50f)
            ?? -50f;

        float fullPowerCoolingSeconds =
            Block.Attributes?[
                "fullPowerCoolingSeconds"
            ].AsFloat(100f)
            ?? 100f;

        float warmingDegreesPerHour =
            Block.Attributes?[
                "warmingDegreesPerHour"
            ].AsFloat(3f)
            ?? 3f;

        float minimumOperatingPowerRatio =
            Block.Attributes?[
                "minimumOperatingPowerRatio"
            ].AsFloat(0.90f)
            ?? 0.90f;

        fullPowerCoolingSeconds = Math.Max(
            1f,
            fullPowerCoolingSeconds
        );

        minimumOperatingPowerRatio =
            GameMath.Clamp(
                minimumOperatingPowerRatio,
                0f,
                0.99f
            );

        suppliedPowerRatio =
            GameMath.Clamp(
                suppliedPowerRatio,
                0f,
                1f
            );

        /*
         * 功率与制冷速度的关系：
         *
         * 100% 功率 -> 100% 速度
         * 99% 功率  -> 90% 速度
         * 98% 功率  -> 80% 速度
         * ...
         * 91% 功率  -> 10% 速度
         * 90% 功率  -> 0% 速度
         *
         * 当最低运行功率为 0.90 时：
         *
         * speedFactor =
         *     (powerRatio - 0.90) / 0.10
         */
        float coolingSpeedFactor = 0f;

        if (suppliedPowerRatio
            > minimumOperatingPowerRatio)
        {
            coolingSpeedFactor =
                (
                    suppliedPowerRatio
                    - minimumOperatingPowerRatio
                )
                / (
                    1f
                    - minimumOperatingPowerRatio
                );

            coolingSpeedFactor =
                GameMath.Clamp(
                    coolingSpeedFactor,
                    0f,
                    1f
                );
        }

        bool canCool =
            working
            && coolingSpeedFactor > 0f
            && CurrentTemperature
                > minimumTemperature;

        if (canCool)
        {
            /*
             * 开始一次新的制冷周期。
             *
             * 周期开始时记录当前温度，确保无论起始温度
             * 是 40°C、20°C 还是 -10°C，满功率下都在
             * 100 秒后到达 -50°C。
             */
            if (!coolingCycleActive)
            {
                coolingCycleActive = true;

                coolingCycleStartTemperature =
                    Math.Max(
                        CurrentTemperature,
                        minimumTemperature
                    );

                coolingCycleProgress = 0f;
            }

            float progressPerSecond =
                1f / fullPowerCoolingSeconds;

            coolingCycleProgress +=
                deltaSeconds
                * progressPerSecond
                * coolingSpeedFactor;

            coolingCycleProgress =
                GameMath.Clamp(
                    coolingCycleProgress,
                    0f,
                    1f
                );

            CurrentTemperature =
                GameMath.Lerp(
                    coolingCycleStartTemperature,
                    minimumTemperature,
                    coolingCycleProgress
                );

            if (coolingCycleProgress >= 1f)
            {
                CurrentTemperature =
                    minimumTemperature;
            }

            return;
        }

        /*
         * 制冷停止后结束当前周期。
         *
         * 再次获得足够电力时，会从当时的实际温度
         * 开始一个新的制冷周期。
         */
        coolingCycleActive = false;
        coolingCycleProgress = 0f;
        coolingCycleStartTemperature =
            CurrentTemperature;

        /*
         * 未制冷时继续使用原来的游戏时间回温逻辑。
         */
        float maxChange =
            warmingDegreesPerHour
            * (float)deltaHours;

        if (CurrentTemperature < ambient)
        {
            CurrentTemperature = Math.Min(
                ambient,
                CurrentTemperature + maxChange
            );
        }
        else if (CurrentTemperature > ambient)
        {
            CurrentTemperature = Math.Max(
                ambient,
                CurrentTemperature - maxChange
            );
        }
    }

    private float CalculatePerishRate(float temperature, float ambient)
    {
        float minimumTemperature = Block.Attributes?["minimumTemperature"].AsFloat(-50f) ?? -50f;
        float minimumPerishRate = global::ElectricalProgressiveColdStore.ConfigIntegration.MinimumPerishRate;

        float denominator = Math.Max(1f, ambient - minimumTemperature);
        float progress = GameMath.Clamp((ambient - temperature) / denominator, 0f, 1f);
        return Math.Max(minimumPerishRate, GameMath.Lerp(1f, minimumPerishRate, progress));
    }

    private float GetAmbientTemperature()
    {
        BlockPos climatePos = Pos.Copy();

        // 保持原有逻辑：使用海平面位置取得基础环境温度。
        climatePos.Y = Api.World.SeaLevel;

        global::ElectricalProgressiveColdStore
            .ElectricalProgressiveColdStoreMod
            .ClimateOverrideSuppressionDepth++;

        try
        {
            ClimateCondition? climate =
                Api.World.BlockAccessor.GetClimateAt(
                    climatePos,
                    EnumGetClimateMode
                        .ForSuppliedDate_TemperatureOnly,
                    Api.World.Calendar.TotalDays
                );

            return climate?.Temperature ?? 20f;
        }
        finally
        {
            global::ElectricalProgressiveColdStore
                .ElectricalProgressiveColdStoreMod
                .ClimateOverrideSuppressionDepth--;
        }
    }

    public override void GetBlockInfo(
        IPlayer forPlayer,
        StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        stringBuilder.AppendLine(
            Lang.Get(
                "electricalprogressivecoldstore:aircooler-status-"
                + statusCode
            )
        );

        if (statusCode == "missing-insulation"
            && missingInsulationPos != null)
        {
            string faceCode = string.IsNullOrWhiteSpace(
                missingInsulationFaceCode
            )
                ? "unknown"
                : missingInsulationFaceCode.ToLowerInvariant();

            string faceName = Lang.Get(
                "electricalprogressivecoldstore:blockface-"
                + faceCode
            );

            /*
             * RoomRegistry 和 BlockAccessor 使用绝对世界坐标。
             * 玩家坐标界面显示的是相对于世界出生点的坐标。
             *
             * 这里只转换用于显示的坐标。
             * missingInsulationPos 本身仍然保留绝对坐标，
             * 以便方块查询、保存和网络同步正常工作。
             */
            /*
             * missingInsulationPos 是需要贴保温层的墙体方块。
             *
             * missingFace 指向冷库内部，因此：
             *
             * 墙体方块 + missingFace
             *     = 玩家可站立的冷库内侧相邻格。
             *
             * 不能固定对 X 或 Z 加减 1，因为不同墙面
             * 的偏移方向不同，地板和天花板还会改变 Y。
             */
            BlockFacing? missingFace =
                BlockFacing.FromCode(faceCode);

            BlockPos wallPosition =
                missingInsulationPos.Copy();

            BlockPos insidePosition =
                missingFace != null
                    ? missingInsulationPos.AddCopy(missingFace)
                    : missingInsulationPos.Copy();

            Vec3i localWallPosition =
                wallPosition.ToLocalPosition(Api);

            Vec3i localInsidePosition =
                insidePosition.ToLocalPosition(Api);

            stringBuilder.AppendLine(
                Lang.Get(
                    "electricalprogressivecoldstore:aircooler-missing-insulation-wall-detail",
                    localWallPosition.X,
                    localWallPosition.Y,
                    localWallPosition.Z,
                    faceName
                )
            );

            stringBuilder.AppendLine(
                Lang.Get(
                    "electricalprogressivecoldstore:aircooler-missing-insulation-inside-detail",
                    localInsidePosition.X,
                    localInsidePosition.Y,
                    localInsidePosition.Z
                )
            );
        }

        stringBuilder.AppendLine(
            Lang.Get(
                "electricalprogressivecoldstore:aircooler-temperature",
                CurrentTemperature
            )
        );

        stringBuilder.AppendLine(
            Lang.Get(
                "electricalprogressivecoldstore:aircooler-perishrate",
                CurrentPerishRate
            )
        );

        if (lastNetwork != null)
        {
            stringBuilder.AppendLine(
                Lang.Get(
                    "electricalprogressivecoldstore:aircooler-pipe-count",
                    lastNetwork.PipeCount
                )
            );
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetFloat(
            TemperatureKey,
            CurrentTemperature
        );

        tree.SetBool(
            InitializedKey,
            temperatureInitialized
        );

        tree.SetDouble(
            LastUpdateHoursKey,
            lastUpdateHours
        );

        tree.SetString(
            "electricalprogressivecoldstore:status",
            statusCode
        );

        tree.SetFloat(
            "electricalprogressivecoldstore:perishrate",
            CurrentPerishRate
        );

        tree.SetBool(
            "electricalprogressivecoldstore:working",
            IsWorking
        );

        bool hasMissingInsulation =
            missingInsulationPos != null
            && !string.IsNullOrEmpty(missingInsulationFaceCode);

        tree.SetBool(
            "electricalprogressivecoldstore:hasmissinginsulation",
            hasMissingInsulation
        );

        if (hasMissingInsulation)
        {
            tree.SetInt(
                "electricalprogressivecoldstore:missinginsulation-x",
                missingInsulationPos!.X
            );

            tree.SetInt(
                "electricalprogressivecoldstore:missinginsulation-y",
                missingInsulationPos.Y
            );

            tree.SetInt(
                "electricalprogressivecoldstore:missinginsulation-z",
                missingInsulationPos.Z
            );

            tree.SetInt(
                "electricalprogressivecoldstore:missinginsulation-dimension",
                missingInsulationPos.dimension
            );

            tree.SetString(
                "electricalprogressivecoldstore:missinginsulation-face",
                missingInsulationFaceCode
            );
        }
    }

    public override void FromTreeAttributes(
        ITreeAttribute tree,
        IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(
            tree,
            worldAccessForResolve
        );

        CurrentTemperature = tree.GetFloat(
            TemperatureKey,
            20f
        );

        temperatureInitialized = tree.GetBool(
            InitializedKey
        );

        lastUpdateHours = tree.GetDouble(
            LastUpdateHoursKey
        );

        statusCode = tree.GetString(
            "electricalprogressivecoldstore:status",
            "initializing"
        );

        CurrentPerishRate = tree.GetFloat(
            "electricalprogressivecoldstore:perishrate",
            1f
        );

        IsWorking = tree.GetBool(
            "electricalprogressivecoldstore:working"
        );

        bool hasMissingInsulation = tree.GetBool(
            "electricalprogressivecoldstore:hasmissinginsulation"
        );

        if (hasMissingInsulation)
        {
            missingInsulationPos = new BlockPos(
                tree.GetInt(
                    "electricalprogressivecoldstore:missinginsulation-x"
                ),
                tree.GetInt(
                    "electricalprogressivecoldstore:missinginsulation-y"
                ),
                tree.GetInt(
                    "electricalprogressivecoldstore:missinginsulation-z"
                ),
                tree.GetInt(
                    "electricalprogressivecoldstore:missinginsulation-dimension"
                )
            );

            missingInsulationFaceCode = tree.GetString(
                "electricalprogressivecoldstore:missinginsulation-face",
                "unknown"
            );
        }
        else
        {
            missingInsulationPos = null;
            missingInsulationFaceCode = "";
        }
    }

    public override void OnBlockRemoved()
    {
        ColdStoreRuntime.GetManager(Api.Side)?.Remove(StateKey);
        if (tickListenerId != 0) UnregisterGameTickListener(tickListenerId);
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        ColdStoreRuntime.GetManager(Api.Side)?.Remove(StateKey);
        if (tickListenerId != 0) UnregisterGameTickListener(tickListenerId);
        base.OnBlockUnloaded();
    }
}
