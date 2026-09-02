using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.WorldBuilding;

namespace VanillaWorldsOverhauled.WorldGeneration;

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

			int width = 68 + HashNoise(centerX, plan.GenerationSeed) % 49;
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
			BreakLongWallSeams(area, manifest, plan.GenerationSeed ^ centerX);

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
			BreakLongWallSeams(
				transition.Area,
				manifest,
				plan.GenerationSeed ^ transition.Area.Center.X);
			BreakLongWallSeams(
				transition.Area,
				manifest,
				plan.GenerationSeed ^ transition.Area.Center.X);
			TileEditor.Frame(transition.Area, border: 2);
		}
	}

	public static int RetainObservable(WorldPlan plan, GenerationManifest manifest)
	{
		int removed = 0;
		for (int index = manifest.BiomeTransitions.Count - 1; index >= 0; index--) {
			if (TryMeasureBoundary(plan, manifest, manifest.BiomeTransitions[index], out _, out _, out _, out _)) {
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
		out int directionChanges,
		out string samples)
	{
		HashSet<int> crossingColumns = [];
		List<int> orderedCrossings = [];
		List<string> sampledCrossings = [];
		observedCrossings = 0;
		directionChanges = 0;
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
			if (bestX != int.MinValue && bestDistance <= Math.Max(12, transition.Area.Width / 6)) {
				crossingColumns.Add(bestX);
				orderedCrossings.Add(bestX);
				observedCrossings++;
				sampledCrossings.Add($"{depth}:{bestX}");
			}
		}

		int priorDirection = 0;
		for (int index = 1; index < orderedCrossings.Count; index++) {
			int direction = Math.Sign(orderedCrossings[index] - orderedCrossings[index - 1]);
			if (direction == 0) {
				continue;
			}
			if (priorDirection != 0 && direction != priorDirection) {
				directionChanges++;
			}
			priorDirection = direction;
		}
		crossingSpan = crossingColumns.Count == 0 ? 0 : crossingColumns.Max() - crossingColumns.Min();
		samples = string.Join(",", sampledCrossings);
		return observedCrossings >= 6
			&& crossingSpan >= Math.Max(18, transition.Area.Width / 5)
			&& directionChanges >= 3;
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
				int bottomFeather = Math.Clamp(
					8 + OrganicBoundary.Profile(
						x,
						plan.GenerationSeed ^ centerX ^ 0x424F_5454,
						31,
						7,
						5,
						2),
					4,
					15);
				if (depth > maximumDepth - bottomFeather) {
					continue;
				}
				if (IsFeatureOwned(manifest, x, y)) {
					continue;
				}
				Tile tile = Main.tile[x, y];
				if (!tile.HasTile || TileEditor.IsProtectedTile(tile) || !IsBlendableTile(tile.TileType)) {
					continue;
				}

				int boundaryX = BoundaryColumn(area, centerX, depth, plan.GenerationSeed);
				double raggedOffset = (OrganicBoundary.Field(
					x,
					y,
					plan.GenerationSeed ^ centerX ^ 0x5449_4C45,
					17,
					5) - 0.5d) * area.Width * 0.22d;
				int leftFeather = Math.Clamp(
					9 + OrganicBoundary.Profile(
						y,
						plan.GenerationSeed ^ centerX ^ 0x4C45_4654,
						23,
						6,
						5,
						2),
					4,
					16);
				int rightFeather = Math.Clamp(
					9 + OrganicBoundary.Profile(
						y,
						plan.GenerationSeed ^ centerX ^ 0x5249_4748,
						29,
						7,
						5,
						2),
					4,
					16);
				if (x < area.Left + leftFeather || x >= area.Right - rightFeather) {
					int sampleX = x < area.Left + leftFeather ? area.Left - 1 : area.Right;
					int sampleY = Math.Clamp(plan.SurfaceAt(sampleX) + depth, 2, Main.maxTilesY - 3);
					Tile sample = Main.tile[sampleX, sampleY];
					if (sample.HasTile && IsBlendableTile(sample.TileType) && tile.TileType != sample.TileType) {
						TileEditor.SetTerrain(x, y, sample.TileType);
						modified++;
					}
					if (depth >= 3 && Main.tile[x, y].WallType != WallID.None
						&& IsTransitionWallType(sample.WallType)) {
						TileEditor.SetWall(x, y, sample.WallType);
					}
					continue;
				}
				BiomeKind selected = x + raggedOffset >= boundaryX ? rightBiome : leftBiome;
				ushort target = MaterialFor(selected, x, y, depth, plan.GenerationSeed ^ centerX);
				if (tile.TileType != target) {
					TileEditor.SetTerrain(x, y, target);
					modified++;
				}
				if (depth >= 3 && Main.tile[x, y].WallType != WallID.None) {
					int wallBoundaryX = BoundaryColumn(
						area,
						centerX,
						depth + 11,
						plan.GenerationSeed ^ 0x5741_4C4C);
					double wallRaggedOffset = (OrganicBoundary.Field(
						x,
						y,
						plan.GenerationSeed ^ centerX ^ 0x5741_4C52,
						23,
						7) - 0.5d) * area.Width * 0.26d;
					BiomeKind wallBiome = x + wallRaggedOffset >= wallBoundaryX ? rightBiome : leftBiome;
					TileEditor.SetWall(x, y, WallFor(wallBiome));
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
		int amplitude = Math.Clamp(area.Width / 4, 17, 30);
		int profile = OrganicBoundary.Profile(
			depth + centerX % 23,
			seed ^ centerX,
			29,
			7,
			amplitude,
			Math.Max(5, amplitude / 3));
		int warp = (int)Math.Round((OrganicBoundary.Field(
			centerX,
			depth,
			seed ^ 0x424F_554E,
			31,
			9) - 0.5d) * area.Width * 0.18d);
		return Math.Clamp(centerX + profile + warp, area.Left + 8, area.Right - 9);
	}

	internal static bool IsFeatureOwned(GenerationManifest manifest, int x, int y)
	{
		Point point = new(x, y);
		return manifest.Terraces.Any(terrace => terrace.Area.Contains(point))
			|| manifest.Landmarks.Any(landmark => landmark.Area.Contains(point))
			|| manifest.Bridges.Any(bridge => bridge.Area.Contains(point))
			|| manifest.ForestLakeBridges.Any(bridge => bridge.Area.Contains(point))
			|| manifest.Valleys.Any(valley => valley.Area.Contains(point))
			|| manifest.MountainWaters.Any(water => water.Area.Contains(point))
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

	private static ushort MaterialFor(BiomeKind biome, int x, int y, int depth, int seed)
	{
		int shallowJitter = OrganicBoundary.Profile(x, seed ^ (int)biome * 193, 37, 9, 5, 3);
		int deepJitter = OrganicBoundary.Profile(x, seed ^ (int)biome * 389 ^ 0x4445_4550, 53, 13, 8, 3);
		if (OrganicBoundary.Field(x, y, seed ^ (int)biome * 769, 19, 6) is < 0.24d or > 0.78d) {
			shallowJitter += depth % 2 == 0 ? 1 : -1;
		}

		return biome switch {
		BiomeKind.Snow => depth < 8 + shallowJitter ? TileID.SnowBlock : depth < 24 + deepJitter ? TileID.IceBlock : TileID.Stone,
		BiomeKind.Desert => depth < 7 + shallowJitter ? TileID.Sand : depth < 22 + deepJitter ? TileID.HardenedSand : TileID.Sandstone,
		BiomeKind.Jungle => depth == 0 ? TileID.JungleGrass : depth < 24 + deepJitter ? TileID.Mud : TileID.Stone,
		BiomeKind.Evil when WorldGen.crimson => depth == 0 ? TileID.CrimsonGrass : TileID.Crimstone,
		BiomeKind.Evil => depth == 0 ? TileID.CorruptGrass : TileID.Ebonstone,
		_ => depth == 0 ? TileID.Grass : depth < 14 + deepJitter ? TileID.Dirt : TileID.Stone
		};
	}

	private static ushort WallFor(BiomeKind biome) => biome switch {
		BiomeKind.Snow => WallID.SnowWallUnsafe,
		BiomeKind.Desert => WallID.Sandstone,
		BiomeKind.Jungle => WallID.JungleUnsafe,
		BiomeKind.Evil => WorldGen.crimson ? WallID.CrimstoneUnsafe : WallID.EbonstoneUnsafe,
		_ => WallID.DirtUnsafe
	};

	private static void BreakLongWallSeams(Rectangle area, GenerationManifest manifest, int seed)
	{
		for (int x = area.Left - 1; x < area.Right; x++) {
			int runStart = -1;
			for (int y = area.Top; y <= area.Bottom; y++) {
				bool boundary = y < area.Bottom
					&& IsTransitionWallCell(manifest, x, y)
					&& IsTransitionWallCell(manifest, x + 1, y)
					&& Main.tile[x, y].WallType != Main.tile[x + 1, y].WallType;
				if (boundary && runStart < 0) {
					runStart = y;
				}
				if (boundary) {
					continue;
				}
				if (runStart >= 0 && y - runStart > 18) {
					for (int seamY = runStart; seamY < y; seamY++) {
						ushort leftWall = Main.tile[x, seamY].WallType;
						ushort rightWall = Main.tile[x + 1, seamY].WallType;
						int push = OrganicBoundary.Profile(seamY, seed ^ x ^ 0x5653_454D, 17, 5, 4, 2);
						if (x < area.Left) {
							push = 1 + Math.Abs(push);
						}
						else if (x >= area.Right - 1) {
							push = -1 - Math.Abs(push);
						}
						int reach = 1 + Math.Min(5, Math.Abs(push));
						for (int offset = 0; offset < reach; offset++) {
							int targetX = push >= 0 ? x + 1 + offset : x - offset;
							if (targetX >= area.Left && targetX < area.Right
								&& IsTransitionWallCell(manifest, targetX, seamY)) {
								TileEditor.SetWall(targetX, seamY, push >= 0 ? leftWall : rightWall);
							}
						}
					}
				}
				runStart = -1;
			}
		}

		for (int y = area.Top - 1; y < area.Bottom; y++) {
			int runStart = -1;
			for (int x = area.Left; x <= area.Right; x++) {
				bool boundary = x < area.Right
					&& IsTransitionWallCell(manifest, x, y)
					&& IsTransitionWallCell(manifest, x, y + 1)
					&& Main.tile[x, y].WallType != Main.tile[x, y + 1].WallType;
				if (boundary && runStart < 0) {
					runStart = x;
				}
				if (boundary) {
					continue;
				}
				if (runStart >= 0 && x - runStart > 18) {
					for (int seamX = runStart; seamX < x; seamX++) {
						ushort upperWall = Main.tile[seamX, y].WallType;
						ushort lowerWall = Main.tile[seamX, y + 1].WallType;
						int push = OrganicBoundary.Profile(seamX, seed ^ y ^ 0x4853_454D, 19, 5, 4, 2);
						if (y < area.Top) {
							push = 1 + Math.Abs(push);
						}
						else if (y >= area.Bottom - 1) {
							push = -1 - Math.Abs(push);
						}
						int reach = 1 + Math.Min(5, Math.Abs(push));
						for (int offset = 0; offset < reach; offset++) {
							int targetY = push >= 0 ? y + 1 + offset : y - offset;
							if (targetY >= area.Top && targetY < area.Bottom
								&& IsTransitionWallCell(manifest, seamX, targetY)) {
								TileEditor.SetWall(seamX, targetY, push >= 0 ? upperWall : lowerWall);
							}
						}
					}
				}
				runStart = -1;
			}
		}
	}

	private static bool IsTransitionWallCell(GenerationManifest manifest, int x, int y)
	{
		if (IsFeatureOwned(manifest, x, y)) {
			return false;
		}
		return IsTransitionWallType(Main.tile[x, y].WallType);
	}

	private static bool IsTransitionWallType(ushort wallType) => wallType is
		WallID.DirtUnsafe or WallID.Stone or WallID.SnowWallUnsafe or WallID.JungleUnsafe
		or WallID.Sandstone or WallID.EbonstoneUnsafe or WallID.CrimstoneUnsafe;

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

	private readonly record struct BiomeRun(BiomeKind Biome, int Left, int Right)
	{
		public int Width => Right - Left + 1;
	}
}
