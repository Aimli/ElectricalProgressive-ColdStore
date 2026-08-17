using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressiveColdStore.Content;

/// <summary>
/// 根据相邻制冷管路和制冷设备，动态生成连接方向。
/// </summary>
public sealed class BlockRefrigerantPipe : Block
{
    private const string Domain =
        "electricalprogressivecoldstore";

    private const string ShapeAssetPath =
        "shapes/block/refrigerantpipe.json";

    /*
     * 六个方向分别占一个位，因此总共有 64 种组合。
     *
     * bit 0～5 对应 BlockFacing.Index。
     */
    private readonly MeshData?[] meshesByMask =
        new MeshData?[64];

    private static readonly Cuboidf[][] BoxesByMask =
        BuildBoxesByMask();

    private ICoreClientAPI? clientApi;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is not ICoreClientAPI capi)
        {
            return;
        }

        clientApi = capi;

        IAsset? shapeAsset = capi.Assets.TryGet(
            new AssetLocation(
                Domain,
                ShapeAssetPath
            )
        );

        Shape? pipeShape =
            shapeAsset?.ToObject<Shape>();

        if (pipeShape == null)
        {
            api.Logger.Error(
                "[ColdStore] Failed to load refrigerant pipe shape: {0}:{1}",
                Domain,
                ShapeAssetPath
            );

            return;
        }

        /*
         * 预先生成全部 64 种连接组合。
         *
         * 区块网格生成时只读取缓存，
         * 不会每一根管道都重新处理 JSON 形状。
         */
        for (int mask = 0; mask < 64; mask++)
        {
            string[] selectedElements =
                BuildElementNames(mask);

            capi.Tesselator.TesselateShape(
                this,
                pipeShape,
                out MeshData mesh,
                null,
                null,
                selectedElements
            );

            meshesByMask[mask] = mesh;
        }
    }

    public override void OnJsonTesselation(
        ref MeshData sourceMesh,
        ref int[] lightRgbsByCorner,
        BlockPos position,
        Block[] chunkExtBlocks,
        int extIndex3d)
    {
        ICoreClientAPI? capi = clientApi;

        if (capi != null)
        {
            int connectionMask =
                GetConnectionMask(
                    capi.World.BlockAccessor,
                    position
                );

            MeshData? connectedMesh =
                meshesByMask[connectionMask];

            if (connectedMesh != null)
            {
                sourceMesh = connectedMesh;
            }
        }

        base.OnJsonTesselation(
            ref sourceMesh,
            ref lightRgbsByCorner,
            position,
            chunkExtBlocks,
            extIndex3d
        );
    }

    public override Cuboidf[] GetSelectionBoxes(
        IBlockAccessor blockAccessor,
        BlockPos position)
    {
        /*
         * 管道已经用墙体方块覆盖时，
         * 使用完整的一立方米选择框。
         */
        if (HasCover(blockAccessor, position))
        {
            return Block.DefaultCollisionSelectionBoxes;
        }

        int mask = GetConnectionMask(
            blockAccessor,
            position
        );

        return BoxesByMask[mask];
    }

    public override Cuboidf[] GetCollisionBoxes(
        IBlockAccessor blockAccessor,
        BlockPos position)
    {
        /*
         * 管道已经用墙体方块覆盖时，
         * 使用完整的一立方米碰撞箱。
         */
        if (HasCover(blockAccessor, position))
        {
            return Block.DefaultCollisionSelectionBoxes;
        }

        int mask = GetConnectionMask(
            blockAccessor,
            position
        );

        return BoxesByMask[mask];
    }

    public override void OnNeighbourBlockChange(
        IWorldAccessor world,
        BlockPos position,
        BlockPos neighbourPosition)
    {
        base.OnNeighbourBlockChange(
            world,
            position,
            neighbourPosition
        );

        /*
         * 相邻管道或机器发生变化后，
         * 重新生成当前位置的区块网格。
         */
        world.BlockAccessor.MarkBlockDirty(
            position
        );
    }

    private static bool HasCover(
        IBlockAccessor blockAccessor,
        BlockPos position)
    {
        BlockEntityBehaviorCoverable? coverable =
            blockAccessor
                .GetBlockEntity(position)?
                .GetBehavior<BlockEntityBehaviorCoverable>();

        return coverable?.WallStack != null;
    }

    private static int GetConnectionMask(
            IBlockAccessor blockAccessor,
        BlockPos position)
    {
        int mask = 0;

        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            BlockPos neighbourPosition =
                OffsetCopy(position, face);

            Block neighbourBlock =
                blockAccessor.GetBlock(
                    neighbourPosition
                );

            if (!CanConnectTo(neighbourBlock))
            {
                continue;
            }

            mask |= FaceBit(face);
        }

        return mask;
    }

    private static bool CanConnectTo(Block? block)
    {
        if (block?.Code == null)
        {
            return false;
        }

        if (block.Code.Domain != Domain)
        {
            return false;
        }

        string path = block.Code.Path;

        return path.StartsWith(
                   "refrigerantpipe",
                   StringComparison.Ordinal
               )
               || path.StartsWith(
                   "aircooler",
                   StringComparison.Ordinal
               )
               || path.StartsWith(
                   "condensingunit",
                   StringComparison.Ordinal
               );
    }

    private static string[] BuildElementNames(
        int mask)
    {
        List<string> elementNames =
            new()
            {
                "core"
            };

        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if ((mask & FaceBit(face)) == 0)
            {
                continue;
            }

            elementNames.Add(face.Code);
        }

        return elementNames.ToArray();
    }

    private static Cuboidf[][] BuildBoxesByMask()
    {
        Cuboidf[][] result =
            new Cuboidf[64][];

        const float coreMin = 5f / 16f;
        const float coreMax = 11f / 16f;

        const float pipeMin = 6f / 16f;
        const float pipeMax = 10f / 16f;

        for (int mask = 0; mask < 64; mask++)
        {
            List<Cuboidf> boxes =
                new()
                {
                    // 中央接头
                    new Cuboidf(
                        coreMin,
                        coreMin,
                        coreMin,
                        coreMax,
                        coreMax,
                        coreMax
                    )
                };

            if (HasFace(mask, BlockFacing.NORTH))
            {
                boxes.Add(
                    new Cuboidf(
                        pipeMin,
                        pipeMin,
                        0f,
                        pipeMax,
                        pipeMax,
                        coreMin
                    )
                );
            }

            if (HasFace(mask, BlockFacing.EAST))
            {
                boxes.Add(
                    new Cuboidf(
                        coreMax,
                        pipeMin,
                        pipeMin,
                        1f,
                        pipeMax,
                        pipeMax
                    )
                );
            }

            if (HasFace(mask, BlockFacing.SOUTH))
            {
                boxes.Add(
                    new Cuboidf(
                        pipeMin,
                        pipeMin,
                        coreMax,
                        pipeMax,
                        pipeMax,
                        1f
                    )
                );
            }

            if (HasFace(mask, BlockFacing.WEST))
            {
                boxes.Add(
                    new Cuboidf(
                        0f,
                        pipeMin,
                        pipeMin,
                        coreMin,
                        pipeMax,
                        pipeMax
                    )
                );
            }

            if (HasFace(mask, BlockFacing.UP))
            {
                boxes.Add(
                    new Cuboidf(
                        pipeMin,
                        coreMax,
                        pipeMin,
                        pipeMax,
                        1f,
                        pipeMax
                    )
                );
            }

            if (HasFace(mask, BlockFacing.DOWN))
            {
                boxes.Add(
                    new Cuboidf(
                        pipeMin,
                        0f,
                        pipeMin,
                        pipeMax,
                        coreMin,
                        pipeMax
                    )
                );
            }

            result[mask] = boxes.ToArray();
        }

        return result;
    }

    private static bool HasFace(
        int mask,
        BlockFacing face)
    {
        return (mask & FaceBit(face)) != 0;
    }

    private static int FaceBit(
        BlockFacing face)
    {
        return 1 << face.Index;
    }

    private static BlockPos OffsetCopy(
        BlockPos position,
        BlockFacing face)
    {
        Vec3i normal = face.Normali;

        return new BlockPos(
            position.X + normal.X,
            position.Y + normal.Y,
            position.Z + normal.Z,
            position.dimension
        );
    }
}