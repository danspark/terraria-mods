using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace RicherBiomes.WorldGeneration;

internal static class TileEditor
{
	public static void ClearTerrain(int x, int y, bool clearWall = false)
	{
		if (!WorldGen.InWorld(x, y, 3)) {
			return;
		}

		Tile tile = Main.tile[x, y];
		tile.ClearTile();
		tile.LiquidAmount = 0;
		if (clearWall) {
			tile.WallType = WallID.None;
		}
	}

	public static void SetTerrain(int x, int y, ushort tileType)
	{
		if (!WorldGen.InWorld(x, y, 3)) {
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

	public static void SetActuatedTerrain(int x, int y, ushort tileType, bool wire = true)
	{
		if (!WorldGen.InWorld(x, y, 3)) {
			return;
		}

		SetTerrain(x, y, tileType);
		WorldGen.SquareTileFrame(x, y, resetFrame: true);
		Tile tile = Main.tile[x, y];
		tile.HasActuator = true;
		tile.IsActuated = true;
		tile.RedWire = wire;
	}

	public static void SetWall(int x, int y, ushort wallType)
	{
		if (WorldGen.InWorld(x, y, 3)) {
			Main.tile[x, y].WallType = wallType;
		}
	}

	public static void SetLiquid(int x, int y, byte liquidType, byte amount)
	{
		if (!WorldGen.InWorld(x, y, 3) || Main.tile[x, y].HasTile) {
			return;
		}

		Tile tile = Main.tile[x, y];
		tile.LiquidType = liquidType;
		tile.LiquidAmount = amount;
	}

	public static bool TryPlacePlatform(int x, int y, int style = 0)
	{
		if (!WorldGen.InWorld(x, y, 3) || Main.tile[x, y].HasTile) {
			return false;
		}

		WorldGen.PlaceTile(x, y, TileID.Platforms, mute: true, forced: false, style: style);
		return Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.Platforms;
	}

	public static bool TryPlacePlatformForced(int x, int y, int style = 0)
	{
		if (!WorldGen.InWorld(x, y, 3)) {
			return false;
		}
		ClearTerrain(x, y);
		WorldGen.PlaceTile(x, y, TileID.Platforms, mute: true, forced: true, style: style);
		if (!Main.tile[x, y].HasTile || Main.tile[x, y].TileType != TileID.Platforms) {
			SetTerrain(x, y, TileID.Platforms);
		}
		return Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.Platforms;
	}

	public static bool TryPlaceTorch(int x, int y, int style = 0)
	{
		if (!WorldGen.InWorld(x, y, 3) || Main.tile[x, y].HasTile || Main.tile[x, y].LiquidAmount > 0) {
			return false;
		}

		WorldGen.PlaceTile(x, y, TileID.Torches, mute: true, forced: false, style: style);
		return Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.Torches;
	}

	public static bool TryPlaceMinecartTrack(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 4) || Main.tile[x, y].HasTile) {
			return false;
		}

		WorldGen.PlaceTile(x, y, TileID.MinecartTrack, mute: true, forced: true);
		if (!Main.tile[x, y].HasTile || Main.tile[x, y].TileType != TileID.MinecartTrack) {
			SetTerrain(x, y, TileID.MinecartTrack);
		}

		return Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.MinecartTrack;
	}

	public static bool TryPlaceObject(int x, int y, ushort tileType, int style = 0, int direction = -1)
	{
		if (!WorldGen.InWorld(x, y, 5) || Main.tile[x, y].HasTile || Main.tile[x, y].LiquidAmount > 0) {
			return false;
		}

		WorldGen.PlaceObject(x, y, tileType, mute: true, style: style, alternate: 0, random: -1, direction: direction);
		return Main.tile[x, y].HasTile && Main.tile[x, y].TileType == tileType;
	}

	public static bool TryPlaceSmallPile(int x, int y, int styleX, int styleY)
	{
		if (!WorldGen.InWorld(x, y, 3) || Main.tile[x, y].HasTile || Main.tile[x, y].LiquidAmount > 0) {
			return false;
		}

		bool placed = WorldGen.PlaceSmallPile(x, y, styleX, styleY, TileID.SmallPiles);
		return placed && Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.SmallPiles;
	}

	public static bool IsSolid(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 2)) {
			return false;
		}

		Tile tile = Main.tile[x, y];
		return tile.HasUnactuatedTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];
	}

	public static bool IsProtectedTile(Tile tile)
	{
		if (!tile.HasTile) {
			return tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Shimmer;
		}

		ushort type = tile.TileType;
		return Main.tileFrameImportant[type]
			|| type is TileID.BlueDungeonBrick or TileID.GreenDungeonBrick or TileID.PinkDungeonBrick
				or TileID.CrackedBlueDungeonBrick or TileID.CrackedGreenDungeonBrick or TileID.CrackedPinkDungeonBrick
				or TileID.LihzahrdBrick or TileID.LihzahrdAltar or TileID.DemonAltar
				or TileID.Containers or TileID.Containers2
			|| tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Shimmer;
	}

	public static bool IsSafeForStructure(Rectangle area)
	{
		if (!WorldGen.InWorld(area.Left, area.Top, 12) || !WorldGen.InWorld(area.Right - 1, area.Bottom - 1, 12)) {
			return false;
		}

		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				if (IsProtectedTile(tile)
					|| tile.RedWire || tile.BlueWire || tile.GreenWire || tile.YellowWire
					|| tile.HasActuator) {
					return false;
				}
			}
		}

		foreach (Chest chest in Main.chest) {
			if (chest is not null && area.Contains(chest.x, chest.y)) {
				return false;
			}
		}

		return true;
	}

	public static bool IsSafeForTerrainFeature(Rectangle area)
	{
		if (!WorldGen.InWorld(area.Left, area.Top, 12) || !WorldGen.InWorld(area.Right - 1, area.Bottom - 1, 12)) {
			return false;
		}

		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				if (IsProgressionTile(tile)
					|| tile.RedWire || tile.BlueWire || tile.GreenWire || tile.YellowWire
					|| tile.HasActuator) {
					return false;
				}
			}
		}

		foreach (Chest chest in Main.chest) {
			if (chest is not null && area.Contains(chest.x, chest.y)) {
				return false;
			}
		}
		return true;
	}

	public static bool IsProgressionTile(Tile tile)
	{
		if (!tile.HasTile) {
			return tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Shimmer;
		}

		return tile.TileType is TileID.BlueDungeonBrick or TileID.GreenDungeonBrick or TileID.PinkDungeonBrick
			or TileID.CrackedBlueDungeonBrick or TileID.CrackedGreenDungeonBrick or TileID.CrackedPinkDungeonBrick
			or TileID.LihzahrdBrick or TileID.LihzahrdAltar or TileID.DemonAltar or TileID.ShadowOrbs
			or TileID.Containers or TileID.Containers2
			|| tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Shimmer;
	}

	public static void Frame(Rectangle area, int border = 2)
	{
		int left = Math.Max(1, area.Left - border);
		int top = Math.Max(1, area.Top - border);
		int right = Math.Min(Main.maxTilesX - 2, area.Right + border);
		int bottom = Math.Min(Main.maxTilesY - 2, area.Bottom + border);
		WorldGen.RangeFrame(left, top, right, bottom);
	}
}
