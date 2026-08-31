using System;
using Terraria;
using Terraria.ID;

namespace RicherBiomes.WorldGeneration;

internal static class TileEditor
{
	public static void ClearTile(int x, int y, bool clearWall = false)
	{
		if (!WorldGen.InWorld(x, y, 2)) {
			return;
		}

		Tile tile = Main.tile[x, y];
		tile.ClearTile();
		tile.LiquidAmount = 0;
		if (clearWall) {
			tile.WallType = WallID.None;
		}
	}

	public static void SetTile(int x, int y, ushort tileType)
	{
		if (!WorldGen.InWorld(x, y, 2)) {
			return;
		}

		Tile tile = Main.tile[x, y];
		tile.ClearTile();
		tile.HasTile = true;
		tile.TileType = tileType;
		tile.Slope = SlopeType.Solid;
		tile.IsHalfBlock = false;
		tile.LiquidAmount = 0;
	}

	public static void SetWall(int x, int y, ushort wallType)
	{
		if (WorldGen.InWorld(x, y, 2)) {
			Main.tile[x, y].WallType = wallType;
		}
	}

	public static void ClearRectangle(int left, int top, int right, int bottom, ushort wallType = WallID.None)
	{
		for (int x = left; x <= right; x++) {
			for (int y = top; y <= bottom; y++) {
				ClearTile(x, y);
				if (wallType != WallID.None) {
					SetWall(x, y, wallType);
				}
			}
		}
	}

	public static void PlacePlatform(int x, int y)
	{
		ClearTile(x, y);
		WorldGen.PlaceTile(x, y, TileID.Platforms, mute: true, forced: true, style: 0);
	}

	public static void PlaceTorch(int x, int y)
	{
		ClearTile(x, y);
		WorldGen.PlaceTile(x, y, TileID.Torches, mute: true, forced: true, style: 0);
	}

	public static void Frame(WorldPlan plan)
	{
		int top = Math.Max(20, plan.PeakY - 25);
		int bottom = Math.Min(Main.maxTilesY - 20, plan.MineBottomY + 30);
		WorldGen.RangeFrame(plan.MinX - 10, top, plan.MaxX + 10, bottom);
	}
}
