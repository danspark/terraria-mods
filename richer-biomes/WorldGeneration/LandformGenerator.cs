using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.WorldBuilding;

namespace RicherBiomes.WorldGeneration;

internal static class LandformGenerator
{
	public static void Apply(WorldPlan plan, GenerationProgress progress)
	{
		int width = plan.RightBoundary - plan.LeftBoundary + 1;
		for (int x = plan.LeftBoundary; x <= plan.RightBoundary; x++) {
			ShapeColumn(plan, x);
			if ((x - plan.LeftBoundary) % 64 == 0) {
				progress.Set((double)(x - plan.LeftBoundary) / width);
			}
		}
	}

	public static void ReinforceMountains(WorldPlan plan, GenerationProgress progress)
	{
		int totalColumns = plan.Mountains.Sum(mountain => plan.Regions[mountain.RegionId].Width);
		int completed = 0;
		foreach (MountainRangePlan mountain in plan.Mountains) {
			WorldRegion region = plan.Regions[mountain.RegionId];
			for (int x = region.Left; x <= region.Right; x++) {
				int targetY = plan.SurfaceAt(x);
				int currentY = BiomeClassifier.TryFindGroundSupport(x, out int supportY)
					? supportY
					: WorldPlanner.FindSurfaceY(x);
				ushort surfaceMaterial = FindStableMountainMaterial(x, currentY);
				for (int y = Math.Max(40, targetY - 10); y < targetY; y++) {
					if (!TileEditor.IsProgressionTile(Main.tile[x, y])) {
						TileEditor.ClearTerrain(x, y, clearWall: true);
					}
				}
				int fillBottom = Math.Max(currentY + 8, (int)Main.worldSurface + 45);
				for (int y = targetY; y <= Math.Min(Main.maxTilesY - 220, fillBottom); y++) {
					if (TileEditor.IsProgressionTile(Main.tile[x, y])) {
						continue;
					}
					int depth = y - targetY;
					TileEditor.SetTerrain(x, y, SelectMountainTerrain(surfaceMaterial, targetY, depth));
					if (depth >= 5) {
						TileEditor.SetWall(x, y, depth < 20 ? WallID.DirtUnsafe : WallID.Stone);
					}
				}
				completed++;
				if (completed % 48 == 0) {
					progress.Set((double)completed / Math.Max(1, totalColumns));
				}
			}
		}
		progress.Set(1d);
	}

	public static void StabilizeSummits(WorldPlan plan, GenerationProgress progress)
	{
		int completed = 0;
		int totalColumns = plan.Mountains.Count * 130;
		foreach (MountainRangePlan mountain in plan.Mountains) {
			SkyHighlandPlan? attachedHighland = plan.SkyHighlands
				.Where(highland => highland.AttachedMountainRegionId == mountain.RegionId)
				.Select<SkyHighlandPlan, SkyHighlandPlan?>(highland => highland)
				.FirstOrDefault();
			HashSet<int> summitColumns = [];
			foreach (int peakX in new[] { mountain.LeftPeakX, mountain.RightPeakX }) {
				for (int x = peakX - 32; x <= peakX + 32; x++) {
					summitColumns.Add(x);
				}
			}

			foreach (int x in summitColumns.Order()) {
				int targetY = plan.SurfaceAt(x);
				int sampleY = Math.Min(Main.maxTilesY - 220, (int)Main.worldSurface + 38);
				ushort surfaceMaterial = FindStableMountainMaterial(x, sampleY);
				for (int y = Math.Max(40, targetY - 4); y < targetY; y++) {
					if (!IsInsideHighlandOwnedCell(attachedHighland, x, y)
						&& !TileEditor.IsProtectedTile(Main.tile[x, y]) && !IsSkyBodyTile(Main.tile[x, y])) {
						TileEditor.ClearTerrain(x, y, clearWall: true);
					}
				}
				for (int y = targetY; y <= sampleY + 22; y++) {
					if (IsInsideHighlandOwnedCell(attachedHighland, x, y)
						|| TileEditor.IsProtectedTile(Main.tile[x, y]) || IsSkyBodyTile(Main.tile[x, y])) {
						continue;
					}
					int depth = y - targetY;
					TileEditor.SetTerrain(x, y, SelectMountainTerrain(surfaceMaterial, targetY, depth));
					if (depth >= 5) {
						TileEditor.SetWall(x, y, depth < 20 ? WallID.DirtUnsafe : WallID.Stone);
					}
				}
				completed++;
				if (completed % 20 == 0) {
					progress.Set((double)completed / Math.Max(1, totalColumns));
				}
			}
		}
		progress.Set(1d);
	}

	public static void FinishMountainMaterials(WorldPlan plan, GenerationManifest manifest)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			WorldRegion region = plan.Regions[mountain.RegionId];
			for (int x = region.Left; x <= region.Right; x++) {
				int surfaceY = plan.SurfaceAt(x);
				for (int depth = 0; depth <= 36; depth++) {
					int y = surfaceY + depth;
					Tile tile = Main.tile[x, y];
					if (IsFinalFeatureOwned(manifest, x, y) || TileEditor.IsProtectedTile(tile)
						|| !IsNaturalMountainTile(tile)) {
						continue;
					}
					SlopeType slope = tile.Slope;
					bool halfBlock = tile.IsHalfBlock;
					TileEditor.SetTerrain(x, y, MountainTerrainAt(x, surfaceY, depth));
					Tile replacement = Main.tile[x, y];
					replacement.Slope = slope;
					replacement.IsHalfBlock = halfBlock;
				}
			}
		}
	}

	private static bool IsFinalFeatureOwned(GenerationManifest manifest, int x, int y)
	{
		Microsoft.Xna.Framework.Point point = new(x, y);
		return manifest.Terraces.Any(record => record.Area.Contains(point))
			|| manifest.Landmarks.Any(record => record.Area.Contains(point))
			|| manifest.Bridges.Any(record => record.Area.Contains(point))
			|| manifest.Valleys.Any(record => record.Area.Contains(point))
			|| manifest.SkyHighlands.Any(record => record.Area.Contains(point))
			|| manifest.BiomeTransitions.Any(record => record.Area.Contains(point))
			|| manifest.MineSections.Any(record => record.Area.Contains(point));
	}

	private static bool IsNaturalMountainTile(Tile tile) => tile.HasUnactuatedTile && tile.TileType is
		TileID.Grass or TileID.Dirt or TileID.Stone or TileID.SnowBlock or TileID.IceBlock or TileID.BreakableIce
		or TileID.Mud or TileID.JungleGrass or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass
		or TileID.Sand or TileID.HardenedSand or TileID.Sandstone or TileID.DesertFossil
		or TileID.CorruptGrass or TileID.Ebonstone or TileID.Ebonsand or TileID.CorruptHardenedSand or TileID.CorruptSandstone
		or TileID.CrimsonGrass or TileID.Crimstone or TileID.Crimsand or TileID.CrimsonHardenedSand or TileID.CrimsonSandstone;

	private static void ShapeColumn(WorldPlan plan, int x)
	{
		int desiredSurfaceY = plan.SurfaceAt(x);
		int originalSurfaceY = WorldPlanner.FindSurfaceY(x);
		int clearTop = Math.Max(35, Math.Min(desiredSurfaceY, originalSurfaceY) - 12);

		for (int y = clearTop; y < desiredSurfaceY; y++) {
			TileEditor.ClearTerrain(x, y, clearWall: true);
		}

		int fillBottom = Math.Min(
			Main.maxTilesY - 220,
			Math.Max(desiredSurfaceY + 28, originalSurfaceY + 18));
		WorldRegion region = plan.RegionAt(x);
		for (int y = desiredSurfaceY; y <= fillBottom; y++) {
			int depth = y - desiredSurfaceY;
			TileEditor.SetTerrain(x, y, SelectTerrain(region.Landform, depth));
			if (depth >= 4) {
				TileEditor.SetWall(x, y, depth < 18 ? WallID.DirtUnsafe : WallID.Stone);
			}
		}
	}

	private static ushort SelectTerrain(LandformKind landform, int depth)
	{
		int soilDepth = landform switch {
			LandformKind.QuietLowland => 18,
			LandformKind.RollingHills => 14,
			LandformKind.Valley => 20,
			LandformKind.Plateau => 11,
			LandformKind.Mountain => 7,
			LandformKind.Basin => 17,
			_ => 12
		};

		return depth < soilDepth ? TileID.Dirt : TileID.Stone;
	}

	internal static ushort FindStableMountainMaterial(int x, int startY)
	{
		int top = Math.Clamp(Math.Max(startY, (int)Main.worldSurface + 24), 4, Main.maxTilesY - 6);
		int bottom = Math.Min(Main.maxTilesY - 6, top + 90);
		for (int y = top; y <= bottom; y++) {
			Tile tile = Main.tile[x, y];
			if (!tile.HasUnactuatedTile || Main.tileFrameImportant[tile.TileType] || IsSkyBodyTile(tile)) {
				continue;
			}
			return tile.TileType;
		}
		return TileID.Dirt;
	}

	private static bool IsSkyBodyTile(Tile tile) =>
		tile.HasTile && tile.TileType is TileID.Cloud or TileID.RainCloud or TileID.SnowCloud or TileID.Sunplate;

	private static bool IsInsideHighlandOwnedCell(SkyHighlandPlan? highland, int x, int y)
	{
		if (highland is not SkyHighlandPlan attached) {
			return false;
		}
		int left = Math.Clamp(attached.CenterX - attached.Width / 2, 55, Main.maxTilesX - attached.Width - 55);
		int right = left + attached.Width - 1;
		if (x < left || x > right || y < attached.SurfaceY - 18 || y > attached.SurfaceY + attached.Depth + 24) {
			return false;
		}
		Tile tile = Main.tile[x, y];
		return IsSkyBodyTile(tile)
			|| tile.HasTile && tile.TileType is TileID.Grass or TileID.Dirt
			|| tile.WallType is WallID.DiscWall or WallID.Cloud;
	}

	internal static ushort MountainTerrainAt(int x, int surfaceY, int depth)
	{
		int deepSampleY = Math.Max((int)Main.worldSurface + 70, surfaceY + 150);
		return SelectMountainTerrain(FindStableMountainMaterial(x, deepSampleY), surfaceY, depth);
	}

	private static ushort SelectMountainTerrain(ushort surfaceMaterial, int surfaceY, int depth)
	{
		if (surfaceMaterial is TileID.SnowBlock or TileID.IceBlock or TileID.BreakableIce) {
			return depth < 5 ? TileID.SnowBlock : depth < 24 ? TileID.IceBlock : TileID.Stone;
		}
		if (surfaceMaterial is TileID.CorruptHardenedSand or TileID.CorruptSandstone or TileID.Ebonsand) {
			return depth < 7 ? TileID.CorruptHardenedSand : TileID.CorruptSandstone;
		}
		if (surfaceMaterial is TileID.CrimsonHardenedSand or TileID.CrimsonSandstone or TileID.Crimsand) {
			return depth < 7 ? TileID.CrimsonHardenedSand : TileID.CrimsonSandstone;
		}
		if (surfaceMaterial is TileID.Sand or TileID.HardenedSand or TileID.Sandstone
			or TileID.DesertFossil) {
			return depth < 7 ? TileID.HardenedSand : TileID.Sandstone;
		}
		if (surfaceMaterial is TileID.CorruptJungleGrass) {
			return depth == 0 ? TileID.CorruptJungleGrass : depth < 18 ? TileID.Mud : TileID.Ebonstone;
		}
		if (surfaceMaterial is TileID.CrimsonJungleGrass) {
			return depth == 0 ? TileID.CrimsonJungleGrass : depth < 18 ? TileID.Mud : TileID.Crimstone;
		}
		if (surfaceMaterial is TileID.Mud or TileID.JungleGrass) {
			return depth == 0 ? TileID.JungleGrass : depth < 18 ? TileID.Mud : TileID.Stone;
		}
		if (surfaceMaterial is TileID.CorruptGrass or TileID.Ebonstone) {
			return depth == 0 ? TileID.CorruptGrass : depth < 7 ? TileID.Dirt : TileID.Ebonstone;
		}
		if (surfaceMaterial is TileID.CrimsonGrass or TileID.Crimstone) {
			return depth == 0 ? TileID.CrimsonGrass : depth < 7 ? TileID.Dirt : TileID.Crimstone;
		}
		return depth == 0 ? TileID.Grass : depth < 8 ? TileID.Dirt : TileID.Stone;
	}
}
