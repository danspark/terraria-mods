using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.WorldBuilding;

namespace RicherBiomes.WorldGeneration;

internal static class WorldValidator
{
	public static GenerationReport Validate(WorldPlan plan, SurfaceMinePlan surfaceMinePlan, GenerationManifest manifest)
	{
		List<string> errors = [];
		int relief = ValidateSurface(plan, errors);
		int mountainCount = ValidateMountains(plan, manifest, errors);
		int connectedCaves = ValidateCaves(plan, errors);
		ValidateTerraces(plan, manifest, errors);
		ValidateLandmarks(manifest, errors);
		ValidateForestLakeBridges(manifest, errors);
		ValidateBiomeTransitions(plan, manifest, errors);
		ValidateBridgesAndValleys(plan, manifest, errors);
		ValidateSkyHighlands(plan, manifest, errors);
		int mineTrackTiles = ValidateSurfaceMine(surfaceMinePlan, manifest, errors);
		ValidateProgressionSites(errors);

		int accentCount = manifest.AccentCounts.Values.Sum();
		int minimumAccents = Math.Max(36, Main.maxTilesX / 90);
		if (accentCount < minimumAccents) {
			errors.Add($"only {accentCount} contextual accents survived; expected at least {minimumAccents}");
		}

		if (errors.Count > 0) {
			throw new InvalidOperationException("Richer Biomes world validation failed: " + string.Join("; ", errors));
		}

		return new GenerationReport(
			true,
			plan.Regions.Count,
			relief,
			mountainCount,
			connectedCaves,
			manifest.Terraces.Count,
			manifest.Landmarks.Count,
			accentCount,
			manifest.Bridges.Count,
			manifest.ForestLakeBridges.Count,
			manifest.MountainWaters.Count,
			manifest.SkyHighlands.Count,
			mineTrackTiles);
	}

	private static int ValidateSurface(WorldPlan plan, List<string> errors)
	{
		int minimumY = int.MaxValue;
		int maximumY = int.MinValue;
		int missingSamples = 0;
		for (int x = plan.LeftBoundary; x <= plan.RightBoundary; x += 8) {
			if (!BiomeClassifier.TryFindGroundSupport(x, out int y)) {
				missingSamples++;
				continue;
			}

			minimumY = Math.Min(minimumY, y);
			maximumY = Math.Max(maximumY, y);
		}

		int relief = maximumY > minimumY ? maximumY - minimumY : 0;
		int expectedRelief = Math.Min(100, Math.Max(65, (int)Main.worldSurface / 3));
		if (relief < expectedRelief) {
			errors.Add($"surface relief was {relief} tiles; expected at least {expectedRelief}");
		}
		if (missingSamples > 4) {
			errors.Add($"{missingSamples} inland surface samples had no natural support");
		}

		return relief;
	}

	private static int ValidateMountains(WorldPlan plan, GenerationManifest manifest, List<string> errors)
	{
		int validMountains = 0;
		int spaceThreshold = (int)Math.Floor(Main.worldSurface * 0.35d);
		foreach (WorldRegion region in plan.Regions.Where(region => region.Landform == LandformKind.Mountain)) {
			MountainRangePlan planned = plan.Mountains.First(mountain => mountain.RegionId == region.Id);
			int peakY = int.MaxValue;
			int summitBandWidth = 0;
			int[] groundedSurface = MeasureGroundedMountainSurface(plan, region);
			for (int x = region.Left; x <= region.Right; x++) {
				int y = groundedSurface[x - region.Left];
				if (y != int.MaxValue) {
					peakY = Math.Min(peakY, y);
					if (y <= spaceThreshold) {
						summitBandWidth++;
					}
				}
			}

			MountainRecord? record = null;
			foreach (MountainRecord mountain in manifest.Mountains) {
				if (mountain.RegionId == region.Id) {
					record = mountain;
					break;
				}
			}
			int authoredPeak = Math.Min(planned.LeftPeakY, planned.RightPeakY);
			bool expectedAltitude = Math.Abs(peakY - authoredPeak) <= 38;
			bool expectedSpaceBand = planned.HeightStyle == MountainHeightStyle.SkyPiercing
				? peakY <= spaceThreshold && summitBandWidth >= 20
				: peakY > spaceThreshold - 12 && summitBandWidth < 20;
			if (expectedAltitude && expectedSpaceBand) {
				validMountains++;
			}
			else {
				errors.Add(
					$"mountain region {region.Id} ({planned.HeightStyle}) had peak y={peakY} "
					+ $"(planned {authoredPeak}) and {summitBandWidth} Space-band columns");
			}
			if (record is MountainRecord final && final.EntranceCount < 2) {
				errors.Add($"mountain region {region.Id} retained only {final.EntranceCount} visible entrances");
			}
			if (planned.HeightStyle == MountainHeightStyle.SkyPiercing
				&& record is MountainRecord cloudRecord && cloudRecord.CloudTiles < 24) {
				errors.Add($"mountain region {region.Id} retained only {cloudRecord.CloudTiles} cloud-belt tiles");
			}
			if (record is not MountainRecord interior) {
				errors.Add($"mountain region {region.Id} has no final interior record");
				continue;
			}
			int minimumCaveAir = interior.Area.Width * 16;
			if (interior.CaveAirTiles < minimumCaveAir) {
				errors.Add($"mountain region {region.Id} retained only {interior.CaveAirTiles} wall-backed cave cells; expected {minimumCaveAir}");
			}
			if (interior.WideCavityColumns < interior.Area.Width / 3) {
				errors.Add($"mountain region {region.Id} has wide chambers in only {interior.WideCavityColumns}/{interior.Area.Width} columns");
			}
			if (interior.PotTiles < 6) {
				errors.Add($"mountain region {region.Id} retained only {interior.PotTiles} pot tiles");
			}
			int minimumVines = Math.Max(180, interior.Area.Width / 2);
			if (interior.VineTiles < minimumVines) {
				errors.Add($"mountain region {region.Id} retained only {interior.VineTiles} vine tiles; expected {minimumVines}");
			}
			if (interior.ClimbAidTiles < 18) {
				errors.Add($"mountain region {region.Id} retained only {interior.ClimbAidTiles} rope or platform tiles");
			}
			if (!MountainBiomeGenerator.HasUsableWallClimb(plan, planned, out string wallClimbReason)) {
				errors.Add($"mountain region {region.Id} lost its wall-only climb section: {wallClimbReason}");
			}
			int suspendedNaturalTiles = CountSuspendedNaturalTiles(plan, planned, interior.Area);
			if (suspendedNaturalTiles < 12) {
				errors.Add($"mountain region {region.Id} retained only {suspendedNaturalTiles} suspended natural ledge tiles");
			}
			int wallVoidCells = CountInteriorWallVoids(plan, interior.Area);
			if (wallVoidCells < interior.Area.Width / 2) {
				errors.Add($"mountain region {region.Id} retained only {wallVoidCells} open-background cave cells");
			}
			int longVineRuns = CountLongVineRuns(interior.Area);
			int minimumVineRuns = Math.Max(8, interior.Area.Width / 90);
			if (longVineRuns < minimumVineRuns) {
				errors.Add($"mountain region {region.Id} retained only {longVineRuns} distinct vine curtains");
			}
			if (interior.WaterBodyCount < 2 || interior.WaterCells < 240) {
				errors.Add(
					$"mountain region {region.Id} retained {interior.WaterBodyCount} interior water bodies "
					+ $"with {interior.WaterCells} water cells; expected at least 2 bodies and 240 cells");
			}
			ValidateMountainWaters(region.Id, manifest, errors);
			(int matchingMaterial, int sampledMaterial) = MeasureMountainMaterialOwnership(plan, planned);
			if (sampledMaterial < 20 || matchingMaterial < sampledMaterial * 3 / 5) {
				errors.Add(
					$"mountain region {region.Id} follows its underlying biome in only "
					+ $"{matchingMaterial}/{sampledMaterial} sampled surface columns");
			}
			ValidateJungleMountainBody(plan, planned, manifest, errors);
			if (!HasGroundedMountainCrowns(plan, planned, manifest, out int connectedCrownCells)) {
				errors.Add(
					$"mountain region {region.Id} has a summit with only {connectedCrownCells} grounded crown cells");
			}
			int repeatedGroundingSlots = CountRepeatedGroundingSlotChains(plan, planned, manifest);
			if (repeatedGroundingSlots > 0) {
				errors.Add(
					$"mountain region {region.Id} retained {repeatedGroundingSlots} vertically repeated rectangular cave-slot chains");
			}
			(int horizontalWallSeam, int verticalWallSeam, Point horizontalStart, Point verticalStart) =
				MeasureNaturalWallSeams(interior.Area, manifest);
			if (horizontalWallSeam > 26 || verticalWallSeam > 26) {
				ushort verticalLeftWall = Main.tile[verticalStart.X, verticalStart.Y].WallType;
				ushort verticalRightWall = Main.tile[verticalStart.X + 1, verticalStart.Y].WallType;
				errors.Add(
					$"mountain region {region.Id} retained axis-aligned natural-wall seams "
					+ $"({horizontalWallSeam} horizontal near {horizontalStart}, "
					+ $"{verticalWallSeam} vertical near {verticalStart}, walls {verticalLeftWall}/{verticalRightWall}); "
					+ $"nearby transitions {DescribeNearbyTransitions(manifest, horizontalStart, verticalStart)}");
			}
			(int horizontalMaterialSeam, int verticalMaterialSeam, Point materialHorizontalStart, Point materialVerticalStart) =
				MeasureSolidMaterialSeams(interior.Area, (x, y) => MountainLayerMaterialAt(manifest, x, y));
			if (horizontalMaterialSeam > 26 || verticalMaterialSeam > 26) {
				ushort horizontalUpper = Main.tile[materialHorizontalStart.X, materialHorizontalStart.Y].TileType;
				ushort horizontalLower = Main.tile[materialHorizontalStart.X, materialHorizontalStart.Y + 1].TileType;
				ushort verticalLeft = Main.tile[materialVerticalStart.X, materialVerticalStart.Y].TileType;
				ushort verticalRight = Main.tile[materialVerticalStart.X + 1, materialVerticalStart.Y].TileType;
				errors.Add(
					$"mountain region {region.Id} retained axis-aligned natural-material seams "
					+ $"({horizontalMaterialSeam} horizontal near {materialHorizontalStart}, "
					+ $"tiles {horizontalUpper}/{horizontalLower}, depth "
					+ $"{materialHorizontalStart.Y - plan.SurfaceAt(materialHorizontalStart.X)}; "
					+ $"{verticalMaterialSeam} vertical near {materialVerticalStart}, "
					+ $"tiles {verticalLeft}/{verticalRight}, depths "
					+ $"{materialVerticalStart.Y - plan.SurfaceAt(materialVerticalStart.X)}/"
					+ $"{materialVerticalStart.Y - plan.SurfaceAt(materialVerticalStart.X + 1)}; "
					+ $"area {interior.Area}; nearby transitions "
					+ $"{DescribeNearbyTransitions(manifest, materialHorizontalStart, materialVerticalStart)})");
			}
		}

		if (plan.Mountains.Count > 1 && plan.Mountains.Select(mountain => mountain.InteriorStyle).Distinct().Count() < 2) {
			errors.Add("all planned mountains use the same interior route style");
		}
		if (plan.Mountains.Count > 1 && plan.Mountains.Select(mountain => mountain.HeightStyle).Distinct().Count() < 2) {
			errors.Add("all planned mountains use the same altitude family");
		}
		return validMountains;
	}

	private static int CountSuspendedNaturalTiles(WorldPlan plan, MountainRangePlan mountain, Rectangle area)
	{
		int count = 0;
		for (int x = area.Left + 4; x < area.Right - 4; x++) {
			for (int y = Math.Max(area.Top + 5, plan.SurfaceAt(x) + 18); y < area.Bottom - 14; y++) {
				Tile tile = Main.tile[x, y];
				if (!IsNaturalMountainTile(tile) || TileEditor.IsSolid(x, y - 1)) {
					continue;
				}
				bool airBelow = false;
				for (int depth = 2; depth <= 12; depth++) {
					if (!TileEditor.IsSolid(x, y + depth)) {
						airBelow = true;
						break;
					}
				}
				count += airBelow ? 1 : 0;
			}
		}
		return count;
	}

	private static int CountInteriorWallVoids(WorldPlan plan, Rectangle area)
	{
		int count = 0;
		for (int x = area.Left + 4; x < area.Right - 4; x++) {
			for (int y = Math.Max(area.Top + 5, plan.SurfaceAt(x) + 16); y < area.Bottom - 5; y++) {
				Tile tile = Main.tile[x, y];
				count += !TileEditor.IsSolid(x, y) && tile.WallType == WallID.None ? 1 : 0;
			}
		}
		return count;
	}

	private static int CountLongVineRuns(Rectangle area)
	{
		int runs = 0;
		for (int x = area.Left; x < area.Right; x++) {
			int run = 0;
			for (int y = area.Top; y < area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				bool vine = tile.HasTile && tile.TileType is TileID.Vines or TileID.JungleVines
					or TileID.CrimsonVines or TileID.CorruptVines or TileID.MushroomVines or TileID.AshVines
					or TileID.VineRope;
				if (vine) {
					run++;
					continue;
				}
				runs += run >= 4 ? 1 : 0;
				run = 0;
			}
			runs += run >= 4 ? 1 : 0;
		}
		return runs;
	}

	private static void ValidateMountainWaters(int regionId, GenerationManifest manifest, List<string> errors)
	{
		foreach (MountainWaterRecord water in manifest.MountainWaters.Where(record => record.RegionId == regionId)) {
			int actualWater = CountLiquid(water.Area, LiquidID.Water);
			int minimumWater = Math.Max(80, water.Area.Width * 2);
			if (actualWater < minimumWater || water.WaterCells != actualWater) {
				errors.Add(
					$"{water.Style} in mountain region {regionId} retained {actualWater} water cells "
					+ $"(recorded {water.WaterCells}, expected at least {minimumWater})");
			}

			List<int> bed = [];
			List<int> waterSurface = [];
			for (int x = water.Area.Left + 3; x < water.Area.Right - 3; x++) {
				int bottom = int.MinValue;
				int top = int.MaxValue;
				for (int y = water.WaterlineY; y < water.Area.Bottom - 2; y++) {
					Tile tile = Main.tile[x, y];
					if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Water) {
						top = Math.Min(top, y);
						bottom = y;
					}
				}
				if (bottom != int.MinValue) {
					bed.Add(bottom);
					waterSurface.Add(top);
				}
			}
			int directionChanges = CountDirectionChanges(bed);
			int bedSpan = bed.Count == 0 ? 0 : bed.Max() - bed.Min();
			int surfaceSpan = waterSurface.Count == 0 ? int.MaxValue : waterSurface.Max() - waterSurface.Min();
			if (bed.Count < water.Area.Width / 2 || surfaceSpan > water.Depth + 6 || bedSpan < 4 || directionChanges < 4) {
				errors.Add(
					$"{water.Style} in mountain region {regionId} has an underdeveloped basin: "
					+ $"wetColumns={bed.Count}, surfaceSpan={surfaceSpan}, bedSpan={bedSpan}, turns={directionChanges}");
			}
		}
	}

	private static (int Matching, int Sampled) MeasureMountainMaterialOwnership(
		WorldPlan plan,
		MountainRangePlan mountain)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		ushort[] materialProfile = LandformGenerator.CaptureMountainMaterialProfile(plan, mountain);
		int matching = 0;
		int sampled = 0;
		for (int x = region.Left + 8; x <= region.Right - 8; x += 3) {
			int surfaceY = plan.SurfaceAt(x);
			Tile actual = Main.tile[x, surfaceY];
			if (!actual.HasUnactuatedTile || Main.tileFrameImportant[actual.TileType] || !IsNaturalMountainTile(actual)) {
				continue;
			}
			ushort expected = LandformGenerator.MountainMaterialAt(
				plan,
				mountain,
				x,
				0,
				materialProfile);
			sampled++;
			matching += SameMountainMaterialFamily(actual.TileType, expected) ? 1 : 0;
		}
		return (matching, sampled);
	}

	private static void ValidateJungleMountainBody(
		WorldPlan plan,
		MountainRangePlan mountain,
		GenerationManifest manifest,
		List<string> errors)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		ushort[] materialProfile = LandformGenerator.CaptureMountainMaterialProfile(plan, mountain);
		int hostColumns = 0;
		int bodySamples = 0;
		int jungleTiles = 0;
		int structuralRockTiles = 0;
		int weakColumns = 0;
		for (int x = region.Left + 8; x <= region.Right - 8; x += 4) {
			int columnSamples = 0;
			int columnJungle = 0;
			int bottomY = Math.Min(Main.maxTilesY - 50, (int)Main.worldSurface + 45);
			for (int y = plan.SurfaceAt(x); y <= bottomY; y += 2) {
				if (IsMountainValidationExcluded(manifest, x, y)) {
					continue;
				}
				ushort intendedMaterial = LandformGenerator.MountainMaterialAt(
					plan,
					mountain,
					x,
					y - plan.SurfaceAt(x),
					materialProfile);
				if (intendedMaterial is not (TileID.JungleGrass or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass)) {
					continue;
				}
				Tile tile = Main.tile[x, y];
				if (!IsNaturalMountainTile(tile)) {
					continue;
				}
				columnSamples++;
				columnJungle += IsMatchingJungleTerrain(tile.TileType, intendedMaterial) ? 1 : 0;
				structuralRockTiles += tile.TileType == JungleRockFor(intendedMaterial) ? 1 : 0;
			}
			bodySamples += columnSamples;
			jungleTiles += columnJungle;
			if (columnSamples < 12) {
				continue;
			}
			hostColumns++;
			if (columnJungle * 100 < columnSamples * 45) {
				weakColumns++;
			}
		}

		if (hostColumns < 8) {
			return;
		}
		const int minimumBodySamples = 160;
		if (bodySamples < minimumBodySamples) {
			errors.Add(
				$"Jungle mountain region {region.Id} exposed only {bodySamples} measurable natural-body samples; "
				+ $"expected at least {minimumBodySamples}");
			return;
		}
		if (jungleTiles * 100 < bodySamples * 60) {
			errors.Add(
				$"Jungle mountain region {region.Id} is stone-dominated: "
				+ $"{jungleTiles}/{bodySamples} body samples are Mud or matching Jungle Grass");
		}
		// The lower rock share is an aesthetic target produced by a correlated
		// field, not a safety invariant. A seed can form broad Mud patches and
		// land just below a quota while still producing a healthy Jungle body.
		// Keep the upper bound strict because that prevents mountains from
		// replacing the Jungle with a wall of Stone.
		if (structuralRockTiles * 100 > bodySamples * 38) {
			errors.Add(
				$"Jungle mountain region {region.Id} retained {structuralRockTiles}/{bodySamples} structural rock samples; "
				+ "expected at most 38%");
		}
		int foreignTiles = bodySamples - jungleTiles - structuralRockTiles;
		if (foreignTiles * 100 > bodySamples * 10) {
			errors.Add(
				$"Jungle mountain region {region.Id} retained {foreignTiles}/{bodySamples} foreign natural-material samples; expected at most 10%");
		}
		if (weakColumns > Math.Max(2, (hostColumns + 3) / 4)) {
			errors.Add(
				$"Jungle mountain region {region.Id} has {weakColumns}/{hostColumns} sampled columns with a thin or absent Jungle body");
		}
	}

	private static bool HasGroundedMountainCrowns(
		WorldPlan plan,
		MountainRangePlan mountain,
		GenerationManifest manifest,
		out int connectedCrownCells)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		int top = Math.Max(45, Math.Min(mountain.LeftPeakY, mountain.RightPeakY) - 12);
		// Seed the connectivity search below the mountain's decorative interior.
		// The upper cavern band can be almost entirely open on some seeds even
		// though the mountain joins ordinary ground a few dozen tiles lower.
		int bottom = Math.Min(Main.maxTilesY - 50, (int)Main.worldSurface + 130);
		Rectangle area = new(region.Left, top, region.Width, bottom - top);
		bool[] grounded = new bool[area.Width * area.Height];
		Queue<Point> queue = new();
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = Math.Max(area.Top, area.Bottom - 18); y < area.Bottom; y++) {
				if (!IsDeepMountainGroundingSeed(x, y)) {
					continue;
				}
				int index = (x - area.Left) + (y - area.Top) * area.Width;
				grounded[index] = true;
				queue.Enqueue(new Point(x, y));
			}
		}
		ReadOnlySpan<Point> directions = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
		while (queue.Count > 0) {
			Point current = queue.Dequeue();
			foreach (Point direction in directions) {
				int x = current.X + direction.X;
				int y = current.Y + direction.Y;
				if (!area.Contains(x, y) || !IsMountainGroundingCell(x, y, manifest)) {
					continue;
				}
				int index = (x - area.Left) + (y - area.Top) * area.Width;
				if (grounded[index]) {
					continue;
				}
				grounded[index] = true;
				queue.Enqueue(new Point(x, y));
			}
		}

		connectedCrownCells = new[] { mountain.LeftPeakX, mountain.RightPeakX }
			.Select(peakX => CountGroundedCrownCells(plan, area, grounded, manifest, peakX))
			.Min();
		return connectedCrownCells >= 12;
	}

	private static int CountGroundedCrownCells(
		WorldPlan plan,
		Rectangle area,
		IReadOnlyList<bool> grounded,
		GenerationManifest manifest,
		int peakX)
	{
		int count = 0;
		for (int x = peakX - 14; x <= peakX + 14; x++) {
			int surfaceY = plan.SurfaceAt(x);
			for (int y = surfaceY; y <= surfaceY + 18; y++) {
				if (!area.Contains(x, y)) {
					continue;
				}
				int index = (x - area.Left) + (y - area.Top) * area.Width;
				count += grounded[index]
					&& (IsNaturalMountainTile(Main.tile[x, y]) || IsAttachedHighlandGroundingTile(manifest, x, y))
					? 1
					: 0;
			}
		}
		return count;
	}

	private static int CountRepeatedGroundingSlotChains(
		WorldPlan plan,
		MountainRangePlan mountain,
		GenerationManifest manifest)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		int top = Math.Max(45, Math.Min(mountain.LeftPeakY, mountain.RightPeakY) - 12);
		int bottom = Math.Min(Main.maxTilesY - 50, (int)Main.worldSurface + 70);
		Rectangle area = new(region.Left, top, region.Width, bottom - top);
		bool[] visited = new bool[area.Width * area.Height];
		List<Rectangle> slots = [];
		ReadOnlySpan<Point> directions = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
		for (int x = area.Left + 2; x < area.Right - 2; x++) {
			for (int y = area.Top + 2; y < area.Bottom - 2; y++) {
				int startIndex = (x - area.Left) + (y - area.Top) * area.Width;
				if (visited[startIndex] || !IsWallBackedMountainOpening(manifest, x, y)) {
					continue;
				}
				Queue<Point> queue = new();
				queue.Enqueue(new Point(x, y));
				visited[startIndex] = true;
				int cells = 0;
				int left = x;
				int right = x;
				int componentTop = y;
				int componentBottom = y;
				while (queue.Count > 0) {
					Point current = queue.Dequeue();
					cells++;
					left = Math.Min(left, current.X);
					right = Math.Max(right, current.X);
					componentTop = Math.Min(componentTop, current.Y);
					componentBottom = Math.Max(componentBottom, current.Y);
					foreach (Point direction in directions) {
						int nextX = current.X + direction.X;
						int nextY = current.Y + direction.Y;
						if (!area.Contains(nextX, nextY)) {
							continue;
						}
						int nextIndex = (nextX - area.Left) + (nextY - area.Top) * area.Width;
						if (visited[nextIndex] || !IsWallBackedMountainOpening(manifest, nextX, nextY)) {
							continue;
						}
						visited[nextIndex] = true;
						queue.Enqueue(new Point(nextX, nextY));
					}
				}
				int width = right - left + 1;
				int height = componentBottom - componentTop + 1;
				if (cells <= 80 && width is >= 4 and <= 9 && height is >= 5 and <= 12
					&& cells * 100 >= width * height * 85) {
					slots.Add(new Rectangle(left, componentTop, width, height));
				}
			}
		}

		slots.Sort((left, right) => left.Center.Y.CompareTo(right.Center.Y));
		int chains = 0;
		for (int first = 0; first < slots.Count; first++) {
			for (int second = first + 1; second < slots.Count; second++) {
				int firstGap = slots[second].Center.Y - slots[first].Center.Y;
				if (firstGap is < 22 or > 32
					|| Math.Abs(slots[second].Center.X - slots[first].Center.X) > 2
					|| Math.Abs(slots[first].Width - slots[second].Width) > 1
					|| Math.Abs(slots[first].Height - slots[second].Height) > 1) {
					continue;
				}
				for (int third = second + 1; third < slots.Count; third++) {
					int secondGap = slots[third].Center.Y - slots[second].Center.Y;
					if (secondGap is >= 22 and <= 32
						&& Math.Abs(firstGap - secondGap) <= 2
						&& Math.Abs(slots[third].Center.X - slots[second].Center.X) <= 2
						&& Math.Abs(slots[second].Width - slots[third].Width) <= 1
						&& Math.Abs(slots[second].Height - slots[third].Height) <= 1) {
						chains++;
						break;
					}
				}
			}
		}
		return chains;
	}

	private static bool IsWallBackedMountainOpening(GenerationManifest manifest, int x, int y)
	{
		Tile tile = Main.tile[x, y];
		return !TileEditor.IsSolid(x, y)
			&& tile.WallType != WallID.None
			&& !IsMountainValidationExcluded(manifest, x, y);
	}

	private static bool IsMountainValidationExcluded(GenerationManifest manifest, int x, int y) =>
		BiomeTransitionGenerator.IsFeatureOwned(manifest, x, y)
		|| manifest.BiomeTransitions.Any(record => record.Area.Contains(x, y));

	private static bool IsMountainGroundingCell(int x, int y, GenerationManifest manifest) =>
		IsNaturalMountainTile(Main.tile[x, y])
		|| IsAttachedHighlandGroundingTile(manifest, x, y)
		|| WorldGen.InWorld(x, y, 3)
			&& !TileEditor.IsSolid(x, y)
			&& IsNaturalMountainWall(Main.tile[x, y].WallType);

	private static bool IsAttachedHighlandGroundingTile(GenerationManifest manifest, int x, int y)
	{
		Tile tile = Main.tile[x, y];
		return tile.HasUnactuatedTile
			&& tile.TileType is TileID.Cloud or TileID.RainCloud or TileID.SnowCloud or TileID.Sunplate
			&& manifest.SkyHighlands.Any(highland => highland.MountainAttached && highland.Area.Contains(x, y));
	}

	private static bool IsDeepMountainGroundingSeed(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 3) || !IsNaturalMountainTile(Main.tile[x, y])) {
			return false;
		}
		int continuation = 0;
		for (int offset = 1; offset <= 18; offset++) {
			continuation += WorldGen.InWorld(x, y + offset, 3)
				&& IsNaturalMountainTile(Main.tile[x, y + offset]) ? 1 : 0;
		}
		return continuation >= 12;
	}

	private static bool IsNaturalMountainWall(ushort wall) => wall is
		WallID.DirtUnsafe or WallID.Stone or WallID.SnowWallUnsafe or WallID.JungleUnsafe
		or WallID.Sandstone or WallID.EbonstoneUnsafe or WallID.CrimstoneUnsafe;

	private static bool IsMatchingJungleTerrain(ushort actual, ushort intended) =>
		actual == TileID.Mud || actual == intended;

	private static ushort JungleRockFor(ushort jungleMaterial) => jungleMaterial switch {
		TileID.CorruptJungleGrass => TileID.Ebonstone,
		TileID.CrimsonJungleGrass => TileID.Crimstone,
		_ => TileID.Stone
	};

	private static bool SameMountainMaterialFamily(ushort left, ushort right) =>
		MountainMaterialFamily(left) == MountainMaterialFamily(right);

	private static int MountainMaterialFamily(ushort tile) => tile switch {
		TileID.SnowBlock or TileID.IceBlock or TileID.BreakableIce => 1,
		TileID.Mud or TileID.JungleGrass or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass => 2,
		TileID.Sand or TileID.HardenedSand or TileID.Sandstone or TileID.DesertFossil => 3,
		TileID.CorruptGrass or TileID.Ebonstone or TileID.Ebonsand
			or TileID.CorruptHardenedSand or TileID.CorruptSandstone => 4,
		TileID.CrimsonGrass or TileID.Crimstone or TileID.Crimsand
			or TileID.CrimsonHardenedSand or TileID.CrimsonSandstone => 5,
		_ => 0
	};

	private static bool IsNaturalMountainTile(Tile tile) => tile.HasUnactuatedTile && tile.TileType is
		TileID.Grass or TileID.Dirt or TileID.Stone or TileID.SnowBlock or TileID.IceBlock or TileID.BreakableIce
		or TileID.Mud or TileID.JungleGrass or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass
		or TileID.Sand or TileID.HardenedSand or TileID.Sandstone or TileID.DesertFossil
		or TileID.CorruptGrass or TileID.Ebonstone or TileID.Ebonsand or TileID.CorruptHardenedSand or TileID.CorruptSandstone
		or TileID.CrimsonGrass or TileID.Crimstone or TileID.Crimsand or TileID.CrimsonHardenedSand or TileID.CrimsonSandstone;

	internal static int[] MeasureGroundedMountainSurface(WorldPlan plan, WorldRegion region)
	{
		int[] surface = Enumerable.Repeat(int.MaxValue, region.Width).ToArray();
		for (int x = region.Left; x <= region.Right; x++) {
			int plannedY = plan.SurfaceAt(x);
			int top = Math.Max(40, plannedY - 20);
			int bottom = Math.Min(Main.maxTilesY - 50, plannedY + 32);
			for (int y = top; y <= bottom; y++) {
				if (IsMountainGround(x, y)) {
					surface[x - region.Left] = y;
					break;
				}
			}
		}
		return surface;
	}

	private static bool IsMountainGround(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 2)) {
			return false;
		}
		Tile tile = Main.tile[x, y];
		if (!tile.HasUnactuatedTile || Main.tileFrameImportant[tile.TileType]
			|| !Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]) {
			return false;
		}
		return tile.TileType is not (TileID.Cloud or TileID.RainCloud or TileID.SnowCloud or TileID.Sunplate);
	}

	private static int ValidateCaves(WorldPlan plan, List<string> errors)
	{
		int connected = 0;
		List<string> blockedRoutes = [];
		foreach (PlannedCave cave in plan.Caves.Where(cave => cave.RequiredRoute)) {
			if (HasAirRoute(cave, out string reason)) {
				connected++;
			}
			else {
				blockedRoutes.Add($"region {cave.RegionId}: {reason}");
			}
		}

		int plannedRequired = plan.Caves.Count(cave => cave.RequiredRoute);
		int minimumConnected = Math.Min(plannedRequired, Math.Max(3, (plannedRequired + 1) / 2));
		if (connected < minimumConnected) {
			errors.Add(
				$"only {connected} of {plannedRequired} protected regional cave routes connect to the Cavern layer; "
				+ $"expected at least {minimumConnected}. Blocked routes: {string.Join(" | ", blockedRoutes)}");
		}

		return connected;
	}

	private static bool HasAirRoute(PlannedCave cave, out string reason)
	{
		int left = Math.Max(10, Math.Min(cave.Start.X, Math.Min(cave.Midpoint.X, cave.End.X)) - cave.Radius - 12);
		int right = Math.Min(Main.maxTilesX - 11, Math.Max(cave.Start.X, Math.Max(cave.Midpoint.X, cave.End.X)) + cave.Radius + 12);
		int top = Math.Max(10, Math.Min(cave.Start.Y, Math.Min(cave.Midpoint.Y, cave.End.Y)) - cave.Radius - 12);
		int bottom = Math.Min(Main.maxTilesY - 11, Math.Max(cave.Start.Y, Math.Max(cave.Midpoint.Y, cave.End.Y)) + cave.Radius + 12);
		if (!TryFindPassable(cave.Start, left, right, top, bottom, out Point start)) {
			reason = $"no passable start near {cave.Start.X},{cave.Start.Y}";
			return false;
		}
		if (!TryFindPassable(cave.End, left, right, top, bottom, out Point end)) {
			reason = $"no passable end near {cave.End.X},{cave.End.Y}";
			return false;
		}

		int width = right - left + 1;
		int height = bottom - top + 1;
		bool[] visited = new bool[width * height];
		Queue<Point> queue = new();
		int visitedCount = 1;
		queue.Enqueue(start);
		visited[(start.X - left) + (start.Y - top) * width] = true;
		ReadOnlySpan<Point> directions = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

		while (queue.Count > 0) {
			Point current = queue.Dequeue();
			if (Math.Abs(current.X - end.X) <= 2 && Math.Abs(current.Y - end.Y) <= 2) {
				reason = string.Empty;
				return true;
			}

			foreach (Point direction in directions) {
				int x = current.X + direction.X;
				int y = current.Y + direction.Y;
				if (x < left || x > right || y < top || y > bottom || !IsPassable(x, y)) {
					continue;
				}

				int index = (x - left) + (y - top) * width;
				if (visited[index]) {
					continue;
				}

				visited[index] = true;
				visitedCount++;
				queue.Enqueue(new Point(x, y));
			}
		}

		reason = $"flood fill visited {visitedCount} cells from {start.X},{start.Y}; target {end.X},{end.Y}; bounds {left},{top}..{right},{bottom}";
		return false;
	}

	private static bool TryFindPassable(
		Point center,
		int left,
		int right,
		int top,
		int bottom,
		out Point passable)
	{
		for (int radius = 0; radius <= 14; radius++) {
			for (int x = Math.Max(left, center.X - radius); x <= Math.Min(right, center.X + radius); x++) {
				for (int y = Math.Max(top, center.Y - radius); y <= Math.Min(bottom, center.Y + radius); y++) {
					if (IsPassable(x, y)) {
						passable = new Point(x, y);
						return true;
					}
				}
			}
		}

		passable = default;
		return false;
	}

	private static bool IsPassable(int x, int y) =>
		!TileEditor.IsSolid(x, y) && !TileEditor.IsSolid(x, y - 1);

	private static void ValidateTerraces(WorldPlan plan, GenerationManifest manifest, List<string> errors)
	{
		int minimumTerraces = TerraceGenerator.MinimumRequiredCount;
		if (manifest.Terraces.Count < minimumTerraces) {
			errors.Add($"only {manifest.Terraces.Count} protected building terraces survived; expected at least {minimumTerraces}");
		}
		if (!manifest.Terraces.Any(terrace => terrace.SpawnTerrace)) {
			errors.Add("the spawn building terrace was not reserved");
		}

		foreach (BuildTerrace terrace in manifest.Terraces) {
			int minimumY = int.MaxValue;
			int maximumY = int.MinValue;
			for (int x = terrace.Area.Left; x < terrace.Area.Right; x += 2) {
				if (!BiomeClassifier.TryFindGroundSupport(x, out int y)) {
					errors.Add($"building terrace at x={terrace.Area.Left} lost its terrain support");
					break;
				}
				minimumY = Math.Min(minimumY, y);
				maximumY = Math.Max(maximumY, y);
			}
			if (maximumY - minimumY > 3) {
				errors.Add($"building terrace at x={terrace.Area.Left} has {maximumY - minimumY} tiles of relief");
			}
		}
	}

	private static void ValidateLandmarks(GenerationManifest manifest, List<string> errors)
	{
		BiomeKind[] required = [
			BiomeKind.Forest,
			BiomeKind.Snow,
			BiomeKind.Desert,
			BiomeKind.Jungle,
			BiomeKind.Evil,
			BiomeKind.Sky,
			BiomeKind.Mushroom,
			BiomeKind.Cavern,
			BiomeKind.Underworld
		];

		foreach (BiomeKind biome in required) {
			if (!manifest.Landmarks.Any(landmark => landmark.Biome == biome)) {
				errors.Add($"the {biome} biome has no landmark");
			}
		}

		if (manifest.Landmarks.Count(landmark => landmark.Biome == BiomeKind.Ocean) < 2) {
			errors.Add("both oceans did not receive a coastal landmark");
		}
		else if (manifest.Landmarks.Where(landmark => landmark.Biome == BiomeKind.Ocean)
			.Select(landmark => landmark.Archetype).Distinct().Count() < 2) {
			errors.Add("both coastal landmarks use the same archetype");
		}

		foreach (LandmarkRecord landmark in manifest.Landmarks) {
			Rectangle authoredArea = new(
				landmark.Area.Left,
				landmark.Area.Top,
				landmark.Area.Width,
				landmark.AnchorY - landmark.Area.Top + 3);
			if (!WorldGen.InWorld(landmark.Area.Left, landmark.Area.Top, 8)
				|| !WorldGen.InWorld(landmark.Area.Right - 1, landmark.Area.Bottom - 1, 8)) {
				errors.Add($"{landmark.Biome} landmark extends outside world padding");
			}
			if (landmark.Area.Width < 55 || landmark.RoomCount < 5 || landmark.FloorCount < 2
				|| landmark.StairCount < landmark.FloorCount - 1) {
				errors.Add(
					$"{landmark.Biome} landmark is not a connected multi-floor complex "
					+ $"({landmark.Area.Width} tiles, {landmark.RoomCount} rooms, {landmark.FloorCount} floors, {landmark.StairCount} stairs)");
			}
			if (landmark.Biome == BiomeKind.Ocean) {
				int waterBelowFoundation = 0;
				for (int x = landmark.Area.Left; x < landmark.Area.Right; x++) {
					for (int y = landmark.AnchorY + 4; y <= Math.Min(landmark.Area.Bottom + 5, landmark.AnchorY + 14); y++) {
						waterBelowFoundation += Main.tile[x, y].LiquidAmount > 0 ? 1 : 0;
					}
				}
				int maximumCoastalWater = landmark.Area.Width * 3;
				if (landmark.Area.Height > 82 || landmark.AnchorY < Main.worldSurface * 0.55d
					|| waterBelowFoundation > maximumCoastalWater) {
					errors.Add(
						$"Ocean landmark at x={landmark.AnchorX} is not grounded on a stable coastal shelf "
						+ $"(height {landmark.Area.Height}, y={landmark.AnchorY}, "
						+ $"water below={waterBelowFoundation}/{maximumCoastalWater})");
				}
			}
			int doorTiles = CountTiles(authoredArea, TileID.ClosedDoor);
			if (doorTiles > 0) {
				errors.Add($"{landmark.Biome} landmark retained {doorTiles} closed-door tiles instead of open traversal arches");
			}
			int furnitureTiles = CountFurnitureTiles(landmark.Area);
			if (landmark.FurnitureCount < Math.Max(8, landmark.RoomCount) || furnitureTiles < 6) {
				errors.Add($"{landmark.Biome} landmark retained {landmark.FurnitureCount} placed objects across {furnitureTiles} furniture tiles");
			}
			HashSet<ushort> wallTypes = CollectWallTypes(landmark.Area);
			if (wallTypes.Count < 2 || CountWalls(landmark.Area, WallID.Glass) < 12) {
				errors.Add($"{landmark.Biome} landmark has a flat interior palette ({wallTypes.Count} wall types, {CountWalls(landmark.Area, WallID.Glass)} glass wall cells)");
			}
			(int horizontalWallSeam, int verticalWallSeam, Point horizontalWallStart, Point verticalWallStart) =
				MeasureWallMaterialSeams(landmark.Area, (x, y) => {
					ushort wall = Main.tile[x, y].WallType;
					return !TileEditor.IsSolid(x, y) && wall is not (WallID.None or WallID.Glass);
				});
			if (horizontalWallSeam > 18 || verticalWallSeam > 18) {
				errors.Add(
					$"{landmark.Biome} landmark retained axis-aligned interior-wall seams "
					+ $"({horizontalWallSeam} horizontal near {horizontalWallStart}, "
					+ $"{verticalWallSeam} vertical near {verticalWallStart})");
			}
			int furnitureFamilies = CountFurnitureFamilies(landmark.Area);
			if (furnitureFamilies < 3) {
				errors.Add($"{landmark.Biome} landmark retained only {furnitureFamilies} furniture families");
			}
			if (!HasOpenLandmarkEntrances(landmark, out string entranceReason)) {
				errors.Add($"{landmark.Biome} landmark has blocked traversal: {entranceReason}");
			}
			if (!LandmarkGenerator.HasConnectedRoomGraph(landmark, out string graphReason)) {
				errors.Add($"{landmark.Biome} landmark has a disconnected room graph: {graphReason}");
			}
			if (!LandmarkGenerator.HasCharacteristicMaterials(landmark)) {
				errors.Add($"{landmark.Biome} landmark lost its biome-characteristic shell materials");
			}
			if (!LandmarkGenerator.HasBuriedIglooRooms(landmark)) {
				errors.Add("Snow buried igloo did not retain its descending room complex");
			}
			if (TryFindValidHousing(authoredArea, out Point validHousingProbe)) {
				errors.Add($"{landmark.Biome} landmark is valid NPC housing near {validHousingProbe.X},{validHousingProbe.Y}");
			}
			int thickFloorColumns = 0;
			int foundationLeft = landmark.Area.Left + 6;
			int foundationRight = landmark.Area.Right - 7;
			bool buriedIgloo = landmark.Archetype == LandmarkArchetype.SnowBuriedIgloo;
			for (int x = foundationLeft; x <= foundationRight; x++) {
				if (TileEditor.IsSolid(x, landmark.AnchorY)
					&& (buriedIgloo || TileEditor.IsSolid(x, landmark.AnchorY + 1))) {
					thickFloorColumns++;
				}
			}
			int foundationWidth = foundationRight - foundationLeft + 1;
			if (thickFloorColumns < foundationWidth * 4 / 5) {
				errors.Add($"{landmark.Biome} landmark has a thin or broken foundation in {foundationWidth - thickFloorColumns} core columns");
			}
			if (landmark.RoomCount >= 5) {
				int platforms = CountTiles(landmark.Area, TileID.Platforms);
				int minimumPlatforms = landmark.StairCount * 6;
				int maximumPlatforms = landmark.StairCount * 20 + 20;
				if (platforms < minimumPlatforms || platforms > maximumPlatforms) {
					errors.Add($"{landmark.Biome} landmark has {platforms} platform tiles; expected bounded stairs and drop portals");
				}
				int slopedPlatforms = CountSlopedTiles(landmark.Area, TileID.Platforms);
				if (slopedPlatforms < landmark.StairCount * 6) {
					errors.Add($"{landmark.Biome} landmark retained only {slopedPlatforms} sloped stair platforms");
				}
			}
			int slopedShellTiles = CountSlopedSolidTiles(landmark.Area);
			if (slopedShellTiles < 4) {
				errors.Add($"{landmark.Biome} landmark retained only {slopedShellTiles} sloped roof tiles");
			}
			if (!LandmarkGenerator.HasCorrectRoofSlopes(landmark)) {
				errors.Add($"{landmark.Biome} landmark has missing or incorrectly oriented roof slopes");
			}
			if (!LandmarkGenerator.HasCorrectStairSlopes(landmark, out string stairReason)) {
				errors.Add($"{landmark.Biome} landmark has missing or incorrectly oriented stair slopes: {stairReason}");
			}
			if (!LandmarkGenerator.HasThickUpperPosts(landmark)) {
				errors.Add($"{landmark.Biome} landmark retained one-tile upper-room posts");
			}
			if (landmark.Biome == BiomeKind.Forest
				&& CountTiles(landmark.Area, TileID.WoodBlock) < landmark.Area.Width * 2) {
				errors.Add("Forest landmark did not retain its ordinary Wood Block shell");
			}
			int wallLeaks = CountWallsAboveRoof(landmark.Area, landmark.AnchorY);
			if (wallLeaks > 2) {
				errors.Add($"{landmark.Biome} landmark has {wallLeaks} background-wall cells exposed above its roof");
			}
		}
	}

	private static void ValidateForestLakeBridges(GenerationManifest manifest, List<string> errors)
	{
		foreach (ForestLakeBridgeRecord bridge in manifest.ForestLakeBridges) {
			int actualWater = CountLiquid(bridge.Area, LiquidID.Water);
			int minimumWater = Math.Max(240, bridge.Area.Width * 2);
			if (actualWater < minimumWater || actualWater != bridge.WaterCells) {
				errors.Add(
					$"{bridge.Style} forest lake retained {actualWater} water cells "
					+ $"(recorded {bridge.WaterCells}, expected at least {minimumWater})");
			}

			int deckColumns = 0;
			int clearColumns = 0;
			for (int x = bridge.Area.Left + 2; x < bridge.Area.Right - 2; x++) {
				Tile deck = Main.tile[x, bridge.DeckY];
				bool walkable = deck.HasTile && deck.TileType is TileID.WoodBlock or TileID.LivingWood
					or TileID.GrayBrick or TileID.Platforms;
				if (!walkable) {
					continue;
				}
				deckColumns++;
				bool clear = true;
				for (int y = bridge.DeckY - 5; y < bridge.DeckY; y++) {
					clear &= !TileEditor.IsSolid(x, y);
				}
				clearColumns += clear ? 1 : 0;
			}
			if (deckColumns < bridge.Area.Width * 4 / 5 || clearColumns < deckColumns * 9 / 10) {
				errors.Add(
					$"{bridge.Style} forest bridge is not continuously traversable: "
					+ $"deck={deckColumns}/{bridge.Area.Width}, clear={clearColumns}/{deckColumns}");
			}
			int supports = CountTiles(bridge.Area, TileID.WoodenBeam);
			if (supports < 24 || bridge.SupportTiles < 24) {
				errors.Add($"{bridge.Style} forest bridge retained only {supports} beam tiles ({bridge.SupportTiles} recorded)");
			}

			List<int> bed = [];
			for (int x = bridge.Area.Left + 8; x < bridge.Area.Right - 8; x++) {
				int bottom = int.MinValue;
				for (int y = bridge.WaterlineY; y < bridge.Area.Bottom - 2; y++) {
					Tile tile = Main.tile[x, y];
					if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Water) {
						bottom = y;
					}
				}
				if (bottom != int.MinValue) {
					bed.Add(bottom);
				}
			}
			int bedSpan = bed.Count == 0 ? 0 : bed.Max() - bed.Min();
			int directionChanges = CountDirectionChanges(bed);
			if (bed.Count < bridge.Area.Width / 2 || bedSpan < 7 || directionChanges < 5) {
				errors.Add(
					$"{bridge.Style} forest lake lacks an organic basin: "
					+ $"wetColumns={bed.Count}, bedSpan={bedSpan}, turns={directionChanges}");
			}

			// A blended bank is deliberately interleaved rather than a hard material
			// cutoff. Judge the four-column approach as a group so one jittered seam
			// cell cannot reject an otherwise continuous Forest shoreline.
			foreach ((int startX, string side) in new[] {
				(bridge.Area.Left - 2, "left"),
				(bridge.Area.Right - 2, "right")
			}) {
				int forestSupports = 0;
				for (int x = startX; x < startX + 4; x++) {
					forestSupports += BiomeClassifier.TryFindGroundSupport(x, out int supportY)
						&& Main.tile[x, supportY].TileType is TileID.Grass or TileID.GolfGrass or TileID.Dirt
						? 1
						: 0;
				}
				if (forestSupports < 2) {
					errors.Add($"{bridge.Style} forest lake retained only {forestSupports}/4 Forest support columns on its {side} approach");
				}
			}
		}
	}

	private static int CountDirectionChanges(IReadOnlyList<int> values)
	{
		int changes = 0;
		int previousDirection = 0;
		for (int index = 1; index < values.Count; index++) {
			int direction = Math.Sign(values[index] - values[index - 1]);
			if (direction == 0) {
				continue;
			}
			changes += previousDirection != 0 && direction != previousDirection ? 1 : 0;
			previousDirection = direction;
		}
		return changes;
	}

	private static void ValidateBiomeTransitions(WorldPlan plan, GenerationManifest manifest, List<string> errors)
	{
		int minimumTransitions = Main.maxTilesX <= 4200 ? 2 : 3;
		if (manifest.BiomeTransitions.Count < minimumTransitions) {
			errors.Add($"only {manifest.BiomeTransitions.Count} irregular surface transitions were recorded; expected at least {minimumTransitions}");
		}

		foreach (BiomeTransitionRecord transition in manifest.BiomeTransitions) {
			if (transition.ModifiedCells < transition.Area.Width * 3) {
				errors.Add($"{transition.LeftBiome}-{transition.RightBiome} transition at x={transition.Area.Center.X} changed only {transition.ModifiedCells} cells");
			}
			if (!BiomeTransitionGenerator.TryMeasureBoundary(
				plan,
				manifest,
				transition,
				out int observedCrossings,
				out int crossingSpan,
				out int directionChanges,
				out string samples)) {
				errors.Add(
					$"{transition.LeftBiome}-{transition.RightBiome} transition at x={transition.Area.Center.X} "
					+ $"still follows a near-straight or unobservable material boundary; "
					+ $"observed={observedCrossings}, span={crossingSpan}, direction changes={directionChanges}, "
					+ $"sampled crossings=[{samples}]");
			}
			(int horizontalWallSeam, int verticalWallSeam, Point horizontalWallStart, Point verticalWallStart) =
				MeasureWallMaterialSeams(transition.Area, (x, y) => {
					ushort wall = Main.tile[x, y].WallType;
					return !BiomeTransitionGenerator.IsFeatureOwned(manifest, x, y)
						&& wall is WallID.DirtUnsafe or WallID.Stone or WallID.SnowWallUnsafe
							or WallID.JungleUnsafe or WallID.Sandstone or WallID.EbonstoneUnsafe or WallID.CrimstoneUnsafe;
				});
			if (horizontalWallSeam > 26 || verticalWallSeam > 26) {
				errors.Add(
					$"{transition.LeftBiome}-{transition.RightBiome} transition retained axis-aligned wall seams "
					+ $"({horizontalWallSeam} horizontal near {horizontalWallStart}, "
					+ $"{verticalWallSeam} vertical near {verticalWallStart})");
			}
		}
	}

	private static void ValidateBridgesAndValleys(WorldPlan plan, GenerationManifest manifest, List<string> errors)
	{
		if (manifest.Bridges.Count != plan.Mountains.Count) {
			errors.Add($"only {manifest.Bridges.Count} bridges were recorded for {plan.Mountains.Count} mountain ranges");
		}
		if (manifest.Valleys.Count != plan.Mountains.Count) {
			errors.Add($"only {manifest.Valleys.Count} valley payloads were recorded for {plan.Mountains.Count} mountain ranges");
		}

		foreach (BridgeRecord bridge in manifest.Bridges) {
			int survivingColumns = 0;
			int thickColumns = 0;
			int clearColumns = 0;
			int platformPortals = 0;
			for (int x = bridge.Area.Left; x < bridge.Area.Right; x++) {
				int deckY = int.MaxValue;
				for (int y = bridge.Area.Top; y < bridge.Area.Bottom; y++) {
					Tile tile = Main.tile[x, y];
					if (tile.HasTile
						&& tile.TileType is TileID.Platforms or TileID.GrayBrick or TileID.LivingWood or TileID.MinecartTrack
						&& !TileEditor.IsSolid(x, y - 1)
						&& !TileEditor.IsSolid(x, y - 2)
						&& !TileEditor.IsSolid(x, y - 3)) {
						deckY = y;
					}
				}
				if (deckY != int.MaxValue) {
					survivingColumns++;
					Tile deck = Main.tile[x, deckY];
					if (deck.TileType == TileID.Platforms) {
						platformPortals++;
					}
					else if (TileEditor.IsSolid(x, deckY + 1) && TileEditor.IsSolid(x, deckY + 2)) {
						thickColumns++;
					}
					if (!TileEditor.IsSolid(x, deckY - 1) && !TileEditor.IsSolid(x, deckY - 2) && !TileEditor.IsSolid(x, deckY - 3)) {
						clearColumns++;
					}
				}
			}
			int minimumColumns = Math.Max(28, bridge.Area.Width * 3 / 5);
			if (survivingColumns < minimumColumns) {
				errors.Add($"{bridge.Style} bridge retained deck material in only {survivingColumns}/{bridge.Area.Width} columns");
			}
			if (thickColumns < bridge.Area.Width / 2) {
				errors.Add($"{bridge.Style} bridge has only {thickColumns}/{bridge.Area.Width} structurally thick deck columns");
			}
			if (clearColumns < survivingColumns * 9 / 10) {
				errors.Add($"{bridge.Style} bridge has blocked headroom in {survivingColumns - clearColumns} deck columns");
			}
			if (bridge.Style != BridgeStyle.StoneArch && platformPortals < 4) {
				errors.Add($"{bridge.Style} bridge has no intentional platform drop bays");
			}
			int actuators = CountActuators(bridge.Area);
			int backdrop = CountWallCells(bridge.Area);
			if (actuators < 16) {
				errors.Add($"{bridge.Style} bridge retained only {actuators} actuated portal blocks");
			}
			if (backdrop < bridge.Area.Width * 2) {
				errors.Add($"{bridge.Style} bridge retained only {backdrop} authored background-wall cells");
			}
		}

		for (int index = 0; index < Math.Min(plan.Mountains.Count, manifest.Bridges.Count); index++) {
			MountainRangePlan mountain = plan.Mountains[index];
			foreach (int endpointX in new[] {
				(mountain.LeftPeakX + mountain.SaddleX) / 2,
				(mountain.SaddleX + mountain.RightPeakX) / 2
			}) {
				int deckY = plan.SurfaceAt(endpointX) - 2;
				int blockers = 0;
				for (int x = endpointX - 7; x <= endpointX + 7; x++) {
					for (int y = deckY - 6; y < deckY; y++) {
						blockers += TileEditor.IsSolid(x, y) ? 1 : 0;
					}
				}
				if (blockers > 0) {
					errors.Add($"{mountain.BridgeStyle} bridge endpoint at x={endpointX} retained {blockers} solid passage blockers");
				}
			}
			if (!MountainBiomeGenerator.HasConnectedBridgePortals(plan, mountain, out string bridgeConnectionFailure)) {
				errors.Add($"{mountain.BridgeStyle} bridge {bridgeConnectionFailure}");
			}
		}

		foreach (ValleyRecord valley in manifest.Valleys) {
			int water = CountLiquid(valley.Area, LiquidID.Water);
			int lava = CountLiquid(valley.Area, LiquidID.Lava);
			if (valley.Theme == ValleyTheme.Lake && water < 80) {
				errors.Add($"lake valley at x={valley.Area.Center.X} retained only {water} water cells");
			}
			if (valley.Theme == ValleyTheme.Lava && lava < 50) {
				errors.Add($"lava valley at x={valley.Area.Center.X} retained only {lava} lava cells");
			}
			if (valley.Theme == ValleyTheme.SealedEvil && CountTiles(valley.Area, TileID.GrayBrick) < 60) {
				errors.Add($"sealed evil valley at x={valley.Area.Center.X} lost its quarantine shell");
			}
		}
	}

	private static void ValidateSkyHighlands(WorldPlan plan, GenerationManifest manifest, List<string> errors)
	{
		if (manifest.SkyHighlands.Count != plan.SkyHighlands.Count) {
			errors.Add($"only {manifest.SkyHighlands.Count} of {plan.SkyHighlands.Count} floating highlands were recorded");
		}

		for (int index = 0; index < manifest.SkyHighlands.Count; index++) {
			SkyHighlandRecord highland = manifest.SkyHighlands[index];
			int minimumWidth = index < plan.SkyHighlands.Count ? plan.SkyHighlands[index].Width : 260;
			if (highland.Area.Width < minimumWidth) {
				errors.Add($"floating highland {index} spans only {highland.Area.Width} tiles; expected at least {minimumWidth}");
			}
			int mass = CountTiles(highland.Area, TileID.Cloud)
				+ CountTiles(highland.Area, TileID.RainCloud)
				+ CountTiles(highland.Area, TileID.Sunplate)
				+ CountTiles(highland.Area, TileID.Stone)
				+ CountTiles(highland.Area, TileID.Dirt);
			if (mass < minimumWidth * 12) {
				errors.Add($"floating highland {index} retained only {mass} terrain tiles");
			}
			int authoredStone = SkyHighlandGenerator.CountStoneInAuthoredBody(plan, index);
			if (authoredStone > minimumWidth / 2) {
				errors.Add($"floating highland {index} retained {authoredStone} stone tiles inside its authored sky body");
			}
			(int componentMass, int componentWidth, int componentHeight) = MeasureLargestHighlandComponent(highland.Area);
			int plannedDepth = index < plan.SkyHighlands.Count ? plan.SkyHighlands[index].Depth : highland.Area.Height - 62;
			if (componentWidth < minimumWidth * 3 / 5 || componentHeight < plannedDepth * 2 / 3) {
				errors.Add(
					$"floating highland {index} has no biome-scale connected body: largest component "
					+ $"is {componentWidth}x{componentHeight} ({componentMass} tiles)");
			}
			if (highland.InteriorRouteTiles < 350) {
				errors.Add($"floating highland {index} recorded only {highland.InteriorRouteTiles} carved route cells");
			}
			bool requiresLake = index < plan.SkyHighlands.Count && plan.SkyHighlands[index].HasLake;
			if (requiresLake && CountLiquid(highland.Area, LiquidID.Water) < 80) {
				errors.Add($"floating highland {index} lost its sealed sky lake");
			}
			int platformTiles = CountTiles(highland.Area, TileID.Platforms);
			if (platformTiles < 50) {
				errors.Add($"floating highland {index} retained only {platformTiles} platform cells in its vertical drop routes");
			}
			int thinSupports = CountThinWalkableSupports(highland.Area);
			if (thinSupports > minimumWidth / 5) {
				errors.Add($"floating highland {index} retained {thinSupports} unsupported one-tile walking cells");
			}
			if (index < plan.SkyHighlands.Count) {
				SkyHighlandPlan planned = plan.SkyHighlands[index];
				Rectangle materialBand = new(
					highland.Area.Left,
					Math.Max(highland.Area.Top, planned.SurfaceY - 8),
					highland.Area.Width,
					Math.Min(38, highland.Area.Bottom - Math.Max(highland.Area.Top, planned.SurfaceY - 8)));
				(int horizontalMaterialSeam, int verticalMaterialSeam, Point horizontalStart, Point verticalStart) =
					MeasureSolidMaterialSeams(materialBand, (x, y) => SkyLayerMaterialAt(manifest, x, y));
				if (horizontalMaterialSeam > 26 || verticalMaterialSeam > 26) {
					errors.Add(
						$"floating highland {index} retained axis-aligned terrain-layer seams "
						+ $"({horizontalMaterialSeam} horizontal near {horizontalStart}, "
						+ $"{verticalMaterialSeam} vertical near {verticalStart})");
				}
			}
		}
		if (manifest.SkyHighlands.Count > 1 && manifest.SkyHighlands.All(highland => highland.MountainAttached)) {
			errors.Add("every floating highland is attached to a mountain");
		}
		if (manifest.SkyHighlands.Count(highland => highland.MountainAttached) > 1) {
			errors.Add("more than one floating highland is attached to a mountain");
		}
		if (manifest.SkyHighlands.Count > 1 && manifest.SkyHighlands.Select(highland => highland.Style).Distinct().Count() < 2) {
			errors.Add("all floating highlands use the same district style");
		}
	}

	private static int ValidateSurfaceMine(SurfaceMinePlan plan, GenerationManifest manifest, List<string> errors)
	{
		ValidateMinePlanTopology(plan, errors);
		if (GenVars.tRight > GenVars.tLeft && GenVars.tBottom > GenVars.tTop) {
			Rectangle templeClearance = new(
				GenVars.tLeft - 28,
				GenVars.tTop - 28,
				GenVars.tRight - GenVars.tLeft + 57,
				GenVars.tBottom - GenVars.tTop + 57);
			if (plan.Sections.Any(section => section.Area.Intersects(templeClearance))
				|| plan.Routes.SelectMany(SurfaceMineGenerator.RasterizeCenterline).Any(templeClearance.Contains)) {
				errors.Add(
					$"surface mine enters the Jungle Temple clearance envelope "
					+ $"({GenVars.tLeft},{GenVars.tTop})-({GenVars.tRight},{GenVars.tBottom})");
			}
		}
		if (manifest.SurfaceMine is not SurfaceMineRecord mine) {
			errors.Add("the guaranteed surface mine has no final manifest record");
			return 0;
		}

		MineSectionKind[] requiredKinds = [
			MineSectionKind.Workyard,
			MineSectionKind.Working,
			MineSectionKind.Collapsed,
			MineSectionKind.Flooded,
			MineSectionKind.SealedEvil,
			MineSectionKind.MountainRail
		];
		foreach (MineSectionKind kind in requiredKinds) {
			if (!manifest.MineSections.Any(section => section.Kind == kind)) {
				errors.Add($"the guaranteed surface mine has no {kind} section");
			}
		}
		foreach (MineSection section in manifest.MineSections) {
			if (section.Area.Width < 68 || section.Area.Height < 34) {
				errors.Add($"mine section {section.Id} ({section.Kind}) is only {section.Area.Width}x{section.Area.Height}");
			}
			int openWallCells = CountOpenWallCells(section.Area);
			if (openWallCells < section.Area.Width * 5) {
				errors.Add($"mine section {section.Id} ({section.Kind}) retained only {openWallCells} open background cells");
			}
			if (section.Kind is MineSectionKind.Working or MineSectionKind.MountainRail
				&& CountFurnitureFamilies(section.Area) < 2) {
				errors.Add($"mine section {section.Id} ({section.Kind}) has no distinct work destination");
			}
			if (TryFindValidHousing(section.Area, out Point mineHousingProbe)) {
				errors.Add($"mine section {section.Id} ({section.Kind}) forms valid NPC housing near {mineHousingProbe.X},{mineHousingProbe.Y}");
			}
			ValidateMineSectionWalls(plan, section, errors);
		}

		int actualTracks = CountTiles(mine.Area, TileID.MinecartTrack);
		int minimumConnected = Main.maxTilesX switch { <= 4200 => 300, <= 6400 => 500, _ => 700 };
		HashSet<Point> entranceComponent = CollectConnectedTracks(plan, mine.Area);
		int connectedTracks = entranceComponent.Count;
		if (connectedTracks < minimumConnected) {
			errors.Add($"surface mine entrance reaches only {connectedTracks} rail tiles; expected {minimumConnected}");
		}
		if (actualTracks < minimumConnected || mine.TrackTiles < minimumConnected) {
			errors.Add($"surface mine retained only {actualTracks} track tiles (manifest {mine.TrackTiles})");
		}
		int requiredAuthoredConnection = mine.TrackTiles * 9 / 10;
		if (connectedTracks < requiredAuthoredConnection) {
			errors.Add(
				$"surface mine rail graph is fragmented: the entrance component reaches {connectedTracks} "
				+ $"of {mine.TrackTiles} authored track tiles; expected at least {requiredAuthoredConnection}");
		}
		if (mine.SupportTiles < minimumConnected / 12) {
			errors.Add($"surface mine retained only {mine.SupportTiles} Wooden Beam support tiles");
		}
		if (mine.FurnitureCount < 12) {
			errors.Add($"surface mine retained only {mine.FurnitureCount} work-district furnishings");
		}
		if (mine.ConnectedRouteCount < mine.RequiredRouteCount) {
			errors.Add($"surface mine retained {mine.ConnectedRouteCount}/{mine.RequiredRouteCount} required rail edges");
		}
		if (mine.Entrance.Y > Main.worldSurface + 20 || !HasTile(plan.Entrance.X, plan.Entrance.Y, TileID.MinecartTrack)) {
			errors.Add("surface mine has no visible rail entrance in the surface band");
		}
		int openMouthColumns = 0;
		for (int x = mine.Entrance.X - 8; x <= mine.Entrance.X + 8; x++) {
			bool clear = true;
			for (int y = mine.Entrance.Y - 6; y < mine.Entrance.Y; y++) {
				clear &= !TileEditor.IsSolid(x, y);
			}
			if (clear) {
				openMouthColumns++;
			}
		}
		if (openMouthColumns < 12) {
			errors.Add($"surface mine entrance exposes only {openMouthColumns}/17 clear approach columns");
		}
		for (int routeIndex = 0; routeIndex < plan.Routes.Count; routeIndex++) {
			MineRoute route = plan.Routes[routeIndex];
			if (!route.HasTrack) {
				continue;
			}
			Point? failure = null;
			string reason = "";
			foreach (Point point in SurfaceMineGenerator.Rasterize(route)) {
				if (!HasTile(point.X, point.Y, TileID.MinecartTrack)) {
					failure = point;
					reason = "missing";
					break;
				}
				if (!entranceComponent.Contains(point)) {
					failure = point;
					reason = "disconnected";
					break;
				}
				for (int offsetX = -1; offsetX <= 1 && failure is null; offsetX++) {
					for (int offsetY = -6; offsetY < 0; offsetY++) {
						if (TileEditor.IsSolid(point.X + offsetX, point.Y + offsetY)
							&& !HasTile(point.X + offsetX, point.Y + offsetY, TileID.MinecartTrack)) {
							Tile blocker = Main.tile[point.X + offsetX, point.Y + offsetY];
							failure = point;
							reason = $"blocked-clearance ({offsetX},{offsetY}) type={blocker.TileType} "
								+ $"frame={blocker.TileFrameX},{blocker.TileFrameY} actuated={blocker.IsActuated}";
							break;
						}
					}
				}
			}
			if (failure is Point failedPoint) {
				errors.Add($"mine rail edge {routeIndex} has a {reason} authored cell at {failedPoint}");
			}
		}
		HashSet<Point> authoredTrack = plan.Routes
			.Where(route => route.HasTrack)
			.SelectMany(SurfaceMineGenerator.Rasterize)
			.ToHashSet();
		int minimumCeiling = int.MaxValue;
		int maximumCeiling = 0;
		int cavernousTrackCells = 0;
		foreach (Point point in authoredTrack) {
			int clearHeight = 0;
			for (int depth = 1; depth <= 18 && !TileEditor.IsSolid(point.X, point.Y - depth); depth++) {
				clearHeight++;
			}
			minimumCeiling = Math.Min(minimumCeiling, clearHeight);
			maximumCeiling = Math.Max(maximumCeiling, clearHeight);
			cavernousTrackCells += clearHeight >= 9 ? 1 : 0;
		}
		if (maximumCeiling - minimumCeiling < 3) {
			errors.Add($"surface mine ceiling varies by only {maximumCeiling - minimumCeiling} tiles");
		}
		if (cavernousTrackCells < authoredTrack.Count / 10) {
			errors.Add($"surface mine has cavernous headroom over only {cavernousTrackCells}/{authoredTrack.Count} rail cells");
		}
		ValidateMineRouteWalls(plan, errors);
		ValidateMineJumpTransfers(plan, errors);
		int timberBents = CountMineTimberBents(plan);
		if (timberBents < plan.Routes.Count * 2) {
			errors.Add($"surface mine retained only {timberBents} complete timber support bents");
		}

		MineSection? flooded = manifest.MineSections
			.Where(section => section.Kind == MineSectionKind.Flooded)
			.Select<MineSection, MineSection?>(section => section)
			.FirstOrDefault();
		if (flooded is MineSection wet && CountLiquid(wet.Area, LiquidID.Water) < 80) {
			errors.Add("the mine's flooded spur lost its water district");
		}
		MineSection? evil = manifest.MineSections
			.Where(section => section.Kind == MineSectionKind.SealedEvil)
			.Select<MineSection, MineSection?>(section => section)
			.FirstOrDefault();
		if (evil is MineSection quarantine && CountTiles(quarantine.Area, TileID.GrayBrick) < 250) {
			errors.Add("the mine's evil annex lost its five-tile quarantine shell");
		}
		if (evil is MineSection irregularQuarantine) {
			HashSet<int> shellTops = [];
			for (int x = irregularQuarantine.Area.Left; x < irregularQuarantine.Area.Right; x++) {
				for (int y = irregularQuarantine.Area.Top; y < irregularQuarantine.Area.Bottom; y++) {
					if (HasTile(x, y, TileID.GrayBrick)) {
						shellTops.Add(y);
						break;
					}
				}
			}
			if (shellTops.Count < 7) {
				errors.Add($"the mine's evil annex still has a rectangular shell profile ({shellTops.Count} distinct top rows)");
			}
			int gateActuators = CountActuators(irregularQuarantine.Area);
			if (gateActuators < 12) {
				errors.Add($"the mine's evil annex retained only {gateActuators} actuators for its passable quarantine gates");
			}
			if (!SurfaceMineGenerator.HasFourTileQuarantine(plan, irregularQuarantine, out string quarantineReason)) {
				errors.Add($"the mine's evil annex lost its four-tile hardmode quarantine: {quarantineReason}");
			}
		}

		return connectedTracks;
	}

	private static (int Mass, int Width, int Height) MeasureLargestHighlandComponent(Rectangle area)
	{
		int left = Math.Max(2, area.Left);
		int top = Math.Max(2, area.Top);
		int right = Math.Min(Main.maxTilesX - 2, area.Right);
		int bottom = Math.Min(Main.maxTilesY - 2, area.Bottom);
		int width = right - left;
		int height = bottom - top;
		bool[] visited = new bool[width * height];
		(int Mass, int Width, int Height) largest = default;
		Queue<Point> queue = new();

		for (int y = top; y < bottom; y++) {
			for (int x = left; x < right; x++) {
				int localIndex = x - left + (y - top) * width;
				if (visited[localIndex] || !IsHighlandMass(x, y)) {
					continue;
				}

				visited[localIndex] = true;
				queue.Enqueue(new Point(x, y));
				int mass = 0;
				int minimumX = x;
				int maximumX = x;
				int minimumY = y;
				int maximumY = y;
				while (queue.Count > 0) {
					Point current = queue.Dequeue();
					mass++;
					minimumX = Math.Min(minimumX, current.X);
					maximumX = Math.Max(maximumX, current.X);
					minimumY = Math.Min(minimumY, current.Y);
					maximumY = Math.Max(maximumY, current.Y);
					ReadOnlySpan<Point> directions = [
						new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
						new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
					];
					foreach (Point direction in directions) {
						Point next = current + direction;
						if (next.X < left || next.X >= right || next.Y < top || next.Y >= bottom) {
							continue;
						}
						int nextIndex = next.X - left + (next.Y - top) * width;
						if (visited[nextIndex] || !IsHighlandMass(next.X, next.Y)) {
							continue;
						}
						visited[nextIndex] = true;
						queue.Enqueue(next);
					}
				}

				(int Mass, int Width, int Height) component =
					(mass, maximumX - minimumX + 1, maximumY - minimumY + 1);
				if (component.Mass > largest.Mass) {
					largest = component;
				}
			}
		}

		return largest;
	}

	private static bool HasOpenLandmarkEntrances(LandmarkRecord landmark, out string reason)
	{
		int leftColumn = landmark.Area.Left + 6;
		int rightColumn = landmark.Area.Right - 7;
		int leftOpen = CountOpenEntryColumns(leftColumn, landmark.AnchorY);
		int rightOpen = CountOpenEntryColumns(rightColumn, landmark.AnchorY);
		if (leftOpen < 5 || rightOpen < 5) {
			reason = $"left arch has {leftOpen}/7 clear columns and right arch has {rightOpen}/7";
			return false;
		}

		int clearInteriorColumns = 0;
		int interiorColumns = 0;
		for (int x = leftColumn + 5; x <= rightColumn - 5; x++) {
			interiorColumns++;
			bool clear = true;
			for (int y = landmark.AnchorY - 5; y < landmark.AnchorY; y++) {
				clear &= !TileEditor.IsSolid(x, y);
			}
			if (clear) {
				clearInteriorColumns++;
			}
		}
		if (clearInteriorColumns < interiorColumns / 3) {
			reason = $"only {clearInteriorColumns}/{interiorColumns} ground-floor columns have five-tile headroom";
			return false;
		}

		reason = string.Empty;
		return true;
	}

	private static int CountOpenEntryColumns(int centerX, int groundY)
	{
		int open = 0;
		for (int x = centerX - 3; x <= centerX + 3; x++) {
			bool clear = true;
			for (int y = groundY - 7; y < groundY; y++) {
				clear &= !TileEditor.IsSolid(x, y);
			}
			open += clear ? 1 : 0;
		}
		return open;
	}

	private static bool TryFindValidHousing(Rectangle area, out Point probe)
	{
		for (int x = area.Left + 4; x < area.Right - 4; x += 4) {
			for (int y = area.Top + 4; y < area.Bottom - 4; y += 3) {
				if (TileEditor.IsSolid(x, y) || !WorldGen.StartRoomCheck(x, y) || !WorldGen.RoomNeeds(NPCID.Guide)) {
					continue;
				}
				WorldGen.ScoreRoom(ignoreNPC: -1, npcTypeAskingToScoreRoom: NPCID.Guide);
				if (WorldGen.canSpawn && WorldGen.hiScore > 0) {
					probe = new Point(x, y);
					return true;
				}
			}
		}

		probe = default;
		return false;
	}

	private static int CountOpenWallCells(Rectangle area)
	{
		int count = 0;
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				Tile tile = Main.tile[x, y];
				if (!TileEditor.IsSolid(x, y) && tile.WallType != WallID.None) {
					count++;
				}
			}
		}
		return count;
	}

	private static void ValidateMineSectionWalls(SurfaceMinePlan plan, MineSection section, List<string> errors)
	{
		if (section.Kind == MineSectionKind.Workyard) {
			return;
		}

		int themed = 0;
		int sampled = 0;
		Rectangle sampleArea = section.Area;
		sampleArea.Inflate(-5, -5);
		HashSet<Point> routeInfluence = [];
		foreach (MineRoute route in plan.Routes) {
			foreach (Point point in route.Centerline) {
				for (int offsetX = -4; offsetX <= 4; offsetX++) {
					for (int offsetY = -15; offsetY <= 3; offsetY++) {
						Point influenced = new(point.X + offsetX, point.Y + offsetY);
						if (sampleArea.Contains(influenced)) {
							routeInfluence.Add(influenced);
						}
					}
				}
			}
		}
		for (int x = sampleArea.Left; x < sampleArea.Right; x++) {
			for (int y = sampleArea.Top; y < sampleArea.Bottom; y++) {
				if (!SurfaceMineGenerator.IsInsideSectionInterior(section, x, y)
					|| TileEditor.IsSolid(x, y) || routeInfluence.Contains(new Point(x, y))) {
					continue;
				}
				ushort wall = Main.tile[x, y].WallType;
				if (wall == WallID.None) {
					continue;
				}
				sampled++;
				themed += SurfaceMineGenerator.IsBiomeWall(section.Theme, wall)
					|| section.Kind == MineSectionKind.SealedEvil && wall == WallID.GrayBrick
					? 1
					: 0;
			}
		}
		if (sampled < section.Area.Width * 3 || themed < sampled * 9 / 10) {
			errors.Add(
				$"mine section {section.Id} ({section.Theme}) uses its biome wall family in only "
				+ $"{themed}/{sampled} open wall cells");
		}
		(int horizontalWallSeam, int verticalWallSeam, Point horizontalStart, Point verticalStart) =
			MeasureWallMaterialSeams(sampleArea, (x, y) => {
				ushort wall = Main.tile[x, y].WallType;
				return SurfaceMineGenerator.IsInsideSectionInterior(section, x, y)
					&& !TileEditor.IsSolid(x, y)
					&& !routeInfluence.Contains(new Point(x, y))
					&& wall is not (WallID.None or WallID.GrayBrick);
			});
		if (horizontalWallSeam > 22 || verticalWallSeam > 22) {
			errors.Add(
				$"mine section {section.Id} ({section.Kind}) retained axis-aligned wall-material seams "
				+ $"({horizontalWallSeam} horizontal near {horizontalStart}, "
				+ $"{verticalWallSeam} vertical near {verticalStart})");
		}
	}

	private static void ValidateMineRouteWalls(SurfaceMinePlan plan, List<string> errors)
	{
		int compatible = 0;
		int sampled = 0;
		int missing = 0;
		foreach (MineRoute route in plan.Routes) {
			foreach (Point point in route.Centerline) {
				BiomeKind theme = plan.ThemeAt(point);
				for (int offsetX = -2; offsetX <= 2; offsetX++) {
					for (int offsetY = -6; offsetY <= 0; offsetY++) {
						int x = point.X + offsetX;
						int y = point.Y + offsetY;
						if (TileEditor.IsSolid(x, y)) {
							continue;
						}
						ushort wall = Main.tile[x, y].WallType;
						if (wall == WallID.GrayBrick) {
							continue;
						}
						sampled++;
						missing += wall == WallID.None ? 1 : 0;
						compatible += SurfaceMineGenerator.IsBiomeWall(theme, wall) ? 1 : 0;
					}
				}
			}
		}
		if (sampled == 0 || compatible < sampled * 19 / 20 || missing > sampled / 50) {
			errors.Add(
				$"mine rail walls match their local biome in {compatible}/{sampled} sampled cells "
				+ $"with {missing} missing walls");
		}
	}

	private static void ValidateMineJumpTransfers(SurfaceMinePlan plan, List<string> errors)
	{
		int transfers = 0;
		foreach (MineRoute route in plan.Routes) {
			if (SurfaceMineGenerator.GetJumpTransfer(route) is not MineRailJump jump) {
				continue;
			}
			transfers++;
			if (!HasTile(jump.Launch.X, jump.Launch.Y, TileID.MinecartTrack)
				|| !HasTile(jump.Landing.X, jump.Landing.Y, TileID.MinecartTrack)) {
				errors.Add($"mine jump {jump.Launch}->{jump.Landing} lost a launch or landing rail");
				continue;
			}
			if (jump.Gap.Width is < 4 or > 6) {
				errors.Add($"mine jump {jump.Launch}->{jump.Landing} has a {jump.Gap.Width}-tile gap");
			}
			Point previous = route.Centerline[Math.Max(0, route.JumpStartIndex - 1)];
			int landingDrop = jump.Landing.Y - jump.Launch.Y;
			if (previous.Y <= jump.Launch.Y || landingDrop is < 1 or > 3) {
				errors.Add(
					$"mine jump {jump.Launch}->{jump.Landing} lacks an upward launch and 1-3 tile landing drop "
					+ $"(previous y={previous.Y}, drop={landingDrop})");
			}

			for (int index = route.JumpStartIndex + 1;
				index <= route.JumpStartIndex + route.JumpGapLength;
				index++) {
				Point gap = route.Centerline[index];
				if (HasTile(gap.X, gap.Y, TileID.MinecartTrack)) {
					errors.Add($"mine jump at {gap} retained track inside its launch gap");
					break;
				}
				for (int offsetY = -4; offsetY <= 3; offsetY++) {
					if (TileEditor.IsSolid(gap.X, gap.Y + offsetY)) {
						errors.Add($"mine jump at {gap} has blocked flight clearance at y={gap.Y + offsetY}");
						index = route.JumpStartIndex + route.JumpGapLength + 1;
						break;
					}
				}
			}
		}
		if (transfers == 0) {
			errors.Add("surface mine has no launch-and-landing rail transfer");
		}
	}

	private static int CountMineTimberBents(SurfaceMinePlan plan)
	{
		int count = 0;
		foreach (MineRoute route in plan.Routes) {
			for (int index = 5; index < route.Centerline.Count - 5; index += 3) {
				Point point = route.Centerline[index];
				bool found = false;
				for (int beamY = point.Y - 10; beamY <= point.Y - 8 && !found; beamY++) {
					int crossbeam = 0;
					for (int x = point.X - 3; x <= point.X + 3; x++) {
						crossbeam += HasTile(x, beamY, TileID.WoodenBeam) ? 1 : 0;
					}
					int leftPost = 0;
					int rightPost = 0;
					for (int y = beamY + 1; y <= point.Y; y++) {
						leftPost += HasTile(point.X - 3, y, TileID.WoodenBeam) ? 1 : 0;
						rightPost += HasTile(point.X + 3, y, TileID.WoodenBeam) ? 1 : 0;
					}
					found = crossbeam >= 5 && Math.Max(leftPost, rightPost) >= 2;
				}
				if (found) {
					count++;
					index += 6;
				}
			}
		}
		return count;
	}

	private static bool IsHighlandMass(int x, int y)
	{
		Tile tile = Main.tile[x, y];
		if (tile.HasUnactuatedTile && tile.TileType == TileID.Platforms) {
			return true;
		}
		if (!tile.HasUnactuatedTile || !Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]) {
			return false;
		}
		return tile.TileType is TileID.Cloud or TileID.RainCloud or TileID.SnowCloud or TileID.Sunplate
			or TileID.Dirt or TileID.Stone or TileID.Grass or TileID.Mud or TileID.JungleGrass
			or TileID.SnowBlock or TileID.IceBlock or TileID.HardenedSand or TileID.Sandstone;
	}

	private static HashSet<Point> CollectConnectedTracks(SurfaceMinePlan plan, Rectangle bounds)
	{
		Point entrance = plan.Entrance;
		if (!HasTile(entrance.X, entrance.Y, TileID.MinecartTrack)) {
			return [];
		}
		Dictionary<Point, Point> jumpLinks = [];
		foreach (MineRoute route in plan.Routes) {
			if (SurfaceMineGenerator.GetJumpTransfer(route) is not MineRailJump jump
				|| !HasTile(jump.Launch.X, jump.Launch.Y, TileID.MinecartTrack)
				|| !HasTile(jump.Landing.X, jump.Landing.Y, TileID.MinecartTrack)) {
				continue;
			}
			jumpLinks[jump.Launch] = jump.Landing;
			jumpLinks[jump.Landing] = jump.Launch;
		}

		HashSet<Point> visited = [entrance];
		Queue<Point> queue = new();
		queue.Enqueue(entrance);
		while (queue.Count > 0) {
			Point current = queue.Dequeue();
			if (jumpLinks.TryGetValue(current, out Point transfer)
				&& bounds.Contains(transfer)
				&& visited.Add(transfer)) {
				queue.Enqueue(transfer);
			}
			for (int offsetX = -1; offsetX <= 1; offsetX++) {
				for (int offsetY = -1; offsetY <= 1; offsetY++) {
					if (offsetX == 0 && offsetY == 0) {
						continue;
					}
					Point next = new(current.X + offsetX, current.Y + offsetY);
					if (!bounds.Contains(next) || visited.Contains(next) || !HasTile(next.X, next.Y, TileID.MinecartTrack)) {
						continue;
					}
					visited.Add(next);
					queue.Enqueue(next);
				}
			}
		}
		return visited;
	}

	private static void ValidateMinePlanTopology(SurfaceMinePlan plan, List<string> errors)
	{
		Dictionary<Point, int> degree = [];
		HashSet<Point> vertices = [];
		int authoredEdges = 0;
		int horizontalEdges = 0;
		int bidirectionalEdges = 0;
		foreach (MineRoute route in plan.Routes.Where(route => route.HasTrack)) {
			authoredEdges++;
			vertices.Add(route.Start);
			vertices.Add(route.End);
			degree[route.Start] = degree.GetValueOrDefault(route.Start) + 1;
			degree[route.End] = degree.GetValueOrDefault(route.End) + 1;
			if (Math.Abs(route.End.Y - route.Start.Y) <= 10) {
				horizontalEdges++;
			}

			Point? previous = null;
			int previousGrade = 0;
			int gradeChanges = 0;
			bool rises = false;
			bool descends = false;
			foreach (Point point in SurfaceMineGenerator.RasterizeCenterline(route)) {
				if (previous is Point prior) {
					int grade = Math.Sign(point.Y - prior.Y);
					rises |= grade < 0;
					descends |= grade > 0;
					if (grade != previousGrade) {
						gradeChanges++;
						previousGrade = grade;
					}
				}
				previous = point;
			}
			bidirectionalEdges += rises && descends ? 1 : 0;
			if (route.Centerline.Count >= 90 && gradeChanges < 3) {
				errors.Add($"mine rail edge {route.Start}->{route.End} has only {gradeChanges} grade changes");
			}
			if (gradeChanges > 18) {
				errors.Add($"mine rail edge {route.Start}->{route.End} has {gradeChanges} grade changes and would render as chatter");
			}
		}

		int junctions = degree.Values.Count(value => value >= 3);
		int cycleRank = authoredEdges - vertices.Count + 1;
		if (authoredEdges < 12 || junctions < 3 || cycleRank < 2 || horizontalEdges < 4) {
			errors.Add(
				$"surface mine graph is too simple: edges={authoredEdges}, junctions={junctions}, "
				+ $"independentLoops={cycleRank}, horizontalEdges={horizontalEdges}");
		}
		if (bidirectionalEdges < 4) {
			errors.Add($"only {bidirectionalEdges} mine rail edges contain both a climb and a descent");
		}
		if (plan.Routes.Select(route => route.Profile).Distinct().Count() < 3) {
			errors.Add("surface mine rail routes use fewer than three grade profiles");
		}
	}

	private static HashSet<ushort> CollectWallTypes(Rectangle area)
	{
		HashSet<ushort> types = [];
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				ushort wall = Main.tile[x, y].WallType;
				if (wall != WallID.None) {
					types.Add(wall);
				}
			}
		}
		return types;
	}

	private static int CountWalls(Rectangle area, ushort wallType)
	{
		int count = 0;
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				count += Main.tile[x, y].WallType == wallType ? 1 : 0;
			}
		}
		return count;
	}

	private static int CountWallCells(Rectangle area)
	{
		int count = 0;
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				count += Main.tile[x, y].WallType != WallID.None ? 1 : 0;
			}
		}
		return count;
	}

	private static int CountActuators(Rectangle area)
	{
		int count = 0;
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				count += Main.tile[x, y].HasActuator ? 1 : 0;
			}
		}
		return count;
	}

	private static int CountThinWalkableSupports(Rectangle area)
	{
		int count = 0;
		for (int x = Math.Max(3, area.Left); x < Math.Min(Main.maxTilesX - 3, area.Right); x++) {
			for (int y = Math.Max(4, area.Top); y < Math.Min(Main.maxTilesY - 4, area.Bottom); y++) {
				Tile tile = Main.tile[x, y];
				if (!tile.HasUnactuatedTile || !Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]) {
					continue;
				}
				if (!TileEditor.IsSolid(x, y - 1) && !TileEditor.IsSolid(x, y - 2) && !TileEditor.IsSolid(x, y - 3)
					&& !TileEditor.IsSolid(x, y + 1) && !TileEditor.IsSolid(x, y + 2)) {
					count++;
				}
			}
		}
		return count;
	}

	private static int CountFurnitureFamilies(Rectangle area)
	{
		ushort[] types = [
			TileID.WorkBenches, TileID.Tables, TileID.Tables2, TileID.Chairs, TileID.Bookcases,
			TileID.Benches, TileID.Anvils, TileID.Chandeliers, TileID.SmallPiles
		];
		return types.Count(type => CountTiles(area, type) > 0);
	}

	private static int CountSummitSand(WorldPlan plan, MountainRangePlan mountain)
	{
		int count = 0;
		foreach (int peakX in new[] { mountain.LeftPeakX, mountain.RightPeakX }) {
			for (int x = peakX - 32; x <= peakX + 32; x++) {
				int surfaceY = plan.SurfaceAt(x);
				if (surfaceY > Main.worldSurface * 0.48d + 18d) {
					continue;
				}
				for (int y = surfaceY; y < surfaceY + 24; y++) {
					Tile tile = Main.tile[x, y];
					if (tile.HasTile && tile.TileType is TileID.Sand or TileID.HardenedSand or TileID.Sandstone
						or TileID.Ebonsand or TileID.Crimsand or TileID.CorruptHardenedSand
						or TileID.CrimsonHardenedSand or TileID.CorruptSandstone or TileID.CrimsonSandstone) {
						count++;
					}
				}
			}
		}
		return count;
	}

	private static int MountainLayerMaterialAt(GenerationManifest manifest, int x, int y)
	{
		if (BiomeTransitionGenerator.IsFeatureOwned(manifest, x, y)) {
			return 0;
		}
		Tile tile = Main.tile[x, y];
		if (!IsNaturalMountainTile(tile)) {
			return 0;
		}
		return tile.TileType switch {
			TileID.Grass or TileID.Dirt => 1,
			TileID.Stone => 2,
			TileID.SnowBlock => 3,
			TileID.IceBlock or TileID.BreakableIce => 4,
			TileID.Sand => 5,
			TileID.HardenedSand => 6,
			TileID.Sandstone or TileID.DesertFossil => 7,
			TileID.Mud or TileID.JungleGrass or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass => 8,
			TileID.CorruptGrass or TileID.Ebonstone or TileID.Ebonsand
				or TileID.CorruptHardenedSand or TileID.CorruptSandstone => 9,
			TileID.CrimsonGrass or TileID.Crimstone or TileID.Crimsand
				or TileID.CrimsonHardenedSand or TileID.CrimsonSandstone => 10,
			_ => 0
		};
	}

	private static string DescribeNearbyTransitions(GenerationManifest manifest, Point first, Point second)
	{
		List<string> nearby = [];
		foreach (BiomeTransitionRecord transition in manifest.BiomeTransitions) {
			Rectangle expanded = transition.Area;
			expanded.Inflate(4, 4);
			if (expanded.Contains(first) || expanded.Contains(second)
				|| Math.Abs(transition.Area.Center.X - first.X) < 90
				|| Math.Abs(transition.Area.Center.X - second.X) < 90) {
				nearby.Add($"{transition.LeftBiome}-{transition.RightBiome}:{transition.Area}");
			}
		}
		return nearby.Count == 0 ? "none" : string.Join(", ", nearby);
	}

	private static int SkyLayerMaterialAt(GenerationManifest manifest, int x, int y)
	{
		Point point = new(x, y);
		if (manifest.Landmarks.Any(record => record.Area.Contains(point))
			|| manifest.Bridges.Any(record => record.Area.Contains(point))
			|| manifest.ForestLakeBridges.Any(record => record.Area.Contains(point))
			|| manifest.Valleys.Any(record => record.Area.Contains(point))
			|| manifest.MountainWaters.Any(record => record.Area.Contains(point))
			|| manifest.MineSections.Any(record => record.Area.Contains(point))) {
			return 0;
		}
		Tile tile = Main.tile[x, y];
		if (!tile.HasUnactuatedTile || !Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]) {
			return 0;
		}
		return tile.TileType switch {
			TileID.Grass or TileID.Dirt => 1,
			TileID.Sunplate => 2,
			TileID.Cloud => 3,
			TileID.RainCloud or TileID.SnowCloud => 4,
			_ => 0
		};
	}

	private static (int Horizontal, int Vertical, Point HorizontalStart, Point VerticalStart) MeasureSolidMaterialSeams(
		Rectangle area,
		Func<int, int, int> classify)
	{
		int left = Math.Max(2, area.Left);
		int top = Math.Max(2, area.Top);
		int right = Math.Min(Main.maxTilesX - 2, area.Right);
		int bottom = Math.Min(Main.maxTilesY - 2, area.Bottom);
		int longestHorizontal = 0;
		Point horizontalStart = Point.Zero;
		for (int y = top; y < bottom - 1; y++) {
			int run = 0;
			for (int x = left; x < right; x++) {
				int upper = classify(x, y);
				int lower = classify(x, y + 1);
				bool boundary = upper != 0 && lower != 0 && upper != lower;
				run = boundary ? run + 1 : 0;
				if (run > longestHorizontal) {
					longestHorizontal = run;
					horizontalStart = new Point(x - run + 1, y);
				}
			}
		}

		int longestVertical = 0;
		Point verticalStart = Point.Zero;
		for (int x = left; x < right - 1; x++) {
			int run = 0;
			for (int y = top; y < bottom; y++) {
				int leftMaterial = classify(x, y);
				int rightMaterial = classify(x + 1, y);
				bool boundary = leftMaterial != 0 && rightMaterial != 0 && leftMaterial != rightMaterial;
				run = boundary ? run + 1 : 0;
				if (run > longestVertical) {
					longestVertical = run;
					verticalStart = new Point(x, y - run + 1);
				}
			}
		}
		return (longestHorizontal, longestVertical, horizontalStart, verticalStart);
	}

	private static (int Horizontal, int Vertical, Point HorizontalStart, Point VerticalStart) MeasureWallMaterialSeams(
		Rectangle area,
		Func<int, int, bool> include)
	{
		int left = Math.Max(2, area.Left);
		int top = Math.Max(2, area.Top);
		int right = Math.Min(Main.maxTilesX - 2, area.Right);
		int bottom = Math.Min(Main.maxTilesY - 2, area.Bottom);
		int longestHorizontal = 0;
		Point horizontalStart = Point.Zero;
		for (int y = top; y < bottom - 1; y++) {
			int run = 0;
			for (int x = left; x < right; x++) {
				bool boundary = include(x, y) && include(x, y + 1)
					&& Main.tile[x, y].WallType != Main.tile[x, y + 1].WallType;
				run = boundary ? run + 1 : 0;
				if (run > longestHorizontal) {
					longestHorizontal = run;
					horizontalStart = new Point(x - run + 1, y);
				}
			}
		}

		int longestVertical = 0;
		Point verticalStart = Point.Zero;
		for (int x = left; x < right - 1; x++) {
			int run = 0;
			for (int y = top; y < bottom; y++) {
				bool boundary = include(x, y) && include(x + 1, y)
					&& Main.tile[x, y].WallType != Main.tile[x + 1, y].WallType;
				run = boundary ? run + 1 : 0;
				if (run > longestVertical) {
					longestVertical = run;
					verticalStart = new Point(x, y - run + 1);
				}
			}
		}
		return (longestHorizontal, longestVertical, horizontalStart, verticalStart);
	}

	private static (int Horizontal, int Vertical, Point HorizontalStart, Point VerticalStart) MeasureNaturalWallSeams(
		Rectangle area,
		GenerationManifest manifest)
	{
		int longestHorizontal = 0;
		Point horizontalStart = Point.Zero;
		for (int y = area.Top + 2; y < area.Bottom - 3; y++) {
			int run = 0;
			for (int x = area.Left + 2; x < area.Right - 2; x++) {
				bool boundary = IsOpenNaturalWall(x, y, manifest) && IsOpenNaturalWall(x, y + 1, manifest)
					&& Main.tile[x, y].WallType != Main.tile[x, y + 1].WallType;
				run = boundary ? run + 1 : 0;
				if (run > longestHorizontal) {
					longestHorizontal = run;
					horizontalStart = new Point(x - run + 1, y);
				}
			}
		}

		int longestVertical = 0;
		Point verticalStart = Point.Zero;
		for (int x = area.Left + 2; x < area.Right - 3; x++) {
			int run = 0;
			for (int y = area.Top + 2; y < area.Bottom - 2; y++) {
				bool boundary = IsOpenNaturalWall(x, y, manifest) && IsOpenNaturalWall(x + 1, y, manifest)
					&& Main.tile[x, y].WallType != Main.tile[x + 1, y].WallType;
				run = boundary ? run + 1 : 0;
				if (run > longestVertical) {
					longestVertical = run;
					verticalStart = new Point(x, y - run + 1);
				}
			}
		}
		return (longestHorizontal, longestVertical, horizontalStart, verticalStart);
	}

	private static bool IsOpenNaturalWall(int x, int y, GenerationManifest manifest)
	{
		Point point = new(x, y);
		if (manifest.Landmarks.Any(record => record.Area.Contains(point))
			|| manifest.Bridges.Any(record => record.Area.Contains(point))
			|| manifest.ForestLakeBridges.Any(record => record.Area.Contains(point))
			|| manifest.Valleys.Any(record => record.Area.Contains(point))
			|| manifest.MountainWaters.Any(record => record.Area.Contains(point))
			|| manifest.SkyHighlands.Any(record => record.Area.Contains(point))
			|| manifest.MineSections.Any(record => record.Area.Contains(point))) {
			return false;
		}
		ushort wall = Main.tile[x, y].WallType;
		return !TileEditor.IsSolid(x, y)
			&& wall is WallID.DirtUnsafe or WallID.Stone or WallID.SnowWallUnsafe or WallID.JungleUnsafe
				or WallID.Sandstone or WallID.EbonstoneUnsafe or WallID.CrimstoneUnsafe;
	}

	private static int CountSlopedTiles(Rectangle area, ushort tileType)
	{
		int count = 0;
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				count += tile.HasTile && tile.TileType == tileType && tile.Slope != SlopeType.Solid ? 1 : 0;
			}
		}
		return count;
	}

	private static int CountSlopedSolidTiles(Rectangle area)
	{
		int count = 0;
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				count += tile.HasUnactuatedTile && tile.TileType != TileID.Platforms
					&& Main.tileSolid[tile.TileType] && tile.Slope != SlopeType.Solid ? 1 : 0;
			}
		}
		return count;
	}

	private static int CountWallsAboveRoof(Rectangle area, int groundY)
	{
		int leaks = 0;
		for (int x = area.Left; x < area.Right; x++) {
			int roofY = int.MaxValue;
			for (int y = area.Top; y < groundY; y++) {
				if (Main.tile[x, y].HasUnactuatedTile && Main.tileSolid[Main.tile[x, y].TileType]) {
					roofY = y;
					break;
				}
			}
			if (roofY == int.MaxValue) {
				continue;
			}
			for (int y = area.Top; y < roofY; y++) {
				leaks += Main.tile[x, y].WallType != WallID.None ? 1 : 0;
			}
		}
		return leaks;
	}

	private static int CountFurnitureTiles(Rectangle area)
	{
		ushort[] types = [
			TileID.WorkBenches, TileID.Tables, TileID.Tables2, TileID.Chairs, TileID.Bookcases,
			TileID.Benches, TileID.Anvils, TileID.Chandeliers, TileID.SmallPiles
		];
		return types.Sum(type => CountTiles(area, type));
	}

	private static int CountTiles(Rectangle area, ushort tileType)
	{
		int count = 0;
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				if (HasTile(x, y, tileType)) {
					count++;
				}
			}
		}
		return count;
	}

	private static int CountLiquid(Rectangle area, int liquidType)
	{
		int count = 0;
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				Tile tile = Main.tile[x, y];
				if (tile.LiquidAmount > 0 && tile.LiquidType == liquidType) {
					count++;
				}
			}
		}
		return count;
	}

	private static bool HasTile(int x, int y, ushort type) =>
		WorldGen.InWorld(x, y, 2) && Main.tile[x, y].HasTile && Main.tile[x, y].TileType == type;

	private static void ValidateProgressionSites(List<string> errors)
	{
		int dungeonTiles = 0;
		int templeTiles = 0;
		int evilObjects = 0;
		int shimmerCells = 0;
		int sampleStep = Main.maxTilesX > 7000 ? 2 : 1;
		for (int x = 40; x < Main.maxTilesX - 40; x += sampleStep) {
			for (int y = 40; y < Main.maxTilesY - 40; y++) {
				Tile tile = Main.tile[x, y];
				if (tile.HasTile) {
					if (tile.TileType is TileID.BlueDungeonBrick or TileID.GreenDungeonBrick or TileID.PinkDungeonBrick) {
						dungeonTiles++;
					}
					else if (tile.TileType == TileID.LihzahrdBrick) {
						templeTiles++;
					}
					else if (tile.TileType is TileID.ShadowOrbs or TileID.DemonAltar) {
						evilObjects++;
					}
				}
				if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Shimmer) {
					shimmerCells++;
				}
			}
		}

		if (dungeonTiles < 250) {
			errors.Add($"Dungeon integrity scan found only {dungeonTiles} brick samples");
		}
		if (templeTiles < 250) {
			errors.Add($"Jungle Temple integrity scan found only {templeTiles} brick samples");
		}
		if (evilObjects == 0) {
			errors.Add("no Shadow Orb, Crimson Heart, or Demon Altar tiles survived");
		}
		if (shimmerCells < 40) {
			errors.Add($"Aether integrity scan found only {shimmerCells} Shimmer cells");
		}
	}
}
