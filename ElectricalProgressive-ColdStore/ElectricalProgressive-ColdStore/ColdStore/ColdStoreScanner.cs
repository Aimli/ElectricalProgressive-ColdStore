using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressiveColdStore;

public static class ColdStoreScanner
{
    private const string Domain =
        "electricalprogressivecoldstore";

    private const int MaximumRoomSize = 14;

    public static ColdStoreValidationResult Validate(
        ICoreAPI api,
        BlockPos airCoolerPos,
        IReadOnlyCollection<BlockPos> connectedCondensers,
        bool refrigerantNetworkIsConnected,
        bool refrigerantNetworkReachedScanLimit)
    {
        RoomRegistry roomRegistry =
            api.ModLoader.GetModSystem<RoomRegistry>();

        Room? room =
            FindInteriorRoom(
                roomRegistry,
                api.World.BlockAccessor,
                airCoolerPos
            );

        /*
         * 第一阶段：检测房间本身是否有效。
         */
        if (room == null)
        {
            return ColdStoreValidationResult.Invalid(
                "no-room"
            );
        }

        if (!IsWithinMaximumRoomSize(room))
        {
            return ColdStoreValidationResult.Invalid(
                "room-too-large",
                room
            );
        }

        if (room.ExitCount != 0)
        {
            return ColdStoreValidationResult.Invalid(
                "room-not-sealed",
                room
            );
        }

        int interiorVolume =
            CountInteriorVolume(
                room,
                airCoolerPos.dimension
            );

        IBlockAccessor blocks =
            api.World.BlockAccessor;

        Dictionary<string, BEBehaviorDoor> doorsByRoot =
            new(StringComparer.Ordinal);

        /*
         * 这些边界已确认能够密闭房间。
         * 冷凝机检查通过后，再统一检查其保温层。
         */
        List<(
            BlockPos Position,
            BlockFacing RoomFacing
        )> boundariesRequiringInsulation =
            new();

        int boundaryFaces = 0;
        int insulatedFaces = 0;

        /*
         * 房间是否密闭只由 RoomRegistry 判断。
         *
         * 冷风机本体会在下面的边界循环中被忽略，
         * 不再进行额外的方向或背墙检查。
         */
        Cuboidi box = room.Location;

        for (int x = box.X1; x <= box.X2; x++)
        {
            for (int y = box.Y1; y <= box.Y2; y++)
            {
                for (int z = box.Z1; z <= box.Z2; z++)
                {
                    BlockPos interiorPos =
                        new(
                            x,
                            y,
                            z,
                            airCoolerPos.dimension
                        );

                    if (!room.Contains(interiorPos))
                    {
                        continue;
                    }

                    foreach (
                        BlockFacing outward
                        in BlockFacing.ALLFACES)
                    {
                        BlockPos boundaryPos =
                            OffsetCopy(
                                interiorPos,
                                outward
                            );

                        if (room.Contains(boundaryPos))
                        {
                            continue;
                        }

                        /*
                         * 冷风机占据冷库内部的一格实体空间。
                         *
                         * 房间是否密闭已经由 room.ExitCount 检查，
                         * 所以这里不再把冷风机本体当作外墙。
                         *
                         * 冷风机直接替代墙体时，RoomRegistry 会把房间
                         * 判定为不密闭，并在前面的 room.ExitCount 阶段失败。
                         */
                        if (boundaryPos.Equals(airCoolerPos))
                        {
                            continue;
                        }

                        /*
                         * 房间包围盒内部的其他实体方块属于室内物体，
                         * 例如箱子、架子和玩家放置的普通方块。
                         */
                        if (IsInsideRoomBounds(
                                room,
                                boundaryPos))
                        {
                            continue;
                        }

                        boundaryFaces++;

                        /*
                         * 冷库门属于房间结构检查。
                         */
                        BEBehaviorDoor? door =
                            GetColdStoreDoor(
                                api.World,
                                boundaryPos
                            );

                        if (door != null)
                        {
                            if (door.Opened)
                            {
                                return
                                    ColdStoreValidationResult
                                        .Invalid(
                                            "door-open",
                                            room
                                        );
                            }

                            doorsByRoot[
                                DoorRootKey(door)
                            ] = door;

                            /*
                             * 冷库门是唯一不需要保温层的边界。
                             */
                            continue;
                        }

                        BlockFacing roomFacing =
                            outward.Opposite;

                        /*
                         * room.ExitCount == 0 已经确认房间边界密闭。
                         *
                         * 此处只记录需要检查保温层的边界表面，
                         * 不再重复读取各方向面的 Heat retention。
                         *
                         * 被完整方块覆盖的制冷管路是否能够构成墙体，
                         * 也由 RoomRegistry 和 Coverable 行为统一决定。
                         */
                        boundariesRequiringInsulation.Add(
                            (
                                boundaryPos.Copy(),
                                roomFacing
                            )
                        );
                    }
                }
            }
        }

        /*
         * 房间必须有一扇单门，
         * 或者一组正确联动的双开门。
         */
        if (doorsByRoot.Count == 0)
        {
            return new ColdStoreValidationResult
            {
                IsValid = false,
                FailureCode =
                    "no-cold-store-door",
                Room = room,
                BoundaryFaceCount =
                    boundaryFaces,
                InsulatedFaceCount =
                    insulatedFaces,
                DoorCount = 0
            };
        }

        if (doorsByRoot.Count > 2)
        {
            return new ColdStoreValidationResult
            {
                IsValid = false,
                FailureCode =
                    "multiple-cold-store-doors",
                Room = room,
                BoundaryFaceCount =
                    boundaryFaces,
                InsulatedFaceCount =
                    insulatedFaces,
                DoorCount =
                    doorsByRoot.Count
            };
        }

        if (doorsByRoot.Count == 2)
        {
            BEBehaviorDoor[] doors =
                doorsByRoot.Values.ToArray();

            if (!AreLinkedDoubleDoors(
                    doors[0],
                    doors[1]))
            {
                return new ColdStoreValidationResult
                {
                    IsValid = false,
                    FailureCode =
                        "double-door-not-linked",
                    Room = room,
                    BoundaryFaceCount =
                        boundaryFaces,
                    InsulatedFaceCount =
                        insulatedFaces,
                    DoorCount = 2
                };
            }
        }

        /*
         * 第二阶段：检测制冷管网和冷凝机。
         *
         * 只有房间结构完全有效后，才返回这些错误。
         */
        if (refrigerantNetworkReachedScanLimit)
        {
            return
                ColdStoreValidationResult.Invalid(
                    "pipe-network-too-large",
                    room
                );
        }

        if (!refrigerantNetworkIsConnected
            || connectedCondensers.Count == 0)
        {
            return
                ColdStoreValidationResult.Invalid(
                    "no-condensing-unit",
                    room
                );
        }

        bool hasOutsideCondenser =
            connectedCondensers.Any(
                condenserPos =>
                    IsOutsideRoom(
                        room,
                        condenserPos
                    )
            );

        if (!hasOutsideCondenser)
        {
            return
                ColdStoreValidationResult.Invalid(
                    "condensing-unit-not-outside",
                    room
                );
        }

        /*
         * 第三阶段：检测保温层。
         *
         * 此时房间、门、管路和冷凝机都已经有效。
         */
        foreach (
            (
                BlockPos Position,
                BlockFacing RoomFacing
            ) boundary
            in boundariesRequiringInsulation)
        {
            Block? decor =
                blocks.GetDecor(
                    boundary.Position,
                    new DecorBits(
                        boundary.RoomFacing
                    )
                );

            if (!IsInsulation(decor))
            {
                return
                    ColdStoreValidationResult.Invalid(
                        "missing-insulation",
                        room,
                        boundary.Position.Copy(),
                        boundary.RoomFacing
                    );
            }

            insulatedFaces++;
        }

        return new ColdStoreValidationResult
        {
            IsValid = true,
            FailureCode = "ok",
            Room = room,
            BoundaryFaceCount =
                boundaryFaces,
            InsulatedFaceCount =
                insulatedFaces,
            DoorCount =
                doorsByRoot.Count,
            InteriorVolume =
                interiorVolume
        };
    }

    private static Room? FindInteriorRoom(
        RoomRegistry registry,
        IBlockAccessor blocks,
        BlockPos airCoolerPos)
    {
        Room? sealedOversizedCandidate = null;
        Room? openCandidate = null;

        foreach (
            BlockFacing face
            in BlockFacing.ALLFACES)
        {
            BlockPos candidate =
                OffsetCopy(
                    airCoolerPos,
                    face
                );

            Block candidateBlock =
                blocks.GetBlock(candidate);

            /*
             * 只允许从能够作为房间内部空间的位置
             * 开始 RoomRegistry 搜索。
             *
             * 完整墙体、地板和天花板不能作为搜索起点，
             * 否则可能把墙体方块识别为一格假房间，
             * 最终错误地提示“没有冷库门”。
             */
            if (!CanSeedRoomSearch(
                    candidateBlock,
                    candidate))
            {
                continue;
            }

            Room? room =
                registry.GetRoomForPosition(
                    candidate
                );

            if (room == null)
            {
                continue;
            }

            if (!room.Contains(candidate))
            {
                continue;
            }

            /*
             * 最高优先级：
             * 已密闭且尺寸合格的真实房间。
             */
            if (room.ExitCount == 0
                && IsWithinMaximumRoomSize(room))
            {
                return room;
            }

            /*
             * 房间已密闭，但尺寸超过限制。
             * 保留它，让 Validate() 返回 room-too-large。
             */
            if (room.ExitCount == 0)
            {
                sealedOversizedCandidate ??=
                    room;

                continue;
            }

            /*
             * 开放空间或未封闭房间。
             * 保留它，让 Validate() 返回 room-not-sealed，
             * 而不是继续进入冷库门检查。
             */
            openCandidate ??=
                room;
        }

        return sealedOversizedCandidate
            ?? openCandidate;
    }

    private static bool CanSeedRoomSearch(
    Block block,
    BlockPos position)
    {
        /*
         * 空气始终可以作为房间搜索起点。
         */
        if (block.Id == 0)
        {
            return true;
        }

        /*
         * 非空气方块只有在至少一个表面不封闭时，
         * 才可能属于房间内部空间。
         *
         * 箱子、架子以及其他非完整室内方块仍可接受；
         * 六面全部封闭的墙体、地板和天花板会被排除。
         */
        foreach (
            BlockFacing face
            in BlockFacing.ALLFACES)
        {
            int retention =
                block.GetRetention(
                    position,
                    face,
                    EnumRetentionType.Heat
                );

            if (retention == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountInteriorVolume(
            Room room,
        int dimension)
    {
        Cuboidi box = room.Location;
        int volume = 0;

        for (int x = box.X1; x <= box.X2; x++)
        {
            for (int y = box.Y1; y <= box.Y2; y++)
            {
                for (int z = box.Z1; z <= box.Z2; z++)
                {
                    BlockPos position = new(
                        x,
                        y,
                        z,
                        dimension
                    );

                    if (room.Contains(position))
                    {
                        volume++;
                    }
                }
            }
        }

        /*
         * 当前房间尺寸限制为每轴最多 14 格，
         * 所以实际容积不应超过 14³ = 2744。
         *
         * Clamp 作为额外保护，避免异常房间数据
         * 让设备请求超过额定功率。
         */
        return Math.Clamp(
            volume,
            0,
            MaximumRoomSize
                * MaximumRoomSize
                * MaximumRoomSize
        );
    }

    private static bool IsInsideRoomBounds(
            Room room,
        BlockPos pos)
    {
        Cuboidi box = room.Location;

        return pos.X >= box.X1
            && pos.X <= box.X2
            && pos.Y >= box.Y1
            && pos.Y <= box.Y2
            && pos.Z >= box.Z1
            && pos.Z <= box.Z2;
    }

    private static bool IsWithinMaximumRoomSize(
        Room room)
    {
        Cuboidi box = room.Location;

        int sizeX =
            box.X2 - box.X1 + 1;

        int sizeY =
            box.Y2 - box.Y1 + 1;

        int sizeZ =
            box.Z2 - box.Z1 + 1;

        return sizeX <= MaximumRoomSize
            && sizeY <= MaximumRoomSize
            && sizeZ <= MaximumRoomSize;
    }

    private static bool IsOutsideRoom(
        Room room,
        BlockPos condenserPos)
    {
        /*
         * 冷凝机不能占据冷库内部空间，
         * 也不能紧邻任何内部空气格。
         */
        return BlockFacing.ALLFACES.All(
            face => !room.Contains(
                OffsetCopy(condenserPos, face)
            )
        );
    }

    private static BEBehaviorDoor? GetColdStoreDoor(
        IWorldAccessor world,
        BlockPos boundaryPos)
    {
        /*
         * 原版 Door 使用一个真实根方块，
         * 其余高度位置是 BlockMultiblock 占位方块。
         *
         * getDoorAt() 可以从根方块或任意占位部分
         * 找到同一个 BEBehaviorDoor。
         */
        BEBehaviorDoor? door =
            BlockBehaviorDoor.getDoorAt(
                world,
                boundaryPos
            );

        if (door == null)
        {
            return null;
        }

        Block rootBlock =
            world.BlockAccessor.GetBlock(
                door.Pos
            );

        if (rootBlock.Attributes?[
                "isColdStoreDoor"
            ].AsBool(false) == true)
        {
            return door;
        }

        if (rootBlock.Code?.Domain != Domain)
        {
            return null;
        }

        if (!rootBlock.Code.Path.StartsWith(
                "coldstoredoor",
                StringComparison.Ordinal))
        {
            return null;
        }

        return door;
    }

    private static bool IsInsulation(Block? block)
    {
        if (block?.Code == null)
        {
            return false;
        }

        // 优先读取保温层 JSON 中的识别属性。
        if (block.Attributes?["coldStoreInsulation"].AsBool(false) == true)
        {
            return true;
        }

        // 属性读取失败时，通过方块代码兼容判断。
        return block.Code.Domain == Domain
            && block.Code.Path.StartsWith(
                "coldstore-insulation",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool AreLinkedDoubleDoors(
    BEBehaviorDoor first,
    BEBehaviorDoor second)
    {
        if (first.Pos.dimension
            != second.Pos.dimension)
        {
            return false;
        }

        /*
         * 两扇门的根部必须位于同一高度。
         */
        if (first.Pos.Y != second.Pos.Y)
        {
            return false;
        }

        /*
         * 双门必须朝向相同。
         */
        if (first.facingWhenClosed
            != second.facingWhenClosed)
        {
            return false;
        }

        /*
         * 原版双开门会将其中一个门扇镜像，
         * 所以两扇门的 InvertHandles 应当相反。
         */
        if (first.InvertHandles
            == second.InvertHandles)
        {
            return false;
        }

        /*
         * 对当前宽度为 1 的冷库门，
         * 两个根方块必须水平紧邻。
         */
        int distanceX =
            Math.Abs(first.Pos.X - second.Pos.X);

        int distanceZ =
            Math.Abs(first.Pos.Z - second.Pos.Z);

        if (distanceX + distanceZ != 1)
        {
            return false;
        }

        /*
         * 最重要的检查：
         * 两扇门必须拥有原版 Door 行为建立的
         * LeftDoor/RightDoor 联动关系。
         */
        bool linked =
            IsSameDoor(first.LeftDoor, second)
            || IsSameDoor(first.RightDoor, second)
            || IsSameDoor(second.LeftDoor, first)
            || IsSameDoor(second.RightDoor, first);

        return linked;
    }

    private static bool IsSameDoor(
        BEBehaviorDoor? candidate,
        BEBehaviorDoor expected)
    {
        return candidate != null
            && candidate.Pos.Equals(expected.Pos);
    }

    private static string DoorRootKey(
        BEBehaviorDoor door)
    {
        BlockPos pos = door.Pos;

        return
            $"{pos.dimension}:{pos.X}:{pos.Y}:{pos.Z}";
    }

    public static BlockPos OffsetCopy(BlockPos pos, BlockFacing face)
    {
        Vec3i normal = face.Normali;
        return new BlockPos(pos.X + normal.X, pos.Y + normal.Y, pos.Z + normal.Z, pos.dimension);
    }
}
