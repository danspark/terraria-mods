using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace RicherBiomes.WorldGeneration;

internal static class WorldValidator
{
	public static GenerationReport Validate(WorldPlan plan)
	{
		List<string> errors = [];
		int forestRelief = ValidateForest(plan, errors);
		int mountainPeakY = ValidateMountainPeak(plan, errors);
		int mountainRouteSamples = ValidateMountainRoute(plan, errors);
		int mineDepth = ValidateMine(plan, errors);
		CountRouteFurniture(plan, out int ropeTiles, out int platformTiles);

		if (Math.Abs(plan.OriginX - plan.SpawnX) < WorldPlan.SpawnBuffer) {
			errors.Add("the featured corridor entered the protected spawn clearing");
		}

		if (ropeTiles < 180) {
			errors.Add($"only {ropeTiles} rope tiles survived; expected at least 180");
		}

		if (platformTiles < 80) {
			errors.Add($"only {platformTiles} platform tiles survived; expected at least 80");
		}

		if (errors.Count > 0) {
			throw new InvalidOperationException("Richer Biomes route validation failed: " + string.Join("; ", errors));
		}

		return new GenerationReport(
			true,
			forestRelief,
			mountainPeakY,
			mountainRouteSamples,
			mineDepth,
			ropeTiles,
			platformTiles);
	}

	private static int ValidateForest(WorldPlan plan, List<string> errors)
	{
		int minSurfaceY = int.MaxValue;
		int maxSurfaceY = int.MinValue;
		int missingSurfaceTiles = 0;
		int blockedRootSamples = 0;

		for (int distance = plan.Forest.Start; distance <= plan.Forest.End; distance += 4) {
			int x = plan.XAt(distance);
			int surfaceY = Profiles.ForestSurface(plan, distance);
			minSurfaceY = Math.Min(minSurfaceY, surfaceY);
			maxSurfaceY = Math.Max(maxSurfaceY, surfaceY);
			if (!HasSurfaceFloor(x, surfaceY)) {
				missingSurfaceTiles++;
			}

			int rootFloorY = Profiles.ForestRootFloor(plan, distance);
			if (!IsSolidFloor(x, rootFloorY) || !HasHeadroom(x, rootFloorY, 5)) {
				blockedRootSamples++;
			}
		}

		int relief = maxSurfaceY - minSurfaceY;
		if (relief < 45) {
			errors.Add($"forest relief was {relief} tiles; expected at least 45");
		}
		if (missingSurfaceTiles > 2) {
			errors.Add($"forest surface route had {missingSurfaceTiles} missing floor samples");
		}
		if (blockedRootSamples > 2) {
			errors.Add($"forest root route had {blockedRootSamples} blocked samples");
		}

		return relief;
	}

	private static int ValidateMountainPeak(WorldPlan plan, List<string> errors)
	{
		int actualPeakY = int.MaxValue;
		for (int distance = plan.Mountain.Start; distance <= plan.Mountain.End; distance++) {
			int x = plan.XAt(distance);
			int expectedY = Profiles.MountainSurface(plan, distance);
			if (IsSolidFloor(x, expectedY)) {
				actualPeakY = Math.Min(actualPeakY, expectedY);
			}
		}

		if (actualPeakY == int.MaxValue) {
			errors.Add("mountain summit has no solid surface");
			return actualPeakY;
		}

		int spaceThreshold = (int)(Main.worldSurface * 0.35d);
		if (actualPeakY >= spaceThreshold) {
			errors.Add($"mountain peak y={actualPeakY} did not reach Space threshold y<{spaceThreshold}");
		}

		return actualPeakY;
	}

	private static int ValidateMountainRoute(WorldPlan plan, List<string> errors)
	{
		int validSamples = 0;
		int blockedSamples = 0;
		int previousFloorY = Profiles.MountainTunnelFloor(plan, plan.Mountain.Start);

		for (int distance = plan.Mountain.Start; distance <= plan.Mountain.End; distance += 3) {
			int x = plan.XAt(distance);
			int floorY = Profiles.MountainTunnelFloor(plan, distance);
			if (Math.Abs(floorY - previousFloorY) > 1 || !IsSolidFloor(x, floorY) || !HasHeadroom(x, floorY, 6)) {
				blockedSamples++;
			}
			else {
				validSamples++;
			}
			previousFloorY = floorY;
		}

		if (blockedSamples > 2) {
			errors.Add($"mountain interior crossing had {blockedSamples} blocked or steep samples");
		}

		return validSamples;
	}

	private static int ValidateMine(WorldPlan plan, List<string> errors)
	{
		int lowestRopeY = 0;
		int shaftX = plan.XAt(plan.Mine.Start + 172);
		for (int y = plan.BaseSurfaceY; y <= plan.MineBottomY + 5; y++) {
			Tile tile = Main.tile[shaftX, y];
			if (tile.HasTile && tile.TileType == TileID.Rope) {
				lowestRopeY = y;
			}
		}

		int depth = lowestRopeY - plan.BaseSurfaceY;
		if (lowestRopeY < plan.MineBottomY - 2) {
			errors.Add($"mine rope ended at y={lowestRopeY}; expected y>={plan.MineBottomY - 2}");
		}
		if (depth < 190) {
			errors.Add($"mine descended only {depth} tiles; expected at least 190");
		}

		return depth;
	}

	private static void CountRouteFurniture(WorldPlan plan, out int ropeTiles, out int platformTiles)
	{
		ropeTiles = 0;
		platformTiles = 0;
		for (int x = plan.MinX; x <= plan.MaxX; x++) {
			for (int y = plan.PeakY - 5; y <= plan.MineBottomY + 5; y++) {
				Tile tile = Main.tile[x, y];
				if (!tile.HasTile) {
					continue;
				}
				if (tile.TileType == TileID.Rope) {
					ropeTiles++;
				}
				else if (tile.TileType == TileID.Platforms) {
					platformTiles++;
				}
			}
		}
	}

	private static bool IsSolidFloor(int x, int y)
	{
		Tile tile = Main.tile[x, y];
		return tile.HasUnactuatedTile && Main.tileSolid[tile.TileType];
	}

	private static bool HasSurfaceFloor(int x, int expectedY)
	{
		for (int y = expectedY; y <= expectedY + 2; y++) {
			Tile tile = Main.tile[x, y];
			if (tile.HasUnactuatedTile &&
				(Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType])) {
				return true;
			}
		}
		return false;
	}

	private static bool HasHeadroom(int x, int floorY, int height)
	{
		for (int y = floorY - height; y < floorY; y++) {
			Tile tile = Main.tile[x, y];
			if (tile.HasUnactuatedTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType]) {
				return false;
			}
		}
		return true;
	}
}
