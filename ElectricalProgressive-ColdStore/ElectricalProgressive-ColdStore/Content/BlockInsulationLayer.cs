using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressiveColdStore.Content;

/// <summary>
/// Places this block as a native Vintage Story decor, allowing one independent layer on each face.
/// The parent wall remains in place; the decor is stored by face by the chunk/block accessor.
/// </summary>
public sealed class BlockInsulationLayer : Block
{
    public override bool TryPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        ItemStack itemstack,
        BlockSelection blockSel,
        ref string failureCode)
    {
        if (blockSel?.Face == null)
        {
            failureCode = "requires-face";
            return false;
        }

        BlockFacing face = blockSel.Face;
        BlockPos anchorPos = blockSel.Position.Copy();
        Block anchorBlock = world.BlockAccessor.GetBlock(anchorPos);

        // Normal block placement often offsets Position into the adjacent replaceable cell.
        // In that case, walk back to the clicked supporting block.
        if (anchorBlock.IsReplacableBy(this))
        {
            Vec3i back = face.Opposite.Normali;
            anchorPos = new BlockPos(
                anchorPos.X + back.X,
                anchorPos.Y + back.Y,
                anchorPos.Z + back.Z,
                anchorPos.dimension);
            anchorBlock = world.BlockAccessor.GetBlock(anchorPos);
        }

        if (!world.Claims.TryAccess(byPlayer, anchorPos, EnumBlockAccessFlags.BuildOrBreak))
        {
            failureCode = "claimed";
            return false;
        }

        if (!anchorBlock.SideIsSolid(world.BlockAccessor, anchorPos, face.Index))
        {
            failureCode = "requires-solid-face";
            return false;
        }

        if (world.BlockAccessor.GetDecor(anchorPos, new DecorBits(face)) != null)
        {
            failureCode = "face-occupied";
            return false;
        }

        world.BlockAccessor.SetDecor(this, anchorPos, face);
        return true;
    }
}
