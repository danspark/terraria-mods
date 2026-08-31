using System;
using Terraria;
using Terraria.ID;

namespace RicherBiomes.WorldGeneration;

internal static class MineGenerator
{
	public static void Apply(WorldPlan plan)
	{
		int quarryStart = plan.Mine.Start + 62;
		int quarryEnd = plan.Mine.End - 18;
		int shaftDistance = plan.Mine.Start + 172;
		int shaftX = plan.XAt(shaftDistance);
		int shaftTopY = CarveQuarry(plan, quarryStart, quarryEnd, shaftDistance);

		CarveCaveJunction(plan, shaftX);
		CarveBranch(plan, shaftX, shaftTopY + 58, plan.Direction, 82);
		CarveBranch(plan, shaftX, shaftTopY + 126, -plan.Direction, 96);
		CarveBranch(plan, shaftX, Math.Min(plan.MineBottomY - 34, shaftTopY + 194), plan.Direction, 118);
		CarveShaft(plan, shaftX, shaftTopY);
	}

	private static int CarveQuarry(WorldPlan plan, int startDistance, int endDistance, int shaftDistance)
	{
		int width = endDistance - startDistance;
		int shaftTopY = plan.BaseSurfaceY + 45;

		for (int distance = startDistance; distance <= endDistance; distance++) {
			int local = distance - startDistance;
			int edgeDistance = Math.Min(local, width - local);
			int depth = 8 + Math.Min(58, edgeDistance * 2 / 3);
			int x = plan.XAt(distance);
			int surfaceY = WorldPlanner.FindSurfaceY(x);
			int floorY = surfaceY + depth;

			for (int y = Math.Max(35, surfaceY - 18); y < floorY; y++) {
				TileEditor.ClearTile(x, y);
				if (y >= surfaceY + 4) {
					TileEditor.SetWall(x, y, WallID.DirtUnsafe);
				}
			}

			TileEditor.SetTile(x, floorY, depth > 35 ? TileID.Stone : TileID.Dirt);
			if (Math.Abs(distance - shaftDistance) <= 3) {
				shaftTopY = Math.Max(shaftTopY, floorY - 2);
			}
		}

		for (int distance = startDistance + 25; distance < endDistance - 20; distance += 34) {
			int x = plan.XAt(distance);
			int surfaceY = WorldPlanner.FindSurfaceY(x);
			for (int offset = -5; offset <= 5; offset++) {
				TileEditor.PlacePlatform(x + offset, surfaceY + 7);
			}
		}

		return shaftTopY;
	}

	private static void CarveShaft(WorldPlan plan, int shaftX, int topY)
	{
		for (int y = topY; y <= plan.MineBottomY; y++) {
			for (int offset = -5; offset <= 5; offset++) {
				TileEditor.ClearTile(shaftX + offset, y);
				TileEditor.SetWall(shaftX + offset, y, y < Main.worldSurface ? WallID.DirtUnsafe : WallID.Stone);
			}

			TileEditor.SetTile(shaftX, y, TileID.Rope);
			TileEditor.SetTile(shaftX - 6, y, TileID.WoodenBeam);
			TileEditor.SetTile(shaftX + 6, y, TileID.WoodenBeam);

			if ((y - topY) % 14 == 0) {
				for (int offset = -5; offset <= 5; offset++) {
					if (offset != 0) {
						TileEditor.PlacePlatform(shaftX + offset, y);
					}
				}
			}
		}
	}

	private static void CarveBranch(WorldPlan plan, int shaftX, int floorY, int direction, int length)
	{
		if (floorY >= plan.MineBottomY - 8) {
			return;
		}

		for (int step = 0; step <= length; step++) {
			int x = shaftX + direction * step;
			int localFloorY = floorY + (step / 26) % 2;
			for (int y = localFloorY - 7; y < localFloorY; y++) {
				TileEditor.ClearTile(x, y);
				TileEditor.SetWall(x, y, WallID.Stone);
			}
			TileEditor.SetTile(x, localFloorY, TileID.Stone);

			if (step > 8 && step < length - 4) {
				WorldGen.PlaceTile(x, localFloorY - 1, TileID.MinecartTrack, mute: true, forced: true);
			}
		}

		for (int step = 18; step < length; step += 22) {
			int x = shaftX + direction * step;
			for (int y = floorY - 6; y < floorY; y++) {
				TileEditor.SetTile(x, y, TileID.WoodenBeam);
			}
			TileEditor.PlaceTorch(x + direction * 2, floorY - 3);
		}
	}

	private static void CarveCaveJunction(WorldPlan plan, int shaftX)
	{
		int floorY = plan.MineBottomY + 2;
		for (int offset = -125; offset <= 125; offset++) {
			int x = shaftX + offset;
			int wave = (int)Math.Round(3d * Math.Sin(offset / 19d));
			for (int y = floorY + wave - 8; y < floorY + wave; y++) {
				TileEditor.ClearTile(x, y);
				TileEditor.SetWall(x, y, WallID.Stone);
			}
			TileEditor.SetTile(x, floorY + wave, TileID.Stone);
		}
	}
}
