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
	private const int MaximumStructureHeight = 30;
	private const int CandidateBudget = 420;
	private const int LandmarkSeedSalt = 0x1D4B_62F3;

	public static void Apply(WorldPlan plan, SurfaceMinePlan surfaceMine, GenerationManifest manifest, GenerationProgress progress)
	{
		List<LandmarkRequest> requests = BuildRequests(plan);
		for (int index = 0; index < requests.Count; index++) {
			LandmarkRequest request = requests[index];
			UnifiedRandom random = new(MixSeed(plan.GenerationSeed, LandmarkSeedSalt, index));
			if (!TryPlaceBest(request, plan.SpawnX, surfaceMine, manifest, random) && request.Required) {
				throw new InvalidOperationException($"Richer Biomes could not place the required {request.Biome} landmark.");
			}
			progress.Set((double)(index + 1) / requests.Count);
		}
	}

	public static void Furnish(GenerationManifest manifest, GenerationProgress progress)
	{
		for (int index = 0; index < manifest.Landmarks.Count; index++) {
			LandmarkRecord landmark = manifest.Landmarks[index];
			LandmarkLayout layout = ResolveLayout(landmark.Biome);
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

	private static List<LandmarkRequest> BuildRequests(WorldPlan plan)
	{
		int inlandLeft = plan.CoastMargin + 30;
		int inlandRight = Main.maxTilesX - plan.CoastMargin - 31;
		return [
			new(BiomeKind.Forest, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Snow, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Desert, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Jungle, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Evil, inlandLeft, inlandRight, Required: true),
			new(BiomeKind.Ocean, 75, plan.CoastMargin + 30, Required: true),
			new(BiomeKind.Ocean, Main.maxTilesX - plan.CoastMargin - 31, Main.maxTilesX - 76, Required: true),
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
		LandmarkLayout layout = ResolveLayout(request.Biome);
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
			FurnitureCount: 0));
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
		for (int centerX = firstCenter; centerX <= lastCenter; centerX += 4) {
			int left = centerX - layout.Width / 2;
			int matchingSupports = 0;
			int minimumGround = int.MaxValue;
			int maximumGround = int.MinValue;
			for (int x = left; x < left + layout.Width; x++) {
				bool found = request.Biome == BiomeKind.Sky
					? BiomeClassifier.TryFindSurfaceSupport(x, out int supportY)
					: BiomeClassifier.TryFindGroundSupport(x, out supportY);
				if (!found) {
					continue;
				}
				minimumGround = Math.Min(minimumGround, supportY);
				maximumGround = Math.Max(maximumGround, supportY);
				if (BiomeClassifier.ClassifySupport(Main.tile[x, supportY].TileType, x, supportY) == request.Biome) {
					matchingSupports++;
				}
			}

			if (matchingSupports < layout.Width / 3 || minimumGround == int.MaxValue) {
				continue;
			}

			// This fallback deliberately levels a safe shelf when vanilla decoration
			// leaves a biome with no naturally calm footprint. Extend ownership above
			// the highest nearby ground so the roof cannot remain buried in a slope.
			int top = Math.Min(maximumGround - layout.Height, minimumGround - 6);
			Rectangle area = new(left, top, layout.Width, maximumGround + 30 - top);
			if (Inflated(surfaceMine.Area, 8).Intersects(area)
				|| manifest.Terraces.Any(terrace => Inflated(terrace.Area, 24).Intersects(area))
				|| manifest.Landmarks.Any(landmark => Inflated(landmark.Area, 50).Intersects(area))
				|| !TileEditor.IsSafeForTerrainFeature(area)) {
				continue;
			}

			int relief = maximumGround - minimumGround;
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

				Rectangle area = new(x - layout.Width / 2, y - layout.Height, layout.Width, layout.Height + 30);
				if (!TileEditor.IsSafeForTerrainFeature(area)
					|| Inflated(surfaceMine.Area, 8).Intersects(area)
					|| manifest.Terraces.Any(terrace => Inflated(terrace.Area, 24).Intersects(area))
					|| manifest.Landmarks.Any(landmark => Inflated(landmark.Area, 50).Intersects(area))) {
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
		int minimumGround = int.MaxValue;
		int maximumGround = int.MinValue;
		for (int x = left; x < left + layout.Width; x++) {
			if (!TryFindNearbyFloor(x, groundY, out int y)) {
				candidate = default;
				return false;
			}

			minimumGround = Math.Min(minimumGround, y);
			maximumGround = Math.Max(maximumGround, y);
			if (BiomeClassifier.ClassifySupport(Main.tile[x, y].TileType, x, y) == biome || biome == BiomeKind.Ocean) {
				matchingSupports++;
			}
		}

		int relief = maximumGround - minimumGround;
		if (relief > 24 || matchingSupports < layout.Width / 2) {
			candidate = default;
			return false;
		}

		// The landmark floor follows the highest support in its footprint. Derive the
		// owned rectangle from that final floor, not from the first anchor sample;
		// otherwise a sloped cavern can put the entire house above its own bounds.
		Rectangle area = new(left, maximumGround - layout.Height, layout.Width, layout.Height + 30);
		if (Inflated(surfaceMine.Area, 8).Intersects(area)
			|| manifest.Terraces.Any(terrace => Inflated(terrace.Area, 24).Intersects(area))
			|| manifest.Landmarks.Any(landmark => Inflated(landmark.Area, 50).Intersects(area))
			|| !TileEditor.IsSafeForTerrainFeature(area)) {
			candidate = default;
			return false;
		}

		int distanceFromSpawn = Math.Abs(anchorX - spawnX);
		int score = distanceFromSpawn / 30 - relief * 18 + matchingSupports * 3;
		candidate = new LandmarkCandidate(biome, anchorX, maximumGround, area, layout, score);
		return true;
	}

	private static bool TryFindGround(BiomeKind biome, int structureHeight, int x, out int groundY)
	{
		if (!WorldGen.InWorld(x, 50, 45)) {
			groundY = 0;
			return false;
		}

		if (biome is BiomeKind.Forest or BiomeKind.Snow or BiomeKind.Desert or BiomeKind.Jungle or BiomeKind.Evil or BiomeKind.Ocean or BiomeKind.Sky) {
			bool foundSurface = biome == BiomeKind.Sky
				? BiomeClassifier.TryFindSurfaceSupport(x, out int y)
				: BiomeClassifier.TryFindGroundSupport(x, out y);
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
				? new LandmarkStyle(TileID.CrimstoneBrick, TileID.Crimstone, WallID.CrimstoneUnsafe, WallID.Planked)
				: new LandmarkStyle(TileID.EbonstoneBrick, TileID.Ebonstone, WallID.EbonstoneUnsafe, WallID.Planked);
		}

		return biome switch {
			BiomeKind.Forest => new LandmarkStyle(TileID.LivingWood, TileID.LivingWood, WallID.LivingWoodUnsafe, WallID.Planked),
			BiomeKind.Snow => new LandmarkStyle(TileID.BorealWood, TileID.BorealWood, WallID.BorealWood, WallID.Planked),
			BiomeKind.Desert => new LandmarkStyle(TileID.SandstoneBrick, TileID.SandstoneColumn, WallID.SandstoneBrick, WallID.Planked),
			BiomeKind.Jungle => new LandmarkStyle(TileID.LivingMahogany, TileID.RichMahogany, WallID.JungleUnsafe, WallID.Planked),
			BiomeKind.Ocean => new LandmarkStyle(TileID.PalmWood, TileID.PalmWood, WallID.PalmWood, WallID.Planked),
			BiomeKind.Sky => new LandmarkStyle(TileID.Sunplate, TileID.Sunplate, WallID.DiscWall, WallID.Cloud),
			BiomeKind.Mushroom => new LandmarkStyle(TileID.MushroomBlock, TileID.MushroomBlock, WallID.MushroomUnsafe, WallID.Planked),
			BiomeKind.Cavern => new LandmarkStyle(TileID.GrayBrick, TileID.WoodenBeam, WallID.Stone, WallID.Planked),
			BiomeKind.Underworld => new LandmarkStyle(TileID.AshWood, TileID.AshWood, WallID.AshWood, WallID.Planked),
			_ => new LandmarkStyle(TileID.GrayBrick, TileID.WoodenBeam, WallID.Stone, WallID.Planked)
		};
	}

	private static LandmarkLayout ResolveLayout(BiomeKind biome) => biome switch {
		BiomeKind.Forest => new LandmarkLayout(45, 24, 3, 0),
		BiomeKind.Snow => new LandmarkLayout(41, 25, 3, 1),
		BiomeKind.Desert => new LandmarkLayout(49, 24, 3, 2),
		BiomeKind.Jungle => new LandmarkLayout(43, 27, 4, 3),
		BiomeKind.Evil => new LandmarkLayout(39, 23, 2, 4),
		BiomeKind.Ocean => new LandmarkLayout(47, 22, 3, 5),
		BiomeKind.Sky => new LandmarkLayout(51, 24, 4, 6),
		BiomeKind.Mushroom => new LandmarkLayout(41, 23, 2, 7),
		BiomeKind.Cavern => new LandmarkLayout(48, 22, 3, 8),
		BiomeKind.Underworld => new LandmarkLayout(45, 22, 3, 9),
		_ => new LandmarkLayout(41, 22, 2, 0)
	};

	private static void Commit(LandmarkCandidate candidate, LandmarkStyle style)
	{
		int left = candidate.Area.Left;
		int right = candidate.Area.Right - 1;
		int groundY = candidate.GroundY;
		int roofY = groundY - candidate.Layout.Height + 4;
		int leftColumn = left + 4;
		int rightColumn = right - 4;
		int centerX = candidate.AnchorX;

		for (int x = left; x <= right; x++) {
			for (int y = candidate.Area.Top; y < groundY; y++) {
				TileEditor.ClearTerrain(x, y);
			}

			for (int y = groundY; y <= Math.Min(candidate.Area.Bottom - 1, groundY + 6); y++) {
				if (!TileEditor.IsSolid(x, y)) {
					TileEditor.SetTerrain(x, y, style.Foundation);
				}
			}
			for (int depth = 0; depth < 3; depth++) {
				TileEditor.SetTerrain(x, groundY + depth, style.Foundation);
			}
		}

		int loftY = groundY - 10;
		int dividerX = centerX + (candidate.Layout.RoofVariant % 2 == 0 ? -3 : 3);
		int portalLeft = dividerX - 7;
		int portalRight = dividerX - 3;
		for (int x = leftColumn + 2; x <= rightColumn - 2; x++) {
			for (int y = roofY + 4; y < groundY; y++) {
				int heightAboveFloor = groundY - y;
				bool window = heightAboveFloor is >= 5 and <= 9
					&& (x >= leftColumn + 5 && x <= leftColumn + 8
						|| x >= rightColumn - 8 && x <= rightColumn - 5);
				ushort wall = window
					? WallID.Glass
					: (x - leftColumn) % 14 < 4 || y == loftY - 1 ? style.AccentWall : style.Wall;
				TileEditor.SetWall(x, y, wall);
			}
		}

		for (int x = leftColumn - 3; x <= rightColumn + 3; x++) {
			int roofOffset = candidate.Layout.RoofVariant % 3 == 0
				? Math.Abs(x - centerX) / 4
				: Math.Abs(x - centerX) / 5;
			for (int thickness = 0; thickness < 3; thickness++) {
				TileEditor.SetTerrain(x, roofY + roofOffset + thickness, thickness == 1 ? style.Pillar : style.Foundation);
			}
		}

		for (int y = roofY + 3; y < groundY; y++) {
			for (int thickness = 0; thickness < 2; thickness++) {
				TileEditor.SetTerrain(leftColumn + thickness, y, style.Pillar);
				TileEditor.SetTerrain(rightColumn - thickness, y, style.Pillar);
			}
		}

		for (int y = roofY + 7; y < groundY - 6; y++) {
			TileEditor.SetTerrain(dividerX, y, style.Pillar);
			TileEditor.SetTerrain(dividerX + 1, y, style.Pillar);
		}

		if (candidate.Layout.RoomCount >= 3) {
			for (int x = leftColumn + 2; x < dividerX; x++) {
				if (x >= portalLeft && x <= portalRight) {
					TileEditor.TryPlacePlatformForced(x, loftY);
					TileEditor.ClearTerrain(x, loftY + 1);
					continue;
				}
				TileEditor.SetTerrain(x, loftY, style.Foundation);
				TileEditor.SetTerrain(x, loftY + 1, style.Pillar);
			}

			for (int step = 0; step < 4; step++) {
				int stepY = groundY - 3 - step * 2;
				int stepLeft = dividerX - 5 - step * 3;
				for (int x = stepLeft; x < stepLeft + 3; x++) {
					TileEditor.TryPlacePlatformForced(x, stepY);
				}
			}
		}

		for (int x = left; x < leftColumn; x++) {
			for (int depth = 0; depth < 3; depth++) {
				TileEditor.SetTerrain(x, groundY + depth, style.Foundation);
			}
		}
		for (int x = rightColumn + 1; x <= right; x++) {
			for (int depth = 0; depth < 3; depth++) {
				TileEditor.SetTerrain(x, groundY + depth, style.Foundation);
			}
			if ((x - rightColumn) % 4 == 0) {
				for (int y = groundY + 3; y <= groundY + 7; y++) {
					TileEditor.SetTerrain(x, y, style.Pillar);
				}
			}
		}

		TileEditor.TryPlaceTorch(leftColumn + 2, groundY - 6);
		TileEditor.TryPlaceTorch(rightColumn - 2, groundY - 6);
		BuildArchitecturalDetails(candidate, style, leftColumn, rightColumn, roofY);
		AddBiomeSilhouette(candidate, style, leftColumn, rightColumn, roofY);
		TileEditor.Frame(candidate.Area);
	}

	private static int FurnishLandmark(LandmarkRecord landmark)
	{
		int leftColumn = landmark.Area.Left + 4;
		int rightColumn = landmark.Area.Right - 5;
		int groundY = landmark.AnchorY;
		int floorY = groundY - 1;
		LandmarkStyle style = ResolveStyle(landmark.Biome, landmark.AnchorX, groundY);
		int count = 0;
		count += TryPlaceDoor(leftColumn, groundY, style.Foundation) ? 1 : 0;
		count += TryPlaceDoor(rightColumn, groundY, style.Foundation) ? 1 : 0;
		if (!HasNearbyType(leftColumn, groundY - 2, TileID.ClosedDoor, 1, 2)
			&& !HasNearbyType(rightColumn, groundY - 2, TileID.ClosedDoor, 1, 2)) {
			count += TryPlaceDoor(landmark.AnchorX - 3, groundY, style.Foundation) ? 1 : 0;
			if (!HasNearbyType(landmark.AnchorX - 3, groundY - 2, TileID.ClosedDoor, 1, 2)) {
				count += TryPlaceDoor(landmark.AnchorX + 3, groundY, style.Foundation) ? 1 : 0;
			}
		}
		count += TryPlaceFurniture(leftColumn + 5, floorY, TileID.WorkBenches) ? 1 : 0;
		count += TryPlaceFurniture(leftColumn + 11, floorY, TileID.Tables) ? 1 : 0;
		count += TryPlaceFurniture(leftColumn + 15, floorY, TileID.Chairs) ? 1 : 0;
		count += TryPlaceFurniture(rightColumn - 8, floorY, TileID.Bookcases) ? 1 : 0;
		count += TryPlaceFurniture(rightColumn - 13, floorY, TileID.Benches) ? 1 : 0;
		count += TileEditor.TryPlaceSmallPile(rightColumn - 3, floorY, (int)landmark.Biome % 6, 0) ? 1 : 0;

		if (landmark.RoomCount >= 3) {
			int loftFloorY = groundY - 10;
			count += TryPlaceFurniture(leftColumn + 9, loftFloorY, TileID.Chairs) ? 1 : 0;
			count += TryPlaceFurniture(leftColumn + 14, loftFloorY, TileID.Tables) ? 1 : 0;
		}

		TileEditor.TryPlaceTorch(landmark.AnchorX - 3, groundY - 6);
		TileEditor.TryPlaceTorch(landmark.AnchorX + 3, groundY - 6);
		WorldGen.PlaceTile(landmark.AnchorX, landmark.Area.Top + 7, TileID.Chandeliers, mute: true, forced: false);
		RepairLandmarkTraversal(landmark);
		if (CountFurnitureTiles(landmark.Area) == 0) {
			int stageY = groundY - 3;
			for (int x = landmark.AnchorX - 12; x <= landmark.AnchorX + 12; x++) {
				TileEditor.SetTerrain(x, stageY, style.Foundation);
			}
			count += PlaceWorkbenchFootprint(landmark.AnchorX - 9, stageY - 1) ? 1 : 0;
			count += PlaceTableFootprint(landmark.AnchorX, stageY - 1) ? 1 : 0;
			count += PlaceChairFootprint(landmark.AnchorX + 8, stageY - 1) ? 1 : 0;
		}
		int retainedFurnitureTiles = CountFurnitureTiles(landmark.Area);
		if (retainedFurnitureTiles == 0) {
			int probeX = landmark.AnchorX - 9;
			int probeY = groundY - 4;
			throw new InvalidOperationException(
				$"Richer Biomes could not furnish the {landmark.Biome} landmark immediately at {landmark.AnchorX},{landmark.AnchorY}; "
				+ $"area={landmark.Area}; probe={probeX},{probeY}; inArea={landmark.Area.Contains(probeX, probeY)}; "
				+ $"probeType={Main.tile[probeX, probeY].TileType}; probeActive={Main.tile[probeX, probeY].HasTile}.");
		}
		return Math.Max(count, retainedFurnitureTiles);
	}

	private static void RepairLandmarkTraversal(LandmarkRecord landmark)
	{
		LandmarkLayout layout = ResolveLayout(landmark.Biome);
		if (layout.RoomCount < 3) {
			return;
		}

		int dividerX = landmark.AnchorX + (layout.RoofVariant % 2 == 0 ? -3 : 3);
		int loftY = landmark.AnchorY - 10;
		for (int x = dividerX - 7; x <= dividerX - 3; x++) {
			TileEditor.TryPlacePlatformForced(x, loftY);
			TileEditor.ClearTerrain(x, loftY + 1);
		}
		for (int step = 0; step < 4; step++) {
			int stepY = landmark.AnchorY - 3 - step * 2;
			int stepLeft = dividerX - 5 - step * 3;
			for (int x = stepLeft; x < stepLeft + 3; x++) {
				TileEditor.TryPlacePlatformForced(x, stepY);
			}
		}
	}

	private static bool PlaceWorkbenchFootprint(int leftX, int bottomY)
	{
		SetFurnitureTile(leftX, bottomY, TileID.WorkBenches, frameX: 0, frameY: 0);
		SetFurnitureTile(leftX + 1, bottomY, TileID.WorkBenches, frameX: 18, frameY: 0);
		return HasType(leftX, bottomY, TileID.WorkBenches)
			&& HasType(leftX + 1, bottomY, TileID.WorkBenches);
	}

	private static bool PlaceTableFootprint(int centerX, int bottomY)
	{
		for (int offsetX = -1; offsetX <= 1; offsetX++) {
			for (int offsetY = -1; offsetY <= 0; offsetY++) {
				SetFurnitureTile(
					centerX + offsetX,
					bottomY + offsetY,
					TileID.Tables,
					(short)((offsetX + 1) * 18),
					(short)((offsetY + 1) * 18));
			}
		}
		return HasType(centerX, bottomY, TileID.Tables);
	}

	private static bool PlaceChairFootprint(int x, int bottomY)
	{
		SetFurnitureTile(x, bottomY - 1, TileID.Chairs, frameX: 0, frameY: 0);
		SetFurnitureTile(x, bottomY, TileID.Chairs, frameX: 0, frameY: 18);
		return HasType(x, bottomY - 1, TileID.Chairs) && HasType(x, bottomY, TileID.Chairs);
	}

	private static void SetFurnitureTile(int x, int y, ushort type, short frameX, short frameY)
	{
		TileEditor.SetTerrain(x, y, type);
		Main.tile[x, y].TileFrameX = frameX;
		Main.tile[x, y].TileFrameY = frameY;
	}

	private static int CountFurnitureTiles(Rectangle area)
	{
		ushort[] types = [
			TileID.WorkBenches, TileID.Tables, TileID.Chairs, TileID.Bookcases,
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

	private static bool TryPlaceDoor(int x, int groundY, ushort lintelType)
	{
		for (int y = groundY - 3; y < groundY; y++) {
			TileEditor.ClearTerrain(x, y);
		}
		TileEditor.SetTerrain(x, groundY - 4, lintelType);
		TileEditor.SetTerrain(x, groundY, lintelType);
		WorldGen.PlaceDoor(x, groundY - 2, TileID.ClosedDoor);
		return HasNearbyType(x, groundY - 2, TileID.ClosedDoor, 1, 2);
	}

	private static bool TryPlaceFurniture(int x, int floorY, ushort tileType)
	{
		for (int clearX = x - 2; clearX <= x + 2; clearX++) {
			for (int clearY = floorY - 4; clearY <= floorY; clearY++) {
				TileEditor.ClearTerrain(clearX, clearY);
			}
		}
		WorldGen.PlaceTile(x, floorY, tileType, mute: true, forced: false, plr: -1, style: 0);
		return HasNearbyType(x, floorY - 1, tileType, 3, 3);
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

	private static void AddBiomeSilhouette(
		LandmarkCandidate candidate,
		LandmarkStyle style,
		int leftColumn,
		int rightColumn,
		int roofY)
	{
		int centerX = candidate.AnchorX;
		int groundY = candidate.GroundY;
		switch (candidate.Biome) {
			case BiomeKind.Forest:
				for (int x = centerX - 7; x <= centerX + 7; x++) {
					TileEditor.SetTerrain(x, roofY - 2 + Math.Abs(x - centerX) / 5, style.Pillar);
				}
				break;
			case BiomeKind.Snow:
				for (int x = centerX - 7; x <= centerX + 7; x++) {
					TileEditor.SetTerrain(x, roofY - 4 + Math.Abs(x - centerX) / 2, style.Foundation);
				}
				break;
			case BiomeKind.Desert:
				for (int y = groundY - 13; y < groundY; y++) {
					TileEditor.SetTerrain(leftColumn - 2, y, style.Pillar);
					TileEditor.SetTerrain(rightColumn + 2, y, style.Pillar);
				}
				TileEditor.SetTerrain(leftColumn - 3, groundY - 11, style.Foundation);
				TileEditor.SetTerrain(rightColumn + 3, groundY - 11, style.Foundation);
				break;
			case BiomeKind.Jungle:
				for (int x = leftColumn - 2; x <= rightColumn + 2; x++) {
					TileEditor.SetTerrain(x, roofY - 2 + Math.Abs(x - centerX) / 8, style.Pillar);
				}
				for (int y = roofY - 1; y <= roofY + 3; y++) {
					TileEditor.SetTerrain(centerX, y, style.Pillar);
				}
				break;
			case BiomeKind.Evil:
				for (int x = leftColumn; x <= rightColumn; x += 4) {
					int baseY = roofY + Math.Abs(x - centerX) / 4;
					int spikeHeight = 2 + Math.Abs(x - centerX) % 4;
					for (int y = baseY - spikeHeight; y < baseY; y++) {
						TileEditor.SetTerrain(x, y, style.Pillar);
					}
				}
				break;
			case BiomeKind.Ocean:
				for (int y = groundY - 15; y < groundY - 4; y++) {
					TileEditor.SetTerrain(centerX, y, style.Pillar);
					TileEditor.SetTerrain(centerX + 1, y, style.Pillar);
				}
				for (int x = centerX - 5; x <= centerX + 6; x++) {
					TileEditor.SetTerrain(x, groundY - 15, style.Foundation);
					TileEditor.SetTerrain(x, groundY - 14, style.Foundation);
				}
				break;
			case BiomeKind.Sky:
				for (int offset = -5; offset <= 5; offset++) {
					TileEditor.SetTerrain(centerX + offset, roofY - 2, style.Foundation);
					TileEditor.SetTerrain(centerX, roofY - 2 + offset, style.Foundation);
				}
				break;
			case BiomeKind.Mushroom:
				for (int x = centerX - 9; x <= centerX + 9; x++) {
					int capY = roofY - 3 + Math.Abs(x - centerX) / 4;
					TileEditor.SetTerrain(x, capY, style.Foundation);
					if (Math.Abs(x - centerX) < 7) {
						TileEditor.SetTerrain(x, capY + 1, style.Foundation);
					}
				}
				break;
			case BiomeKind.Cavern:
				for (int offset = 0; offset <= 6; offset++) {
					TileEditor.SetTerrain(leftColumn + offset, groundY - 1 - offset, style.Pillar);
					TileEditor.SetTerrain(rightColumn - offset, groundY - 1 - offset, style.Pillar);
				}
				break;
			case BiomeKind.Underworld:
				for (int x = leftColumn; x <= rightColumn; x += 4) {
					for (int y = roofY - 3; y < roofY; y++) {
						TileEditor.SetTerrain(x, y, style.Foundation);
					}
				}
				break;
		}
	}

	private static void BuildArchitecturalDetails(
		LandmarkCandidate candidate,
		LandmarkStyle style,
		int leftColumn,
		int rightColumn,
		int roofY)
	{
		int groundY = candidate.GroundY;
		int[] windowCenters = [leftColumn + 7, rightColumn - 7];
		foreach (int windowX in windowCenters) {
			for (int x = windowX - 3; x <= windowX + 3; x++) {
				TileEditor.SetTerrain(x, groundY - 10, style.Pillar);
				TileEditor.SetTerrain(x, groundY - 4, style.Pillar);
			}
			for (int y = groundY - 9; y < groundY - 4; y++) {
				TileEditor.SetTerrain(windowX - 3, y, style.Pillar);
				TileEditor.SetTerrain(windowX + 3, y, style.Pillar);
				for (int x = windowX - 2; x <= windowX + 2; x++) {
					TileEditor.ClearTerrain(x, y);
					TileEditor.SetWall(x, y, WallID.Glass);
				}
			}
		}

		int dormerCenter = candidate.AnchorX + (candidate.Layout.RoofVariant % 2 == 0 ? 9 : -9);
		int dormerPeakY = roofY - 3;
		for (int x = dormerCenter - 6; x <= dormerCenter + 6; x++) {
			int offset = Math.Abs(x - dormerCenter) / 2;
			for (int thickness = 0; thickness < 2; thickness++) {
				TileEditor.SetTerrain(x, dormerPeakY + offset + thickness, style.Foundation);
			}
		}
		for (int y = dormerPeakY + 4; y <= roofY + 4; y++) {
			TileEditor.SetTerrain(dormerCenter - 5, y, style.Pillar);
			TileEditor.SetTerrain(dormerCenter + 5, y, style.Pillar);
			for (int x = dormerCenter - 4; x <= dormerCenter + 4; x++) {
				TileEditor.SetWall(x, y, y >= roofY ? style.AccentWall : WallID.Glass);
			}
		}

		if (candidate.Biome is BiomeKind.Forest or BiomeKind.Snow or BiomeKind.Underworld) {
			int chimneyX = candidate.AnchorX - 13;
			for (int x = chimneyX; x <= chimneyX + 2; x++) {
				for (int y = roofY - 5; y <= roofY + 3; y++) {
					TileEditor.SetTerrain(x, y, style.Pillar);
				}
			}
		}
	}

	private static bool ValidateFootprint(LandmarkCandidate candidate, LandmarkStyle style)
	{
		int leftColumn = candidate.Area.Left + 4;
		int rightColumn = candidate.Area.Right - 5;
		int roofY = candidate.GroundY - candidate.Layout.Height + 4;
		for (int y = roofY + 7; y < candidate.GroundY - 3; y++) {
			if (!HasType(leftColumn, y, style.Pillar) || !HasType(rightColumn, y, style.Pillar)) {
				return false;
			}
		}

		for (int x = candidate.Area.Left; x < candidate.Area.Right; x++) {
			if (!HasType(x, candidate.GroundY, style.Foundation)) {
				return false;
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

	private readonly record struct LandmarkRequest(BiomeKind Biome, int LeftX, int RightX, bool Required);

	private readonly record struct LandmarkCandidate(
		BiomeKind Biome,
		int AnchorX,
		int GroundY,
		Rectangle Area,
		LandmarkLayout Layout,
		int Score);

	private readonly record struct LandmarkStyle(ushort Foundation, ushort Pillar, ushort Wall, ushort AccentWall);

	private readonly record struct LandmarkLayout(int Width, int Height, int RoomCount, int RoofVariant);
}
