using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressiveColdStore;

public sealed class ColdStoreManager
{
    private const long StateTimeoutMilliseconds = 12_000;
    private readonly object sync = new();
    private readonly Dictionary<string, ColdStoreState> states = new(StringComparer.Ordinal);
    private readonly ICoreAPI api;

    public ColdStoreManager(ICoreAPI api)
    {
        this.api = api;
    }

    public void Upsert(ColdStoreState state)
    {
        lock (sync)
        {
            RemoveExpiredUnsafe();
            states[state.Key] = state;
        }
    }

    public void Remove(string key)
    {
        lock (sync)
        {
            states.Remove(key);
        }
    }

    public bool TryGetTemperature(
        BlockPos pos,
        out float temperature)
    {
        lock (sync)
        {
            RemoveExpiredUnsafe();

            float lowestTemperature = float.MaxValue;

            foreach (ColdStoreState state in states.Values)
            {
                if (state.AirCoolerPos.dimension != pos.dimension)
                {
                    continue;
                }

                if (state.Room.Contains(pos))
                {
                    lowestTemperature = Math.Min(
                        lowestTemperature,
                        state.Temperature
                    );
                }
            }

            if (lowestTemperature < float.MaxValue)
            {
                temperature = lowestTemperature;
                return true;
            }
        }

        temperature = 0f;
        return false;
    }

    public bool TryGetPerishRate(
        BlockPos pos,
        out float perishRate)
    {
        lock (sync)
        {
            RemoveExpiredUnsafe();

            float bestRate = float.MaxValue;

            foreach (ColdStoreState state in states.Values)
            {
                if (state.AirCoolerPos.dimension != pos.dimension)
                {
                    continue;
                }

                if (state.Room.Contains(pos))
                {
                    bestRate = Math.Min(
                        bestRate,
                        state.PerishRate
                    );
                }
            }

            if (bestRate < float.MaxValue)
            {
                perishRate = bestRate;
                return true;
            }
        }

        perishRate = 1f;
        return false;
    }

    public IReadOnlyList<ColdStoreState> Snapshot()
    {
        lock (sync)
        {
            RemoveExpiredUnsafe();
            return states.Values.ToArray();
        }
    }

    private void RemoveExpiredUnsafe()
    {
        long now = api.World.ElapsedMilliseconds;
        foreach (string key in states
                     .Where(pair => now - pair.Value.LastRefreshMilliseconds > StateTimeoutMilliseconds)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            states.Remove(key);
        }
    }
}
