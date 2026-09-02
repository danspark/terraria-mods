using System.Collections.Generic;
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
		=> TryFindGroundSupport(x, minimumY: 45, out surfaceY);

	public static bool TryFindGroundSupport(int x, int minimumY, out int surfaceY)
	{
		int bottom = System.Math.Min(Main.maxTilesY - 45, (int)Main.worldSurface + 240);
		for (int y = System.Math.Max(45, minimumY); y < bottom; y++) {
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
		if (y < Main.worldSurface * 0.48d && IsNaturalSupport(tileType)) {
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

	public static BiomeKind ClassifyAreaTheme(int centerX, int centerY)
	{
		Dictionary<BiomeKind, int> scores = [];
		for (int x = centerX - 24; x <= centerX + 24; x += 4) {
			for (int y = centerY - 18; y <= centerY + 18; y += 3) {
				if (!WorldGen.InWorld(x, y, 12)) {
					continue;
				}
				Tile tile = Main.tile[x, y];
				if (!tile.HasUnactuatedTile || !IsNaturalSupport(tile.TileType)) {
					continue;
				}
				BiomeKind biome = ClassifySupport(tile.TileType, x, y);
				if (biome is BiomeKind.Sky or BiomeKind.Ocean) {
					continue;
				}
				int weight = biome is BiomeKind.Forest or BiomeKind.Cavern ? 1 : 4;
				scores[biome] = scores.GetValueOrDefault(biome) + weight;
			}
		}

		BiomeKind fallback = centerY > Main.rockLayer ? BiomeKind.Cavern : BiomeKind.Forest;
		BiomeKind best = fallback;
		int bestScore = -1;
		foreach ((BiomeKind biome, int score) in scores) {
			if (score > bestScore || score == bestScore && biome < best) {
				best = biome;
				bestScore = score;
			}
		}
		return best;
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
