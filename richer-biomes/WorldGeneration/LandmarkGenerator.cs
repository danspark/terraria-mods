using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace RicherBiomes.WorldGeneration;

internal static class LandmarkGenerator
{
	private const int MaximumStructureHeight = 42;
	private const int CandidateBudget = 420;
	private const int LandmarkSeedSalt = 0x1D4B_62F3;
	private const int ShellThickness = 2;

	public static void Apply(WorldPlan plan, SurfaceMinePlan surfaceMine, GenerationManifest manifest, GenerationProgress progress)
	{
		List<LandmarkRequest> requests = BuildRequests(plan);
		for (int index = 0; index < requests.Count; index++) {
			LandmarkRequest request = requests[index];
			UnifiedRandom random = new(MixSeed(plan.GenerationSeed, LandmarkSeedSalt, index));
			if (!TryPlaceBest(request, plan.SpawnX, surfaceMine, manifest, random) && request.Required) {
				string diagnostic = request.Biome == BiomeKind.Ocean
					? $" {DescribeOceanSite(request, surfaceMine, manifest)}"
					: string.Empty;
				throw new InvalidOperationException(
					$"Richer Biomes could not place the required {request.Biome} landmark "
					+ $"inside columns {request.LeftX}..{request.RightX}.{diagnostic}");
			}
			progress.Set((double)(index + 1) / requests.Count);
		}
	}

	public static void Furnish(GenerationManifest manifest, GenerationProgress progress)
	{
		for (int index = 0; index < manifest.Landmarks.Count; index++) {
			LandmarkRecord landmark = manifest.Landmarks[index];
			LandmarkLayout layout = ResolveLayout(landmark.Biome, landmark.LayoutVariant);
			LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.AnchorX, landmark.AnchorY);
			Commit(new LandmarkCandidate(
				landmark.Biome,
				landmark.AnchorX,
				landmark.AnchorY,
				landmark.Area,
				layout,
				Score: 0), style);
			int furniture = FurnishLandmark(landmark);
			manifest.Landmarks[index] = landmark with { FurnitureCount = furniture };
			progress.Set((double)(index + 1) / manifest.Landmarks.Count);
		}
	}

	public static void RepairTraversal(GenerationManifest manifest)
	{
		foreach (LandmarkRecord landmark in manifest.Landmarks) {
			LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.AnchorX, landmark.AnchorY);
			LandmarkBlueprint blueprint = BuildBlueprint(landmark);
			RepairLandmarkTraversal(landmark);
			CarveOpenEntrances(
				landmark.Area,
				landmark.AnchorY,
				blueprint.LeftColumn,
				blueprint.RightColumn,
				style.Foundation);
			TileEditor.Frame(landmark.Area, border: 2);
		}
	}

	internal static bool HasCorrectRoofSlopes(LandmarkRecord landmark)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(landmark);
		LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.AnchorX, landmark.AnchorY);
		int expected = 0;
		int matching = 0;
		foreach (LandmarkRoom room in blueprint.Rooms.Where(room => ShouldBuildRoof(blueprint, room))) {
			int centerX = room.Shell.Center.X;
			int halfWidth = Math.Max(1, room.Shell.Width / 2 + 2);
			int rise = Math.Clamp(halfWidth / 2, 4, 8);
			for (int x = room.Shell.Left - 2; x <= room.Shell.Right + 1; x++) {
				int roofY = room.Shell.Top - rise + Math.Abs(x - centerX) / 2;
				if (!landmark.Area.Contains(x, roofY)) {
					continue;
				}
				int distance = Math.Abs(x - centerX);
				SlopeType slope = distance % 2 == 0 || x == centerX
					? SlopeType.Solid
					: x < centerX ? SlopeType.SlopeDownRight : SlopeType.SlopeDownLeft;
				Tile tile = Main.tile[x, roofY];
				expected++;
				matching += tile.HasTile && tile.TileType == style.Foundation && tile.Slope == slope ? 1 : 0;
			}
		}
		return expected > 0 && matching >= expected * 9 / 10;
	}

	internal static bool HasCorrectStairSlopes(LandmarkRecord landmark)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(landmark);
		foreach (StairConnection stair in blueprint.Stairs) {
			SlopeType expected = stair.Direction == 1 ? SlopeType.SlopeDownLeft : SlopeType.SlopeDownRight;
			for (int step = 1; step <= stair.StepCount; step++) {
				int x = stair.LandingX + stair.Direction * step;
				int y = stair.FloorY + step;
				Tile tile = Main.tile[x, y];
				if (!tile.HasTile || tile.TileType != TileID.Platforms || tile.Slope != expected) {
					return false;
				}
			}
		}
		return true;
	}

	internal static bool HasThickUpperPosts(LandmarkRecord landmark)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(landmark);
		LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.AnchorX, landmark.AnchorY);
		foreach (LandmarkRoom room in blueprint.Rooms.Where(room => room.IsUpper)) {
			int expected = 0;
			int matching = 0;
			for (int y = room.Shell.Top + ShellThickness; y < room.Shell.Bottom - 1; y++) {
				expected += 2;
				matching += HasType(room.Shell.Left, y, style.Pillar) && HasType(room.Shell.Left + 1, y, style.Pillar) ? 1 : 0;
				matching += HasType(room.Shell.Right - 1, y, style.Pillar) && HasType(room.Shell.Right - 2, y, style.Pillar) ? 1 : 0;
			}
			if (expected == 0 || matching < expected * 4 / 5) {
				return false;
			}
		}
		return true;
	}

	private static List<LandmarkRequest> BuildRequests(WorldPlan plan)
	{
		int inlandLeft = plan.CoastMargin + 30;
		int inlandRight = Main.maxTilesX - plan.CoastMargin - 31;
		return [
			// Claim the scarce beach shelves before inland landmarks consider the
			// same boundary terrain. The later candidates respect these footprints.
			new(BiomeKind.Ocean, 75, plan.CoastMargin + 350, Required: true),
			new(BiomeKind.Ocean, Main.maxTilesX - plan.CoastMargin - 351, Main.maxTilesX - 76, Required: true),
			new(BiomeKind.Forest, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Snow, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Desert, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Jungle, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Evil, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Sky, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Mushroom, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Cavern, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Underworld, inlandLeft, inlandRight, Required: true)
		];
	}

	private static bool TryPlaceBest(
		LandmarkRequest request,
		int spawnX,
		SurfaceMinePlan surfaceMine,
		GenerationManifest manifest,
		UnifiedRandom random)
	{
		LandmarkLayout layout = ResolveLayout(request.Biome, random.Next(3));
		LandmarkCandidate? best = null;
		for (int attempt = 0; attempt < CandidateBudget; attempt++) {
			int x = random.Next(request.LeftX, request.RightX + 1);
			if (!TryCreateCandidate(request.Biome, layout, x, spawnX, surfaceMine, manifest, out LandmarkCandidate candidate)) {
				continue;
			}

			if (best is null || candidate.Score > best.Value.Score) {
				best = candidate;
			}
		}
		if (best is null && request.Required) {
			for (int x = request.LeftX; x <= request.RightX; x += 4) {
				if (!TryCreateCandidate(request.Biome, layout, x, spawnX, surfaceMine, manifest, out LandmarkCandidate candidate)) {
					continue;
				}
				if (best is null || candidate.Score > best.Value.Score) {
					best = candidate;
				}
			}
		}
		if (best is null && request.Biome is (BiomeKind.Evil or BiomeKind.Mushroom or BiomeKind.Cavern or BiomeKind.Underworld)
			&& TryCreateEmbeddedCandidate(request, layout, spawnX, surfaceMine, manifest, out LandmarkCandidate embeddedFallback)) {
			best = embeddedFallback;
		}
		if (best is null && request.Biome is (BiomeKind.Forest or BiomeKind.Snow or BiomeKind.Desert
			or BiomeKind.Jungle or BiomeKind.Ocean or BiomeKind.Sky)
			&& TryCreatePreparedSurfaceCandidate(request, layout, spawnX, surfaceMine, manifest, out LandmarkCandidate surfaceFallback)) {
			best = surfaceFallback;
		}
		if (best is null && request.Biome == BiomeKind.Ocean) {
			LandmarkLayout compact = ResolveLayout(BiomeKind.Ocean, variant: 0);
			for (int x = request.LeftX; x <= request.RightX; x += 2) {
				if (TryCreateCandidate(request.Biome, compact, x, spawnX, surfaceMine, manifest, out LandmarkCandidate coastalCandidate)
					&& (best is null || coastalCandidate.Score > best.Value.Score)) {
					best = coastalCandidate;
				}
			}
			if (best is null
				&& TryCreatePreparedSurfaceCandidate(request, compact, spawnX, surfaceMine, manifest, out LandmarkCandidate compactFallback)) {
				best = compactFallback;
			}
		}
		if (best is null && request.Biome is (BiomeKind.Forest or BiomeKind.Snow or BiomeKind.Desert or BiomeKind.Jungle)
			&& TryCreateEmbeddedCandidate(request, layout, spawnX, surfaceMine, manifest, out LandmarkCandidate embeddedBiomeFallback)) {
			best = embeddedBiomeFallback;
		}

		if (best is null) {
			return false;
		}

		LandmarkCandidate accepted = best.Value;
		LandmarkStyle style = ResolveStyle(request.Biome, accepted.AnchorX, accepted.GroundY);
		Commit(accepted, style);
		if (!ValidateFootprint(accepted, style)) {
			throw new InvalidOperationException($"Richer Biomes placed an incomplete {request.Biome} landmark at {accepted.AnchorX}, {accepted.GroundY}.");
		}

		GenVars.structures.AddProtectedStructure(accepted.Area, padding: 10);
		manifest.Landmarks.Add(new LandmarkRecord(
			request.Biome,
			accepted.Area,
			accepted.AnchorX,
			accepted.GroundY,
			accepted.Layout.RoomCount,
			FurnitureCount: 0,
			accepted.Layout.Variant));
		return true;
	}

	private static bool TryCreatePreparedSurfaceCandidate(
		LandmarkRequest request,
		LandmarkLayout layout,
		int spawnX,
		SurfaceMinePlan surfaceMine,
		GenerationManifest manifest,
		out LandmarkCandidate candidate)
	{
		LandmarkCandidate? best = null;
		int firstCenter = request.LeftX + layout.Width / 2;
		int lastCenter = request.RightX - layout.Width / 2;
		for (int centerX = firstCenter; centerX <= lastCenter; centerX += request.Biome == BiomeKind.Ocean ? 2 : 4) {
			int left = centerX - layout.Width / 2;
			int matchingSupports = 0;
			int drySupports = 0;
			int minimumGround = int.MaxValue;
			int maximumGround = int.MinValue;
			for (int x = left; x < left + layout.Width; x++) {
				int supportY;
				bool found = request.Biome switch {
					BiomeKind.Sky => BiomeClassifier.TryFindSurfaceSupport(x, out supportY),
					BiomeKind.Ocean => TryFindCoastalGroundSupport(x, out supportY),
					_ => BiomeClassifier.TryFindGroundSupport(x, out supportY)
				};
				if (!found) {
					continue;
				}
				minimumGround = Math.Min(minimumGround, supportY);
				maximumGround = Math.Max(maximumGround, supportY);
				drySupports += IsDryColumn(x, supportY) ? 1 : 0;
				if (request.Biome == BiomeKind.Ocean
					? IsDryOceanSupport(x, supportY)
					: BiomeClassifier.ClassifySupport(Main.tile[x, supportY].TileType, x, supportY) == request.Biome) {
					matchingSupports++;
				}
			}

			int relief = maximumGround - minimumGround;
			if (matchingSupports < (request.Biome == BiomeKind.Ocean ? layout.Width * 3 / 5 : layout.Width * 4 / 5)
				|| minimumGround == int.MaxValue
				|| request.Biome == BiomeKind.Ocean && (drySupports < layout.Width
					|| maximumGround < Main.worldSurface * 0.55d)
				|| relief > (request.Biome == BiomeKind.Ocean ? 16 : 20)) {
				continue;
			}

			// This fallback deliberately levels a safe shelf when vanilla decoration
			// leaves a biome with no naturally calm footprint. Extend ownership above
			// the highest nearby ground so the roof cannot remain buried in a slope.
			int top = Math.Min(maximumGround - layout.Height, minimumGround - 6);
			Rectangle area = new(left, top, layout.Width, maximumGround + 10 - top);
			if (Inflated(surfaceMine.Area, 8).Intersects(area)
				|| manifest.Terraces.Any(terrace => Inflated(terrace.Area, 24).Intersects(area))
				|| manifest.Landmarks.Any(landmark => Inflated(landmark.Area, 50).Intersects(area))
				|| manifest.BiomeTransitions.Any(transition => Inflated(transition.Area, 8).Intersects(area))
				|| !TileEditor.IsSafeForTerrainFeature(area)) {
				continue;
			}

			int score = matchingSupports * 8 - relief * 4 + Math.Abs(centerX - spawnX) / 40;
			LandmarkCandidate current = new(request.Biome, centerX, maximumGround, area, layout, score);
			if (best is null || current.Score > best.Value.Score) {
				best = current;
			}
		}

		candidate = best ?? default;
		return best is not null;
	}

	private static bool TryCreateEmbeddedCandidate(
		LandmarkRequest request,
		LandmarkLayout layout,
		int spawnX,
		SurfaceMinePlan surfaceMine,
		GenerationManifest manifest,
		out LandmarkCandidate candidate)
	{
		int top = request.Biome == BiomeKind.Underworld
			? Main.UnderworldLayer + 20
			: request.Biome == BiomeKind.Cavern
				? (int)Main.rockLayer + 20
				: (int)Main.worldSurface + 35;
		int bottom = request.Biome == BiomeKind.Underworld
			? Main.maxTilesY - 80
			: Math.Min(Main.UnderworldLayer - 100, (int)Main.rockLayer + 320);
		for (int x = request.LeftX + layout.Width; x <= request.RightX - layout.Width; x += 5) {
			for (int y = top; y < bottom; y += 3) {
				Tile tile = Main.tile[x, y];
				if (!tile.HasTile || BiomeClassifier.ClassifySupport(tile.TileType, x, y) != request.Biome) {
					continue;
				}

				Rectangle area = new(x - layout.Width / 2, y - layout.Height, layout.Width, layout.Height + 10);
				if (!TileEditor.IsSafeForTerrainFeature(area)
					|| Inflated(surfaceMine.Area, 8).Intersects(area)
					|| manifest.Terraces.Any(terrace => Inflated(terrace.Area, 24).Intersects(area))
					|| manifest.Landmarks.Any(landmark => Inflated(landmark.Area, 50).Intersects(area))
					|| manifest.BiomeTransitions.Any(transition => Inflated(transition.Area, 8).Intersects(area))) {
					continue;
				}

				candidate = new LandmarkCandidate(
					request.Biome,
					x,
					y,
					area,
					layout,
					Math.Abs(x - spawnX) / 30);
				return true;
			}
		}

		candidate = default;
		return false;
	}

	private static bool TryCreateCandidate(
		BiomeKind biome,
		LandmarkLayout layout,
		int anchorX,
		int spawnX,
		SurfaceMinePlan surfaceMine,
		GenerationManifest manifest,
		out LandmarkCandidate candidate)
	{
		if (!TryFindGround(biome, layout.Height, anchorX, out int groundY)) {
			candidate = default;
			return false;
		}

		int left = anchorX - layout.Width / 2;

		int matchingSupports = 0;
		int drySupports = 0;
		int minimumGround = int.MaxValue;
		int maximumGround = int.MinValue;
		for (int x = left; x < left + layout.Width; x++) {
			if (!TryFindNearbyFloor(x, groundY, out int y)) {
				candidate = default;
				return false;
			}

			minimumGround = Math.Min(minimumGround, y);
			maximumGround = Math.Max(maximumGround, y);
			drySupports += IsDryColumn(x, y) ? 1 : 0;
			if (biome == BiomeKind.Ocean
				? IsDryOceanSupport(x, y)
				: BiomeClassifier.ClassifySupport(Main.tile[x, y].TileType, x, y) == biome) {
				matchingSupports++;
			}
		}

		int relief = maximumGround - minimumGround;
		if (relief > (biome == BiomeKind.Ocean ? 16 : 24) || matchingSupports < layout.Width / 2
			|| biome == BiomeKind.Ocean && (drySupports < layout.Width
				|| maximumGround < Main.worldSurface * 0.55d)) {
			candidate = default;
			return false;
		}

		// The landmark floor follows the highest support in its footprint. Derive the
		// owned rectangle from that final floor, not from the first anchor sample;
		// otherwise a sloped cavern can put the entire house above its own bounds.
		Rectangle area = new(left, maximumGround - layout.Height, layout.Width, layout.Height + 10);
		if (Inflated(surfaceMine.Area, 8).Intersects(area)
			|| manifest.Terraces.Any(terrace => Inflated(terrace.Area, 24).Intersects(area))
			|| manifest.Landmarks.Any(landmark => Inflated(landmark.Area, 50).Intersects(area))
			|| manifest.BiomeTransitions.Any(transition => Inflated(transition.Area, 8).Intersects(area))
			|| !TileEditor.IsSafeForTerrainFeature(area)) {
			candidate = default;
			return false;
		}

		int distanceFromSpawn = Math.Abs(anchorX - spawnX);
		int score = distanceFromSpawn / 30 - relief * 18 + matchingSupports * 3;
		candidate = new LandmarkCandidate(biome, anchorX, maximumGround, area, layout, score);
		return true;
	}

	private static bool IsDryOceanSupport(int x, int supportY)
	{
		Tile support = Main.tile[x, supportY];
		if (!support.HasUnactuatedTile || !Main.tileSolid[support.TileType] || Main.tileSolidTop[support.TileType]) {
			return false;
		}
		return IsDryColumn(x, supportY);
	}

	private static string DescribeOceanSite(
		LandmarkRequest request,
		SurfaceMinePlan surfaceMine,
		GenerationManifest manifest)
	{
		int supportColumns = 0;
		int dryColumns = 0;
		int currentDryRun = 0;
		int longestDryRun = 0;
		int minimumDryY = int.MaxValue;
		int maximumDryY = int.MinValue;
		for (int x = request.LeftX; x <= request.RightX; x++) {
			if (!TryFindCoastalGroundSupport(x, out int supportY)) {
				currentDryRun = 0;
				continue;
			}
			supportColumns++;
			if (!IsDryOceanSupport(x, supportY)) {
				currentDryRun = 0;
				continue;
			}

			dryColumns++;
			currentDryRun++;
			longestDryRun = Math.Max(longestDryRun, currentDryRun);
			minimumDryY = Math.Min(minimumDryY, supportY);
			maximumDryY = Math.Max(maximumDryY, supportY);
		}

		LandmarkLayout layout = ResolveLayout(BiomeKind.Ocean, variant: 0);
		int completeWindows = 0;
		int calmWindows = 0;
		int collisionFreeWindows = 0;
		int safeWindows = 0;
		int mineCollisions = 0;
		int terraceCollisions = 0;
		int landmarkCollisions = 0;
		int transitionCollisions = 0;
		int minimumRelief = int.MaxValue;
		for (int centerX = request.LeftX + layout.Width / 2;
			centerX <= request.RightX - layout.Width / 2;
			centerX += 2) {
			int minimumGround = int.MaxValue;
			int maximumGround = int.MinValue;
			bool complete = true;
			for (int x = centerX - layout.Width / 2; x < centerX - layout.Width / 2 + layout.Width; x++) {
				if (!TryFindCoastalGroundSupport(x, out int supportY) || !IsDryOceanSupport(x, supportY)) {
					complete = false;
					break;
				}
				minimumGround = Math.Min(minimumGround, supportY);
				maximumGround = Math.Max(maximumGround, supportY);
			}
			if (!complete) {
				continue;
			}

			completeWindows++;
			int relief = maximumGround - minimumGround;
			minimumRelief = Math.Min(minimumRelief, relief);
			if (relief > 16) {
				continue;
			}
			calmWindows++;

			int top = Math.Min(maximumGround - layout.Height, minimumGround - 6);
			Rectangle area = new(centerX - layout.Width / 2, top, layout.Width, maximumGround + 10 - top);
			bool mineCollision = Inflated(surfaceMine.Area, 8).Intersects(area);
			bool terraceCollision = manifest.Terraces.Any(terrace => Inflated(terrace.Area, 24).Intersects(area));
			bool landmarkCollision = manifest.Landmarks.Any(landmark => Inflated(landmark.Area, 50).Intersects(area));
			bool transitionCollision = manifest.BiomeTransitions.Any(transition => Inflated(transition.Area, 8).Intersects(area));
			mineCollisions += mineCollision ? 1 : 0;
			terraceCollisions += terraceCollision ? 1 : 0;
			landmarkCollisions += landmarkCollision ? 1 : 0;
			transitionCollisions += transitionCollision ? 1 : 0;
			if (mineCollision || terraceCollision || landmarkCollision || transitionCollision) {
				continue;
			}
			collisionFreeWindows++;
			safeWindows += TileEditor.IsSafeForTerrainFeature(area) ? 1 : 0;
		}

		string dryRange = dryColumns == 0 ? "none" : $"{minimumDryY}..{maximumDryY}";
		string bestRelief = minimumRelief == int.MaxValue ? "none" : minimumRelief.ToString();
		return $"Coast diagnostic: supports={supportColumns}, dry={dryColumns}, "
			+ $"longestDryRun={longestDryRun}, dryY={dryRange}, completeWindows={completeWindows}, "
			+ $"bestRelief={bestRelief}, calmWindows={calmWindows}, collisionFree={collisionFreeWindows}, "
			+ $"collisions[mine={mineCollisions},terrace={terraceCollisions},landmark={landmarkCollisions},"
			+ $"transition={transitionCollisions}], safe={safeWindows}, worldSurface={Main.worldSurface:F1}.";
	}

	private static bool IsDryColumn(int x, int supportY)
	{
		for (int y = Math.Max(45, supportY - MaximumStructureHeight - 4); y <= supportY; y++) {
			if (Main.tile[x, y].LiquidAmount > 0) {
				return false;
			}
		}
		return true;
	}

	private static bool TryFindCoastalGroundSupport(int x, out int supportY) =>
		BiomeClassifier.TryFindGroundSupport(x, (int)(Main.worldSurface * 0.55d), out supportY);

	private static bool TryFindGround(BiomeKind biome, int structureHeight, int x, out int groundY)
	{
		if (!WorldGen.InWorld(x, 50, 45)) {
			groundY = 0;
			return false;
		}

		if (biome is BiomeKind.Forest or BiomeKind.Snow or BiomeKind.Desert or BiomeKind.Jungle or BiomeKind.Evil or BiomeKind.Ocean or BiomeKind.Sky) {
			int y;
			bool foundSurface = biome switch {
				BiomeKind.Sky => BiomeClassifier.TryFindSurfaceSupport(x, out y),
				BiomeKind.Ocean => TryFindCoastalGroundSupport(x, out y),
				_ => BiomeClassifier.TryFindGroundSupport(x, out y)
			};
			if (!foundSurface) {
				if (biome != BiomeKind.Evil) {
					groundY = 0;
					return false;
				}
			}
			else if (BiomeClassifier.ClassifySupport(Main.tile[x, y].TileType, x, y) == biome) {
				groundY = y;
				return true;
			}
			if (biome != BiomeKind.Evil) {
				groundY = 0;
				return false;
			}
		}

		int top = biome switch {
			BiomeKind.Mushroom => (int)Main.worldSurface + 80,
			BiomeKind.Evil => (int)Main.worldSurface + 20,
			BiomeKind.Cavern => (int)Main.rockLayer + 20,
			BiomeKind.Underworld => Main.UnderworldLayer + 20,
			_ => (int)Main.rockLayer
		};
		int bottom = biome switch {
			BiomeKind.Underworld => Main.maxTilesY - 80,
			BiomeKind.Evil => Math.Min(Main.UnderworldLayer - 80, (int)Main.rockLayer + 180),
			_ => Math.Min(Main.UnderworldLayer - 80, top + 360)
		};
		for (int y = top; y < bottom; y++) {
			if (!TileEditor.IsSolid(x, y) || BiomeClassifier.ClassifySupport(Main.tile[x, y].TileType, x, y) != biome) {
				continue;
			}

			int clearCells = 0;
			for (int above = y - 1; above >= y - structureHeight; above--) {
				if (!TileEditor.IsSolid(x, above)) {
					clearCells++;
				}
			}
			if (clearCells >= structureHeight - 3) {
				groundY = y;
				return true;
			}
		}

		groundY = 0;
		return false;
	}

	private static bool TryFindNearbyFloor(int x, int expectedY, out int floorY)
	{
		for (int offset = -24; offset <= 24; offset++) {
			int y = expectedY + offset;
			if (TileEditor.IsSolid(x, y)) {
				floorY = y;
				return true;
			}
		}

		floorY = 0;
		return false;
	}

	private static LandmarkStyle ResolveStyle(BiomeKind biome, int anchorX, int groundY)
	{
		if (biome == BiomeKind.Evil) {
			ushort support = Main.tile[anchorX, groundY].TileType;
			bool crimson = support is TileID.CrimsonGrass or TileID.Crimstone or TileID.CrimstoneBrick or TileID.Crimsand
				or TileID.CrimsonSandstone or TileID.CrimsonHardenedSand;
			return crimson
				? new LandmarkStyle(TileID.CrimstoneBrick, TileID.Crimstone, WallID.CrimstoneUnsafe, WallID.Planked, 0, TileID.Tables, 0, 0, 0, 0)
				: new LandmarkStyle(TileID.EbonstoneBrick, TileID.Ebonstone, WallID.EbonstoneUnsafe, WallID.Planked, 0, TileID.Tables, 0, 0, 0, 0);
		}

		// Furniture styles mirror the installed 1.4.4.9 cave-house palettes where
		// those palettes exist. Other districts retain ordinary wooden furniture
		// inside biome-specific shells instead of guessing unsupported frame styles.
		return biome switch {
			BiomeKind.Forest => new LandmarkStyle(TileID.WoodBlock, TileID.WoodenBeam, WallID.Wood, WallID.Planked, 0, TileID.Tables, 0, 0, 0, 0),
			BiomeKind.Snow => new LandmarkStyle(TileID.BorealWood, TileID.BorealWood, WallID.SnowWallUnsafe, WallID.Stone, 19, TileID.Tables, 28, 30, 23, 25),
			BiomeKind.Desert => new LandmarkStyle(TileID.SandstoneBrick, TileID.SandstoneColumn, WallID.Sandstone, WallID.Stone, 42, TileID.Tables2, 7, 43, 39, 39),
			BiomeKind.Jungle => new LandmarkStyle(TileID.LivingMahogany, TileID.RichMahogany, WallID.JungleUnsafe, WallID.Planked, 2, TileID.Tables, 2, 3, 2, 12),
			BiomeKind.Ocean => new LandmarkStyle(TileID.PalmWood, TileID.PalmWood, WallID.Sandstone, WallID.LivingWoodUnsafe, 0, TileID.Tables, 0, 0, 0, 0),
			BiomeKind.Sky => new LandmarkStyle(TileID.Sunplate, TileID.Sunplate, WallID.DiscWall, WallID.Cloud, 0, TileID.Tables, 0, 0, 0, 0),
			BiomeKind.Mushroom => new LandmarkStyle(TileID.MushroomBlock, TileID.MushroomBlock, WallID.MushroomUnsafe, WallID.Planked, 18, TileID.Tables, 27, 9, 7, 24),
			BiomeKind.Cavern => new LandmarkStyle(TileID.GrayBrick, TileID.WoodenBeam, WallID.Stone, WallID.Planked, 0, TileID.Tables, 0, 0, 0, 0),
			BiomeKind.Underworld => new LandmarkStyle(TileID.AshWood, TileID.AshWood, WallID.HellstoneBrickUnsafe, WallID.ObsidianBrickUnsafe, 0, TileID.Tables, 0, 0, 0, 0),
			_ => new LandmarkStyle(TileID.GrayBrick, TileID.WoodenBeam, WallID.Stone, WallID.Planked, 0, TileID.Tables, 0, 0, 0, 0)
		};
	}

	private static LandmarkLayout ResolveLayout(BiomeKind biome, int variant)
	{
		LandmarkLayout layout = biome switch {
			BiomeKind.Forest => new LandmarkLayout(57, 30, 4, 0, variant),
			BiomeKind.Snow => new LandmarkLayout(59, 31, 4, 1, variant),
			BiomeKind.Desert => new LandmarkLayout(65, 30, 4, 2, variant),
			BiomeKind.Jungle => new LandmarkLayout(61, 34, 5, 3, variant),
			BiomeKind.Evil => new LandmarkLayout(55, 29, 3, 4, variant),
			BiomeKind.Ocean => new LandmarkLayout(57, 29, 4, 5, variant),
			BiomeKind.Sky => new LandmarkLayout(69, 30, 5, 6, variant),
			BiomeKind.Mushroom => new LandmarkLayout(57, 29, 3, 7, variant),
			BiomeKind.Cavern => new LandmarkLayout(66, 30, 4, 8, variant),
			BiomeKind.Underworld => new LandmarkLayout(61, 29, 4, 9, variant),
			_ => new LandmarkLayout(57, 29, 3, 0, variant)
		};

		return variant switch {
			1 => layout with { Width = layout.Width + 8, Height = layout.Height + 3, RoofVariant = layout.RoofVariant + 2 },
			2 => layout with { Width = layout.Width + 14, Height = layout.Height + 1, RoomCount = layout.RoomCount + 1, RoofVariant = layout.RoofVariant + 5 },
			_ => layout
		};
	}

	private static void Commit(LandmarkCandidate candidate, LandmarkStyle style)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(candidate);
		PrepareFootprint(candidate.Area, candidate.GroundY, style.Foundation);
		foreach (LandmarkRoom room in blueprint.Rooms) {
			BuildRoomShell(room, style, candidate.Layout.Variant);
		}
		CarveRoomArches(blueprint);
		BuildRoofs(blueprint, style);
		BuildStairs(blueprint, style.PlatformStyle);
		BuildFoundationSupports(blueprint, style);
		CarveOpenEntrances(candidate.Area, candidate.GroundY, blueprint.LeftColumn, blueprint.RightColumn, style.Foundation);
		ClearExteriorWalls(candidate.Area, blueprint);
		TileEditor.TryPlaceTorch(blueprint.LeftColumn + 5, candidate.GroundY - 5);
		TileEditor.TryPlaceTorch(blueprint.RightColumn - 5, candidate.GroundY - 5);
		TileEditor.Frame(candidate.Area, border: 3);
	}

	private static LandmarkBlueprint BuildBlueprint(LandmarkCandidate candidate) =>
		BuildBlueprint(candidate.Area, candidate.AnchorX, candidate.GroundY, candidate.Layout);

	private static LandmarkBlueprint BuildBlueprint(LandmarkRecord landmark) =>
		BuildBlueprint(
			landmark.Area,
			landmark.AnchorX,
			landmark.AnchorY,
			ResolveLayout(landmark.Biome, landmark.LayoutVariant));

	private static LandmarkBlueprint BuildBlueprint(
		Rectangle area,
		int anchorX,
		int groundY,
		LandmarkLayout layout)
	{
		int leftColumn = area.Left + 5;
		int rightColumn = area.Right - 6;
		int loftY = groundY - 11;
		int groundRoomCount = layout.RoomCount >= 5 ? 3 : 2;
		List<LandmarkRoom> rooms = [];
		for (int index = 0; index < groundRoomCount; index++) {
			int left = index == 0
				? leftColumn
				: leftColumn + (rightColumn - leftColumn) * index / groundRoomCount;
			int right = index == groundRoomCount - 1
				? rightColumn
				: leftColumn + (rightColumn - leftColumn) * (index + 1) / groundRoomCount;
			rooms.Add(new LandmarkRoom(
				new Rectangle(left, loftY, right - left + 1, groundY - loftY + 1),
				(RoomRole)(index % 3),
				IsUpper: false));
		}

		int upperRoomCount = Math.Max(1, layout.RoomCount - groundRoomCount);
		for (int index = 0; index < upperRoomCount; index++) {
			bool leftSide = upperRoomCount == 1
				? layout.Variant % 2 == 0
				: index == 0;
			int width = upperRoomCount == 1
				? Math.Clamp((rightColumn - leftColumn + 1) * 5 / 9, 22, 31)
				: Math.Clamp((rightColumn - leftColumn + 1) / 2 - 3, 19, 28);
			int left = leftSide ? leftColumn + 2 : rightColumn - width - 1;
			int height = 9 + (layout.RoofVariant + index * 2) % 3;
			Rectangle shell = new(left, loftY - height, width, height + 1);
			rooms.Add(new LandmarkRoom(
				shell,
				index == 0 ? RoomRole.Study : RoomRole.Lookout,
				IsUpper: true));
		}

		List<StairConnection> stairs = [];
		foreach (LandmarkRoom upper in rooms.Where(room => room.IsUpper)) {
			int direction = upper.Shell.Center.X < anchorX ? 1 : -1;
			int landingX = direction == 1 ? upper.Shell.Left + 4 : upper.Shell.Right - 5;
			stairs.Add(new StairConnection(landingX, loftY, direction, groundY - loftY - 2));
		}

		return new LandmarkBlueprint(rooms, stairs, leftColumn, rightColumn, groundY);
	}

	private static void PrepareFootprint(Rectangle area, int groundY, ushort foundation)
	{
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < groundY; y++) {
				TileEditor.ClearTerrain(x, y, clearWall: true);
			}
			int edgeDistance = Math.Min(x - area.Left, area.Right - 1 - x);
			int foundationDepth = edgeDistance switch {
				0 => 1,
				1 or 2 => 2,
				_ => 3
			};
			for (int depth = 0; depth < foundationDepth; depth++) {
				TileEditor.SetTerrain(x, groundY + depth, foundation);
			}
			int irregularBottom = groundY + 5 + OrganicBoundary.Profile(
				x,
				area.Center.X ^ groundY ^ 0x464F_4F54,
				29,
				7,
				3,
				2);
			irregularBottom = Math.Max(groundY + 3, irregularBottom);
			for (int y = groundY + 3; y <= Math.Min(area.Bottom - 1, irregularBottom); y++) {
				if (!TileEditor.IsSolid(x, y)) {
					TileEditor.SetTerrain(x, y, foundation);
				}
			}
		}

		TileEditor.SetSlopedTerrain(area.Left, groundY, foundation, SlopeType.SlopeDownRight);
		TileEditor.SetSlopedTerrain(area.Right - 1, groundY, foundation, SlopeType.SlopeDownLeft);
		Tile leftShoulder = Main.tile[area.Left + 1, groundY];
		leftShoulder.IsHalfBlock = true;
		Tile rightShoulder = Main.tile[area.Right - 2, groundY];
		rightShoulder.IsHalfBlock = true;
	}

	private static void BuildRoomShell(LandmarkRoom room, LandmarkStyle style, int variant)
	{
		Rectangle shell = room.Shell;
		int wallSeed = shell.Center.X ^ shell.Center.Y * 31 ^ variant * 193;
		for (int x = shell.Left; x < shell.Right; x++) {
			for (int y = shell.Top; y < shell.Bottom; y++) {
				bool horizontal = y < shell.Top + ShellThickness || y == shell.Bottom - 1;
				bool vertical = x < shell.Left + ShellThickness || x >= shell.Right - ShellThickness;
				if (horizontal || vertical) {
					TileEditor.SetTerrain(x, y, horizontal ? style.Foundation : style.Pillar);
					continue;
				}

				TileEditor.ClearTerrain(x, y);
				int wainscotTop = shell.Bottom - 4 + OrganicBoundary.Profile(
					x,
					wallSeed ^ 0x5741_494E,
					17,
					5,
					2,
					1);
				bool wainscot = y >= wainscotTop;
				double panelField = OrganicBoundary.Field(
					x,
					y,
					wallSeed ^ 0x5041_4E45,
					13,
					4);
				int sideWarp = OrganicBoundary.Profile(
					y,
					wallSeed ^ 0x5349_4445,
					11,
					4,
					3,
					1);
				bool accentPanel = !wainscot
					&& Math.Abs(x + sideWarp - shell.Center.X) >= Math.Max(3, shell.Width / 4)
					&& panelField > 0.43d;
				TileEditor.SetWall(x, y, wainscot || accentPanel ? style.AccentWall : style.Wall);
			}
		}

		int windowWidth = shell.Width >= 24 ? 4 : 3;
		int windowLeft = shell.Center.X - windowWidth / 2;
		int windowTop = Math.Max(shell.Top + ShellThickness + 1, shell.Bottom - 8);
		for (int x = windowLeft; x < windowLeft + windowWidth; x++) {
			for (int y = windowTop; y < Math.Min(shell.Bottom - 3, windowTop + 3); y++) {
				TileEditor.SetWall(x, y, WallID.Glass);
			}
		}
	}

	private static void CarveRoomArches(LandmarkBlueprint blueprint)
	{
		List<LandmarkRoom> groundRooms = blueprint.Rooms.Where(room => !room.IsUpper).ToList();
		for (int index = 1; index < groundRooms.Count; index++) {
			int dividerX = groundRooms[index].Shell.Left;
			for (int x = dividerX - 2; x <= dividerX + 2; x++) {
				int archLift = Math.Abs(x - dividerX) == 2 ? 1 : 0;
				for (int y = blueprint.GroundY - 8 + archLift; y < blueprint.GroundY; y++) {
					TileEditor.ClearTerrain(x, y);
				}
			}
		}
	}

	private static void BuildRoofs(LandmarkBlueprint blueprint, LandmarkStyle style)
	{
		foreach (LandmarkRoom room in blueprint.Rooms) {
			if (ShouldBuildRoof(blueprint, room)) {
				BuildGabledRoof(room.Shell, style);
			}
		}
	}

	private static bool ShouldBuildRoof(LandmarkBlueprint blueprint, LandmarkRoom room) =>
		room.IsUpper || !blueprint.Rooms.Any(upper =>
			upper.IsUpper && upper.Shell.Left < room.Shell.Right - 3 && upper.Shell.Right > room.Shell.Left + 3);

	private static void BuildGabledRoof(Rectangle room, LandmarkStyle style)
	{
		int centerX = room.Center.X;
		int halfWidth = Math.Max(1, room.Width / 2 + 2);
		int rise = Math.Clamp(halfWidth / 2, 4, 8);
		int wallSeed = room.Center.X ^ room.Center.Y * 31 ^ 0x4741_424C;
		for (int x = room.Left - 2; x <= room.Right + 1; x++) {
			int distance = Math.Abs(x - centerX);
			int roofY = room.Top - rise + distance / 2;
			// Terraria names slopes by the solid corner. A roof rising toward the
			// center therefore uses DownRight on its left face and DownLeft on its
			// right face.
			SlopeType slope = distance % 2 == 0 || x == centerX
				? SlopeType.Solid
				: x < centerX ? SlopeType.SlopeDownRight : SlopeType.SlopeDownLeft;
			TileEditor.SetSlopedTerrain(x, roofY, style.Foundation, slope);
			TileEditor.SetTerrain(x, roofY + 1, distance % 4 == 0 ? style.Pillar : style.Foundation);
			for (int y = roofY + 2; y < room.Top; y++) {
				TileEditor.ClearTerrain(x, y);
				double accentField = OrganicBoundary.Field(x, y, wallSeed, 11, 4);
				int edgeBias = OrganicBoundary.Profile(y, wallSeed ^ 0x4544_4745, 13, 5, 3, 1);
				bool accent = distance + edgeBias > halfWidth / 2 && accentField > 0.47d;
				TileEditor.SetWall(x, y, accent ? style.AccentWall : style.Wall);
			}
		}
	}

	private static void BuildStairs(LandmarkBlueprint blueprint, int platformStyle)
	{
		foreach (StairConnection stair in blueprint.Stairs) {
			for (int step = 1; step <= stair.StepCount; step++) {
				int x = stair.LandingX + stair.Direction * step;
				int y = stair.FloorY + step;
				for (int clearX = x - 1; clearX <= x + 1; clearX++) {
					for (int clearY = y - 3; clearY <= y; clearY++) {
						TileEditor.ClearTerrain(clearX, clearY);
					}
				}
			}

			int landingStart = stair.Direction == 1 ? stair.LandingX - 3 : stair.LandingX;
			for (int offset = 0; offset < 4; offset++) {
				int x = landingStart + offset;
				TileEditor.ClearTerrain(x, stair.FloorY);
				TileEditor.TryPlacePlatformForced(x, stair.FloorY, platformStyle);
			}

			for (int step = 1; step <= stair.StepCount; step++) {
				int x = stair.LandingX + stair.Direction * step;
				int y = stair.FloorY + step;
				// Vanilla platform stairs use slope 1 while descending right and slope 2
				// while descending left. The enum names describe the solid corner, not
				// the direction the player walks.
				SlopeType slope = stair.Direction == 1 ? SlopeType.SlopeDownLeft : SlopeType.SlopeDownRight;
				TileEditor.TryPlaceSlopedPlatform(x, y, platformStyle, slope);
			}
		}
	}

	private static void BuildFoundationSupports(LandmarkBlueprint blueprint, LandmarkStyle style)
	{
		int cadence = 5 + Math.Abs(blueprint.LeftColumn + blueprint.RightColumn) % 2;
		for (int x = blueprint.LeftColumn + 2; x <= blueprint.RightColumn - 2; x += cadence) {
			for (int y = blueprint.GroundY + 3; y <= blueprint.GroundY + 7; y++) {
				TileEditor.SetTerrain(x, y, style.Pillar);
			}
		}
	}

	private static int FurnishLandmark(LandmarkRecord landmark)
	{
		LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.AnchorX, landmark.AnchorY);
		LandmarkBlueprint blueprint = BuildBlueprint(landmark);
		int placed = 0;
		foreach (LandmarkRoom room in blueprint.Rooms) {
			placed += FurnishRoom(room, style, landmark.Biome);
		}

		BuildStairs(blueprint, style.PlatformStyle);
		CarveOpenEntrances(
			landmark.Area,
			landmark.AnchorY,
			blueprint.LeftColumn,
			blueprint.RightColumn,
			style.Foundation);
		ClearExteriorWalls(landmark.Area, blueprint);
		TileEditor.Frame(landmark.Area, border: 3);
		int retainedFurnitureTiles = CountFurnitureTiles(landmark.Area);
		if (retainedFurnitureTiles < 6) {
			throw new InvalidOperationException(
				$"Richer Biomes could not retain enough furnishing in the {landmark.Biome} landmark at "
				+ $"{landmark.AnchorX},{landmark.AnchorY}; placed={placed}; retainedTiles={retainedFurnitureTiles}.");
		}
		return Math.Max(placed, Math.Min(retainedFurnitureTiles, 24));
	}

	private static int FurnishRoom(LandmarkRoom room, LandmarkStyle style, BiomeKind biome)
	{
		int centerX = room.Shell.Center.X;
		int floorY = room.FloorY;
		int count = 0;
		switch (room.Role) {
			case RoomRole.Workshop:
				count += TryPlaceFurniture(room, centerX - 4, TileID.WorkBenches, style.WorkbenchStyle) ? 1 : 0;
				count += TryPlaceFurniture(room, centerX + 5, TileID.Anvils, 0) ? 1 : 0;
				break;
			case RoomRole.Commons:
				count += TryPlaceFurniture(room, centerX, style.TableTile, style.TableStyle) ? 1 : 0;
				count += TryPlaceFurniture(room, centerX - 5, TileID.Chairs, style.ChairStyle) ? 1 : 0;
				count += TryPlaceFurniture(room, centerX + 5, TileID.Chairs, style.ChairStyle) ? 1 : 0;
				break;
			case RoomRole.Study:
				count += TryPlaceFurniture(room, centerX - 4, TileID.Bookcases, style.BookcaseStyle) ? 1 : 0;
				count += TryPlaceFurniture(room, centerX + 5, TileID.Benches, 0) ? 1 : 0;
				break;
			case RoomRole.Lookout:
				count += TryPlaceFurniture(room, centerX - 2, style.TableTile, style.TableStyle) ? 1 : 0;
				count += TryPlaceFurniture(room, centerX + 4, TileID.Chairs, style.ChairStyle) ? 1 : 0;
				break;
		}

		if (TileEditor.TryPlaceSmallPile(room.Shell.Right - 3, floorY, ((int)biome + room.Shell.Width) % 6, 0)) {
			count++;
		}
		TileEditor.TryPlaceTorch(room.Shell.Left + 2, room.Shell.Top + 4);
		TileEditor.TryPlaceTorch(room.Shell.Right - 3, room.Shell.Top + 4);
		return count;
	}

	private static void RepairLandmarkTraversal(LandmarkRecord landmark)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(landmark);
		LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.AnchorX, landmark.AnchorY);
		BuildStairs(blueprint, style.PlatformStyle);
		ClearExteriorWalls(landmark.Area, blueprint);
	}

	private static void CarveOpenEntrances(
		Rectangle area,
		int groundY,
		int leftColumn,
		int rightColumn,
		ushort foundation)
	{
		foreach (int centerX in new[] { leftColumn, rightColumn }) {
			for (int x = centerX - 3; x <= centerX + 3; x++) {
				int edgeLift = Math.Max(0, Math.Abs(x - centerX) - 1) * 2;
				for (int y = groundY - 9 + edgeLift; y < groundY; y++) {
					TileEditor.ClearTerrain(x, y, clearWall: Math.Abs(x - centerX) >= 2);
				}
				for (int depth = 0; depth < 3; depth++) {
					TileEditor.SetTerrain(x, groundY + depth, foundation);
				}
			}

			int outsideDirection = centerX == leftColumn ? -1 : 1;
			for (int step = 1; step <= 7; step++) {
				int x = centerX + outsideDirection * step;
				if (x <= area.Left || x >= area.Right - 1) {
					continue;
				}
				int ceilingY = groundY - 7 + step / 3 + Math.Abs(HashNoise(x, step, centerX)) % 2;
				for (int y = ceilingY; y < groundY; y++) {
					TileEditor.ClearTerrain(x, y, clearWall: true);
				}
			}
		}
	}

	private static int CountFurnitureTiles(Rectangle area)
	{
		ushort[] types = [
			TileID.WorkBenches, TileID.Tables, TileID.Tables2, TileID.Chairs, TileID.Bookcases,
			TileID.Benches, TileID.Anvils, TileID.Chandeliers, TileID.SmallPiles
		];
		int count = 0;
		foreach (ushort type in types) {
			for (int x = area.Left; x < area.Right; x++) {
				for (int y = area.Top; y < area.Bottom; y++) {
					if (WorldGen.InWorld(x, y, 3) && HasType(x, y, type)) {
						count++;
					}
				}
			}
		}
		return count;
	}

	private static bool TryPlaceFurniture(LandmarkRoom room, int preferredX, ushort tileType, int style)
	{
		ReadOnlySpan<int> offsets = [0, -3, 3, -6, 6];
		foreach (int offset in offsets) {
			int x = preferredX + offset;
			if (x < room.Shell.Left + 3 || x > room.Shell.Right - 4
				|| !HasFurnitureClearance(x, room.FloorY)) {
				continue;
			}
			WorldGen.PlaceTile(x, room.FloorY, tileType, mute: true, forced: false, plr: -1, style: style);
			if (HasNearbyType(x, room.FloorY - 1, tileType, 3, 3)) {
				return true;
			}
		}
		return false;
	}

	private static bool HasFurnitureClearance(int x, int floorY)
	{
		for (int clearX = x - 2; clearX <= x + 2; clearX++) {
			for (int clearY = floorY - 4; clearY <= floorY; clearY++) {
				if (Main.tile[clearX, clearY].HasTile) {
					return false;
				}
			}
		}
		return TileEditor.IsSolid(x, floorY + 1);
	}

	private static bool HasNearbyType(int x, int y, ushort type, int radiusX, int radiusY)
	{
		for (int offsetX = -radiusX; offsetX <= radiusX; offsetX++) {
			for (int offsetY = -radiusY; offsetY <= radiusY; offsetY++) {
				if (WorldGen.InWorld(x + offsetX, y + offsetY, 3) && HasType(x + offsetX, y + offsetY, type)) {
					return true;
				}
			}
		}
		return false;
	}

	private static void ClearExteriorWalls(Rectangle area, LandmarkBlueprint blueprint)
	{
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < blueprint.GroundY; y++) {
				bool interior = blueprint.Rooms.Any(room =>
					x > room.Shell.Left && x < room.Shell.Right - 1
					&& y > room.Shell.Top && y < room.Shell.Bottom - 1)
					|| blueprint.Rooms.Any(room => IsInsideRoofGable(room.Shell, x, y));
				if (!interior) {
					Main.tile[x, y].WallType = WallID.None;
				}
			}
		}
	}

	private static bool IsInsideRoofGable(Rectangle room, int x, int y)
	{
		if (x < room.Left - 1 || x > room.Right || y >= room.Top) {
			return false;
		}
		int halfWidth = Math.Max(1, room.Width / 2 + 2);
		int rise = Math.Clamp(halfWidth / 2, 4, 8);
		int roofY = room.Top - rise + Math.Abs(x - room.Center.X) / 2;
		return y > roofY + 1;
	}

	private static bool ValidateFootprint(LandmarkCandidate candidate, LandmarkStyle style)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(candidate);
		int foundationColumns = 0;
		int slopedRoofTiles = 0;
		int slopedPlatforms = 0;
		for (int x = candidate.Area.Left; x < candidate.Area.Right; x++) {
			foundationColumns += HasType(x, candidate.GroundY, style.Foundation) ? 1 : 0;
			for (int y = candidate.Area.Top; y < candidate.GroundY; y++) {
				Tile tile = Main.tile[x, y];
				if (tile.HasTile && tile.TileType == style.Foundation && tile.Slope != SlopeType.Solid) {
					slopedRoofTiles++;
				}
				if (tile.HasTile && tile.TileType == TileID.Platforms && tile.Slope != SlopeType.Solid) {
					slopedPlatforms++;
				}
			}
		}
		if (foundationColumns < candidate.Area.Width * 9 / 10 || slopedRoofTiles < 10 || slopedPlatforms < 6) {
			return false;
		}
		int downLeft = 0;
		int downRight = 0;
		foreach (LandmarkRoom room in blueprint.Rooms) {
			for (int x = room.Shell.Left - 2; x <= room.Shell.Right + 1; x++) {
				for (int y = candidate.Area.Top; y < room.Shell.Top; y++) {
					Tile tile = Main.tile[x, y];
					if (!tile.HasTile || tile.TileType != style.Foundation) {
						continue;
					}
					downLeft += tile.Slope == SlopeType.SlopeDownLeft ? 1 : 0;
					downRight += tile.Slope == SlopeType.SlopeDownRight ? 1 : 0;
				}
			}
		}
		if (downLeft < 4 || downRight < 4) {
			return false;
		}
		foreach (int centerX in new[] { blueprint.LeftColumn, blueprint.RightColumn }) {
			for (int x = centerX - 2; x <= centerX + 2; x++) {
				for (int y = candidate.GroundY - 7; y < candidate.GroundY; y++) {
					if (TileEditor.IsSolid(x, y)) {
						return false;
					}
				}
			}
		}
		return true;
	}

	private static bool HasType(int x, int y, ushort type)
	{
		Tile tile = Main.tile[x, y];
		return tile.HasTile && tile.TileType == type;
	}

	private static Rectangle Inflated(Rectangle rectangle, int amount)
	{
		rectangle.Inflate(amount, amount);
		return rectangle;
	}

	private static int MixSeed(int seed, int salt, int index)
	{
		unchecked {
			uint value = (uint)seed ^ (uint)salt ^ (uint)index * 0x9E37_79B9u;
			value ^= value >> 16;
			value *= 0x7FEB_352Du;
			value ^= value >> 15;
			return (int)value;
		}
	}

	private static int HashNoise(int x, int y, int seed)
	{
		unchecked {
			uint value = (uint)(x * 73_856_093) ^ (uint)(y * 19_349_663) ^ (uint)seed;
			value ^= value >> 16;
			value *= 0x7FEB_352Du;
			value ^= value >> 15;
			return (int)(value & 0x7FFF_FFFFu);
		}
	}

	private readonly record struct LandmarkRequest(BiomeKind Biome, int LeftX, int RightX, bool Required);

	private readonly record struct LandmarkCandidate(
		BiomeKind Biome,
		int AnchorX,
		int GroundY,
		Rectangle Area,
		LandmarkLayout Layout,
		int Score);

	private readonly record struct LandmarkStyle(
		ushort Foundation,
		ushort Pillar,
		ushort Wall,
		ushort AccentWall,
		int PlatformStyle,
		ushort TableTile,
		int TableStyle,
		int ChairStyle,
		int WorkbenchStyle,
		int BookcaseStyle);

	private readonly record struct LandmarkLayout(int Width, int Height, int RoomCount, int RoofVariant, int Variant);

	private enum RoomRole
	{
		Workshop,
		Commons,
		Study,
		Lookout
	}

	private readonly record struct LandmarkRoom(Rectangle Shell, RoomRole Role, bool IsUpper)
	{
		public int FloorY => Shell.Bottom - 2;
	}

	private readonly record struct StairConnection(int LandingX, int FloorY, int Direction, int StepCount);

	private sealed record LandmarkBlueprint(
		IReadOnlyList<LandmarkRoom> Rooms,
		IReadOnlyList<StairConnection> Stairs,
		int LeftColumn,
		int RightColumn,
		int GroundY);
}
