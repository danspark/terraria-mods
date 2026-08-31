using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace RicherBiomes.WorldGeneration;

internal static class WorldPlanner
{
	private const int ForestLength = 380;
	private const int MountainStart = 430;
	private const int MountainLength = 640;
	private const int MineLength = 290;
	private static readonly int[] MineStartCandidates = [1010, 1050, 1090];

	public static WorldPlan Create()
	{
		Candidate left = Evaluate(-1);
		Candidate right = Evaluate(1);
		Candidate chosen = right.Score > left.Score ? right : left;

		if (right.Score == left.Score && WorldGen.genRand.NextBool()) {
			chosen = right;
		}

		int originX = Main.spawnTileX + chosen.Direction * WorldPlan.SpawnBuffer;
		int baseSurfaceY = MedianSurface(originX, chosen.Direction, ForestLength);
		int peakY = Math.Max(55, Math.Min((int)(Main.worldSurface * 0.30d), baseSurfaceY - 175));
		int mineBottomY = Math.Min(Main.maxTilesY - 260,
			Math.Max(baseSurfaceY + 210, (int)Main.rockLayer + 70));

		FeatureSpan forest = new("Vertical forest", 0, ForestLength);
		FeatureSpan mountain = new("Sky-piercing mountain", MountainStart, MountainLength);
		FeatureSpan mine = new("Surface mine", chosen.MineStart, MineLength);
		int totalLength = Math.Max(mountain.End, mine.End) + 1;
		int[] originalSurfaceY = new int[totalLength];
		for (int distance = 0; distance < totalLength; distance++) {
			originalSurfaceY[distance] = FindSurfaceY(originX + chosen.Direction * distance);
		}

		return new WorldPlan(
			Main.spawnTileX,
			chosen.Direction,
			originX,
			baseSurfaceY,
			peakY,
			mineBottomY,
			originalSurfaceY,
			forest,
			mountain,
			mine);
	}

	public static int FindSurfaceY(int x)
	{
		int lowerLimit = Math.Min(Main.maxTilesY - 10, (int)Main.worldSurface + 180);
		for (int y = 45; y < lowerLimit; y++) {
			Tile tile = Main.tile[x, y];
			if (tile.HasUnactuatedTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType]) {
				return y;
			}
		}

		return (int)Main.worldSurface;
	}

	private static Candidate Evaluate(int direction)
	{
		int originX = Main.spawnTileX + direction * WorldPlan.SpawnBuffer;
		int bestMineStart = MineStartCandidates[0];
		int bestMinePenalty = int.MaxValue;

		foreach (int mineStart in MineStartCandidates) {
			int penalty = CountChests(originX, direction, mineStart, mineStart + MineLength, (int)Main.rockLayer + 120) * 500;
			if (penalty < bestMinePenalty) {
				bestMinePenalty = penalty;
				bestMineStart = mineStart;
			}
		}

		int totalLength = Math.Max(MountainStart + MountainLength, bestMineStart + MineLength);
		int score = -bestMinePenalty;
		for (int distance = 0; distance < totalLength; distance += 8) {
			int x = originX + direction * distance;
			if (x < 320 || x >= Main.maxTilesX - 320) {
				return new Candidate(direction, bestMineStart, int.MinValue / 2);
			}

			int surfaceY = FindSurfaceY(x);
			Tile tile = Main.tile[x, surfaceY];
			score += SurfaceScore(tile);
		}

		score -= CountChests(originX, direction, 0, totalLength, (int)Main.worldSurface + 100) * 250;
		return new Candidate(direction, bestMineStart, score);
	}

	private static int SurfaceScore(Tile tile)
	{
		if (tile.LiquidType == LiquidID.Shimmer) {
			return -200;
		}

		return tile.TileType switch {
			TileID.Grass or TileID.Dirt or TileID.Stone => 8,
			TileID.SnowBlock or TileID.IceBlock => 3,
			TileID.Mud or TileID.JungleGrass => 1,
			TileID.Sand or TileID.HardenedSand or TileID.Sandstone => -3,
			TileID.Ebonstone or TileID.Crimstone => -4,
			TileID.BlueDungeonBrick or TileID.GreenDungeonBrick or TileID.PinkDungeonBrick or TileID.LihzahrdBrick => -200,
			_ => 0
		};
	}

	private static int CountChests(int originX, int direction, int start, int end, int bottomY)
	{
		int firstX = originX + direction * start;
		int lastX = originX + direction * end;
		int minX = Math.Min(firstX, lastX) - 8;
		int maxX = Math.Max(firstX, lastX) + 8;
		int count = 0;

		foreach (Chest chest in Main.chest) {
			if (chest is not null && chest.x >= minX && chest.x <= maxX && chest.y < bottomY) {
				count++;
			}
		}

		return count;
	}

	private static int MedianSurface(int originX, int direction, int length)
	{
		List<int> samples = [];
		for (int distance = 0; distance < length; distance += 16) {
			samples.Add(FindSurfaceY(originX + direction * distance));
		}

		samples.Sort();
		int median = samples[samples.Count / 2];
		return Math.Clamp(median, 170, (int)Main.worldSurface + 25);
	}

	private readonly record struct Candidate(int Direction, int MineStart, int Score);
}
