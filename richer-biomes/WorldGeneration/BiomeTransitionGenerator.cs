using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.WorldBuilding;

namespace RicherBiomes.WorldGeneration;

internal static class BiomeTransitionGenerator
{
	private const int SampleStride = 12;
	private const int MinimumRunWidth = 84;

	public static void Apply(WorldPlan plan, GenerationManifest manifest, GenerationProgress progress)
	{
		manifest.BiomeTransitions.Clear();
		List<BiomeRun> runs = FindRuns(plan);
		List<int> acceptedCenters = [];
		for (int index = 1; index < runs.Count; index++) {
			BiomeRun leftRun = runs[index - 1];
			BiomeRun rightRun = runs[index];
			if (leftRun.Biome == rightRun.Biome
				|| leftRun.Width < MinimumRunWidth
				|| rightRun.Width < MinimumRunWidth
				|| !CanBlend(leftRun.Biome, rightRun.Biome)) {
				continue;
			}

			int centerX = (leftRun.Right + rightRun.Left) / 2;
			if (acceptedCenters.Any(existing => Math.Abs(existing - centerX) < 110)) {
				continue;
			}

			int width = 52 + HashNoise(centerX, plan.GenerationSeed) % 43;
			int left = Math.Max(plan.LeftBoundary + 8, centerX - width / 2);
			int right = Math.Min(plan.RightBoundary - 8, centerX + width / 2);
			int minimumSurface = int.MaxValue;
			int maximumSurface = int.MinValue;
			int maximumBandBottom = int.MinValue;
			for (int x = left; x <= right; x += 3) {
				int surface = BiomeClassifier.TryFindGroundSupport(x, out int supportY) ? supportY : plan.SurfaceAt(x);
				minimumSurface = Math.Min(minimumSurface, surface);
				maximumSurface = Math.Max(maximumSurface, surface);
				maximumBandBottom = Math.Max(maximumBandBottom, plan.SurfaceAt(x) + MaximumBlendDepth(plan, x) + 1);
			}

			int top = Math.Max(40, minimumSurface - 5);
			int bottom = Math.Min(Main.maxTilesY - 45, Math.Max(maximumSurface + 52, maximumBandBottom));
			Rectangle area = new(left, top, right - left + 1, bottom - top);
			int modified = BlendBand(plan, manifest, area, leftRun.Biome, rightRun.Biome, centerX);
			if (modified < area.Width * 3) {
				continue;
			}

			manifest.BiomeTransitions.Add(new BiomeTransitionRecord(leftRun.Biome, rightRun.Biome, area, modified));
			acceptedCenters.Add(centerX);
			GenVars.structures.AddProtectedStructure(area, padding: 1);
			TileEditor.Frame(area, border: 2);
		}

		progress.Set(1d);
	}

	public static void Repair(WorldPlan plan, GenerationManifest manifest)
	{
		for (int index = 0; index < manifest.BiomeTransitions.Count; index++) {
			BiomeTransitionRecord transition = manifest.BiomeTransitions[index];
			int modified = BlendBand(
				plan,
				manifest,
				transition.Area,
				transition.LeftBiome,
				transition.RightBiome,
				transition.Area.Center.X);
			manifest.BiomeTransitions[index] = transition with { ModifiedCells = Math.Max(transition.ModifiedCells, modified) };
			TileEditor.Frame(transition.Area, border: 2);
		}
	}

	public static int RetainObservable(WorldPlan plan, GenerationManifest manifest)
	{
		int removed = 0;
		for (int index = manifest.BiomeTransitions.Count - 1; index >= 0; index--) {
			if (TryMeasureBoundary(plan, manifest, manifest.BiomeTransitions[index], out _, out _, out _)) {
				continue;
			}

			manifest.BiomeTransitions.RemoveAt(index);
			removed++;
		}
		return removed;
	}

	internal static bool TryMeasureBoundary(
		WorldPlan plan,
		GenerationManifest manifest,
		BiomeTransitionRecord transition,
		out int observedCrossings,
		out int crossingSpan,
		out string samples)
	{
		HashSet<int> crossingColumns = [];
		List<string> sampledCrossings = [];
		observedCrossings = 0;
		int centerX = transition.Area.Center.X;
		int maximumDepth = Math.Min(
			MaximumBlendDepth(plan, centerX) - 2,
			transition.Area.Bottom - plan.SurfaceAt(centerX) - 2);
		int depthStep = maximumDepth <= 52 ? 2 : Math.Clamp(maximumDepth / 24, 4, 12);
		for (int depth = 2; depth <= maximumDepth; depth += depthStep) {
			int bestX = int.MinValue;
			int bestDistance = int.MaxValue;
			int expectedBoundary = BoundaryColumn(
				transition.Area,
				centerX,
				depth,
				plan.GenerationSeed);
			for (int x = transition.Area.Left + 1; x < transition.Area.Right - 1; x++) {
				int leftY = plan.SurfaceAt(x - 1) + depth;
				int rightY = plan.SurfaceAt(x) + depth;
				if (IsFeatureOwned(manifest, x - 1, leftY) || IsFeatureOwned(manifest, x, rightY)) {
					continue;
				}
				Tile leftTile = Main.tile[x - 1, leftY];
				Tile rightTile = Main.tile[x, rightY];
				if (!leftTile.HasTile || !rightTile.HasTile) {
					continue;
				}
				BiomeKind left = ClassifyMaterial(leftTile);
				BiomeKind right = ClassifyMaterial(rightTile);
				if (left == right
					|| left != transition.LeftBiome && left != transition.RightBiome
					|| right != transition.LeftBiome && right != transition.RightBiome) {
					continue;
				}
				int distance = Math.Abs(x - expectedBoundary);
				if (distance < bestDistance) {
					bestDistance = distance;
					bestX = x;
				}
			}
			if (bestX != int.MinValue && bestDistance <= 8) {
				crossingColumns.Add(bestX);
				observedCrossings++;
				sampledCrossings.Add($"{depth}:{bestX}");
			}
		}

		crossingSpan = crossingColumns.Count == 0 ? 0 : crossingColumns.Max() - crossingColumns.Min();
		samples = string.Join(",", sampledCrossings);
		return observedCrossings >= 6 && crossingSpan >= 8;
	}

	private static List<BiomeRun> FindRuns(WorldPlan plan)
	{
		List<BiomeRun> runs = [];
		BiomeKind current = DominantBiomeAt(plan.LeftBoundary + 20);
		int runLeft = plan.LeftBoundary;
		for (int x = plan.LeftBoundary + SampleStride; x <= plan.RightBoundary; x += SampleStride) {
			BiomeKind next = DominantBiomeAt(x);
			if (next == current) {
				continue;
			}

			runs.Add(new BiomeRun(current, runLeft, x - 1));
			current = next;
			runLeft = x;
		}
		runs.Add(new BiomeRun(current, runLeft, plan.RightBoundary));
		return MergeShortRuns(runs);
	}

	private static List<BiomeRun> MergeShortRuns(List<BiomeRun> runs)
	{
		bool changed;
		do {
			changed = false;
			for (int index = 0; index < runs.Count; index++) {
				if (runs[index].Width >= MinimumRunWidth / 2 || runs.Count == 1) {
					continue;
				}

				if (index > 0 && index < runs.Count - 1 && runs[index - 1].Biome == runs[index + 1].Biome) {
					runs[index - 1] = runs[index - 1] with { Right = runs[index + 1].Right };
					runs.RemoveAt(index + 1);
					runs.RemoveAt(index);
				}
				else if (index == 0) {
					runs[1] = runs[1] with { Left = runs[0].Left };
					runs.RemoveAt(0);
				}
				else {
					runs[index - 1] = runs[index - 1] with { Right = runs[index].Right };
					runs.RemoveAt(index);
				}
				changed = true;
				break;
			}
		} while (changed);

		return runs;
	}

	private static BiomeKind DominantBiomeAt(int centerX)
	{
		Dictionary<BiomeKind, int> counts = [];
		for (int offset = -36; offset <= 36; offset += 6) {
			int x = Math.Clamp(centerX + offset, 45, Main.maxTilesX - 46);
			if (!BiomeClassifier.TryFindGroundSupport(x, out int y)) {
				continue;
			}
			BiomeKind biome = BiomeClassifier.ClassifySupport(Main.tile[x, y].TileType, x, y);
			if (!IsSurfaceBiome(biome)) {
				continue;
			}
			counts[biome] = counts.GetValueOrDefault(biome) + 1;
		}

		return counts.Count == 0
			? BiomeKind.Forest
			: counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
	}

	private static int BlendBand(
		WorldPlan plan,
		GenerationManifest manifest,
		Rectangle area,
		BiomeKind leftBiome,
		BiomeKind rightBiome,
		int centerX)
	{
		int modified = 0;
		for (int x = area.Left; x < area.Right; x++) {
			int surfaceY = plan.SurfaceAt(x);
			int maximumDepth = MaximumBlendDepth(plan, x);
			for (int depth = 0; depth <= maximumDepth; depth++) {
				int y = surfaceY + depth;
				if (IsFeatureOwned(manifest, x, y)) {
					continue;
				}
				Tile tile = Main.tile[x, y];
				if (!tile.HasTile || TileEditor.IsProtectedTile(tile) || !IsBlendableTile(tile.TileType)) {
					continue;
				}

				int boundaryX = BoundaryColumn(area, centerX, depth, plan.GenerationSeed);
				double raggedOffset = (SampleNoise(x, y, plan.GenerationSeed + centerX) - 0.5d) * 7d;
				BiomeKind selected = x + raggedOffset >= boundaryX ? rightBiome : leftBiome;
				ushort target = MaterialFor(selected, depth);
				if (tile.TileType != target) {
					TileEditor.SetTerrain(x, y, target);
					modified++;
				}
				if (depth >= 3 && Main.tile[x, y].WallType != WallID.None) {
					TileEditor.SetWall(x, y, WallFor(selected));
				}
			}
		}
		return modified;
	}

	private static int MaximumBlendDepth(WorldPlan plan, int x)
	{
		int surfaceY = plan.SurfaceAt(x);
		bool mountainScale = plan.RegionAt(x).Landform == LandformKind.Mountain
			|| surfaceY < Main.worldSurface - 80d;
		if (!mountainScale) {
			return 46;
		}

		return Math.Clamp(
			(int)Math.Ceiling(Main.worldSurface) + 60 - surfaceY,
			46,
			Main.maxTilesY - surfaceY - 50);
	}

	internal static int BoundaryColumn(Rectangle area, int centerX, int depth, int seed)
	{
		int depthBand = depth / 4;
		int profile = depthBand % 10 switch {
			0 => -100,
			1 => -35,
			2 => 70,
			3 => -80,
			4 => 100,
			5 => 15,
			6 => -60,
			7 => 65,
			8 => 90,
			_ => -20
		};
		int amplitude = Math.Clamp(area.Width / 7, 8, 14);
		int steppedOffset = profile * amplitude / 100;
		int waveOffset = (int)Math.Round(Math.Sin((depth + centerX % 19) * 0.37d) * area.Width * 0.035d);
		int seedOffset = HashNoise(centerX + depthBand * 977, seed) % 5 - 2;
		return Math.Clamp(centerX + steppedOffset + waveOffset + seedOffset, area.Left + 7, area.Right - 8);
	}

	internal static bool IsFeatureOwned(GenerationManifest manifest, int x, int y)
	{
		Point point = new(x, y);
		return manifest.Terraces.Any(terrace => terrace.Area.Contains(point))
			|| manifest.Landmarks.Any(landmark => landmark.Area.Contains(point))
			|| manifest.Bridges.Any(bridge => bridge.Area.Contains(point))
			|| manifest.Valleys.Any(valley => valley.Area.Contains(point))
			|| manifest.SkyHighlands.Any(highland => highland.Area.Contains(point))
			|| manifest.MineSections.Any(section => section.Area.Contains(point));
	}

	private static bool CanBlend(BiomeKind left, BiomeKind right) =>
		IsSurfaceBiome(left) && IsSurfaceBiome(right) && left != BiomeKind.Ocean && right != BiomeKind.Ocean;

	private static bool IsSurfaceBiome(BiomeKind biome) => biome is
		BiomeKind.Forest or BiomeKind.Snow or BiomeKind.Desert or BiomeKind.Jungle or BiomeKind.Evil or BiomeKind.Ocean;

	private static bool IsBlendableTile(ushort type) => type is
		TileID.Dirt or TileID.Stone or TileID.Grass or TileID.CorruptGrass or TileID.CrimsonGrass
		or TileID.SnowBlock or TileID.IceBlock or TileID.BreakableIce
		or TileID.Sand or TileID.HardenedSand or TileID.Sandstone
		or TileID.Mud or TileID.JungleGrass or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass
		or TileID.Ebonstone or TileID.Crimstone or TileID.Ebonsand or TileID.Crimsand;

	private static ushort MaterialFor(BiomeKind biome, int depth) => biome switch {
		BiomeKind.Snow => depth < 8 ? TileID.SnowBlock : depth < 24 ? TileID.IceBlock : TileID.Stone,
		BiomeKind.Desert => depth < 7 ? TileID.Sand : depth < 22 ? TileID.HardenedSand : TileID.Sandstone,
		BiomeKind.Jungle => depth == 0 ? TileID.JungleGrass : depth < 24 ? TileID.Mud : TileID.Stone,
		BiomeKind.Evil when WorldGen.crimson => depth == 0 ? TileID.CrimsonGrass : TileID.Crimstone,
		BiomeKind.Evil => depth == 0 ? TileID.CorruptGrass : TileID.Ebonstone,
		_ => depth == 0 ? TileID.Grass : depth < 14 ? TileID.Dirt : TileID.Stone
	};

	private static ushort WallFor(BiomeKind biome) => biome switch {
		BiomeKind.Snow => WallID.SnowWallUnsafe,
		BiomeKind.Desert => WallID.Sandstone,
		BiomeKind.Jungle => WallID.JungleUnsafe,
		BiomeKind.Evil => WorldGen.crimson ? WallID.CrimstoneUnsafe : WallID.EbonstoneUnsafe,
		_ => WallID.DirtUnsafe
	};

	private static BiomeKind ClassifyMaterial(Tile tile)
	{
		if (tile.TileType is TileID.SnowBlock or TileID.IceBlock or TileID.BreakableIce || tile.WallType == WallID.SnowWallUnsafe) {
			return BiomeKind.Snow;
		}
		if (tile.TileType is TileID.Sand or TileID.HardenedSand or TileID.Sandstone || tile.WallType == WallID.Sandstone) {
			return BiomeKind.Desert;
		}
		if (tile.TileType is TileID.Mud or TileID.JungleGrass or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass
			|| tile.WallType == WallID.JungleUnsafe) {
			return BiomeKind.Jungle;
		}
		if (tile.TileType is TileID.CorruptGrass or TileID.CrimsonGrass or TileID.Ebonstone or TileID.Crimstone
			or TileID.Ebonsand or TileID.Crimsand || tile.WallType is WallID.EbonstoneUnsafe or WallID.CrimstoneUnsafe) {
			return BiomeKind.Evil;
		}
		return BiomeKind.Forest;
	}

	private static double SampleNoise(int x, int y, int salt)
	{
		const int cellWidth = 11;
		const int cellHeight = 8;
		int cellX = x / cellWidth;
		int cellY = y / cellHeight;
		double localX = Smooth((double)(x % cellWidth) / cellWidth);
		double localY = Smooth((double)(y % cellHeight) / cellHeight);
		double top = Lerp(Noise01(cellX, cellY, salt), Noise01(cellX + 1, cellY, salt), localX);
		double bottom = Lerp(Noise01(cellX, cellY + 1, salt), Noise01(cellX + 1, cellY + 1, salt), localX);
		return Lerp(top, bottom, localY);
	}

	private static double Noise01(int x, int y, int salt) => HashNoise(x * 193 + y * 389, salt) / (double)int.MaxValue;

	private static int HashNoise(int value, int salt)
	{
		unchecked {
			uint hash = (uint)(value * 0x45D9F3B) ^ (uint)salt;
			hash ^= hash >> 16;
			hash *= 0x7FEB352D;
			hash ^= hash >> 15;
			return (int)(hash & 0x7FFFFFFF);
		}
	}

	private static double Smooth(double value) => value * value * (3d - 2d * value);

	private static double Lerp(double left, double right, double amount) => left + (right - left) * amount;

	private readonly record struct BiomeRun(BiomeKind Biome, int Left, int Right)
	{
		public int Width => Right - Left + 1;
	}
}
