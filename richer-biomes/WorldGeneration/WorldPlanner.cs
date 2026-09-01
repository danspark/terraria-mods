using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Utilities;

namespace RicherBiomes.WorldGeneration;

internal static class WorldPlanner
{
	private const int WorldPadding = 45;
	private const int SpawnTerraceWidth = 150;

	public static WorldPlan Create()
	{
		RejectUnsupportedWorldVariants();
		int generationSeed = WorldGen.genRand.Next();
		UnifiedRandom random = new(generationSeed);
		int spawnX = Main.maxTilesX / 2;
		int coastMargin = Math.Clamp(Main.maxTilesX / 24, 190, 360);
		int left = coastMargin;
		int right = Main.maxTilesX - coastMargin - 1;
		List<WorldRegion> regions = BuildRegions(random, left, right, spawnX);
		List<MountainRangePlan> mountains = PlanMountains(random, regions, spawnX);
		int[] surfaceY = BuildSurface(random, regions, mountains, coastMargin);
		List<TerraceRequest> terraces = PlanTerraces(random, regions, surfaceY, spawnX);
		List<PlannedCave> caves = PlanCaves(random, regions, surfaceY, terraces);
		List<SkyHighlandPlan> skyHighlands = PlanSkyHighlands(random, regions, mountains);

		return new WorldPlan(
			generationSeed,
			spawnX,
			coastMargin,
			surfaceY,
			regions,
			terraces,
			caves,
			mountains,
			skyHighlands);
	}

	private static void RejectUnsupportedWorldVariants()
	{
		if (!WorldGen.drunkWorldGen
			&& !WorldGen.getGoodWorldGen
			&& !WorldGen.tenthAnniversaryWorldGen
			&& !WorldGen.dontStarveWorldGen
			&& !WorldGen.notTheBees
			&& !WorldGen.remixWorldGen
			&& !WorldGen.noTrapsWorldGen
			&& !WorldGen.everythingWorldGen) {
			return;
		}

		throw new NotSupportedException(
			"Richer Biomes currently supports ordinary Terraria seeds only. "
			+ "Secret seeds alter biome layers and progression-site assumptions, so generation stops before Richer Biomes mutates the world.");
	}

	public static int FindSurfaceY(int x)
	{
		if (!WorldGen.InWorld(x, WorldPadding, WorldPadding)) {
			return (int)Main.worldSurface;
		}

		int lowerLimit = Math.Min(Main.maxTilesY - WorldPadding, (int)Main.worldSurface + 220);
		for (int y = WorldPadding; y < lowerLimit; y++) {
			Tile tile = Main.tile[x, y];
			if (tile.HasUnactuatedTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType]) {
				return y;
			}
		}

		return (int)Main.worldSurface;
	}

	private static List<WorldRegion> BuildRegions(UnifiedRandom random, int left, int right, int spawnX)
	{
		int minimumWidth = Main.maxTilesX switch {
			<= 4200 => 360,
			<= 6400 => 420,
			_ => 480
		};
		int maximumWidth = minimumWidth + 240;
		List<(int Left, int Right)> spans = [];
		int cursor = left;
		while (cursor <= right) {
			int remaining = right - cursor + 1;
			int width = remaining <= maximumWidth + minimumWidth
				? remaining
				: random.Next(minimumWidth, maximumWidth + 1);
			spans.Add((cursor, cursor + width - 1));
			cursor += width;
		}

		int mountainQuota = Math.Clamp(Main.maxTilesX / 3000, 1, 3);
		int mountainsPlaced = 0;
		LandformKind previous = LandformKind.QuietLowland;
		List<WorldRegion> regions = [];
		for (int index = 0; index < spans.Count; index++) {
			(int spanLeft, int spanRight) = spans[index];
			bool containsSpawn = spawnX >= spanLeft - 80 && spawnX <= spanRight + 80;
			LandformKind landform;
			if (containsSpawn) {
				landform = LandformKind.QuietLowland;
			}
			else if (mountainsPlaced < mountainQuota && ShouldPlaceMountain(index, spans.Count, mountainsPlaced, mountainQuota)) {
				landform = LandformKind.Mountain;
				mountainsPlaced++;
			}
			else {
				landform = PickLandform(random, previous);
			}

			int landmarkBudget = Math.Max(1, (spanRight - spanLeft + 1) / 320);
			int quietBudget = landform is LandformKind.Mountain or LandformKind.Valley ? 0 : 1;
			regions.Add(new WorldRegion(index, spanLeft, spanRight, landform, landmarkBudget, quietBudget));
			previous = landform;
		}

		if (mountainsPlaced == 0) {
			int farthestIndex = regions
				.Select((region, index) => (index, Distance: Math.Abs(region.CenterX - spawnX)))
				.OrderByDescending(item => item.Distance)
				.First().index;
			WorldRegion selected = regions[farthestIndex];
			regions[farthestIndex] = selected with { Landform = LandformKind.Mountain, QuietBudget = 0 };
		}

		return regions;
	}

	private static List<MountainRangePlan> PlanMountains(
		UnifiedRandom random,
		IReadOnlyList<WorldRegion> regions,
		int spawnX)
	{
		List<MountainRangePlan> mountains = [];
		MountainInteriorStyle? previousInterior = null;
		BridgeStyle? previousBridge = null;
		foreach (WorldRegion region in regions.Where(region => region.Landform == LandformKind.Mountain)) {
			MountainInteriorStyle interiorStyle = PickDifferent(
				random,
				previousInterior,
				MountainInteriorStyle.BranchingGrottoes,
				MountainInteriorStyle.SwitchbackClimb,
				MountainInteriorStyle.SplitLevelCaves,
				MountainInteriorStyle.OpenFault);
			BridgeStyle bridgeStyle = PickDifferent(
				random,
				previousBridge,
				BridgeStyle.TimberSuspension,
				BridgeStyle.StoneArch,
				BridgeStyle.RailTrestle);
			previousInterior = interiorStyle;
			previousBridge = bridgeStyle;

			int leftPeakX = region.Left + region.Width * random.Next(19, 39) / 100;
			int rightPeakX = region.Left + region.Width * random.Next(62, 83) / 100;
			int saddleX = (leftPeakX + rightPeakX) / 2 + random.Next(-region.Width / 18, region.Width / 18 + 1);
			int skyY = Math.Clamp((int)Math.Round(Main.worldSurface * random.Next(19, 28) / 100d), 54, 124);
			int summitCeiling = Math.Max(58, (int)Math.Floor(Main.worldSurface * 0.35d) - 14);
			int leftPeakY = Math.Min(summitCeiling, skyY + random.Next(-16, 17));
			int rightPeakY = Math.Min(summitCeiling, skyY + random.Next(-20, 21));
			if (interiorStyle == MountainInteriorStyle.SwitchbackClimb) {
				if (random.NextBool()) {
					leftPeakY -= random.Next(10, 25);
				}
				else {
					rightPeakY -= random.Next(10, 25);
				}
			}
			int saddleY = Math.Min(
				(int)Main.worldSurface - random.Next(24, 52),
				Math.Max(leftPeakY, rightPeakY) + random.Next(
					interiorStyle == MountainInteriorStyle.OpenFault ? 98 : 48,
					interiorStyle == MountainInteriorStyle.OpenFault ? 148 : 126));

			ValleyTheme[] themes = [
				ValleyTheme.Lake,
				ValleyTheme.Wooded,
				ValleyTheme.Lake,
				ValleyTheme.SealedEvil,
				ValleyTheme.Lava
			];
			ValleyTheme theme = themes[random.Next(themes.Length)];
			if (theme == ValleyTheme.Lava && Math.Abs(region.CenterX - spawnX) < Main.maxTilesX / 4) {
				theme = ValleyTheme.Lake;
			}

			mountains.Add(new MountainRangePlan(
				region.Id,
				leftPeakX,
				leftPeakY,
				saddleX,
				saddleY,
				rightPeakX,
				rightPeakY,
				theme,
				bridgeStyle,
				interiorStyle,
				random.Next()));
		}

		return mountains;
	}

	private static bool ShouldPlaceMountain(int index, int count, int placed, int quota)
	{
		int remainingSlots = count - index;
		int needed = quota - placed;
		if (remainingSlots <= needed) {
			return true;
		}

		double target = (placed + 1d) * count / (quota + 1d);
		return index >= Math.Round(target);
	}

	private static LandformKind PickLandform(UnifiedRandom random, LandformKind previous)
	{
		LandformKind[] choices = [
			LandformKind.RollingHills,
			LandformKind.RollingHills,
			LandformKind.Valley,
			LandformKind.Plateau,
			LandformKind.Basin,
			LandformKind.QuietLowland
		];

		for (int attempt = 0; attempt < choices.Length; attempt++) {
			LandformKind choice = choices[random.Next(choices.Length)];
			if (choice != previous || attempt == choices.Length - 1) {
				return choice;
			}
		}

		return LandformKind.RollingHills;
	}

	private static int[] BuildSurface(
		UnifiedRandom random,
		IReadOnlyList<WorldRegion> regions,
		IReadOnlyList<MountainRangePlan> mountains,
		int coastMargin)
	{
		int[] surface = new int[Main.maxTilesX];
		for (int x = 0; x < surface.Length; x++) {
			surface[x] = FindSurfaceY(x);
		}

		int minimumY = Math.Max(70, (int)(Main.worldSurface * 0.24d));
		int maximumY = Math.Min((int)Main.rockLayer - 90, (int)Main.worldSurface + 90);
		int boundaryY = MedianSurface(surface, coastMargin, coastMargin + 50);
		Dictionary<int, MountainRangePlan> mountainByRegion = mountains.ToDictionary(mountain => mountain.RegionId);

		foreach (WorldRegion region in regions) {
			int nextBoundaryY = Math.Clamp(boundaryY + random.Next(-24, 25), minimumY + 45, maximumY - 35);
			int centerOffset = CenterOffset(random, region.Landform, boundaryY, minimumY);
			int detailAmplitude = region.Landform switch {
				LandformKind.QuietLowland => 3,
				LandformKind.Plateau => 5,
				LandformKind.Mountain => 9,
				_ => 13
			};
			List<(int X, int Value)> detail = BuildDetailPoints(random, region.Left, region.Right, detailAmplitude);

			for (int x = region.Left; x <= region.Right; x++) {
				double detailY = SampleDetail(detail, x);
				if (mountainByRegion.TryGetValue(region.Id, out MountainRangePlan mountain)) {
					double mountainY = SampleMountainProfile(region, mountain, boundaryY, nextBoundaryY, x);
					surface[x] = Math.Clamp((int)Math.Round(mountainY + detailY), WorldPadding + 8, maximumY);
				}
				else {
					double t = region.Width <= 1 ? 0d : (double)(x - region.Left) / (region.Width - 1);
					double smooth = SmoothStep(t);
					double baseY = Lerp(boundaryY, nextBoundaryY, smooth);
					double envelope = region.Landform == LandformKind.Plateau
						? FlatTopEnvelope(t)
						: 4d * t * (1d - t);
					surface[x] = Math.Clamp((int)Math.Round(baseY + centerOffset * envelope + detailY), minimumY, maximumY);
				}
			}

			boundaryY = nextBoundaryY;
		}

		ClampSlopes(surface, regions);
		BlendCoasts(surface, coastMargin);
		return surface;
	}

	private static double SampleMountainProfile(
		WorldRegion region,
		MountainRangePlan mountain,
		int leftBoundaryY,
		int rightBoundaryY,
		int x)
	{
		double baseProfile;
		if (x <= mountain.LeftPeakX) {
			baseProfile = SmoothSegment(region.Left, leftBoundaryY, mountain.LeftPeakX, mountain.LeftPeakY, x);
		}
		else if (x <= mountain.SaddleX) {
			baseProfile = SmoothSegment(mountain.LeftPeakX, mountain.LeftPeakY, mountain.SaddleX, mountain.SaddleY, x);
		}
		else if (x <= mountain.RightPeakX) {
			baseProfile = SmoothSegment(mountain.SaddleX, mountain.SaddleY, mountain.RightPeakX, mountain.RightPeakY, x);
		}
		else {
			baseProfile = SmoothSegment(mountain.RightPeakX, mountain.RightPeakY, region.Right, rightBoundaryY, x);
		}

		int leftPeakDistance = Math.Abs(x - mountain.LeftPeakX);
		int rightPeakDistance = Math.Abs(x - mountain.RightPeakX);
		int nearestPeakDistance = Math.Min(leftPeakDistance, rightPeakDistance);
		if (nearestPeakDistance <= 52) {
			double peakY = leftPeakDistance <= rightPeakDistance ? mountain.LeftPeakY : mountain.RightPeakY;
			double plateauBlend = nearestPeakDistance <= 24
				? 0d
				: SmoothStep((nearestPeakDistance - 24d) / 28d);
			baseProfile = Lerp(peakY, baseProfile, plateauBlend);
		}

		double t = (double)(x - region.Left) / Math.Max(1, region.Width - 1);
		double envelope = Math.Sin(Math.PI * t);
		double shape = mountain.InteriorStyle switch {
			MountainInteriorStyle.BranchingGrottoes => Math.Sin(t * Math.PI * 5d + 0.4d) * 7d,
			MountainInteriorStyle.SwitchbackClimb => Math.Sin(t * Math.PI * 3d + 1.1d) * 11d,
			MountainInteriorStyle.SplitLevelCaves => Math.Sin(t * Math.PI * 7d + 2.2d) * 6d,
			MountainInteriorStyle.OpenFault => Math.Sin(t * Math.PI * 4d + 0.7d) * 13d,
			_ => 0d
		};
		return baseProfile + shape * envelope;
	}

	private static double SmoothSegment(int leftX, double leftY, int rightX, double rightY, int x)
	{
		double t = rightX == leftX ? 0d : Math.Clamp((double)(x - leftX) / (rightX - leftX), 0d, 1d);
		return Lerp(leftY, rightY, SmoothStep(t));
	}

	private static List<SkyHighlandPlan> PlanSkyHighlands(
		UnifiedRandom random,
		IReadOnlyList<WorldRegion> regions,
		IReadOnlyList<MountainRangePlan> mountains)
	{
		if (mountains.Count == 0) {
			return [];
		}

		int desiredCount = Main.maxTilesX switch {
			<= 4200 => 1,
			<= 6400 => Math.Min(2, mountains.Count),
			_ => Math.Min(2, mountains.Count)
		};
		int baseWidth = Main.maxTilesX switch {
			<= 4200 => 280,
			<= 6400 => 360,
			_ => 440
		};
		int baseDepth = Main.maxTilesY switch {
			<= 1200 => 90,
			<= 1800 => 110,
			_ => 140
		};

		List<SkyHighlandPlan> highlands = [];
		int attachmentQuota = random.NextBool(3) ? 1 : 0;
		SkyHighlandStyle? previousStyle = null;
		for (int index = 0; index < desiredCount; index++) {
			SkyHighlandStyle style = PickDifferent(
				random,
				previousStyle,
				SkyHighlandStyle.TerracedMeadow,
				SkyHighlandStyle.CloudBasin,
				SkyHighlandStyle.BrokenArchipelago);
			previousStyle = style;
			int width = baseWidth + random.Next(-baseWidth / 7, baseWidth / 7 + 1);
			int depth = baseDepth + random.Next(-baseDepth / 7, baseDepth / 7 + 1);
			int satelliteCount = style switch {
				SkyHighlandStyle.TerracedMeadow => random.Next(2, 5),
				SkyHighlandStyle.CloudBasin => random.Next(1, 4),
				SkyHighlandStyle.BrokenArchipelago => random.Next(6, 10),
				_ => 3
			};
			bool hasLake = style == SkyHighlandStyle.CloudBasin
				|| style == SkyHighlandStyle.TerracedMeadow && random.NextBool(2);

			if (index < attachmentQuota) {
				MountainRangePlan mountain = mountains[random.Next(mountains.Count)];
				int worldCenter = Main.maxTilesX / 2;
				int leftDistance = Math.Abs(mountain.LeftPeakX - worldCenter);
				int rightDistance = Math.Abs(mountain.RightPeakX - worldCenter);
				bool attachLeft = leftDistance == rightDistance ? random.NextBool() : leftDistance > rightDistance;
				int peakX = attachLeft ? mountain.LeftPeakX : mountain.RightPeakX;
				int peakY = attachLeft ? mountain.LeftPeakY : mountain.RightPeakY;
				int outwardDirection = Math.Sign(peakX - worldCenter);
				if (outwardDirection == 0) {
					outwardDirection = attachLeft ? -1 : 1;
				}
				int centerX = peakX + outwardDirection * width / 5;
				int surfaceY = Math.Max(WorldPadding + 8, peakY + random.Next(-5, 8));
				highlands.Add(new SkyHighlandPlan(
					mountain.RegionId,
					centerX,
					surfaceY,
					width,
					depth,
					satelliteCount,
					style,
					hasLake));
				continue;
			}

			int detachedCenter = FindDetachedHighlandCenter(random, regions, mountains, highlands, width);
			int minimumSurface = WorldPadding + 14;
			int maximumSurface = Math.Max(minimumSurface + 1, (int)Math.Round(Main.worldSurface * 0.22d));
			int detachedSurfaceY = random.Next(minimumSurface, maximumSurface);
			highlands.Add(new SkyHighlandPlan(
				null,
				detachedCenter,
				detachedSurfaceY,
				width,
				depth,
				satelliteCount,
				style,
				hasLake));
		}

		return highlands;
	}

	private static int FindDetachedHighlandCenter(
		UnifiedRandom random,
		IReadOnlyList<WorldRegion> regions,
		IReadOnlyList<MountainRangePlan> mountains,
		IReadOnlyList<SkyHighlandPlan> existing,
		int width)
	{
		int minimumCenter = WorldPadding + width / 2 + 40;
		int maximumCenter = Main.maxTilesX - minimumCenter;
		for (int attempt = 0; attempt < 160; attempt++) {
			int centerX = random.Next(minimumCenter, maximumCenter + 1);
			Rectangle candidate = new(centerX - width / 2 - 70, 0, width + 140, 1);
			bool touchesMountain = mountains.Any(mountain => {
				WorldRegion region = regions[mountain.RegionId];
				return candidate.Right >= region.Left - 70 && candidate.Left <= region.Right + 70;
			});
			bool touchesHighland = existing.Any(highland => Math.Abs(highland.CenterX - centerX) < (highland.Width + width) / 2 + 120);
			if (!touchesMountain && !touchesHighland) {
				return centerX;
			}
		}

		for (int centerX = minimumCenter; centerX <= maximumCenter; centerX += 20) {
			if (existing.All(highland => Math.Abs(highland.CenterX - centerX) >= (highland.Width + width) / 2 + 80)) {
				return centerX;
			}
		}

		return Math.Clamp(Main.maxTilesX / 2, minimumCenter, maximumCenter);
	}

	private static T PickDifferent<T>(UnifiedRandom random, T? previous, params T[] choices)
		where T : struct, Enum
	{
		T choice = choices[random.Next(choices.Length)];
		if (previous is null || choices.Length < 2 || !EqualityComparer<T>.Default.Equals(choice, previous.Value)) {
			return choice;
		}

		int previousIndex = Array.IndexOf(choices, choice);
		return choices[(previousIndex + 1 + random.Next(choices.Length - 1)) % choices.Length];
	}

	private static int CenterOffset(UnifiedRandom random, LandformKind landform, int boundaryY, int minimumY) => landform switch {
		LandformKind.QuietLowland => random.Next(-4, 5),
		LandformKind.RollingHills => random.Next(-35, 36),
		LandformKind.Valley => random.Next(38, 70),
		LandformKind.Plateau => -random.Next(26, 48),
		LandformKind.Mountain => -Math.Min(random.Next(130, 205), Math.Max(80, boundaryY - minimumY)),
		LandformKind.Basin => random.Next(22, 45),
		_ => 0
	};

	private static List<(int X, int Value)> BuildDetailPoints(
		UnifiedRandom random,
		int left,
		int right,
		int amplitude)
	{
		List<(int X, int Value)> points = [(left, 0)];
		int cursor = left;
		int previous = 0;
		while (cursor < right) {
			cursor = Math.Min(right, cursor + random.Next(38, 74));
			int value = cursor == right
				? 0
				: Math.Clamp(previous + random.Next(-amplitude, amplitude + 1), -amplitude, amplitude);
			points.Add((cursor, value));
			previous = value;
		}

		return points;
	}

	private static double SampleDetail(IReadOnlyList<(int X, int Value)> points, int x)
	{
		for (int index = 1; index < points.Count; index++) {
			if (x > points[index].X) {
				continue;
			}

			(int leftX, int leftValue) = points[index - 1];
			(int rightX, int rightValue) = points[index];
			double t = rightX == leftX ? 0d : (double)(x - leftX) / (rightX - leftX);
			return Lerp(leftValue, rightValue, SmoothStep(t));
		}

		return points[^1].Value;
	}

	private static List<TerraceRequest> PlanTerraces(
		UnifiedRandom random,
		IReadOnlyList<WorldRegion> regions,
		int[] surface,
		int spawnX)
	{
		List<TerraceRequest> terraces = [new(spawnX, SpawnTerraceWidth, Required: true)];
		ApplyTerrace(surface, spawnX, SpawnTerraceWidth);

		int optionalTarget = Math.Clamp(Main.maxTilesX / 1700, 2, 5);
		foreach (WorldRegion region in regions
			.Where(region => region.QuietBudget > 0 && Math.Abs(region.CenterX - spawnX) > SpawnTerraceWidth)
			.OrderByDescending(region => Math.Abs(region.CenterX - spawnX))) {
			if (terraces.Count - 1 >= optionalTarget) {
				break;
			}

			int width = random.Next(54, 82);
			int halfWidth = width / 2;
			if (region.Width <= width + 40) {
				continue;
			}

			int x = random.Next(region.Left + halfWidth + 20, region.Right - halfWidth - 19);
			if (terraces.Any(terrace => Math.Abs(terrace.PreferredX - x) < 240)) {
				continue;
			}

			terraces.Add(new TerraceRequest(x, width, Required: false));
			ApplyTerrace(surface, x, width);
		}

		return terraces;
	}

	private static void ApplyTerrace(int[] surface, int centerX, int width)
	{
		int halfWidth = width / 2;
		int left = Math.Clamp(centerX - halfWidth, 1, surface.Length - 2);
		int right = Math.Clamp(centerX + halfWidth, 1, surface.Length - 2);
		int targetY = MedianSurface(surface, left, right);
		const int blendWidth = 24;

		for (int x = left; x <= right; x++) {
			surface[x] = targetY;
		}

		for (int step = 1; step <= blendWidth; step++) {
			double t = SmoothStep((double)step / blendWidth);
			int leftX = left - step;
			int rightX = right + step;
			if (leftX > 0) {
				surface[leftX] = (int)Math.Round(Lerp(targetY, surface[leftX], t));
			}
			if (rightX < surface.Length - 1) {
				surface[rightX] = (int)Math.Round(Lerp(targetY, surface[rightX], t));
			}
		}
	}

	private static List<PlannedCave> PlanCaves(
		UnifiedRandom random,
		IReadOnlyList<WorldRegion> regions,
		int[] surface,
		IReadOnlyList<TerraceRequest> terraces)
	{
		List<PlannedCave> caves = [];
		int lowerCavernY = Math.Min(Main.maxTilesY - 260, (int)Main.rockLayer + 150);
		foreach (WorldRegion region in regions) {
			int entryX = region.Left + region.Width / 3 + random.Next(-30, 31);
			if (terraces.Any(terrace => Math.Abs(terrace.PreferredX - entryX) < terrace.Width)) {
				entryX = region.Left + region.Width * 2 / 3;
			}

			int exitX = region.Left + region.Width * 2 / 3 + random.Next(-25, 26);
			Point start = new(entryX, surface[entryX] + random.Next(1, 5));
			Point midpoint = new(
				Math.Clamp(region.CenterX + random.Next(-70, 71), region.Left + 20, region.Right - 20),
				(int)Main.worldSurface + random.Next(60, 125));
			Point end = new(exitX, lowerCavernY + random.Next(-45, 46));
			bool requiredRoute = region.Id > 0 && region.Id < regions.Count - 1;
			caves.Add(new PlannedCave(region.Id, start, midpoint, end, random.Next(5, 8), requiredRoute));

			Point loopStart = new(region.Left + region.Width / 4, (int)Main.rockLayer + random.Next(35, 100));
			Point loopMidpoint = new(region.CenterX, loopStart.Y + random.Next(-50, 51));
			Point loopEnd = new(region.Right - region.Width / 4, (int)Main.rockLayer + random.Next(35, 100));
			caves.Add(new PlannedCave(region.Id, loopStart, loopMidpoint, loopEnd, random.Next(7, 11), RequiredRoute: false));
		}

		return caves;
	}

	private static void ClampSlopes(int[] surface, IReadOnlyList<WorldRegion> regions)
	{
		foreach (WorldRegion region in regions) {
			int maximumDelta = region.Landform == LandformKind.Mountain ? 2 : 1;
			for (int x = region.Left + 1; x <= region.Right; x++) {
				surface[x] = Math.Clamp(surface[x], surface[x - 1] - maximumDelta, surface[x - 1] + maximumDelta);
			}
			for (int x = region.Right - 1; x >= region.Left; x--) {
				surface[x] = Math.Clamp(surface[x], surface[x + 1] - maximumDelta, surface[x + 1] + maximumDelta);
			}
		}
	}

	private static void BlendCoasts(int[] surface, int coastMargin)
	{
		const int blendWidth = 80;
		for (int offset = 0; offset < blendWidth; offset++) {
			double t = SmoothStep((double)offset / (blendWidth - 1));
			int leftX = coastMargin + offset;
			int rightX = surface.Length - coastMargin - 1 - offset;
			int originalLeft = FindSurfaceY(leftX);
			int originalRight = FindSurfaceY(rightX);
			surface[leftX] = (int)Math.Round(Lerp(originalLeft, surface[leftX], t));
			surface[rightX] = (int)Math.Round(Lerp(originalRight, surface[rightX], t));
		}
	}

	private static int MedianSurface(int[] samples, int left, int right)
	{
		List<int> values = [];
		for (int x = Math.Max(0, left); x <= Math.Min(samples.Length - 1, right); x += 3) {
			values.Add(samples[x]);
		}

		values.Sort();
		return values[values.Count / 2];
	}

	private static double SmoothStep(double value) => value * value * (3d - 2d * value);

	private static double FlatTopEnvelope(double value)
	{
		if (value < 0.25d) {
			return SmoothStep(value / 0.25d);
		}
		if (value > 0.75d) {
			return SmoothStep((1d - value) / 0.25d);
		}
		return 1d;
	}

	private static double Lerp(double start, double end, double amount) => start + (end - start) * amount;
}
