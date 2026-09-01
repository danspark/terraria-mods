using System;
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
				ushort surfaceMaterial = Main.tile[x, currentY].HasTile ? Main.tile[x, currentY].TileType : TileID.Dirt;
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
					TileEditor.SetTerrain(x, y, SelectMountainTerrain(surfaceMaterial, depth));
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
		int totalColumns = plan.Mountains.Count * 81;
		foreach (MountainRangePlan mountain in plan.Mountains) {
			SkyHighlandPlan? attachedHighland = plan.SkyHighlands
				.Where(highland => highland.AttachedMountainRegionId == mountain.RegionId)
				.Select<SkyHighlandPlan, SkyHighlandPlan?>(highland => highland)
				.FirstOrDefault();
			int primaryPeakX = attachedHighland is SkyHighlandPlan highland
				&& Math.Abs(highland.CenterX - mountain.LeftPeakX) < Math.Abs(highland.CenterX - mountain.RightPeakX)
					? mountain.RightPeakX
					: mountain.LeftPeakX;

			for (int x = primaryPeakX - 40; x <= primaryPeakX + 40; x++) {
				int targetY = plan.SurfaceAt(x);
				int sampleY = Math.Min(Main.maxTilesY - 220, (int)Main.worldSurface + 38);
				ushort surfaceMaterial = Main.tile[x, sampleY].HasTile
					? Main.tile[x, sampleY].TileType
					: TileID.Dirt;
				for (int y = Math.Max(40, targetY - 4); y < targetY; y++) {
					if (!TileEditor.IsProtectedTile(Main.tile[x, y])) {
						TileEditor.ClearTerrain(x, y, clearWall: true);
					}
				}
				for (int y = targetY; y <= sampleY + 22; y++) {
					if (TileEditor.IsProtectedTile(Main.tile[x, y])) {
						continue;
					}
					int depth = y - targetY;
					TileEditor.SetTerrain(x, y, SelectMountainTerrain(surfaceMaterial, depth));
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

	private static ushort SelectMountainTerrain(ushort surfaceMaterial, int depth)
	{
		if (surfaceMaterial is TileID.SnowBlock or TileID.IceBlock or TileID.BreakableIce) {
			return depth < 9 ? TileID.SnowBlock : depth < 24 ? TileID.IceBlock : TileID.Stone;
		}
		if (surfaceMaterial is TileID.Sand or TileID.HardenedSand or TileID.Sandstone) {
			return depth < 7 ? TileID.HardenedSand : TileID.Sandstone;
		}
		if (surfaceMaterial is TileID.Mud or TileID.JungleGrass or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass) {
			return depth == 0 ? TileID.JungleGrass : depth < 18 ? TileID.Mud : TileID.Stone;
		}
		if (surfaceMaterial is TileID.CorruptGrass or TileID.Ebonstone or TileID.Ebonsand) {
			return depth < 8 ? TileID.CorruptGrass : TileID.Ebonstone;
		}
		if (surfaceMaterial is TileID.CrimsonGrass or TileID.Crimstone or TileID.Crimsand) {
			return depth < 8 ? TileID.CrimsonGrass : TileID.Crimstone;
		}
		return depth < 8 ? TileID.Dirt : TileID.Stone;
	}
}
