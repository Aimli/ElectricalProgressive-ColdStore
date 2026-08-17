using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressiveColdStore;

public sealed class RefrigerantNetworkResult
{
    public bool IsConnected => Condensers.Count > 0;
    public bool ReachedScanLimit { get; init; }
    public int PipeCount { get; init; }
    public List<BlockPos> Condensers { get; init; } = [];
}

public static class RefrigerantNetwork
{
    private const string Domain = "electricalprogressivecoldstore";
    private const int MaxNetworkBlocks = 128;

    public static RefrigerantNetworkResult Scan(IBlockAccessor blocks, BlockPos airCoolerPos)
    {
        Queue<BlockPos> queue = new();
        HashSet<string> visited = new(StringComparer.Ordinal);
        List<BlockPos> condensers = [];
        int pipeCount = 0;
        bool reachedLimit = false;

        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            queue.Enqueue(ColdStoreScanner.OffsetCopy(airCoolerPos, face));
        }

        while (queue.Count > 0)
        {
            BlockPos pos = queue.Dequeue();
            string key = Key(pos);
            if (!visited.Add(key)) continue;

            if (visited.Count > MaxNetworkBlocks)
            {
                reachedLimit = true;
                break;
            }

            Block block = blocks.GetBlock(pos);
            if (block.Code?.Domain != Domain) continue;

            if (block.Code.Path.StartsWith("condensingunit", StringComparison.Ordinal))
            {
                condensers.Add(pos.Copy());
                continue;
            }

            if (!block.Code.Path.StartsWith("refrigerantpipe", StringComparison.Ordinal))
                continue;

            pipeCount++;
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                queue.Enqueue(ColdStoreScanner.OffsetCopy(pos, face));
            }
        }

        return new RefrigerantNetworkResult
        {
            Condensers = condensers,
            PipeCount = pipeCount,
            ReachedScanLimit = reachedLimit
        };
    }

    private static string Key(BlockPos pos) => $"{pos.dimension}:{pos.X}:{pos.Y}:{pos.Z}";
}
