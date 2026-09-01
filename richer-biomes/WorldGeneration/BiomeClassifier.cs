using Terraria;
using Terraria.ID;

namespace RicherBiomes.WorldGeneration;

internal static class BiomeClassifier
{
	public static bool TryFindSurfaceSupport(int x, out int surfaceY)
	{
		int bottom = System.Math.Min(Main.maxTilesY - 45, (int)Main.worldSurface + 240);
		for (int y = 45; y < bottom; y++) {
			Tile tile = Main.tile[x, y];
			if (tile.HasUnactuatedTile && Main.tileSolid[tile.TileType]
				&& !Main.tileSolidTop[tile.TileType] && IsNaturalSupport(tile.TileType)) {
				surfaceY = y;
				return true;
			}
		}

		surfaceY = 0;
		return false;
	}

	public static bool TryFindGroundSupport(int x, out int surfaceY)
	{
		int bottom = System.Math.Min(Main.maxTilesY - 45, (int)Main.worldSurface + 240);
		for (int y = 45; y < bottom; y++) {
			Tile tile = Main.tile[x, y];
			if (tile.HasUnactuatedTile && Main.tileSolid[tile.TileType]
				&& !Main.tileSolidTop[tile.TileType] && IsNaturalSupport(tile.TileType)
				&& tile.TileType is not (TileID.Cloud or TileID.RainCloud or TileID.SnowCloud or TileID.Sunplate)) {
				surfaceY = y;
				return true;
			}
		}
		surfaceY = 0;
		return false;
	}

	public static BiomeKind ClassifySupport(ushort tileType, int x, int y)
	{
		if (y >= Main.UnderworldLayer) {
			return BiomeKind.Underworld;
		}
		if (y < Main.worldSurface * 0.48d && tileType is TileID.Cloud or TileID.RainCloud or TileID.Sunplate) {
			return BiomeKind.Sky;
		}
		if (x < Main.maxTilesX / 20 || x > Main.maxTilesX - Main.maxTilesX / 20) {
			return BiomeKind.Ocean;
		}
		if (tileType == TileID.MushroomGrass) {
			return BiomeKind.Mushroom;
		}
		if (TileID.Sets.Conversion.JungleGrass[tileType] || tileType == TileID.Mud) {
			return BiomeKind.Jungle;
		}
		if (tileType is TileID.CorruptGrass or TileID.CrimsonGrass or TileID.Ebonstone or TileID.Crimstone
			or TileID.Ebonsand or TileID.Crimsand or TileID.CorruptSandstone or TileID.CrimsonSandstone
			or TileID.CorruptHardenedSand or TileID.CrimsonHardenedSand) {
			return BiomeKind.Evil;
		}
		if (TileID.Sets.Conversion.Snow[tileType] || TileID.Sets.Conversion.Ice[tileType]) {
			return BiomeKind.Snow;
		}
		if (TileID.Sets.Conversion.Sand[tileType]
			|| TileID.Sets.Conversion.HardenedSand[tileType]
			|| TileID.Sets.Conversion.Sandstone[tileType]) {
			return BiomeKind.Desert;
		}
		if (y > Main.rockLayer) {
			return BiomeKind.Cavern;
		}
		return BiomeKind.Forest;
	}

	private static bool IsNaturalSupport(ushort type) =>
		type is TileID.Dirt or TileID.Stone or TileID.Grass or TileID.GolfGrass
			or TileID.SnowBlock or TileID.IceBlock or TileID.BreakableIce
			or TileID.Mud or TileID.JungleGrass or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass
			or TileID.Sand or TileID.Ebonsand or TileID.Crimsand
			or TileID.HardenedSand or TileID.CorruptHardenedSand or TileID.CrimsonHardenedSand
			or TileID.Sandstone or TileID.CorruptSandstone or TileID.CrimsonSandstone
			or TileID.CorruptGrass or TileID.CrimsonGrass or TileID.Ebonstone or TileID.Crimstone
			or TileID.MushroomGrass or TileID.Cloud or TileID.RainCloud or TileID.Sunplate;
}
