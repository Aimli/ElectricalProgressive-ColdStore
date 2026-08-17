using Vintagestory.API.Common;

namespace ElectricalProgressiveColdStore;

public static class ColdStoreRuntime
{
    private static readonly object Sync = new();
    private static ColdStoreManager? serverManager;
    private static ColdStoreManager? clientManager;

    public static ColdStoreManager? ServerManager
    {
        get { lock (Sync) return serverManager; }
    }

    public static ColdStoreManager? ClientManager
    {
        get { lock (Sync) return clientManager; }
    }

    public static void SetManager(EnumAppSide side, ColdStoreManager manager)
    {
        lock (Sync)
        {
            if (side == EnumAppSide.Server) serverManager = manager;
            else clientManager = manager;
        }
    }

    public static ColdStoreManager? GetManager(EnumAppSide side)
    {
        return side == EnumAppSide.Server ? ServerManager : ClientManager;
    }

    public static void ClearManager(EnumAppSide side)
    {
        lock (Sync)
        {
            if (side == EnumAppSide.Server) serverManager = null;
            else clientManager = null;
        }
    }
}
