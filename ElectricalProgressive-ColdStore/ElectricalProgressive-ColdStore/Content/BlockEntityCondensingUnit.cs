using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ElectricalProgressiveColdStore.Content;

public sealed class BlockEntityCondensingUnit : BlockEntity
{
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);
        stringBuilder.AppendLine(Lang.Get("electricalprogressivecoldstore:condensingunit-info"));
    }
}
