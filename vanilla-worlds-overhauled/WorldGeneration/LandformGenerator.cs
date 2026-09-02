using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.WorldBuilding;

namespace VanillaWorldsOverhauled.WorldGeneration;

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
				for (int y = Math.Max(40, targetY - 10); y < targetY; y++) {
					if (!TileEditor.IsProgressionTile(Main.tile[x, y])) {
						TileEditor.ClearTerrain(x, y, clearWall: true);
					}
				}
				int originalJoin = currentY + 8 + OrganicBoundary.Profile(
					x,
					mountain.FeatureSeed ^ 0x4F52_4947,
					41,
					9,
					10,
					4);
				int fillBottom = Math.Max(
					originalJoin,
					(int)Main.worldSurface + 45 + OrganicBoundary.Profile(
						x,
						mountain.FeatureSeed ^ 0x424F_5454,
						47,
						11,
						12,
						4));
				for (int y = targetY; y <= Math.Min(Main.maxTilesY - 220, fillBottom); y++) {
					if (TileEditor.IsProgressionTile(Main.tile[x, y])) {
						continue;
					}
					int depth = y - targetY;
					TileEditor.SetTerrain(x, y, MountainTerrainAt(plan, mountain, x, depth));
					if (depth >= 5) {
						TileEditor.SetWall(x, y, MountainWallAtDepth(mountain, x, depth));
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
				for (int y = Math.Max(40, targetY - 4); y < targetY; y++) {
					if (!IsInsideHighlandOwnedCell(attachedHighland, x, y)
						&& !TileEditor.IsProtectedTile(Main.tile[x, y]) && !IsSkyBodyTile(Main.tile[x, y])) {
						TileEditor.ClearTerrain(x, y, clearWall: true);
					}
				}
				int stabilizeBottom = sampleY + 22 + OrganicBoundary.Profile(
					x,
					mountain.FeatureSeed ^ 0x5354_424F,
					37,
					9,
					9,
					3);
				for (int y = targetY; y <= stabilizeBottom; y++) {
					if (IsInsideHighlandOwnedCell(attachedHighland, x, y)
						|| TileEditor.IsProtectedTile(Main.tile[x, y]) || IsSkyBodyTile(Main.tile[x, y])) {
						continue;
					}
					int depth = y - targetY;
					TileEditor.SetTerrain(x, y, MountainTerrainAt(plan, mountain, x, depth));
					if (depth >= 5) {
						TileEditor.SetWall(x, y, MountainWallAtDepth(mountain, x, depth));
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
			ushort[] materialProfile = CaptureMountainMaterialProfile(plan, mountain);
			for (int x = region.Left; x <= region.Right; x++) {
				int surfaceY = plan.SurfaceAt(x);
				int shallowFinishDepth = Math.Max(24, 36 + OrganicBoundary.Profile(
					x,
					mountain.FeatureSeed ^ 0x4649_4E44,
					47,
					11,
					12,
					4));
				int deepBodyBottom = (int)Main.worldSurface + 45 + OrganicBoundary.Profile(
					x,
					mountain.FeatureSeed ^ 0x4445_4558,
					53,
					13,
					14,
					5);
				int finishDepth = Math.Max(shallowFinishDepth, deepBodyBottom - surfaceY);
				for (int depth = 0; depth <= finishDepth; depth++) {
					int y = surfaceY + depth;
					Tile tile = Main.tile[x, y];
					if (IsFinalFeatureOwned(manifest, x, y) || TileEditor.IsProtectedTile(tile)
						|| !IsNaturalMountainTile(tile)) {
						continue;
					}
					SlopeType slope = tile.Slope;
					bool halfBlock = tile.IsHalfBlock;
					TileEditor.SetTerrain(x, y, MountainTerrainAt(plan, mountain, x, depth, materialProfile));
					Tile replacement = Main.tile[x, y];
					replacement.Slope = slope;
					replacement.IsHalfBlock = halfBlock;
				}
			}
		}
	}

	public static void RepairMountainMaterialSeams(WorldPlan plan, GenerationManifest manifest)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			WorldRegion region = plan.Regions[mountain.RegionId];
			int top = Math.Max(45, Enumerable.Range(region.Left, region.Width).Min(plan.SurfaceAt));
			int bottom = Math.Min(Main.maxTilesY - 50, (int)Main.worldSurface + 70);
			Rectangle area = new(region.Left, top, region.Width, bottom - top);
			BreakResidualMountainMaterialSeams(plan, manifest, area, mountain.FeatureSeed);
			BreakResidualMountainMaterialSeams(plan, manifest, area, mountain.FeatureSeed ^ 0x5345_414D);
			TileEditor.Frame(area, border: 2);
		}
	}

	private static void BreakResidualMountainMaterialSeams(
		WorldPlan plan,
		GenerationManifest manifest,
		Rectangle area,
		int seed)
	{
		for (int x = area.Left + 2; x < area.Right - 2; x++) {
			int runStart = -1;
			for (int y = area.Top + 2; y <= area.Bottom - 2; y++) {
				bool boundary = y < area.Bottom - 2
					&& IsMutableMountainMaterial(plan, manifest, x, y)
					&& IsMutableMountainMaterial(plan, manifest, x + 1, y)
					&& MountainMaterialFamily(Main.tile[x, y].TileType)
						!= MountainMaterialFamily(Main.tile[x + 1, y].TileType);
				if (boundary && runStart < 0) {
					runStart = y;
				}
				if (boundary) {
					continue;
				}
				if (runStart >= 0 && y - runStart > 22) {
					for (int notchY = runStart + 11, notch = 0; notchY < y - 2; notchY += 13, notch++) {
						bool pushRight = ((notch + seed + x) & 1) == 0;
						int targetX = pushRight ? x + 1 : x;
						int sourceX = pushRight ? x : x + 1;
						for (int offset = 0; offset < 2; offset++) {
							int targetY = notchY + offset;
							if (IsMutableMountainMaterial(plan, manifest, targetX, targetY)
								&& IsMutableMountainMaterial(plan, manifest, sourceX, targetY)) {
								SetMountainMaterial(targetX, targetY, Main.tile[sourceX, targetY].TileType);
							}
						}
					}
				}
				runStart = -1;
			}
		}

		for (int y = area.Top + 2; y < area.Bottom - 2; y++) {
			int runStart = -1;
			for (int x = area.Left + 2; x <= area.Right - 2; x++) {
				bool boundary = x < area.Right - 2
					&& IsMutableMountainMaterial(plan, manifest, x, y)
					&& IsMutableMountainMaterial(plan, manifest, x, y + 1)
					&& MountainMaterialFamily(Main.tile[x, y].TileType)
						!= MountainMaterialFamily(Main.tile[x, y + 1].TileType);
				if (boundary && runStart < 0) {
					runStart = x;
				}
				if (boundary) {
					continue;
				}
				if (runStart >= 0 && x - runStart > 22) {
					for (int notchX = runStart + 11, notch = 0; notchX < x - 2; notchX += 13, notch++) {
						bool pushDown = ((notch + seed + y) & 1) == 0;
						int targetY = pushDown ? y + 1 : y;
						int sourceY = pushDown ? y : y + 1;
						for (int offset = 0; offset < 2; offset++) {
							int targetX = notchX + offset;
							if (IsMutableMountainMaterial(plan, manifest, targetX, targetY)
								&& IsMutableMountainMaterial(plan, manifest, targetX, sourceY)) {
								SetMountainMaterial(targetX, targetY, Main.tile[targetX, sourceY].TileType);
							}
						}
					}
				}
				runStart = -1;
			}
		}
	}

	private static bool IsMutableMountainMaterial(WorldPlan plan, GenerationManifest manifest, int x, int y)
	{
		Tile tile = Main.tile[x, y];
		return y > plan.SurfaceAt(x)
			&& !IsFinalFeatureOwned(manifest, x, y)
			&& !TileEditor.IsProtectedTile(tile)
			&& MountainMaterialFamily(tile.TileType) != 0;
	}

	private static int MountainMaterialFamily(ushort tileType) => tileType switch {
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

	private static void SetMountainMaterial(int x, int y, ushort tileType)
	{
		Tile tile = Main.tile[x, y];
		SlopeType slope = tile.Slope;
		bool halfBlock = tile.IsHalfBlock;
		TileEditor.SetTerrain(x, y, tileType);
		Tile replacement = Main.tile[x, y];
		replacement.Slope = slope;
		replacement.IsHalfBlock = halfBlock;
	}

	private static bool IsFinalFeatureOwned(GenerationManifest manifest, int x, int y)
	{
		Microsoft.Xna.Framework.Point point = new(x, y);
		return manifest.Terraces.Any(record => record.Area.Contains(point))
			|| manifest.Landmarks.Any(record => record.Area.Contains(point))
			|| manifest.Bridges.Any(record => record.Area.Contains(point))
			|| manifest.ForestLakeBridges.Any(record => record.Area.Contains(point))
			|| manifest.Valleys.Any(record => record.Area.Contains(point))
			|| manifest.MountainWaters.Any(record => record.Area.Contains(point))
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
			TileEditor.SetTerrain(x, y, SelectTerrain(plan, region, x, depth));
			if (depth >= 4) {
				TileEditor.SetWall(x, y, TerrainWallAtDepth(plan, region, x, depth));
			}
		}
	}

	private static ushort SelectTerrain(WorldPlan plan, WorldRegion region, int x, int depth)
	{
		int soilDepth = region.Landform switch {
			LandformKind.QuietLowland => 18,
			LandformKind.RollingHills => 14,
			LandformKind.Valley => 20,
			LandformKind.Plateau => 11,
			LandformKind.Mountain => 7,
			LandformKind.Basin => 17,
			_ => 12
		};
		soilDepth += OrganicBoundary.Profile(
			x,
			plan.GenerationSeed ^ region.Id * 0x2C92_77B5,
			43,
			11,
			6,
			3);

		return depth < Math.Max(3, soilDepth) ? TileID.Dirt : TileID.Stone;
	}

	private static ushort TerrainWallAtDepth(WorldPlan plan, WorldRegion region, int x, int depth)
	{
		int boundary = 18 + OrganicBoundary.Profile(
			x,
			plan.GenerationSeed ^ region.Id * 0x1656_67B1 ^ 0x5741_4C4C,
			47,
			13,
			7,
			3);
		return depth < Math.Max(7, boundary) ? WallID.DirtUnsafe : WallID.Stone;
	}

	internal static ushort MountainWallAtDepth(MountainRangePlan mountain, int x, int depth)
	{
		int boundary = 19 + OrganicBoundary.Profile(
			x,
			mountain.FeatureSeed ^ 0x4D57_414C,
			53,
			13,
			8,
			4);
		return depth < Math.Max(8, boundary) ? WallID.DirtUnsafe : WallID.Stone;
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

	internal static ushort MountainTerrainAt(WorldPlan plan, MountainRangePlan mountain, int x, int depth)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		int sourceX = MountainMaterialSourceX(plan, mountain, region, x, depth);
		int deepSampleY = Math.Max((int)Main.worldSurface + 70, plan.SurfaceAt(sourceX) + 150);
		return SelectMountainTerrain(
			FindStableMountainMaterial(sourceX, deepSampleY),
			x,
			plan.SurfaceAt(x),
			depth,
			plan.GenerationSeed,
			preserveJungleBody: false);
	}

	internal static ushort MountainTerrainAt(
		WorldPlan plan,
		MountainRangePlan mountain,
		int x,
		int depth,
		IReadOnlyList<ushort> materialProfile)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		int sourceX = MountainMaterialSourceX(plan, mountain, region, x, depth);
		int profileIndex = Math.Clamp(sourceX - region.Left, 0, materialProfile.Count - 1);
		return SelectMountainTerrain(
			materialProfile[profileIndex],
			x,
			plan.SurfaceAt(x),
			depth,
			plan.GenerationSeed,
			preserveJungleBody: true);
	}

	internal static ushort MountainMaterialAt(
		WorldPlan plan,
		MountainRangePlan mountain,
		int x,
		int depth,
		IReadOnlyList<ushort> materialProfile)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		int sourceX = MountainMaterialSourceX(plan, mountain, region, x, depth);
		return materialProfile[Math.Clamp(sourceX - region.Left, 0, materialProfile.Count - 1)];
	}

	internal static ushort[] CaptureMountainMaterialProfile(WorldPlan plan, MountainRangePlan mountain)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		ushort[] profile = new ushort[region.Width];
		for (int x = region.Left; x <= region.Right; x++) {
			profile[x - region.Left] = FindRepresentativeMountainMaterial(region, x);
		}
		return profile;
	}

	private static int MountainMaterialSourceX(
		WorldPlan plan,
		MountainRangePlan mountain,
		WorldRegion region,
		int x,
		int depth)
	{
		int surfaceY = plan.SurfaceAt(x);
		int lateralShift = OrganicBoundary.Profile(
			depth + surfaceY / 3,
			mountain.FeatureSeed ^ 0x4D41_5442,
			31,
			7,
			22,
			8);
		lateralShift += (int)Math.Round((OrganicBoundary.Field(
			x,
			surfaceY + depth,
			mountain.FeatureSeed ^ 0x4D41_5446,
			23,
			6) - 0.5d) * 20d);
		return Math.Clamp(x + lateralShift, region.Left, region.Right);
	}

	private static ushort FindRepresentativeMountainMaterial(WorldRegion region, int x)
	{
		int startY = Math.Clamp((int)Main.worldSurface + 76, 8, Main.maxTilesY - 9);
		int bottomY = Math.Min(
			Main.UnderworldLayer - 70,
			Math.Max(startY + 120, (int)Main.rockLayer + 90));
		int naturalSamples = 0;
		int snow = 0;
		int desert = 0;
		int jungle = 0;
		int corruptJungle = 0;
		int crimsonJungle = 0;
		int corruption = 0;
		int crimson = 0;

		for (int offsetX = -24; offsetX <= 24; offsetX += 8) {
			int sampleX = Math.Clamp(x + offsetX, Math.Max(8, region.Left - 24), Math.Min(Main.maxTilesX - 9, region.Right + 24));
			for (int y = startY; y <= bottomY; y += 13) {
				Tile tile = Main.tile[sampleX, y];
				if (!tile.HasUnactuatedTile || Main.tileFrameImportant[tile.TileType] || IsSkyBodyTile(tile)
					|| !IsMountainProfileMaterial(tile.TileType)) {
					continue;
				}
				naturalSamples++;
				switch (tile.TileType) {
					case TileID.SnowBlock:
					case TileID.IceBlock:
					case TileID.BreakableIce:
						snow++;
						break;
					case TileID.Sand:
					case TileID.HardenedSand:
					case TileID.Sandstone:
					case TileID.DesertFossil:
						desert++;
						break;
					case TileID.CorruptJungleGrass:
						corruptJungle++;
						jungle++;
						break;
					case TileID.CrimsonJungleGrass:
						crimsonJungle++;
						jungle++;
						break;
					case TileID.Mud:
					case TileID.JungleGrass:
						jungle++;
						break;
					case TileID.CorruptGrass:
					case TileID.Ebonstone:
					case TileID.Ebonsand:
					case TileID.CorruptHardenedSand:
					case TileID.CorruptSandstone:
						corruption++;
						break;
					case TileID.CrimsonGrass:
					case TileID.Crimstone:
					case TileID.Crimsand:
					case TileID.CrimsonHardenedSand:
					case TileID.CrimsonSandstone:
						crimson++;
						break;
				}
			}
		}

		int threshold = Math.Max(4, naturalSamples / 9);
		int strongest = Math.Max(jungle, Math.Max(snow, Math.Max(desert, Math.Max(corruption, crimson))));
		if (strongest < threshold) {
			return TileID.Dirt;
		}
		if (jungle == strongest) {
			if (corruptJungle > crimsonJungle && corruptJungle >= Math.Max(2, jungle / 5)) {
				return TileID.CorruptJungleGrass;
			}
			if (crimsonJungle >= Math.Max(2, jungle / 5)) {
				return TileID.CrimsonJungleGrass;
			}
			return TileID.JungleGrass;
		}
		if (snow == strongest) {
			return TileID.SnowBlock;
		}
		if (desert == strongest) {
			return TileID.Sandstone;
		}
		return corruption >= crimson ? TileID.Ebonstone : TileID.Crimstone;
	}

	private static bool IsMountainProfileMaterial(ushort tileType) => tileType is
		TileID.Grass or TileID.Dirt or TileID.Stone
		or TileID.SnowBlock or TileID.IceBlock or TileID.BreakableIce
		or TileID.Sand or TileID.HardenedSand or TileID.Sandstone or TileID.DesertFossil
		or TileID.Mud or TileID.JungleGrass or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass
		or TileID.CorruptGrass or TileID.Ebonstone or TileID.Ebonsand
		or TileID.CorruptHardenedSand or TileID.CorruptSandstone
		or TileID.CrimsonGrass or TileID.Crimstone or TileID.Crimsand
		or TileID.CrimsonHardenedSand or TileID.CrimsonSandstone;

	private static ushort SelectMountainTerrain(
		ushort surfaceMaterial,
		int x,
		int surfaceY,
		int depth,
		int seed,
		bool preserveJungleBody)
	{
		int shallow = OrganicBoundary.Profile(x, seed ^ surfaceY ^ 0x5348_414C, 41, 9, 4, 2);
		int deep = OrganicBoundary.Profile(x, seed ^ surfaceY ^ 0x4445_4550, 59, 13, 7, 3);
		if (surfaceMaterial is TileID.SnowBlock or TileID.IceBlock or TileID.BreakableIce) {
			return depth < Math.Max(2, 5 + shallow)
				? TileID.SnowBlock
				: depth < Math.Max(11, 24 + deep) ? TileID.IceBlock : TileID.Stone;
		}
		if (surfaceMaterial is TileID.CorruptHardenedSand or TileID.CorruptSandstone or TileID.Ebonsand) {
			return depth < Math.Max(3, 7 + shallow) ? TileID.CorruptHardenedSand : TileID.CorruptSandstone;
		}
		if (surfaceMaterial is TileID.CrimsonHardenedSand or TileID.CrimsonSandstone or TileID.Crimsand) {
			return depth < Math.Max(3, 7 + shallow) ? TileID.CrimsonHardenedSand : TileID.CrimsonSandstone;
		}
		if (surfaceMaterial is TileID.Sand or TileID.HardenedSand or TileID.Sandstone
			or TileID.DesertFossil) {
			return depth < Math.Max(3, 7 + shallow) ? TileID.HardenedSand : TileID.Sandstone;
		}
		if (surfaceMaterial is TileID.CorruptJungleGrass) {
			return preserveJungleBody
				? SelectJungleMountainTerrain(TileID.CorruptJungleGrass, TileID.Ebonstone, x, surfaceY, depth, seed, shallow)
				: depth == 0 ? TileID.CorruptJungleGrass : depth < Math.Max(7, 18 + deep) ? TileID.Mud : TileID.Ebonstone;
		}
		if (surfaceMaterial is TileID.CrimsonJungleGrass) {
			return preserveJungleBody
				? SelectJungleMountainTerrain(TileID.CrimsonJungleGrass, TileID.Crimstone, x, surfaceY, depth, seed, shallow)
				: depth == 0 ? TileID.CrimsonJungleGrass : depth < Math.Max(7, 18 + deep) ? TileID.Mud : TileID.Crimstone;
		}
		if (surfaceMaterial is TileID.Mud or TileID.JungleGrass) {
			return preserveJungleBody
				? SelectJungleMountainTerrain(TileID.JungleGrass, TileID.Stone, x, surfaceY, depth, seed, shallow)
				: depth == 0 ? TileID.JungleGrass : depth < Math.Max(7, 18 + deep) ? TileID.Mud : TileID.Stone;
		}
		if (surfaceMaterial is TileID.CorruptGrass or TileID.Ebonstone) {
			return depth == 0 ? TileID.CorruptGrass : depth < Math.Max(3, 7 + shallow) ? TileID.Dirt : TileID.Ebonstone;
		}
		if (surfaceMaterial is TileID.CrimsonGrass or TileID.Crimstone) {
			return depth == 0 ? TileID.CrimsonGrass : depth < Math.Max(3, 7 + shallow) ? TileID.Dirt : TileID.Crimstone;
		}
		return depth == 0 ? TileID.Grass : depth < Math.Max(3, 8 + shallow) ? TileID.Dirt : TileID.Stone;
	}

	private static ushort SelectJungleMountainTerrain(
		ushort grass,
		ushort rock,
		int x,
		int surfaceY,
		int depth,
		int seed,
		int shallow)
	{
		if (depth == 0) {
			return grass;
		}
		if (depth < Math.Max(7, 11 + shallow)) {
			return TileID.Mud;
		}

		int y = surfaceY + depth;
		double broad = OrganicBoundary.Field(x, y, seed ^ 0x4A55_4E47, 71, 23);
		double detail = OrganicBoundary.Field(x, y, seed ^ 0x5249_4253, 29, 9);
		double rockField = broad * 0.76d + detail * 0.24d;
		double depthBias = Math.Clamp((depth - 80) / 260d, 0d, 1d) * 0.025d;
		return rockField > 0.61d - depthBias ? rock : TileID.Mud;
	}
}
