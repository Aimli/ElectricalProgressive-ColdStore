using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ElectricalProgressiveColdStore.Patches;

/// <summary>
/// 将冷库温度直接作用到玩家实际体温。
///
/// 原版在封闭房间中不会使用 ambientTempChange，
/// 因此这里只补充冷库产生的负温度变化。
/// </summary>
[HarmonyPatch(
    typeof(EntityBehaviorBodyTemperature),
    "updateBodyTemperature"
)]
internal static class BodyTemperatureClimatePatch
{
    private static readonly FieldInfo EntityField =
        AccessTools.Field(
            typeof(EntityBehavior),
            "entity"
        )
        ?? throw new MissingFieldException(
            typeof(EntityBehavior).FullName,
            "entity"
        );

    internal struct PatchState
    {
        public bool Active;

        public double PreviousBodyTempUpdateHours;

        public float ColdStoreTemperature;

        public float PreviousDamagingFreezeHours;
    }

    [HarmonyPrefix]
    internal static void Prefix(
        EntityBehaviorBodyTemperature __instance,
        float ___damagingFreezeHours,
        out PatchState __state)
    {
        __state = default;

        Entity? entity = GetEntity(__instance);

        if (entity?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer)
        {
            return;
        }

        IPlayer? player = entityPlayer.Player;

        if (player == null)
        {
            return;
        }

        if (player is IServerPlayer serverPlayer
            && serverPlayer.ConnectionState != EnumClientState.Playing)
        {
            return;
        }

        EnumGameMode gameMode =
            player.WorldData.CurrentGameMode;

        if (gameMode == EnumGameMode.Creative
            || gameMode == EnumGameMode.Spectator)
        {
            return;
        }

        ColdStoreManager? manager =
            ColdStoreRuntime.GetManager(EnumAppSide.Server);

        if (manager == null)
        {
            return;
        }

        BlockPos playerPos =
            entity.Pos.AsBlockPos;

        if (!manager.TryGetTemperature(
                playerPos,
                out float coldStoreTemperature))
        {
            return;
        }

        __state = new PatchState
        {
            Active = true,
            PreviousBodyTempUpdateHours =
                __instance.BodyTempUpdateTotalHours,
            ColdStoreTemperature =
                coldStoreTemperature,
            PreviousDamagingFreezeHours =
                ___damagingFreezeHours
        };
    }

    [HarmonyPostfix]
    internal static void Postfix(
        EntityBehaviorBodyTemperature __instance,
        float ___clothingBonus,
        int ___sprinterCounter,
        float ___bodyTemperatureResistance,
        ref float ___damagingFreezeHours,
        PatchState __state)
    {
        if (!__state.Active)
        {
            return;
        }

        Entity? entity = GetEntity(__instance);

        if (entity?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        /*
         * 原版着火时会把温度变化提升到至少 25。
         * 冷库不应覆盖着火产生的强烈热量。
         */
        if (entity.IsOnFire)
        {
            return;
        }

        /*
         * 原版正常完成体温更新后，会更新
         * BodyTempUpdateTotalHours。
         *
         * 如果数值没有变化，说明原方法提前返回，
         * 本补丁也不应改变体温。
         */
        double updatedHours =
            __instance.BodyTempUpdateTotalHours;

        double elapsedHours =
            updatedHours
            - __state.PreviousBodyTempUpdateHours;

        if (elapsedHours <= 0.01)
        {
            return;
        }

        float sprintBonus =
            ___sprinterCounter / 2f;

        float wetnessDebuff =
            (float)Math.Max(
                0,
                __instance.Wetness - 0.1f
            ) * 15f;

        /*
         * 使用与原版相同的温度组成：
         *
         * 环境温度
         * + 衣物保暖
         * + 奔跑加成
         * - 潮湿惩罚
         */
        float effectiveTemperature =
            __state.ColdStoreTemperature
            + ___clothingBonus
            + sprintBonus
            - wetnessDebuff;

        float temperatureDifference =
            effectiveTemperature
            - GameMath.Clamp(
                effectiveTemperature,
                ___bodyTemperatureResistance,
                50f
            );

        if (temperatureDifference == 0)
        {
            temperatureDifference =
                Math.Max(
                    effectiveTemperature
                    - ___bodyTemperatureResistance,
                    0f
                );
        }

        /*
         * 使用原版的最大降温限制：
         * 最多每游戏小时降低 6°C。
         *
         * 这里只允许冷却，不允许该补丁加热玩家。
         */
        float coldStoreTemperatureChange =
            GameMath.Clamp(
                temperatureDifference / 6f,
                -6f,
                0f
            );

        /*
         * 与原版一致：
         * 很小的负温度变化不修改玩家体温。
         */
        if (coldStoreTemperatureChange >= -0.5f)
        {
            return;
        }

        float newBodyTemperature =
            __instance.CurBodyTemperature
            + coldStoreTemperatureChange
            * (float)elapsedHours;

        __instance.CurBodyTemperature =
            GameMath.Clamp(
                newBodyTemperature,
                31f,
                45f
            );

        /*
         * 原版已经在 Postfix 之前计算过一次冻僵强度。
         * 由于我们刚刚再次降低了实际体温，需要重新计算。
         */
        float freezingEffectStrength =
            GameMath.Clamp(
                (
                    __instance.NormalBodyTemperature
                    - __instance.CurBodyTemperature
                ) / 4f - 0.5f,
                0f,
                1f
            );

        entity.WatchedAttributes.SetFloat(
            "freezingEffectStrength",
            freezingEffectStrength
        );

        /*
         * 防止 damagingFreezeHours 被重复累计。
         * 从原方法运行前的值重新计算一次。
         */
        if (__instance.NormalBodyTemperature
            - __instance.CurBodyTemperature > 4f)
        {
            ___damagingFreezeHours =
                __state.PreviousDamagingFreezeHours
                + (float)elapsedHours;
        }
        else
        {
            ___damagingFreezeHours = 0f;
        }

        entity.WatchedAttributes.MarkPathDirty(
            "bodyTemp"
        );
    }

    private static Entity? GetEntity(
        EntityBehaviorBodyTemperature behavior)
    {
        return EntityField.GetValue(behavior)
            as Entity;
    }
}