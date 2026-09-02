using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace VanillaWorldsOverhauled.WorldGeneration;

internal static class LandmarkGenerator
{
	private const int MaximumStructureHeight = 62;
	private const int CandidateBudget = 420;
	private const int LandmarkSeedSalt = 0x1D4B_62F3;
	private const int ShellThickness = 2;

	public static void Apply(WorldPlan plan, SurfaceMinePlan surfaceMine, GenerationManifest manifest, GenerationProgress progress)
	{
		List<LandmarkRequest> requests = BuildRequests(plan);
		for (int index = 0; index < requests.Count; index++) {
			LandmarkRequest request = requests[index];
			UnifiedRandom random = new(MixSeed(plan.GenerationSeed, LandmarkSeedSalt, index));
			if (!TryPlaceBest(request, plan, plan.SpawnX, surfaceMine, manifest, random) && request.Required) {
				string diagnostic = request.Biome == BiomeKind.Ocean
					? $" {DescribeOceanSite(request, surfaceMine, manifest)}"
					: string.Empty;
				throw new InvalidOperationException(
					$"Vanilla Worlds Overhauled could not place the required {request.Biome} landmark "
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
			LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.Archetype, landmark.AnchorX, landmark.AnchorY);
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
			LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.Archetype, landmark.AnchorX, landmark.AnchorY);
			LandmarkBlueprint blueprint = BuildBlueprint(landmark);
			RepairFoundation(landmark, style);
			RepairLandmarkTraversal(landmark);
			CarveOpenEntrances(
				landmark.Area,
				landmark.AnchorY,
				blueprint.LeftColumn,
				blueprint.RightColumn,
				style.Foundation);
			ClearClosedDoors(landmark.Area);
			ClearWallsAboveRoof(landmark.Area, landmark.AnchorY);
			TileEditor.Frame(landmark.Area, border: 2);
			// Roof and stair slopes own the final framed geometry. Generic framing
			// can normalize them when adjoining tiles differ between biome palettes.
			BuildRoofs(blueprint, style);
			BuildStairs(blueprint, style.PlatformStyle);
		}
	}

	public static void RepairFinalGeometry(GenerationManifest manifest)
	{
		foreach (LandmarkRecord landmark in manifest.Landmarks) {
			LandmarkBlueprint blueprint = BuildBlueprint(landmark);
			LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.Archetype, landmark.AnchorX, landmark.AnchorY);
			BuildRoofs(blueprint, style);
			EnsureRoofSlopeBalance(blueprint, style, landmark.Area);
			BuildStairs(blueprint, style.PlatformStyle);
		}
	}

	private static void EnsureRoofSlopeBalance(
		LandmarkBlueprint blueprint,
		LandmarkStyle style,
		Rectangle area)
	{
		foreach ((bool leftSide, SlopeType slope) in new[] {
			(true, SlopeType.SlopeDownRight),
			(false, SlopeType.SlopeDownLeft)
		}) {
			HashSet<Point> candidates = [];
			int matching = 0;
			foreach (LandmarkRoom room in blueprint.Rooms.Where(room => ShouldBuildRoof(blueprint, room))) {
				for (int x = room.Shell.Left - 4; x <= room.Shell.Right + 3; x++) {
					if (leftSide ? x >= room.Shell.Center.X : x <= room.Shell.Center.X) {
						continue;
					}
					for (int y = area.Top; y < room.Shell.Top; y++) {
						Tile tile = Main.tile[x, y];
						if (!tile.HasTile || tile.TileType != style.Roof && tile.TileType != style.RoofAccent) {
							continue;
						}
						Point point = new(x, y);
						if (tile.Slope == slope) {
							matching++;
						}
						else {
							candidates.Add(point);
						}
					}
				}
			}

			foreach (Point candidate in candidates.OrderBy(point => point.Y).ThenBy(point => point.X)) {
				if (matching >= 2) {
					break;
				}
				Tile tile = Main.tile[candidate.X, candidate.Y];
				tile.IsHalfBlock = false;
				tile.Slope = slope;
				matching++;
			}
		}
	}

	private static void ClearWallsAboveRoof(Rectangle area, int groundY)
	{
		for (int x = area.Left; x < area.Right; x++) {
			int roofY = int.MaxValue;
			for (int y = area.Top; y < groundY; y++) {
				Tile tile = Main.tile[x, y];
				if (tile.HasUnactuatedTile && Main.tileSolid[tile.TileType]) {
					roofY = y;
					break;
				}
			}
			if (roofY == int.MaxValue) {
				continue;
			}
			for (int y = area.Top; y < roofY; y++) {
				Main.tile[x, y].WallType = WallID.None;
			}
		}
	}

	private static void RepairFoundation(LandmarkRecord landmark, LandmarkStyle style)
	{
		int left = landmark.Area.Left + 6;
		int right = landmark.Area.Right - 7;
		int depth = landmark.Archetype == LandmarkArchetype.SnowBuriedIgloo ? 1 : 2;
		for (int x = left; x <= right; x++) {
			for (int offsetY = 0; offsetY < depth; offsetY++) {
				int y = landmark.AnchorY + offsetY;
				Tile tile = Main.tile[x, y];
				if (!TileEditor.IsProgressionTile(tile) && !TileEditor.IsTempleOrDungeonCell(tile)) {
					TileEditor.SetTerrain(x, y, style.Foundation);
				}
			}
		}
	}

	internal static bool HasCorrectRoofSlopes(LandmarkRecord landmark, out string reason)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(landmark);
		LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.Archetype, landmark.AnchorX, landmark.AnchorY);
		int leftFacing = 0;
		int rightFacing = 0;
		foreach (LandmarkRoom room in blueprint.Rooms.Where(room => ShouldBuildRoof(blueprint, room))) {
			for (int x = room.Shell.Left - 4; x <= room.Shell.Right + 3; x++) {
				for (int y = landmark.Area.Top; y < room.Shell.Top; y++) {
					Tile tile = Main.tile[x, y];
					ushort type = tile.TileType;
					if (!tile.HasTile || type != style.Roof && type != style.RoofAccent) {
						continue;
					}
					leftFacing += x < room.Shell.Center.X && tile.Slope == SlopeType.SlopeDownRight ? 1 : 0;
					rightFacing += x > room.Shell.Center.X && tile.Slope == SlopeType.SlopeDownLeft ? 1 : 0;
				}
			}
		}
		bool valid = leftFacing >= 2 && rightFacing >= 2;
		reason = valid
			? string.Empty
			: $"{landmark.Archetype} layout {landmark.LayoutVariant} retained {leftFacing} left-facing and {rightFacing} right-facing slopes";
		return valid;
	}

	internal static bool HasCorrectStairSlopes(LandmarkRecord landmark, out string reason)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(landmark);
		for (int stairIndex = 0; stairIndex < blueprint.Stairs.Count; stairIndex++) {
			StairConnection stair = blueprint.Stairs[stairIndex];
			SlopeType expected = stair.Direction == 1 ? SlopeType.SlopeDownLeft : SlopeType.SlopeDownRight;
			for (int step = 1; step <= stair.StepCount; step++) {
				int x = stair.LandingX + stair.Direction * step;
				int y = stair.FloorY + step;
				Tile tile = Main.tile[x, y];
				if (!tile.HasTile || tile.TileType != TileID.Platforms || tile.Slope != expected) {
					reason = $"stair {stairIndex} step {step}/{stair.StepCount} at {x},{y} "
						+ $"expected platform slope {expected}, found tile={(tile.HasTile ? tile.TileType : -1)} slope={tile.Slope}";
					return false;
				}
			}
		}
		reason = string.Empty;
		return true;
	}

	internal static bool HasThickUpperPosts(LandmarkRecord landmark)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(landmark);
		LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.Archetype, landmark.AnchorX, landmark.AnchorY);
		foreach (LandmarkRoom room in blueprint.Rooms.Where(room => room.IsUpper)) {
			bool exposedLeft = !blueprint.Rooms.Any(other => other != room && other.Level == room.Level
				&& other.Shell.Right >= room.Shell.Left && other.Shell.Left < room.Shell.Left);
			bool exposedRight = !blueprint.Rooms.Any(other => other != room && other.Level == room.Level
				&& other.Shell.Left <= room.Shell.Right && other.Shell.Right > room.Shell.Right);
			int expected = 0;
			int matching = 0;
			for (int y = room.Shell.Top + ShellThickness; y < room.Shell.Bottom - 1; y++) {
				if (exposedLeft) {
					expected++;
					matching += Main.tile[room.Shell.Left, y].HasTile && Main.tile[room.Shell.Left + 1, y].HasTile ? 1 : 0;
				}
				if (exposedRight) {
					expected++;
					matching += Main.tile[room.Shell.Right - 1, y].HasTile && Main.tile[room.Shell.Right - 2, y].HasTile ? 1 : 0;
				}
			}
			if (expected > 0 && matching < expected / 2) {
				return false;
			}
		}
		return true;
	}

	internal static bool HasConnectedRoomGraph(LandmarkRecord landmark, out string reason)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(landmark);
		List<HashSet<int>> edges = Enumerable.Range(0, blueprint.Rooms.Count)
			.Select(_ => new HashSet<int>())
			.ToList();
		foreach (IGrouping<int, (LandmarkRoom Room, int Index)> level in blueprint.Rooms
			.Select((room, index) => (Room: room, Index: index))
			.GroupBy(entry => entry.Room.Level)) {
			List<(LandmarkRoom Room, int Index)> rooms = level.OrderBy(entry => entry.Room.Shell.Left).ToList();
			for (int index = 1; index < rooms.Count; index++) {
				(LandmarkRoom left, int leftIndex) = rooms[index - 1];
				(LandmarkRoom right, int rightIndex) = rooms[index];
				if (right.Shell.Left - left.Shell.Right > 2) {
					continue;
				}
				int dividerX = right.Shell.Left;
				int floorY = Math.Min(left.FloorY, right.FloorY);
				int clearCells = 0;
				for (int x = dividerX - 2; x <= dividerX + 2; x++) {
					for (int y = floorY - 6; y < floorY; y++) {
						clearCells += !TileEditor.IsSolid(x, y) ? 1 : 0;
					}
				}
				if (clearCells < 24) {
					reason = $"room arch near {dividerX},{floorY} retains {30 - clearCells} blockers";
					return false;
				}
				edges[leftIndex].Add(rightIndex);
				edges[rightIndex].Add(leftIndex);
			}
		}

		foreach (StairConnection stair in blueprint.Stairs) {
			int upperIndex = Enumerable.Range(0, blueprint.Rooms.Count)
				.Where(index => blueprint.Rooms[index].FloorY == stair.FloorY)
				.OrderBy(index => Math.Abs(blueprint.Rooms[index].Shell.Center.X - stair.LandingX))
				.FirstOrDefault(-1);
			int endpointX = stair.LandingX + stair.Direction * stair.StepCount;
			int endpointY = stair.FloorY + stair.StepCount + 1;
			int lowerIndex = Enumerable.Range(0, blueprint.Rooms.Count)
				.Where(index => blueprint.Rooms[index].FloorY > stair.FloorY)
				.OrderBy(index => Math.Abs(blueprint.Rooms[index].FloorY - endpointY) * 8
					+ Math.Abs(blueprint.Rooms[index].Shell.Center.X - endpointX))
				.FirstOrDefault(-1);
			if (upperIndex < 0 || lowerIndex < 0) {
				reason = $"stair at {stair.LandingX},{stair.FloorY} has no room landing";
				return false;
			}
			edges[upperIndex].Add(lowerIndex);
			edges[lowerIndex].Add(upperIndex);
		}

		HashSet<int> visited = [0];
		Queue<int> queue = new();
		queue.Enqueue(0);
		while (queue.Count > 0) {
			foreach (int next in edges[queue.Dequeue()]) {
				if (visited.Add(next)) {
					queue.Enqueue(next);
				}
			}
		}
		reason = visited.Count == blueprint.Rooms.Count
			? string.Empty
			: $"only {visited.Count}/{blueprint.Rooms.Count} rooms join the traversal graph";
		return visited.Count == blueprint.Rooms.Count;
	}

	internal static bool HasCharacteristicMaterials(LandmarkRecord landmark)
	{
		LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.Archetype, landmark.AnchorX, landmark.AnchorY);
		HashSet<ushort> materials = [style.Foundation, style.Pillar, style.Roof, style.RoofAccent];
		int matching = 0;
		for (int x = landmark.Area.Left; x < landmark.Area.Right; x++) {
			for (int y = landmark.Area.Top; y < landmark.Area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				matching += tile.HasTile && materials.Contains(tile.TileType) ? 1 : 0;
			}
		}
		return matching >= landmark.Area.Width * 2;
	}

	internal static bool HasBuriedIglooRooms(LandmarkRecord landmark)
	{
		if (landmark.Archetype != LandmarkArchetype.SnowBuriedIgloo) {
			return true;
		}
		int authoredCells = 0;
		for (int x = landmark.Area.Left + 5; x < landmark.Area.Right - 5; x++) {
			for (int y = landmark.AnchorY + 2; y < landmark.Area.Bottom - 2; y++) {
				Tile tile = Main.tile[x, y];
				authoredCells += !TileEditor.IsSolid(x, y) && tile.WallType != WallID.None ? 1 : 0;
			}
		}
		return authoredCells >= landmark.Area.Width * 5;
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
		WorldPlan plan,
		int spawnX,
		SurfaceMinePlan surfaceMine,
		GenerationManifest manifest,
		UnifiedRandom random)
	{
		int firstVariant = random.Next(6);
		HashSet<LandmarkArchetype> usedArchetypes = manifest.Landmarks
			.Where(landmark => landmark.Biome == request.Biome)
			.Select(landmark => landmark.Archetype)
			.ToHashSet();
		LandmarkLayout layout = Enumerable.Range(0, 6)
			.Select(offset => ResolveLayout(request.Biome, (firstVariant + offset) % 6))
			.First(candidate => !usedArchetypes.Contains(candidate.Archetype) || usedArchetypes.Count >= 3);
		LandmarkCandidate? best = null;
		for (int attempt = 0; attempt < CandidateBudget; attempt++) {
			int x = random.Next(request.LeftX, request.RightX + 1);
			if (!TryCreateCandidate(request.Biome, layout, x, plan, spawnX, surfaceMine, manifest, out LandmarkCandidate candidate)) {
				continue;
			}

			if (best is null || candidate.Score > best.Value.Score) {
				best = candidate;
			}
		}
		if (best is null && request.Required) {
			for (int x = request.LeftX; x <= request.RightX; x += 4) {
				if (!TryCreateCandidate(request.Biome, layout, x, plan, spawnX, surfaceMine, manifest, out LandmarkCandidate candidate)) {
					continue;
				}
				if (best is null || candidate.Score > best.Value.Score) {
					best = candidate;
				}
			}
		}
		if (best is null && request.Biome is (BiomeKind.Evil or BiomeKind.Mushroom or BiomeKind.Cavern or BiomeKind.Underworld)
			&& TryCreateEmbeddedCandidate(request, layout, plan, spawnX, surfaceMine, manifest, out LandmarkCandidate embeddedFallback)) {
			best = embeddedFallback;
		}
		if (best is null && request.Biome is (BiomeKind.Forest or BiomeKind.Snow or BiomeKind.Desert
			or BiomeKind.Jungle or BiomeKind.Ocean or BiomeKind.Sky)
			&& TryCreatePreparedSurfaceCandidate(request, layout, plan, spawnX, surfaceMine, manifest, out LandmarkCandidate surfaceFallback)) {
			best = surfaceFallback;
		}
		if (best is null && request.Biome == BiomeKind.Ocean) {
			// Small worlds can leave less dry beach than the broad harbor layouts
			// require. Try every Ocean archetype from narrowest to widest so the
			// lighthouse can claim a short coast without flattening or deleting it.
			foreach (LandmarkLayout compact in Enumerable.Range(0, 6)
				.Select(variant => ResolveLayout(BiomeKind.Ocean, variant))
				.OrderBy(candidate => candidate.Width)) {
				for (int x = request.LeftX; x <= request.RightX; x += 2) {
					if (TryCreateCandidate(request.Biome, compact, x, plan, spawnX, surfaceMine, manifest, out LandmarkCandidate coastalCandidate)
						&& (best is null || coastalCandidate.Score > best.Value.Score)) {
						best = coastalCandidate;
					}
				}
				if (best is null
					&& TryCreatePreparedSurfaceCandidate(
						request,
						compact,
						plan,
						spawnX,
						surfaceMine,
						manifest,
						out LandmarkCandidate compactFallback,
						allowTransitionOverlap: true)) {
					best = compactFallback;
				}
				if (best is not null) {
					break;
				}
			}
		}
		if (best is null && request.Biome is (BiomeKind.Forest or BiomeKind.Snow or BiomeKind.Desert or BiomeKind.Jungle)
			&& TryCreateEmbeddedCandidate(request, layout, plan, spawnX, surfaceMine, manifest, out LandmarkCandidate embeddedBiomeFallback)) {
			best = embeddedBiomeFallback;
		}

		if (best is null) {
			return false;
		}

		LandmarkCandidate accepted = best.Value;
		// Required surface landmarks own their footprint over an aesthetic seam.
		// The final transition pass already omits records hidden by later features.
		manifest.BiomeTransitions.RemoveAll(transition => Inflated(transition.Area, 8).Intersects(accepted.Area));
		LandmarkStyle style = ResolveStyle(request.Biome, accepted.Layout.Archetype, accepted.AnchorX, accepted.GroundY);
		Commit(accepted, style);
		if (!ValidateFootprint(accepted, style)) {
			throw new InvalidOperationException($"Vanilla Worlds Overhauled placed an incomplete {request.Biome} landmark at {accepted.AnchorX}, {accepted.GroundY}.");
		}

		GenVars.structures.AddProtectedStructure(accepted.Area, padding: 10);
		LandmarkBlueprint blueprint = BuildBlueprint(accepted);
		manifest.Landmarks.Add(new LandmarkRecord(
			request.Biome,
			accepted.Area,
			accepted.AnchorX,
			accepted.GroundY,
			accepted.Layout.Archetype,
			blueprint.Rooms.Count,
			blueprint.Rooms.Select(room => room.Level).Distinct().Count(),
			blueprint.Stairs.Count,
			FurnitureCount: 0,
			accepted.Layout.Variant));
		return true;
	}

	private static bool TryCreatePreparedSurfaceCandidate(
		LandmarkRequest request,
		LandmarkLayout layout,
		WorldPlan plan,
		int spawnX,
		SurfaceMinePlan surfaceMine,
		GenerationManifest manifest,
		out LandmarkCandidate candidate,
		bool allowTransitionOverlap = false)
	{
		LandmarkCandidate? best = null;
		int firstCenter = request.LeftX + layout.Width / 2;
		int lastCenter = request.RightX - layout.Width / 2;
		for (int centerX = firstCenter; centerX <= lastCenter; centerX += request.Biome == BiomeKind.Ocean ? 2 : 4) {
			int left = centerX - layout.Width / 2;
			int matchingSupports = 0;
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
				if (request.Biome == BiomeKind.Ocean
					? IsDryOceanSupport(x, supportY)
					: BiomeClassifier.ClassifySupport(Main.tile[x, supportY].TileType, x, supportY) == request.Biome) {
					matchingSupports++;
				}
			}

			int relief = maximumGround - minimumGround;
			if (matchingSupports < (request.Biome == BiomeKind.Ocean ? layout.Width * 3 / 5 : layout.Width * 4 / 5)
				|| minimumGround == int.MaxValue
				|| request.Biome == BiomeKind.Ocean && maximumGround < Main.worldSurface * 0.55d
				|| relief > (request.Biome == BiomeKind.Ocean ? 44 : 20)) {
				continue;
			}

			// This fallback deliberately levels a safe shelf when vanilla decoration
			// leaves a biome with no naturally calm footprint. Extend ownership above
			// the highest nearby ground so the roof cannot remain buried in a slope.
			int top = Math.Min(maximumGround - layout.AboveGroundHeight, minimumGround - 6);
			Rectangle area = new(left, top, layout.Width, maximumGround + layout.BelowGroundDepth - top);
			if (Inflated(surfaceMine.Area, 8).Intersects(area)
					|| manifest.Terraces.Any(terrace => Inflated(terrace.Area, 24).Intersects(area))
					|| manifest.Landmarks.Any(landmark => Inflated(landmark.Area, 50).Intersects(area))
					|| manifest.ForestLakeBridges.Any(bridge => Inflated(bridge.Area, 18).Intersects(area))
					|| manifest.MountainWaters.Any(water => Inflated(water.Area, 12).Intersects(area))
					|| !allowTransitionOverlap
						&& manifest.BiomeTransitions.Any(transition => Inflated(transition.Area, 8).Intersects(area))
					|| MountainBiomeGenerator.IntersectsBridgePassage(plan, area)
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
		WorldPlan plan,
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

				Rectangle area = new(
					x - layout.Width / 2,
					y - layout.AboveGroundHeight,
					layout.Width,
					layout.AboveGroundHeight + layout.BelowGroundDepth);
				if (!TileEditor.IsSafeForTerrainFeature(area)
					|| Inflated(surfaceMine.Area, 8).Intersects(area)
					|| manifest.Terraces.Any(terrace => Inflated(terrace.Area, 24).Intersects(area))
					|| manifest.Landmarks.Any(landmark => Inflated(landmark.Area, 50).Intersects(area))
					|| manifest.ForestLakeBridges.Any(bridge => Inflated(bridge.Area, 18).Intersects(area))
					|| manifest.MountainWaters.Any(water => Inflated(water.Area, 12).Intersects(area))
					|| manifest.BiomeTransitions.Any(transition => Inflated(transition.Area, 8).Intersects(area))
					|| MountainBiomeGenerator.IntersectsBridgePassage(plan, area)) {
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
		WorldPlan plan,
		int spawnX,
		SurfaceMinePlan surfaceMine,
		GenerationManifest manifest,
		out LandmarkCandidate candidate)
	{
		if (!TryFindGround(biome, layout.AboveGroundHeight, anchorX, out int groundY)) {
			candidate = default;
			return false;
		}

		int left = anchorX - layout.Width / 2;

		int matchingSupports = 0;
		int minimumGround = int.MaxValue;
		int maximumGround = int.MinValue;
		for (int x = left; x < left + layout.Width; x++) {
			if (!TryFindNearbyFloor(x, groundY, out int y)) {
				candidate = default;
				return false;
			}

			minimumGround = Math.Min(minimumGround, y);
			maximumGround = Math.Max(maximumGround, y);
			if (biome == BiomeKind.Ocean
				? IsDryOceanSupport(x, y)
				: BiomeClassifier.ClassifySupport(Main.tile[x, y].TileType, x, y) == biome) {
				matchingSupports++;
			}
		}

		int relief = maximumGround - minimumGround;
		int minimumMatchingSupports = biome == BiomeKind.Ocean ? layout.Width * 3 / 5 : layout.Width / 2;
		if (relief > (biome == BiomeKind.Ocean ? 16 : 24) || matchingSupports < minimumMatchingSupports
			|| biome == BiomeKind.Ocean && maximumGround < Main.worldSurface * 0.55d) {
			candidate = default;
			return false;
		}

		// The landmark floor follows the highest support in its footprint. Derive the
		// owned rectangle from that final floor, not from the first anchor sample;
		// otherwise a sloped cavern can put the entire house above its own bounds.
		Rectangle area = new(
			left,
			maximumGround - layout.AboveGroundHeight,
			layout.Width,
			layout.AboveGroundHeight + layout.BelowGroundDepth);
		if (Inflated(surfaceMine.Area, 8).Intersects(area)
				|| manifest.Terraces.Any(terrace => Inflated(terrace.Area, 24).Intersects(area))
				|| manifest.Landmarks.Any(landmark => Inflated(landmark.Area, 50).Intersects(area))
				|| manifest.ForestLakeBridges.Any(bridge => Inflated(bridge.Area, 18).Intersects(area))
				|| manifest.MountainWaters.Any(water => Inflated(water.Area, 12).Intersects(area))
				|| manifest.BiomeTransitions.Any(transition => Inflated(transition.Area, 8).Intersects(area))
				|| MountainBiomeGenerator.IntersectsBridgePassage(plan, area)
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
			int windowDrySupports = 0;
			for (int x = centerX - layout.Width / 2; x < centerX - layout.Width / 2 + layout.Width; x++) {
				if (!TryFindCoastalGroundSupport(x, out int supportY)) {
					continue;
				}
				windowDrySupports += IsDryOceanSupport(x, supportY) ? 1 : 0;
				minimumGround = Math.Min(minimumGround, supportY);
				maximumGround = Math.Max(maximumGround, supportY);
			}
			if (windowDrySupports < layout.Width * 3 / 5 || minimumGround == int.MaxValue) {
				continue;
			}

			completeWindows++;
			int relief = maximumGround - minimumGround;
			minimumRelief = Math.Min(minimumRelief, relief);
			if (relief > 16) {
				continue;
			}
			calmWindows++;

			int top = Math.Min(maximumGround - layout.AboveGroundHeight, minimumGround - 6);
			Rectangle area = new(
				centerX - layout.Width / 2,
				top,
				layout.Width,
				maximumGround + layout.BelowGroundDepth - top);
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

	private static LandmarkStyle ResolveStyle(
		BiomeKind biome,
		LandmarkArchetype archetype,
		int anchorX,
		int groundY)
	{
		if (biome == BiomeKind.Evil) {
			ushort support = Main.tile[anchorX, groundY].TileType;
			bool crimson = support is TileID.CrimsonGrass or TileID.Crimstone or TileID.CrimstoneBrick or TileID.Crimsand
				or TileID.CrimsonSandstone or TileID.CrimsonHardenedSand;
			return crimson
				? new LandmarkStyle(TileID.CrimstoneBrick, TileID.Crimstone, TileID.CrimstoneBrick, TileID.Crimstone, WallID.CrimstoneUnsafe, WallID.Planked, 0, TileID.Tables, 0, 0, 0, 0)
				: new LandmarkStyle(TileID.EbonstoneBrick, TileID.Ebonstone, TileID.EbonstoneBrick, TileID.Ebonstone, WallID.EbonstoneUnsafe, WallID.Planked, 0, TileID.Tables, 0, 0, 0, 0);
		}

		// Furniture styles mirror the installed 1.4.4.9 cave-house palettes where
		// those palettes exist. Other districts retain ordinary wooden furniture
		// inside biome-specific shells instead of guessing unsupported frame styles.
		return biome switch {
			BiomeKind.Forest => new LandmarkStyle(TileID.WoodBlock, TileID.WoodenBeam, TileID.WoodBlock, TileID.GrayBrick, WallID.Wood, WallID.Planked, 0, TileID.Tables, 0, 0, 0, 0),
			BiomeKind.Snow when archetype == LandmarkArchetype.SnowBuriedIgloo => new LandmarkStyle(TileID.SnowBlock, TileID.IceBlock, TileID.SnowBlock, TileID.IceBlock, WallID.SnowWallUnsafe, WallID.Stone, 19, TileID.Tables, 28, 30, 23, 25),
			BiomeKind.Snow => new LandmarkStyle(TileID.BorealWood, TileID.BorealWood, TileID.SnowBlock, TileID.IceBlock, WallID.SnowWallUnsafe, WallID.Stone, 19, TileID.Tables, 28, 30, 23, 25),
			BiomeKind.Desert => new LandmarkStyle(TileID.SandstoneBrick, TileID.SandstoneColumn, TileID.SandstoneBrick, TileID.HardenedSand, WallID.Sandstone, WallID.HardenedSand, 42, TileID.Tables2, 7, 43, 39, 39),
			BiomeKind.Jungle => new LandmarkStyle(TileID.RichMahogany, TileID.LivingMahogany, TileID.RichMahogany, TileID.JungleGrass, WallID.JungleUnsafe, WallID.Planked, 2, TileID.Tables, 2, 3, 2, 12),
			BiomeKind.Ocean => new LandmarkStyle(TileID.PalmWood, TileID.PalmWood, TileID.PalmWood, TileID.SandstoneBrick, WallID.Sandstone, WallID.LivingWoodUnsafe, 0, TileID.Tables, 0, 0, 0, 0),
			BiomeKind.Sky => new LandmarkStyle(TileID.Sunplate, TileID.Sunplate, TileID.Sunplate, TileID.Cloud, WallID.DiscWall, WallID.Cloud, 0, TileID.Tables, 0, 0, 0, 0),
			BiomeKind.Mushroom => new LandmarkStyle(TileID.MushroomBlock, TileID.MushroomBlock, TileID.MushroomBlock, TileID.MushroomGrass, WallID.MushroomUnsafe, WallID.Planked, 18, TileID.Tables, 27, 9, 7, 24),
			BiomeKind.Cavern => new LandmarkStyle(TileID.GrayBrick, TileID.WoodenBeam, TileID.StoneSlab, TileID.Stone, WallID.Stone, WallID.Planked, 0, TileID.Tables, 0, 0, 0, 0),
			BiomeKind.Underworld => new LandmarkStyle(TileID.AshWood, TileID.AshWood, TileID.HellstoneBrick, TileID.ObsidianBrick, WallID.HellstoneBrickUnsafe, WallID.ObsidianBrickUnsafe, 0, TileID.Tables, 0, 0, 0, 0),
			_ => new LandmarkStyle(TileID.GrayBrick, TileID.WoodenBeam, TileID.StoneSlab, TileID.Stone, WallID.Stone, WallID.Planked, 0, TileID.Tables, 0, 0, 0, 0)
		};
	}

	private static LandmarkLayout ResolveLayout(BiomeKind biome, int variant)
	{
		int normalized = Math.Abs(variant) % 6;
		LandmarkArchetype archetype = (LandmarkArchetype)((int)biome * 3 + normalized % 3);
		bool alternate = normalized >= 3;
		return archetype switch {
			LandmarkArchetype.ForestRangerLodge => Layout(76, 45, 12, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.BroadHall, LandmarkRoofStyle.Gable, normalized),
			LandmarkArchetype.ForestSplitHall => Layout(86, 50, 12, archetype, alternate ? LandmarkTopology.TowerWing : LandmarkTopology.TwinTower, LandmarkRoofStyle.Gable, normalized),
			LandmarkArchetype.ForestWatchHouse => Layout(70, 54, 12, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.TowerWing, LandmarkRoofStyle.SteepGable, normalized),
			LandmarkArchetype.SnowChalet => Layout(78, 49, 13, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.Terraced, LandmarkRoofStyle.SteepGable, normalized),
			LandmarkArchetype.SnowIceWatch => Layout(70, 55, 13, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.TowerWing, LandmarkRoofStyle.Spire, normalized),
			LandmarkArchetype.SnowBuriedIgloo => Layout(alternate ? 88 : 78, 30, alternate ? 37 : 31, archetype, LandmarkTopology.Buried, LandmarkRoofStyle.IglooDome, normalized),
			LandmarkArchetype.DesertCourtyard => Layout(90, 45, 12, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.BroadHall, LandmarkRoofStyle.FlatParapet, normalized),
			LandmarkArchetype.DesertCaravanserai => Layout(94, 49, 12, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.TwinTower, LandmarkRoofStyle.FlatParapet, normalized),
			LandmarkArchetype.DesertSunTower => Layout(72, 55, 12, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.TowerWing, LandmarkRoofStyle.Spire, normalized),
			LandmarkArchetype.JungleCanopyLodge => Layout(86, 49, 14, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.Terraced, LandmarkRoofStyle.Canopy, normalized),
			LandmarkArchetype.JungleStiltHall => Layout(92, 46, 16, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.BroadHall, LandmarkRoofStyle.Canopy, normalized),
			LandmarkArchetype.JungleOvergrownTower => Layout(72, 56, 14, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.TowerWing, LandmarkRoofStyle.Spire, normalized),
			LandmarkArchetype.EvilRiftChapel => Layout(78, 50, 14, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.TwinTower, LandmarkRoofStyle.Spire, normalized),
			LandmarkArchetype.EvilQuarantineKeep => Layout(88, 48, 14, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.BroadHall, LandmarkRoofStyle.Battlement, normalized),
			LandmarkArchetype.EvilBrokenSpire => Layout(70, 57, 14, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.TowerWing, LandmarkRoofStyle.Spire, normalized),
			LandmarkArchetype.OceanStiltHouse => Layout(76, 44, 18, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.BroadHall, LandmarkRoofStyle.StiltGable, normalized),
			LandmarkArchetype.OceanHarborHall => Layout(90, 45, 18, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.BroadHall, LandmarkRoofStyle.StiltGable, normalized),
			LandmarkArchetype.OceanLighthouse => Layout(68, 56, 18, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.TowerWing, LandmarkRoofStyle.Spire, normalized),
			LandmarkArchetype.SkyObservatory => Layout(82, 50, 12, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.Terraced, LandmarkRoofStyle.CloudArch, normalized),
			LandmarkArchetype.SkySunplateAerie => Layout(90, 47, 12, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.BroadHall, LandmarkRoofStyle.CloudArch, normalized),
			LandmarkArchetype.SkyCloudMonastery => Layout(86, 54, 12, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.TowerWing, LandmarkRoofStyle.CloudArch, normalized),
			LandmarkArchetype.MushroomCapHouse => Layout(78, 44, 14, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.BroadHall, LandmarkRoofStyle.MushroomCap, normalized),
			LandmarkArchetype.MushroomSporeTower => Layout(68, 54, 14, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.TowerWing, LandmarkRoofStyle.MushroomCap, normalized),
			LandmarkArchetype.MushroomMyceliumHall => Layout(88, 47, 14, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.Terraced, LandmarkRoofStyle.MushroomCap, normalized),
			LandmarkArchetype.CavernStoneDepot => Layout(86, 45, 15, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.BroadHall, LandmarkRoofStyle.StoneVault, normalized),
			LandmarkArchetype.CavernArchVault => Layout(92, 49, 16, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.Terraced, LandmarkRoofStyle.StoneVault, normalized),
			LandmarkArchetype.CavernShaftHouse => Layout(72, 56, 16, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.TowerWing, LandmarkRoofStyle.StoneVault, normalized),
			LandmarkArchetype.UnderworldAshForge => Layout(82, 48, 15, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.Terraced, LandmarkRoofStyle.Battlement, normalized),
			LandmarkArchetype.UnderworldObsidianKeep => Layout(90, 50, 15, archetype, alternate ? LandmarkTopology.Terraced : LandmarkTopology.BroadHall, LandmarkRoofStyle.Battlement, normalized),
			LandmarkArchetype.UnderworldHangingFort => Layout(76, 56, 16, archetype, alternate ? LandmarkTopology.TwinTower : LandmarkTopology.TowerWing, LandmarkRoofStyle.Spire, normalized),
			_ => Layout(76, 45, 12, LandmarkArchetype.ForestRangerLodge, LandmarkTopology.BroadHall, LandmarkRoofStyle.Gable, normalized)
		};
	}

	private static LandmarkLayout Layout(
		int width,
		int aboveGroundHeight,
		int belowGroundDepth,
		LandmarkArchetype archetype,
		LandmarkTopology topology,
		LandmarkRoofStyle roofStyle,
		int variant) => new(
		width,
		aboveGroundHeight,
		belowGroundDepth,
		RoofVariant: (int)archetype + variant * 3,
		variant,
		archetype,
		topology,
		roofStyle);

	private static void Commit(LandmarkCandidate candidate, LandmarkStyle style)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(candidate);
		PrepareFootprint(candidate, blueprint, style);
		foreach (LandmarkRoom room in blueprint.Rooms) {
			BuildRoomShell(room, style, candidate.Layout.Variant);
		}
		CarveRoomArches(blueprint);
		BuildRoofs(blueprint, style);
		BuildStairs(blueprint, style.PlatformStyle);
		BuildFoundationSupports(blueprint, style);
		BuildBiomeDetails(blueprint, style);
		CarveOpenEntrances(candidate.Area, candidate.GroundY, blueprint.LeftColumn, blueprint.RightColumn, style.Foundation);
		ClearExteriorWalls(candidate.Area, blueprint, style);
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
		int leftColumn = area.Left + 6;
		int rightColumn = area.Right - 7;
		List<LandmarkRoom> rooms = [];
		int totalWidth = rightColumn - leftColumn + 1;
		int firstCut = Math.Clamp(
			leftColumn + totalWidth / 3 + OrganicBoundary.Profile(
				anchorX,
				layout.RoofVariant ^ 0x434F_4C31,
				17,
				5,
				3,
				1),
			leftColumn + 18,
			rightColumn - 36);
		int secondCut = Math.Clamp(
			leftColumn + totalWidth * 2 / 3 + OrganicBoundary.Profile(
				anchorX,
				layout.RoofVariant ^ 0x434F_4C32,
				19,
				7,
				3,
				1),
			firstCut + 18,
			rightColumn - 18);
		int[] columnEdges = [leftColumn, firstCut, secondCut, rightColumn];

		if (layout.Topology == LandmarkTopology.Buried) {
			rooms.Add(new LandmarkRoom(
				new Rectangle(leftColumn, groundY - 13, totalWidth, 14),
				RoleFor(layout.Archetype, Level: 0, Column: 1, index: 0),
				Level: 0,
				Column: 1));
			AddGridLevel(rooms, columnEdges, groundY + 1, groundY + 11, Level: -1, [true, true, true], layout);
			bool mirror = layout.Variant % 2 != 0;
			AddGridLevel(
				rooms,
				columnEdges,
				groundY + 12,
				groundY + 22,
				Level: -2,
				mirror ? [false, true, true] : [true, true, false],
				layout);
		}
		else {
			int groundTop = groundY - 12;
			AddGridLevel(rooms, columnEdges, groundTop, groundY, Level: 0, [true, true, true], layout);
			(bool[] first, bool[] second) = OccupancyFor(layout.Topology, layout.Variant);
			int firstBottom = groundTop + 1;
			int firstTop = firstBottom - 11;
			AddGridLevel(rooms, columnEdges, firstTop, firstBottom, Level: 1, first, layout);
			if (second.Any(occupied => occupied)) {
				int secondBottom = firstTop + 1;
				int secondTop = secondBottom - 11;
				AddGridLevel(rooms, columnEdges, secondTop, secondBottom, Level: 2, second, layout);
			}
		}

		List<StairConnection> stairs = BuildVerticalConnections(rooms, layout);
		return new LandmarkBlueprint(rooms, stairs, leftColumn, rightColumn, groundY, layout);
	}

	private static void AddGridLevel(
		List<LandmarkRoom> rooms,
		IReadOnlyList<int> columnEdges,
		int top,
		int bottom,
		int Level,
		IReadOnlyList<bool> occupancy,
		LandmarkLayout layout)
	{
		for (int column = 0; column < 3; column++) {
			if (!occupancy[column]) {
				continue;
			}
			int left = columnEdges[column];
			int right = columnEdges[column + 1];
			rooms.Add(new LandmarkRoom(
				new Rectangle(left, top, right - left + 1, bottom - top + 1),
				RoleFor(layout.Archetype, Level, column, rooms.Count),
				Level,
				column));
		}
	}

	private static (bool[] First, bool[] Second) OccupancyFor(LandmarkTopology topology, int variant)
	{
		bool mirror = variant % 2 != 0;
		return topology switch {
			LandmarkTopology.BroadHall => ([true, true, true], variant >= 3 ? [false, true, false] : [false, false, false]),
			LandmarkTopology.Terraced => (mirror ? [false, true, true] : [true, true, false], mirror ? [false, false, true] : [true, false, false]),
			LandmarkTopology.TwinTower => ([true, false, true], mirror ? [false, false, true] : [true, false, false]),
			LandmarkTopology.TowerWing => (mirror ? [true, true, false] : [false, true, true], mirror ? [true, false, false] : [false, false, true]),
			_ => ([true, true, true], [false, true, false])
		};
	}

	private static List<StairConnection> BuildVerticalConnections(
		IReadOnlyList<LandmarkRoom> rooms,
		LandmarkLayout layout)
	{
		List<StairConnection> stairs = [];
		List<int> levels = rooms
			.GroupBy(room => room.Level)
			.OrderBy(group => group.Min(room => room.FloorY))
			.Select(group => group.Key)
			.ToList();
		for (int index = 1; index < levels.Count; index++) {
			List<LandmarkRoom> upperRooms = rooms.Where(room => room.Level == levels[index - 1]).ToList();
			List<LandmarkRoom> lowerRooms = rooms.Where(room => room.Level == levels[index]).ToList();
			List<List<LandmarkRoom>> upperComponents = [];
			foreach (LandmarkRoom room in upperRooms.OrderBy(room => room.Column)) {
				if (upperComponents.Count == 0 || room.Column - upperComponents[^1][^1].Column > 1) {
					upperComponents.Add([]);
				}
				upperComponents[^1].Add(room);
			}
			for (int componentIndex = 0; componentIndex < upperComponents.Count; componentIndex++) {
				List<LandmarkRoom> component = upperComponents[componentIndex];
				LandmarkRoom upper = component[0];
				LandmarkRoom lower = lowerRooms[0];
				int bestOverlap = int.MinValue;
				foreach (LandmarkRoom upperCandidate in component) {
					foreach (LandmarkRoom lowerCandidate in lowerRooms) {
						int overlap = Math.Min(upperCandidate.Shell.Right, lowerCandidate.Shell.Right)
							- Math.Max(upperCandidate.Shell.Left, lowerCandidate.Shell.Left);
						if (overlap > bestOverlap) {
							bestOverlap = overlap;
							upper = upperCandidate;
							lower = lowerCandidate;
						}
					}
				}

				int stepCount = Math.Clamp(lower.FloorY - upper.FloorY - 1, 6, 11);
				int overlapLeft = Math.Max(upper.Shell.Left, lower.Shell.Left) + 3;
				int overlapRight = Math.Min(upper.Shell.Right, lower.Shell.Right) - 4;
				int direction = (layout.RoofVariant + index + componentIndex) % 2 == 0 ? 1 : -1;
				if (overlapRight - overlapLeft < stepCount + 2) {
					direction = upper.Shell.Center.X < lower.Shell.Center.X ? 1 : -1;
				}
				int landingX = direction == 1 ? overlapLeft : overlapRight;
				stairs.Add(new StairConnection(landingX, upper.FloorY, direction, stepCount));
			}
		}
		return stairs;
	}

	private static RoomRole RoleFor(
		LandmarkArchetype archetype,
		int Level,
		int Column,
		int index)
	{
		BiomeKind biome = (BiomeKind)((int)archetype / 3);
		RoomRole[] palette = biome switch {
			BiomeKind.Forest => [RoomRole.Commons, RoomRole.Workshop, RoomRole.Storehouse, RoomRole.Study, RoomRole.Lookout],
			BiomeKind.Snow => [RoomRole.Hearth, RoomRole.Storehouse, RoomRole.Workshop, RoomRole.Study, RoomRole.Lookout],
			BiomeKind.Desert => [RoomRole.Commons, RoomRole.Shrine, RoomRole.Storehouse, RoomRole.Study, RoomRole.Lookout],
			BiomeKind.Jungle => [RoomRole.Greenhouse, RoomRole.Workshop, RoomRole.Commons, RoomRole.Study, RoomRole.Lookout],
			BiomeKind.Evil => [RoomRole.Shrine, RoomRole.Storehouse, RoomRole.Workshop, RoomRole.Study, RoomRole.Lookout],
			BiomeKind.Ocean => [RoomRole.Commons, RoomRole.Storehouse, RoomRole.Workshop, RoomRole.Hearth, RoomRole.Lookout],
			BiomeKind.Sky => [RoomRole.Observatory, RoomRole.Study, RoomRole.Commons, RoomRole.Workshop, RoomRole.Lookout],
			BiomeKind.Mushroom => [RoomRole.Greenhouse, RoomRole.Commons, RoomRole.Storehouse, RoomRole.Study, RoomRole.Lookout],
			BiomeKind.Cavern => [RoomRole.Workshop, RoomRole.Storehouse, RoomRole.Forge, RoomRole.Commons, RoomRole.Lookout],
			BiomeKind.Underworld => [RoomRole.Forge, RoomRole.Workshop, RoomRole.Storehouse, RoomRole.Shrine, RoomRole.Lookout],
			_ => [RoomRole.Commons, RoomRole.Workshop, RoomRole.Study, RoomRole.Lookout]
		};
		return palette[Math.Abs(index + Level * 3 + Column + (int)archetype) % palette.Length];
	}

	private static void PrepareFootprint(
		LandmarkCandidate candidate,
		LandmarkBlueprint blueprint,
		LandmarkStyle style)
	{
		Rectangle area = candidate.Area;
		int groundY = candidate.GroundY;
		for (int x = area.Left; x < area.Right; x++) {
			int ownedTop = groundY;
			foreach (LandmarkRoom room in blueprint.Rooms) {
				if (x < room.Shell.Left - 4 || x > room.Shell.Right + 3 || room.Level < 0) {
					continue;
				}
				int roofAllowance = blueprint.Layout.RoofStyle switch {
					LandmarkRoofStyle.Spire => 13,
					LandmarkRoofStyle.SteepGable => 11,
					LandmarkRoofStyle.MushroomCap or LandmarkRoofStyle.CloudArch => 10,
					_ => 8
				};
				ownedTop = Math.Min(ownedTop, room.Shell.Top - roofAllowance);
			}
			if (ownedTop < groundY) {
				int clearTop = Math.Clamp(
					ownedTop + OrganicBoundary.Profile(
						x,
						candidate.AnchorX ^ candidate.GroundY ^ 0x434C_4541,
						19,
						5,
						3,
						1),
					area.Top + 1,
					groundY - 1);
				for (int y = clearTop; y < groundY; y++) {
					TileEditor.ClearTerrain(x, y, clearWall: true);
				}
			}
			int edgeDistance = Math.Min(x - area.Left, area.Right - 1 - x);
			int foundationDepth = Math.Clamp(
				3 + OrganicBoundary.Profile(
					x,
					candidate.AnchorX ^ candidate.GroundY ^ 0x464F_554E,
					23,
					7,
					3,
					1)
					- Math.Max(0, 4 - edgeDistance),
				1,
				7);
			for (int depth = 0; depth < foundationDepth; depth++) {
				ushort material = depth >= 3 && OrganicBoundary.Field(
					x,
					groundY + depth,
					candidate.AnchorX ^ 0x424C_454E,
					17,
					5) > 0.66d
					? style.Pillar
					: style.Foundation;
				TileEditor.SetTerrain(x, groundY + depth, material);
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
					TileEditor.SetTerrain(x, y, style.Foundation);
				}
			}
		}

		TileEditor.SetSlopedTerrain(area.Left, groundY, style.Foundation, SlopeType.SlopeDownRight);
		TileEditor.SetSlopedTerrain(area.Right - 1, groundY, style.Foundation, SlopeType.SlopeDownLeft);
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
		foreach (IGrouping<int, LandmarkRoom> level in blueprint.Rooms.GroupBy(room => room.Level)) {
			List<LandmarkRoom> rooms = level.OrderBy(room => room.Shell.Left).ToList();
			for (int index = 1; index < rooms.Count; index++) {
				LandmarkRoom left = rooms[index - 1];
				LandmarkRoom right = rooms[index];
				if (right.Shell.Left - left.Shell.Right > 2) {
					continue;
				}
				int dividerX = right.Shell.Left;
				int floorY = Math.Min(left.FloorY, right.FloorY);
				for (int x = dividerX - 2; x <= dividerX + 2; x++) {
					int archLift = Math.Abs(x - dividerX) == 2 ? 1 : 0;
					for (int y = floorY - 7 + archLift; y <= floorY; y++) {
						TileEditor.ClearTerrain(x, y);
					}
				}
			}
		}
	}

	private static void BuildRoofs(LandmarkBlueprint blueprint, LandmarkStyle style)
	{
		foreach (LandmarkRoom room in blueprint.Rooms) {
			if (!ShouldBuildRoof(blueprint, room)) {
				continue;
			}
			switch (blueprint.Layout.RoofStyle) {
				case LandmarkRoofStyle.FlatParapet:
				case LandmarkRoofStyle.Battlement:
					BuildFlatRoof(room.Shell, style, blueprint.Layout.RoofStyle == LandmarkRoofStyle.Battlement);
					break;
				case LandmarkRoofStyle.CloudArch:
				case LandmarkRoofStyle.MushroomCap:
				case LandmarkRoofStyle.StoneVault:
				case LandmarkRoofStyle.IglooDome:
					BuildCurvedRoof(room.Shell, style, blueprint.Layout.RoofStyle);
					break;
				case LandmarkRoofStyle.SteepGable:
				case LandmarkRoofStyle.Spire:
					BuildGabledRoof(room.Shell, style, steep: true);
					break;
				default:
					BuildGabledRoof(room.Shell, style, steep: false);
					break;
			}
		}
	}

	private static bool ShouldBuildRoof(LandmarkBlueprint blueprint, LandmarkRoom room) =>
		!blueprint.Rooms.Any(upper =>
			upper.FloorY < room.FloorY
			&& upper.Shell.Bottom <= room.Shell.Top + 3
			&& upper.Shell.Left < room.Shell.Right - 3
			&& upper.Shell.Right > room.Shell.Left + 3);

	private static void BuildGabledRoof(Rectangle room, LandmarkStyle style, bool steep)
	{
		int centerX = room.Center.X;
		int halfWidth = Math.Max(1, room.Width / 2 + 2);
		int rise = steep
			? Math.Clamp(halfWidth * 2 / 3, 7, 13)
			: Math.Clamp(halfWidth / 2, 4, 9);
		int wallSeed = room.Center.X ^ room.Center.Y * 31 ^ 0x4741_424C;
		for (int x = room.Left - 2; x <= room.Right + 1; x++) {
			int distance = Math.Abs(x - centerX);
			int roofY = room.Top - rise + (int)Math.Round(distance * rise / (double)halfWidth);
			// Terraria names slopes by the solid corner. A roof rising toward the
			// center therefore uses DownRight on its left face and DownLeft on its
			// right face.
			SlopeType slope = distance % 2 == 0 || x == centerX
				? SlopeType.Solid
				: x < centerX ? SlopeType.SlopeDownRight : SlopeType.SlopeDownLeft;
			TileEditor.SetSlopedTerrain(x, roofY, style.Roof, slope);
			TileEditor.SetTerrain(x, roofY + 1, distance % 4 == 0 ? style.RoofAccent : style.Roof);
			for (int y = roofY + 2; y < room.Top; y++) {
				TileEditor.ClearTerrain(x, y);
				double accentField = OrganicBoundary.Field(x, y, wallSeed, 11, 4);
				int edgeBias = OrganicBoundary.Profile(y, wallSeed ^ 0x4544_4745, 13, 5, 3, 1);
				bool accent = distance + edgeBias > halfWidth / 2 && accentField > 0.47d;
				TileEditor.SetWall(x, y, accent ? style.AccentWall : style.Wall);
			}
		}
	}

	private static void BuildCurvedRoof(
		Rectangle room,
		LandmarkStyle style,
		LandmarkRoofStyle roofStyle)
	{
		int overhang = roofStyle == LandmarkRoofStyle.MushroomCap ? 5 : 3;
		int halfWidth = room.Width / 2 + overhang;
		int centerX = room.Center.X;
		int rise = roofStyle switch {
			LandmarkRoofStyle.IglooDome => Math.Clamp(room.Width / 5, 8, 13),
			LandmarkRoofStyle.MushroomCap => Math.Clamp(room.Width / 6, 7, 11),
			_ => Math.Clamp(room.Width / 7, 6, 10)
		};
		int seed = centerX ^ room.Top * 31 ^ (int)roofStyle * 977;
		for (int x = centerX - halfWidth; x <= centerX + halfWidth; x++) {
			double normalized = (x - centerX) / (double)Math.Max(1, halfWidth);
			double curve = Math.Sqrt(Math.Max(0d, 1d - normalized * normalized));
			int roofY = room.Top - (int)Math.Round(rise * curve)
				+ OrganicBoundary.Profile(x, seed, 17, 5, 1, 1);
			SlopeType slope = x < centerX ? SlopeType.SlopeDownRight
				: x > centerX ? SlopeType.SlopeDownLeft
				: SlopeType.Solid;
			if (Math.Abs(x - centerX) % 3 == 0) {
				slope = SlopeType.Solid;
			}
			TileEditor.SetSlopedTerrain(x, roofY, style.Roof, slope);
			TileEditor.SetTerrain(x, roofY + 1, OrganicBoundary.Field(x, roofY, seed, 13, 5) > 0.56d
				? style.RoofAccent
				: style.Roof);
			FillRoofInterior(room, style, x, roofY + 2, seed);
		}
	}

	private static void BuildFlatRoof(Rectangle room, LandmarkStyle style, bool battlement)
	{
		int seed = room.Center.X ^ room.Top * 31 ^ 0x464C_4154;
		for (int x = room.Left - 3; x <= room.Right + 2; x++) {
			int roofY = room.Top - 3 + OrganicBoundary.Profile(x, seed, 29, 7, 1, 0);
			SlopeType slope = x == room.Left - 3 ? SlopeType.SlopeDownRight
				: x == room.Right + 2 ? SlopeType.SlopeDownLeft
				: SlopeType.Solid;
			TileEditor.SetSlopedTerrain(x, roofY, style.Roof, slope);
			TileEditor.SetTerrain(x, roofY + 1, style.RoofAccent);
			FillRoofInterior(room, style, x, roofY + 2, seed);
		}
		if (!battlement) {
			return;
		}
		for (int x = room.Left - 1; x < room.Right;) {
			TileEditor.SetTerrain(x, room.Top - 5, style.RoofAccent);
			x += 4 + Math.Abs(HashNoise(x, room.Center.X)) % 3;
		}
	}

	private static void FillRoofInterior(
		Rectangle room,
		LandmarkStyle style,
		int x,
		int startY,
		int seed)
	{
		for (int y = startY; y < room.Top; y++) {
			TileEditor.ClearTerrain(x, y);
			double accentField = OrganicBoundary.Field(x, y, seed, 11, 4);
			TileEditor.SetWall(x, y, accentField > 0.58d ? style.AccentWall : style.Wall);
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
				TileEditor.ClearTerrain(landingStart + offset, stair.FloorY);
			}
		}

		// Clear every flight before placing any platform. A lower connector can
		// overlap the landing envelope of an upper flight in compact towers; doing
		// this in one pass used to erase the final step of the earlier staircase.
		foreach (StairConnection stair in blueprint.Stairs) {
			int landingStart = stair.Direction == 1 ? stair.LandingX - 3 : stair.LandingX;
			for (int offset = 0; offset < 4; offset++) {
				TileEditor.TryPlacePlatformForced(landingStart + offset, stair.FloorY, platformStyle);
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
		if (blueprint.Layout.Topology == LandmarkTopology.Buried) {
			return;
		}
		int cadence = 5 + Math.Abs(blueprint.LeftColumn + blueprint.RightColumn) % 2;
		for (int x = blueprint.LeftColumn + 2; x <= blueprint.RightColumn - 2; x += cadence) {
			for (int y = blueprint.GroundY + 3; y <= blueprint.GroundY + 7; y++) {
				TileEditor.SetTerrain(x, y, style.Pillar);
			}
		}
	}

	private static void BuildBiomeDetails(LandmarkBlueprint blueprint, LandmarkStyle style)
	{
		BiomeKind biome = (BiomeKind)((int)blueprint.Layout.Archetype / 3);
		switch (biome) {
			case BiomeKind.Forest:
				BuildPorch(blueprint, style, width: 9);
				break;
			case BiomeKind.Snow:
				if (blueprint.Layout.Archetype == LandmarkArchetype.SnowBuriedIgloo) {
					BuildBuriedIceRibs(blueprint, style);
				}
				else {
					BuildPorch(blueprint, style, width: 7);
				}
				break;
			case BiomeKind.Desert:
				BuildExteriorColumns(blueprint, style, height: 11);
				break;
			case BiomeKind.Jungle:
				BuildExteriorColumns(blueprint, style, height: 13);
				BuildEaveVines(blueprint);
				break;
			case BiomeKind.Evil:
				BuildExteriorColumns(blueprint, style, height: 15);
				break;
			case BiomeKind.Ocean:
				BuildStilts(blueprint, style);
				BuildPorch(blueprint, style, width: 11);
				break;
			case BiomeKind.Sky:
				BuildCloudFooting(blueprint);
				break;
			case BiomeKind.Mushroom:
				BuildEaveVines(blueprint);
				break;
			case BiomeKind.Cavern:
			case BiomeKind.Underworld:
				BuildExteriorColumns(blueprint, style, height: 12);
				break;
		}
	}

	private static void BuildPorch(LandmarkBlueprint blueprint, LandmarkStyle style, int width)
	{
		foreach ((int centerX, int direction) in new[] { (blueprint.LeftColumn, -1), (blueprint.RightColumn, 1) }) {
			for (int step = 1; step <= width; step++) {
				int x = centerX + direction * step;
				int y = blueprint.GroundY - Math.Max(0, (step - 3) / 4);
				TileEditor.ClearTerrain(x, y - 1);
				if (step > width - 3) {
					TileEditor.TryPlacePlatformForced(x, y, style.PlatformStyle);
				}
				else {
					TileEditor.SetTerrain(x, y, style.Foundation);
				}
			}
		}
	}

	private static void BuildExteriorColumns(LandmarkBlueprint blueprint, LandmarkStyle style, int height)
	{
		foreach (int x in new[] { blueprint.LeftColumn, blueprint.LeftColumn + 1, blueprint.RightColumn - 1, blueprint.RightColumn }) {
			int top = blueprint.GroundY - height
				+ OrganicBoundary.Profile(x, blueprint.Layout.RoofVariant ^ 0x434F_4C55, 13, 5, 2, 1);
			for (int y = top; y <= blueprint.GroundY + 4; y++) {
				TileEditor.SetTerrain(x, y, style.Pillar);
			}
		}
	}

	private static void BuildEaveVines(LandmarkBlueprint blueprint)
	{
		int seed = blueprint.Layout.RoofVariant ^ blueprint.GroundY ^ 0x5649_4E45;
		foreach (LandmarkRoom room in blueprint.Rooms.Where(room => ShouldBuildRoof(blueprint, room))) {
			for (int x = room.Shell.Left + 3; x < room.Shell.Right - 3;) {
				int ceilingY = room.Shell.Top - 1;
				int minimumY = blueprint.Layout.Topology == LandmarkTopology.Buried
					? room.Shell.Top - 6
					: room.Shell.Top - 15;
				for (int y = ceilingY; y >= minimumY; y--) {
					if (!TileEditor.IsSolid(x, y)) {
						continue;
					}
					int length = 4 + Math.Abs(HashNoise(x, seed)) % 9;
					for (int offset = 1; offset <= length; offset++) {
						int vineY = y + offset;
						if (Main.tile[x, vineY].HasTile || Main.tile[x, vineY].LiquidAmount > 0) {
							break;
						}
						TileEditor.SetTerrain(x, vineY, biomeVine(blueprint.Layout.Archetype));
					}
					break;
				}
				x += 5 + Math.Abs(HashNoise(x, seed ^ 0x4741_5021)) % 5;
			}
		}

		static ushort biomeVine(LandmarkArchetype archetype) => archetype switch {
			LandmarkArchetype.JungleCanopyLodge or LandmarkArchetype.JungleStiltHall or LandmarkArchetype.JungleOvergrownTower => TileID.JungleVines,
			LandmarkArchetype.MushroomCapHouse or LandmarkArchetype.MushroomSporeTower or LandmarkArchetype.MushroomMyceliumHall => TileID.MushroomVines,
			_ => TileID.Vines
		};
	}

	private static void BuildBuriedIceRibs(LandmarkBlueprint blueprint, LandmarkStyle style)
	{
		foreach (LandmarkRoom room in blueprint.Rooms.Where(room => room.Level < 0)) {
			for (int y = room.Shell.Top + 2; y < room.Shell.Bottom - 2; y += 5) {
				TileEditor.SetTerrain(room.Shell.Left + 1, y, style.RoofAccent);
				TileEditor.SetTerrain(room.Shell.Right - 2, y, style.RoofAccent);
			}
		}
	}

	private static void BuildStilts(LandmarkBlueprint blueprint, LandmarkStyle style)
	{
		for (int x = blueprint.LeftColumn + 3; x <= blueprint.RightColumn - 3;) {
			int depth = 8 + Math.Abs(HashNoise(x, blueprint.GroundY ^ 0x5354_494C)) % 9;
			for (int y = blueprint.GroundY + 3; y <= blueprint.GroundY + depth; y++) {
				TileEditor.SetTerrain(x, y, style.Pillar);
			}
			x += 7 + Math.Abs(HashNoise(x, blueprint.GroundY)) % 5;
		}
	}

	private static void BuildCloudFooting(LandmarkBlueprint blueprint)
	{
		for (int x = blueprint.LeftColumn - 4; x <= blueprint.RightColumn + 4; x++) {
			int depth = 2 + Math.Max(0, OrganicBoundary.Profile(
				x,
				blueprint.Layout.RoofVariant ^ 0x434C_4F55,
				21,
				7,
				4,
				1));
			for (int y = blueprint.GroundY + 3; y < blueprint.GroundY + 3 + depth; y++) {
				double cloudField = OrganicBoundary.Field(
					x,
					y,
					blueprint.Layout.RoofVariant ^ 0x434C_4D58,
					13,
					5);
				TileEditor.SetTerrain(x, y, cloudField > 0.68d ? TileID.RainCloud : TileID.Cloud);
			}
		}
	}

	private static int FurnishLandmark(LandmarkRecord landmark)
	{
		LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.Archetype, landmark.AnchorX, landmark.AnchorY);
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
		ClearExteriorWalls(landmark.Area, blueprint, style);
		TileEditor.Frame(landmark.Area, border: 3);
		int retainedFurnitureTiles = CountFurnitureTiles(landmark.Area);
		if (retainedFurnitureTiles < 6) {
			throw new InvalidOperationException(
				$"Vanilla Worlds Overhauled could not retain enough furnishing in the {landmark.Biome} landmark at "
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
			case RoomRole.Hearth:
				count += TryPlaceFurniture(room, centerX, TileID.Campfire, 0) ? 1 : 0;
				count += TryPlaceFurniture(room, centerX + 6, TileID.Benches, 0) ? 1 : 0;
				break;
			case RoomRole.Storehouse:
				count += TryPlaceFurniture(room, centerX - 4, TileID.Kegs, 0) ? 1 : 0;
				count += TryPlaceFurniture(room, centerX + 5, TileID.Loom, 0) ? 1 : 0;
				break;
			case RoomRole.Greenhouse:
				count += TryPlaceFurniture(room, centerX - 4, style.TableTile, style.TableStyle) ? 1 : 0;
				count += TryPlaceFurniture(room, centerX + 5, TileID.Chairs, style.ChairStyle) ? 1 : 0;
				break;
			case RoomRole.Shrine:
				count += TryPlaceFurniture(room, centerX, TileID.Benches, 0) ? 1 : 0;
				count += TryPlaceFurniture(room, centerX + 6, TileID.Bookcases, style.BookcaseStyle) ? 1 : 0;
				break;
			case RoomRole.Forge:
				count += TryPlaceFurniture(room, centerX - 4, TileID.Anvils, 0) ? 1 : 0;
				count += TryPlaceFurniture(room, centerX + 5, TileID.WorkBenches, style.WorkbenchStyle) ? 1 : 0;
				break;
			case RoomRole.Observatory:
				count += TryPlaceFurniture(room, centerX - 4, TileID.Bookcases, style.BookcaseStyle) ? 1 : 0;
				count += TryPlaceFurniture(room, centerX + 5, TileID.Pianos, style.BookcaseStyle) ? 1 : 0;
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
		LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.Archetype, landmark.AnchorX, landmark.AnchorY);
		BuildStairs(blueprint, style.PlatformStyle);
		ClearExteriorWalls(landmark.Area, blueprint, style);
	}

	private static void ClearClosedDoors(Rectangle area)
	{
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.ClosedDoor) {
					TileEditor.ClearTerrain(x, y);
				}
			}
		}
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

	private static void ClearExteriorWalls(
		Rectangle area,
		LandmarkBlueprint blueprint,
		LandmarkStyle style)
	{
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < blueprint.GroundY; y++) {
				bool interior = blueprint.Rooms.Any(room =>
					x > room.Shell.Left && x < room.Shell.Right - 1
					&& y > room.Shell.Top && y < room.Shell.Bottom - 1)
					|| blueprint.Rooms.Any(room => IsInsideRoofEnvelope(room.Shell, style, x, y));
				if (!interior) {
					Main.tile[x, y].WallType = WallID.None;
				}
			}
		}
	}

	private static bool IsInsideRoofEnvelope(
		Rectangle room,
		LandmarkStyle style,
		int x,
		int y)
	{
		if (x < room.Left - 4 || x > room.Right + 3 || y >= room.Top) {
			return false;
		}
		for (int roofY = Math.Max(45, room.Top - 18); roofY < room.Top; roofY++) {
			Tile tile = Main.tile[x, roofY];
			ushort type = tile.TileType;
			if (tile.HasTile && (type == style.Roof || type == style.RoofAccent)) {
				return y > roofY + 1;
			}
		}
		return false;
	}

	private static bool ValidateFootprint(LandmarkCandidate candidate, LandmarkStyle style)
	{
		LandmarkBlueprint blueprint = BuildBlueprint(candidate);
		int foundationColumns = 0;
		int slopedRoofTiles = 0;
		int slopedPlatforms = 0;
		for (int x = candidate.Area.Left; x < candidate.Area.Right; x++) {
			foundationColumns += HasType(x, candidate.GroundY, style.Foundation) ? 1 : 0;
			for (int y = candidate.Area.Top; y < candidate.Area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				if (y < candidate.GroundY && tile.HasTile && tile.TileType is ushort type
					&& (type == style.Roof || type == style.RoofAccent)
					&& tile.Slope != SlopeType.Solid) {
					slopedRoofTiles++;
				}
				if (tile.HasTile && tile.TileType == TileID.Platforms && tile.Slope != SlopeType.Solid) {
					slopedPlatforms++;
				}
			}
		}
		int minimumRoofSlopes = candidate.Layout.RoofStyle is LandmarkRoofStyle.FlatParapet or LandmarkRoofStyle.Battlement ? 4 : 8;
		if (foundationColumns < candidate.Area.Width * 4 / 5
			|| slopedRoofTiles < minimumRoofSlopes
			|| slopedPlatforms < 6) {
			return false;
		}
		int downLeft = 0;
		int downRight = 0;
		foreach (LandmarkRoom room in blueprint.Rooms) {
			// Flat roofs place their two facing slopes on the outer overhang at
			// Shell.Left - 3 and Shell.Right + 2. Include the complete authored
			// roof envelope instead of rejecting otherwise complete battlements.
			for (int x = room.Shell.Left - 4; x <= room.Shell.Right + 3; x++) {
				for (int y = candidate.Area.Top; y < room.Shell.Top; y++) {
					Tile tile = Main.tile[x, y];
					if (!tile.HasTile || tile.TileType != style.Roof && tile.TileType != style.RoofAccent) {
						continue;
					}
					downLeft += tile.Slope == SlopeType.SlopeDownLeft ? 1 : 0;
					downRight += tile.Slope == SlopeType.SlopeDownRight ? 1 : 0;
				}
			}
		}
		int minimumFacingSlopes = candidate.Layout.RoofStyle is LandmarkRoofStyle.FlatParapet or LandmarkRoofStyle.Battlement ? 2 : 4;
		if (downLeft < minimumFacingSlopes || downRight < minimumFacingSlopes) {
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

	private static int HashNoise(int value, int seed) => HashNoise(value, 0, seed);

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
		ushort Roof,
		ushort RoofAccent,
		ushort Wall,
		ushort AccentWall,
		int PlatformStyle,
		ushort TableTile,
		int TableStyle,
		int ChairStyle,
		int WorkbenchStyle,
		int BookcaseStyle);

	private readonly record struct LandmarkLayout(
		int Width,
		int AboveGroundHeight,
		int BelowGroundDepth,
		int RoofVariant,
		int Variant,
		LandmarkArchetype Archetype,
		LandmarkTopology Topology,
		LandmarkRoofStyle RoofStyle);

	private enum LandmarkTopology
	{
		BroadHall,
		Terraced,
		TwinTower,
		TowerWing,
		Buried
	}

	private enum LandmarkRoofStyle
	{
		Gable,
		SteepGable,
		FlatParapet,
		Canopy,
		Spire,
		StiltGable,
		CloudArch,
		MushroomCap,
		StoneVault,
		Battlement,
		IglooDome
	}

	private enum RoomRole
	{
		Workshop,
		Commons,
		Study,
		Lookout,
		Hearth,
		Storehouse,
		Greenhouse,
		Shrine,
		Forge,
		Observatory
	}

	private readonly record struct LandmarkRoom(Rectangle Shell, RoomRole Role, int Level, int Column)
	{
		public int FloorY => Shell.Bottom - 2;
		public bool IsUpper => Level > 0;
	}

	private readonly record struct StairConnection(int LandingX, int FloorY, int Direction, int StepCount);

	private sealed record LandmarkBlueprint(
		IReadOnlyList<LandmarkRoom> Rooms,
		IReadOnlyList<StairConnection> Stairs,
		int LeftColumn,
		int RightColumn,
		int GroundY,
		LandmarkLayout Layout);
}
