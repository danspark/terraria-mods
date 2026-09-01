using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace RicherBiomes.WorldGeneration;

internal static class BiomeAccentGenerator
{
	private const int AccentSeedSalt = 0x6B12_C9E5;

	public static void Apply(WorldPlan plan, GenerationManifest manifest, GenerationProgress progress)
	{
		UnifiedRandom random = new(MixSeed(plan.GenerationSeed, AccentSeedSalt));
		List<Point> accepted = [];
		PlaceSurfaceAccents(plan, manifest, random, accepted);
		progress.Set(0.45d);
		PlaceCaveAccents(plan, manifest, random, accepted);
		progress.Set(1d);
	}

	private static void PlaceSurfaceAccents(
		WorldPlan plan,
		GenerationManifest manifest,
		UnifiedRandom random,
		List<Point> accepted)
	{
		foreach (WorldRegion region in plan.Regions) {
			(int minimumGap, int maximumGap) = region.Landform switch {
				LandformKind.QuietLowland => (28, 45),
				LandformKind.Plateau => (22, 38),
				LandformKind.Mountain => (12, 24),
				LandformKind.Valley => (13, 25),
				_ => (17, 31)
			};

			for (int x = region.Left + random.Next(minimumGap, maximumGap + 1); x < region.Right;) {
				if (BiomeClassifier.TryFindSurfaceSupport(x, out int groundY)) {
					TryPlaceAccent(x, groundY - 1, manifest, random, accepted);
				}
				x += random.Next(minimumGap, maximumGap + 1);
			}
		}
	}

	private static void PlaceCaveAccents(
		WorldPlan plan,
		GenerationManifest manifest,
		UnifiedRandom random,
		List<Point> accepted)
	{
		int attempts = Main.maxTilesX / 2;
		for (int attempt = 0; attempt < attempts; attempt++) {
			int x = random.Next(plan.LeftBoundary, plan.RightBoundary + 1);
			int startY = random.Next((int)Main.worldSurface + 25, Main.maxTilesY - 90);
			for (int y = startY; y < Math.Min(startY + 45, Main.maxTilesY - 50); y++) {
				if (Main.tile[x, y].HasTile || !TileEditor.IsSolid(x, y + 1)) {
					continue;
				}

				if (Main.wallDungeon[Main.tile[x, y].WallType]) {
					break;
				}

				TryPlaceAccent(x, y, manifest, random, accepted);
				break;
			}
		}
	}

	private static void TryPlaceAccent(
		int x,
		int y,
		GenerationManifest manifest,
		UnifiedRandom random,
		List<Point> accepted)
	{
		if (!WorldGen.InWorld(x, y, 8)
			|| Main.tile[x, y].HasTile
			|| Main.tile[x, y].LiquidAmount > 0
			|| IsInQuietArea(x, y, manifest)
			|| accepted.Any(point => Math.Abs(point.X - x) < 7 && Math.Abs(point.Y - y) < 5)) {
			return;
		}

		Tile support = Main.tile[x, y + 1];
		if (!support.HasUnactuatedTile || Main.tileFrameImportant[support.TileType]) {
			return;
		}

		BiomeKind biome = BiomeClassifier.ClassifySupport(support.TileType, x, y + 1);
		(int styleX, int styleY) = PickPileStyle(biome, random);
		if (!TileEditor.TryPlaceSmallPile(x, y, styleX, styleY)) {
			return;
		}

		accepted.Add(new Point(x, y));
		manifest.AccentCounts[biome] = manifest.AccentCounts.GetValueOrDefault(biome) + 1;
	}

	private static bool IsInQuietArea(int x, int y, GenerationManifest manifest)
	{
		foreach (BuildTerrace terrace in manifest.Terraces) {
			Rectangle quiet = terrace.Area;
			quiet.Inflate(14, 8);
			if (quiet.Contains(x, y)) {
				return true;
			}
		}

		foreach (LandmarkRecord landmark in manifest.Landmarks) {
			Rectangle quiet = landmark.Area;
			quiet.Inflate(8, 5);
			if (quiet.Contains(x, y)) {
				return true;
			}
		}

		return false;
	}

	private static (int X, int Y) PickPileStyle(BiomeKind biome, UnifiedRandom random) => biome switch {
		BiomeKind.Snow => random.NextBool()
			? (random.Next(36, 48), 0)
			: (random.Next(25, 31), 1),
		BiomeKind.Desert => random.NextBool()
			? (random.Next(12, 28), 0)
			: (random.Next(12, 19), 1),
		BiomeKind.Forest => random.NextBool(3)
			? (random.Next(38, 41), 1)
			: (random.Next(0, 11), 0),
		BiomeKind.Underworld => random.NextBool()
			? (random.Next(12, 28), 0)
			: (random.Next(6, 16), 1),
		BiomeKind.Evil => random.NextBool()
			? (random.Next(12, 28), 0)
			: (random.Next(6, 17), 1),
		_ => random.NextBool()
			? (random.Next(0, 12), 0)
			: (random.Next(0, 6), 1)
	};

	private static int MixSeed(int seed, int salt)
	{
		unchecked {
			uint value = (uint)seed ^ (uint)salt;
			value ^= value >> 16;
			value *= 0x7FEB_352Du;
			value ^= value >> 15;
			value *= 0x846C_A68Bu;
			value ^= value >> 16;
			return (int)value;
		}
	}
}
