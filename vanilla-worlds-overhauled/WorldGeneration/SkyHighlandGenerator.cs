using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace VanillaWorldsOverhauled.WorldGeneration;

internal static class SkyHighlandGenerator
{
	private const int SkySeedSalt = 0x51C4_73A9;

	public static void Apply(WorldPlan plan, GenerationManifest manifest, GenerationProgress progress)
	{
		if (plan.SkyHighlands.Count == 0) {
			progress.Set(1d);
			return;
		}

		for (int index = 0; index < plan.SkyHighlands.Count; index++) {
			SkyHighlandPlan highland = plan.SkyHighlands[index];
			UnifiedRandom random = new(MixSeed(plan.GenerationSeed, SkySeedSalt, index));
			SkyHighlandRecord record = BuildHighland(highland, random);
			manifest.SkyHighlands.Add(record);
			GenVars.structures.AddProtectedStructure(record.Area, padding: 8);
			progress.Set((double)(index + 1) / plan.SkyHighlands.Count);
		}

		AddMountainCloudBelts(plan);
	}

	public static void RefillLakes(WorldPlan plan, GenerationManifest manifest)
	{
		for (int index = 0; index < plan.SkyHighlands.Count; index++) {
			SkyHighlandPlan highland = plan.SkyHighlands[index];
			if (!highland.HasLake) {
				continue;
			}
			int left = Math.Clamp(highland.CenterX - highland.Width / 2, 55, Main.maxTilesX - highland.Width - 55);
			int right = left + highland.Width - 1;
			int centerX = Math.Clamp(highland.CenterX + highland.Width / 5, left + 50, right - 50);
			int halfWidth = Math.Clamp(highland.Width / 11, 18, 30);
			int rimY = highland.SurfaceY + 4;
			int liquidCells = 0;
			for (int x = centerX - halfWidth; x <= centerX + halfWidth; x++) {
				int floorY = SkyLakeFloorY(x, centerX, halfWidth, rimY, highland.CenterX);
				for (int y = rimY; y < floorY; y++) {
					TileEditor.ClearTerrain(x, y);
					if (y >= rimY + 3) {
						TileEditor.SetLiquid(x, y, (byte)LiquidID.Water, byte.MaxValue);
						liquidCells++;
					}
				}
				TileEditor.SetTerrain(x, floorY, TileID.Sunplate);
				TileEditor.SetTerrain(x, floorY + 1, TileID.Cloud);
			}
			foreach (int wallX in new[] { centerX - halfWidth - 1, centerX + halfWidth + 1 }) {
				for (int y = rimY - 1; y <= rimY + 10; y++) {
					TileEditor.SetTerrain(wallX, y, TileID.Sunplate);
				}
			}
			if (index < manifest.SkyHighlands.Count) {
				manifest.SkyHighlands[index] = manifest.SkyHighlands[index] with { LiquidCells = liquidCells };
			}
			TileEditor.Frame(new Rectangle(centerX - halfWidth - 3, rimY - 3, halfWidth * 2 + 7, 26));
		}
	}

	public static void RepairKeels(WorldPlan plan)
	{
		foreach (SkyHighlandPlan highland in plan.SkyHighlands) {
			int left = Math.Clamp(highland.CenterX - highland.Width / 2, 55, Main.maxTilesX - highland.Width - 55);
			int right = left + highland.Width - 1;
			int routeLeft = left + highland.Width / 10;
			int routeRight = right - highland.Width / 10;
			int undersideY = highland.SurfaceY + Math.Max(50, highland.Depth * 2 / 3);
			for (int x = routeLeft; x <= routeRight; x++) {
				int lowerWave = (int)Math.Round(Math.Sin((x - routeLeft) / 17d) * 3d);
				int floorY = undersideY + lowerWave;
				for (int depth = 0; depth < 3; depth++) {
					if (!TileEditor.IsProtectedTile(Main.tile[x, floorY + depth])) {
						TileEditor.SetTerrain(x, floorY + depth, depth == 0 ? TileID.Sunplate : TileID.Cloud);
					}
				}
			}
			ReinforceThinSupports(new Rectangle(
				left - 20,
				Math.Max(45, highland.SurfaceY - 18),
				highland.Width + 40,
				highland.Depth + 62));
		}
	}

	public static void RepairVerticalRoutes(WorldPlan worldPlan, GenerationManifest manifest)
	{
		for (int index = 0; index < worldPlan.SkyHighlands.Count; index++) {
			SkyHighlandPlan plan = worldPlan.SkyHighlands[index];
			UnifiedRandom random = new(MixSeed(worldPlan.GenerationSeed, SkySeedSalt, index));
			int left = Math.Clamp(plan.CenterX - plan.Width / 2, 55, Main.maxTilesX - plan.Width - 55);
			int right = left + plan.Width - 1;
			int[] surface = BuildSurfaceProfile(plan, random, left, right);
			int undersideY = plan.SurfaceY + Math.Max(50, plan.Depth * 2 / 3);
			int[] connectors = plan.Style switch {
				SkyHighlandStyle.TerracedMeadow => [left + plan.Width / 3, right - plan.Width / 3],
				SkyHighlandStyle.CloudBasin => [left + plan.Width / 2],
				SkyHighlandStyle.BrokenArchipelago => [left + plan.Width / 4, left + plan.Width / 2, right - plan.Width / 4],
				_ => [left + plan.Width / 3, right - plan.Width / 3]
			};
			foreach (int x in connectors) {
				int topY = surface[x - left] + 1;
				int bottomY = undersideY - 5;
				for (int offset = -3; offset <= 3; offset++) {
					if (!IsVerticalRouteExcluded(manifest, x + offset, surface[x - left])) {
						TileEditor.TryPlacePlatformForced(x + offset, surface[x - left]);
					}
				}
				for (int y = topY; y <= bottomY; y++) {
					for (int offset = -3; offset <= 3; offset++) {
						int cellX = x + offset;
						if (IsVerticalRouteExcluded(manifest, cellX, y)
							|| TileEditor.IsProgressionTile(Main.tile[cellX, y])) {
							continue;
						}
						TileEditor.ClearTerrain(cellX, y);
						TileEditor.SetWall(cellX, y, SampleSkyNoise(cellX, y, plan.CenterX + 887) > 0.5d
							? WallID.DiscWall
							: WallID.Cloud);
					}
					if (!IsVerticalRouteExcluded(manifest, x, y)) {
						TileEditor.SetTerrain(x, y, TileID.Rope);
					}
					if ((y - topY) % 8 == 0) {
						for (int offset = -3; offset <= 3; offset++) {
							if (offset != 0 && !IsVerticalRouteExcluded(manifest, x + offset, y)) {
								TileEditor.TryPlacePlatformForced(x + offset, y);
							}
						}
					}
				}
			}
			TileEditor.Frame(new Rectangle(left, Math.Max(45, plan.SurfaceY - 8), plan.Width, plan.Depth + 40), border: 2);
		}
	}

	public static void RepairOrganicMaterialSeams(WorldPlan worldPlan, GenerationManifest manifest)
	{
		foreach (SkyHighlandPlan plan in worldPlan.SkyHighlands) {
			int left = Math.Clamp(plan.CenterX - plan.Width / 2, 55, Main.maxTilesX - plan.Width - 55);
			Rectangle materialBand = new(
				left - 20,
				Math.Max(45, plan.SurfaceY - 8),
				plan.Width + 40,
				42);
			BreakLongMaterialSeams(materialBand, manifest, plan.CenterX);
			TileEditor.Frame(materialBand, border: 2);
		}
	}

	private static void BreakLongMaterialSeams(Rectangle area, GenerationManifest manifest, int seed)
	{
		for (int x = area.Left + 2; x < area.Right - 3; x++) {
			int runStart = -1;
			for (int y = area.Top + 2; y <= area.Bottom - 2; y++) {
				bool boundary = y < area.Bottom - 2
					&& IsHighlandMaterialCell(manifest, x, y)
					&& IsHighlandMaterialCell(manifest, x + 1, y)
					&& HighlandMaterialFamily(Main.tile[x, y].TileType) != HighlandMaterialFamily(Main.tile[x + 1, y].TileType);
				if (boundary && runStart < 0) {
					runStart = y;
				}
				if (boundary) {
					continue;
				}
				if (runStart >= 0 && y - runStart > 18) {
					for (int seamY = runStart; seamY < y; seamY++) {
						ushort leftTile = Main.tile[x, seamY].TileType;
						ushort rightTile = Main.tile[x + 1, seamY].TileType;
						int push = OrganicBoundary.Profile(seamY, seed ^ x ^ 0x5356_4552, 17, 5, 4, 2);
						int reach = 1 + Math.Min(5, Math.Abs(push));
						for (int offset = 0; offset < reach; offset++) {
							int targetX = push >= 0 ? x + 1 + offset : x - offset;
							if (targetX > area.Left && targetX < area.Right - 1
								&& IsHighlandMaterialCell(manifest, targetX, seamY)) {
								SetHighlandMaterial(targetX, seamY, push >= 0 ? leftTile : rightTile);
							}
						}
					}
				}
				runStart = -1;
			}
		}

		for (int y = area.Top + 2; y < area.Bottom - 3; y++) {
			int runStart = -1;
			for (int x = area.Left + 2; x <= area.Right - 2; x++) {
				bool boundary = x < area.Right - 2
					&& IsHighlandMaterialCell(manifest, x, y)
					&& IsHighlandMaterialCell(manifest, x, y + 1)
					&& HighlandMaterialFamily(Main.tile[x, y].TileType) != HighlandMaterialFamily(Main.tile[x, y + 1].TileType);
				if (boundary && runStart < 0) {
					runStart = x;
				}
				if (boundary) {
					continue;
				}
				if (runStart >= 0 && x - runStart > 18) {
					for (int seamX = runStart; seamX < x; seamX++) {
						ushort upperTile = Main.tile[seamX, y].TileType;
						ushort lowerTile = Main.tile[seamX, y + 1].TileType;
						int push = OrganicBoundary.Profile(seamX, seed ^ y ^ 0x5348_4F52, 19, 5, 4, 2);
						int reach = 1 + Math.Min(5, Math.Abs(push));
						for (int offset = 0; offset < reach; offset++) {
							int targetY = push >= 0 ? y + 1 + offset : y - offset;
							if (targetY > area.Top && targetY < area.Bottom - 1
								&& IsHighlandMaterialCell(manifest, seamX, targetY)) {
								SetHighlandMaterial(seamX, targetY, push >= 0 ? upperTile : lowerTile);
							}
						}
					}
				}
				runStart = -1;
			}
		}
	}

	private static bool IsHighlandMaterialCell(GenerationManifest manifest, int x, int y) =>
		WorldGen.InWorld(x, y, 4)
		&& !IsVerticalRouteExcluded(manifest, x, y)
		&& !TileEditor.IsProtectedTile(Main.tile[x, y])
		&& HighlandMaterialFamily(Main.tile[x, y].TileType) != 0;

	private static int HighlandMaterialFamily(ushort tileType) => tileType switch {
		TileID.Grass or TileID.Dirt => 1,
		TileID.Sunplate => 2,
		TileID.Cloud => 3,
		TileID.RainCloud or TileID.SnowCloud => 4,
		_ => 0
	};

	private static void SetHighlandMaterial(int x, int y, ushort tileType)
	{
		Tile tile = Main.tile[x, y];
		SlopeType slope = tile.Slope;
		bool halfBlock = tile.IsHalfBlock;
		TileEditor.SetTerrain(x, y, tileType);
		Tile replacement = Main.tile[x, y];
		replacement.Slope = slope;
		replacement.IsHalfBlock = halfBlock;
	}

	private static bool IsVerticalRouteExcluded(GenerationManifest manifest, int x, int y)
	{
		Point point = new(x, y);
		return manifest.Landmarks.Any(record => record.Area.Contains(point))
			|| manifest.Bridges.Any(record => record.Area.Contains(point))
			|| manifest.ForestLakeBridges.Any(record => record.Area.Contains(point))
			|| manifest.Valleys.Any(record => record.Area.Contains(point))
			|| manifest.MountainWaters.Any(record => record.Area.Contains(point))
			|| manifest.MineSections.Any(record => record.Area.Contains(point));
	}

	private static void ReinforceThinSupports(Rectangle area)
	{
		for (int x = Math.Max(3, area.Left); x < Math.Min(Main.maxTilesX - 3, area.Right); x++) {
			for (int y = Math.Max(4, area.Top); y < Math.Min(Main.maxTilesY - 5, area.Bottom); y++) {
				Tile tile = Main.tile[x, y];
				if (!tile.HasUnactuatedTile || !Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]
					|| TileEditor.IsSolid(x, y - 1) || TileEditor.IsSolid(x, y - 2) || TileEditor.IsSolid(x, y - 3)
					|| TileEditor.IsSolid(x, y + 1) || TileEditor.IsSolid(x, y + 2)) {
					continue;
				}

				for (int depth = 1; depth <= 2; depth++) {
					if (CanReplace(x, y + depth)) {
						TileEditor.SetTerrain(x, y + depth, TileID.Cloud);
					}
				}
			}
		}
	}

	private static SkyHighlandRecord BuildHighland(SkyHighlandPlan plan, UnifiedRandom random)
	{
		int left = Math.Clamp(plan.CenterX - plan.Width / 2, 55, Main.maxTilesX - plan.Width - 55);
		int right = left + plan.Width - 1;
		int[] surface = BuildSurfaceProfile(plan, random, left, right);
		int walkableSurfaceTiles = 0;
		int cloudTiles = 0;
		int bottom = Math.Min((int)Main.worldSurface - 12, plan.SurfaceY + plan.Depth);

		for (int x = left; x <= right; x++) {
			int columnBottom = SkyBodyBottom(plan, left, x, surface[x - left], bottom);
			int soilDepth = Math.Max(3, 6 + OrganicBoundary.Profile(
				x,
				plan.CenterX ^ 0x534B_5953,
				37,
				9,
				4,
				2));
			int cloudDepth = Math.Max(soilDepth + 2, 11 + OrganicBoundary.Profile(
				x,
				plan.CenterX ^ 0x534B_5943,
				53,
				13,
				7,
				3));
			int keelThickness = Math.Clamp(7 + OrganicBoundary.Profile(
				x,
				plan.CenterX ^ 0x4B45_454C,
				31,
				7,
				3,
				1), 4, 11);
			for (int y = surface[x - left]; y <= columnBottom; y++) {
				if (!CanReplace(x, y)) {
					continue;
				}

				int depth = y - surface[x - left];
				ushort material;
				if (depth == 0) {
					material = TileID.Grass;
					walkableSurfaceTiles++;
				}
				else if (depth < soilDepth) {
					material = TileID.Dirt;
				}
				else if (depth < cloudDepth) {
					material = OrganicBoundary.Field(x, y, plan.CenterX ^ 0x5355_4E50, 17, 5) > 0.64d
						? TileID.Sunplate
						: TileID.Dirt;
				}
				else if (y >= columnBottom - keelThickness) {
					material = SampleSkyNoise(x, y, plan.CenterX + 193) < 0.28d ? TileID.RainCloud : TileID.Cloud;
					cloudTiles++;
				}
				else {
					double materialField = SampleSkyNoise(x, y, plan.CenterX);
					if (materialField > 0.76d) {
						material = TileID.Sunplate;
					}
					else {
						material = materialField < 0.17d ? TileID.RainCloud : TileID.Cloud;
						cloudTiles++;
					}
				}
				TileEditor.SetTerrain(x, y, material);
			}
		}

		int interiorRouteTiles = CarveInteriorChambers(plan, left, right);
		interiorRouteTiles += CarveInteriorRoutes(plan, left, right, surface, random);
		int liquidCells = plan.HasLake ? BuildSkyLake(plan, left, right, surface) : 0;
		BuildStructuralButtresses(plan, left, right, surface);
		cloudTiles += BuildSatellites(plan, left, right, random);
		Rectangle area = new(left - 20, Math.Max(45, plan.SurfaceY - 18), plan.Width + 40, plan.Depth + 62);
		TileEditor.Frame(area, border: 3);
		return new SkyHighlandRecord(
			area,
			walkableSurfaceTiles,
			interiorRouteTiles,
			cloudTiles,
			liquidCells,
			plan.Style,
			plan.AttachedMountainRegionId is not null);
	}

	private static void BuildStructuralButtresses(SkyHighlandPlan plan, int left, int right, int[] surface)
	{
		int interiorY = plan.SurfaceY + Math.Max(26, plan.Depth / 3);
		int undersideY = plan.SurfaceY + Math.Max(50, plan.Depth * 2 / 3);
		foreach (int centerX in new[] { left + plan.Width * 22 / 100, left + plan.Width * 78 / 100 }) {
			for (int x = centerX - 4; x <= centerX + 4; x++) {
				int topY = surface[x - left];
				for (int y = topY; y <= undersideY + 2; y++) {
					if (CanReplace(x, y)) {
						TileEditor.SetTerrain(x, y, y == topY ? TileID.Grass : y < topY + 5 ? TileID.Dirt : TileID.Cloud);
					}
				}
			}

			int routeLeft = left + plan.Width / 10;
			int upperAmplitude = plan.Style == SkyHighlandStyle.BrokenArchipelago ? 7 : 4;
			double upperPeriod = plan.Style switch {
				SkyHighlandStyle.TerracedMeadow => 31d,
				SkyHighlandStyle.CloudBasin => 19d,
				SkyHighlandStyle.BrokenArchipelago => 13d,
				_ => 23d
			};
			int upperFloorY = interiorY + (int)Math.Round(Math.Sin((centerX - routeLeft) / upperPeriod) * upperAmplitude);
			int lowerFloorY = undersideY + (int)Math.Round(
				Math.Sin((centerX - routeLeft) / (plan.Style == SkyHighlandStyle.CloudBasin ? 14d : 23d)) * 3d);
			CarveButtressPortal(centerX, upperFloorY, height: 8, wallSalt: plan.CenterX + 1201);
			CarveButtressPortal(centerX, lowerFloorY, height: 7, wallSalt: plan.CenterX + 1301);
		}
	}

	private static void CarveButtressPortal(int centerX, int floorY, int height, int wallSalt)
	{
		for (int x = centerX - 2; x <= centerX + 2; x++) {
			for (int y = floorY - height; y < floorY; y++) {
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, SampleSkyNoise(x, y, wallSalt) > 0.5d ? WallID.DiscWall : WallID.Cloud);
			}
		}
	}

	private static int[] BuildSurfaceProfile(SkyHighlandPlan plan, UnifiedRandom random, int left, int right)
	{
		int[] surface = new int[plan.Width];
		int previous = plan.SurfaceY + random.Next(-5, 6);
		int nextControlX = left;
		for (int x = left; x <= right; x++) {
			int edgeDistance = Math.Min(x - left, right - x);
			int edgeDrop = plan.Style switch {
				SkyHighlandStyle.TerracedMeadow => Math.Max(0, 12 - edgeDistance / 2),
				SkyHighlandStyle.CloudBasin => Math.Max(0, 18 - edgeDistance / 2),
				SkyHighlandStyle.BrokenArchipelago => Math.Max(0, 25 - edgeDistance),
				_ => 14
			};
			if (x >= nextControlX) {
				int amplitude = plan.Style == SkyHighlandStyle.BrokenArchipelago ? 7 : 4;
				previous = Math.Clamp(previous + random.Next(-amplitude, amplitude + 1), plan.SurfaceY - 11, plan.SurfaceY + 13);
				nextControlX = x + random.Next(
					plan.Style == SkyHighlandStyle.TerracedMeadow ? 36 : 19,
					plan.Style == SkyHighlandStyle.TerracedMeadow ? 65 : 48);
			}
			int basin = plan.Style == SkyHighlandStyle.CloudBasin
				? (int)Math.Round(Math.Sin(Math.PI * (x - left) / Math.Max(1, plan.Width - 1)) * 6d)
				: 0;
			surface[x - left] = previous + edgeDrop + basin;
		}

		for (int x = 1; x < surface.Length; x++) {
			surface[x] = Math.Clamp(surface[x], surface[x - 1] - (x % 3 == 0 ? 1 : 0), surface[x - 1] + (x % 3 == 0 ? 1 : 0));
		}
		for (int x = surface.Length - 2; x >= 0; x--) {
			surface[x] = Math.Clamp(surface[x], surface[x + 1] - 1, surface[x + 1] + 1);
		}
		return surface;
	}

	private static int CarveInteriorRoutes(
		SkyHighlandPlan plan,
		int left,
		int right,
		int[] surface,
		UnifiedRandom random)
	{
		int routeTiles = 0;
		int interiorY = plan.SurfaceY + Math.Max(26, plan.Depth / 3);
		int undersideY = plan.SurfaceY + Math.Max(50, plan.Depth * 2 / 3);
		// Keep solid endcaps around the interior gallery. Without them, the long tunnel
		// slices the tapered highland into unrelated floating shelves.
		int routeLeft = left + plan.Width / 10;
		int routeRight = right - plan.Width / 10;
		for (int x = routeLeft; x <= routeRight; x++) {
			double upperPeriod = plan.Style switch {
				SkyHighlandStyle.TerracedMeadow => 31d,
				SkyHighlandStyle.CloudBasin => 19d,
				SkyHighlandStyle.BrokenArchipelago => 13d,
				_ => 23d
			};
			int waveAmplitude = plan.Style == SkyHighlandStyle.BrokenArchipelago ? 7 : 4;
			int wave = (int)Math.Round(Math.Sin((x - routeLeft) / upperPeriod) * waveAmplitude);
			int floorY = interiorY + wave;
			ushort upperWall = SampleSkyNoise(x, floorY, plan.CenterX + 331) > 0.48d ? WallID.DiscWall : WallID.Cloud;
			routeTiles += CarveCorridorColumn(x, floorY, 8, upperWall);
			if (HashNoise(x, floorY, plan.CenterX + 719) % 47 == 0) {
				TileEditor.TryPlaceTorch(x, floorY - 2);
			}

			// The underside gallery is also the highland's continuous Sunplate keel.
			// Extending it nearly edge-to-edge keeps the lake, ruins, and interior
			// caverns as districts of one traversable biome instead of separate shelves.
			if (x >= left + 20 && x <= right - 20) {
				int lowerWave = (int)Math.Round(Math.Sin((x - routeLeft) / (plan.Style == SkyHighlandStyle.CloudBasin ? 14d : 23d)) * 3d);
				routeTiles += CarveCorridorColumn(x, undersideY + lowerWave, 7, WallID.Cloud);
			}
		}

		int[] connectors = plan.Style switch {
			SkyHighlandStyle.TerracedMeadow => [left + plan.Width / 3, right - plan.Width / 3],
			SkyHighlandStyle.CloudBasin => [left + plan.Width / 2],
			SkyHighlandStyle.BrokenArchipelago => [left + plan.Width / 4, left + plan.Width / 2, right - plan.Width / 4],
			_ => [left + plan.Width / 3, right - plan.Width / 3]
		};
		foreach (int x in connectors) {
			int surfaceY = surface[x - left];
			int topY = surfaceY + 1;
			// Stop above the wavy underside floor. Clearing through the last few
			// rows would cut the continuous keel into three disconnected districts.
			int bottomY = undersideY - 5;
			for (int offset = -3; offset <= 3; offset++) {
				TileEditor.TryPlacePlatformForced(x + offset, surfaceY);
			}
			for (int y = topY; y <= bottomY; y++) {
				for (int offset = -3; offset <= 3; offset++) {
					TileEditor.ClearTerrain(x + offset, y);
					TileEditor.SetWall(
						x + offset,
						y,
						SampleSkyNoise(x + offset, y, plan.CenterX + 887) > 0.5d ? WallID.DiscWall : WallID.Cloud);
				}
				TileEditor.SetTerrain(x, y, TileID.Rope);
				if ((y - topY) % 8 == 0) {
					for (int offset = -3; offset <= 3; offset++) {
						if (offset != 0) {
							TileEditor.TryPlacePlatformForced(x + offset, y);
						}
					}
				}
				routeTiles++;
			}
			for (int offset = -3; offset <= 3; offset++) {
				if (offset != 0) {
					TileEditor.TryPlacePlatformForced(x + offset, undersideY);
				}
			}
		}

		return routeTiles;
	}

	private static int CarveInteriorChambers(SkyHighlandPlan plan, int left, int right)
	{
		int carved = 0;
		int chamberCount = plan.Style switch {
			SkyHighlandStyle.TerracedMeadow => 3,
			SkyHighlandStyle.CloudBasin => 5,
			SkyHighlandStyle.BrokenArchipelago => 6,
			_ => 4
		};
		for (int chamberIndex = 0; chamberIndex < chamberCount; chamberIndex++) {
			int centerX = left + plan.Width * (chamberIndex + 1) / (chamberCount + 1);
			int centerY = chamberIndex % 2 == 0
				? plan.SurfaceY + Math.Max(24, plan.Depth / 3) + 8
				: plan.SurfaceY + Math.Max(48, plan.Depth * 2 / 3) - 7;
			int radiusX = 20 + HashNoise(centerX, centerY, plan.CenterX) % (plan.Style == SkyHighlandStyle.BrokenArchipelago ? 24 : 17);
			int radiusY = 10 + HashNoise(centerX, centerY, plan.CenterX + 71) % 11;
			for (int offsetX = -radiusX; offsetX <= radiusX; offsetX++) {
				for (int offsetY = -radiusY; offsetY <= radiusY; offsetY++) {
					double normalized =
						(double)(offsetX * offsetX) / (radiusX * radiusX)
						+ (double)(offsetY * offsetY) / (radiusY * radiusY);
					double edgeJitter = (SampleSkyNoise(centerX + offsetX, centerY + offsetY, plan.CenterX + chamberIndex * 43) - 0.5d) * 0.18d;
					if (normalized > 1d + edgeJitter) {
						continue;
					}
					int x = centerX + offsetX;
					int y = centerY + offsetY;
					if (!CanReplace(x, y)) {
						continue;
					}
					TileEditor.ClearTerrain(x, y);
					TileEditor.SetWall(x, y, SampleSkyNoise(x, y, plan.CenterX + chamberIndex * 43) > 0.5d ? WallID.DiscWall : WallID.Cloud);
					carved++;
				}
			}

			int floorY = centerY + radiusY / 2;
			for (int x = centerX - radiusX + 5; x <= centerX + radiusX - 5; x++) {
				for (int depth = 0; depth < 3; depth++) {
					TileEditor.SetTerrain(x, floorY + depth, depth == 0 ? TileID.Sunplate : TileID.Cloud);
				}
			}
			for (int x = centerX - radiusX + 8; x <= centerX + radiusX - 8; x += 11) {
				for (int y = centerY - radiusY + 4; y < floorY; y++) {
					TileEditor.SetTerrain(x, y, TileID.WoodenBeam);
					TileEditor.SetTerrain(x + 1, y, TileID.WoodenBeam);
				}
				TileEditor.TryPlaceTorch(x + 1, floorY - 3);
			}
		}
		return carved;
	}

	private static int CarveCorridorColumn(int x, int floorY, int height, ushort wall)
	{
		int carved = 0;
		for (int y = floorY - height; y < floorY; y++) {
			if (!WorldGen.InWorld(x, y, 8) || TileEditor.IsProtectedTile(Main.tile[x, y])) {
				continue;
			}
			TileEditor.ClearTerrain(x, y);
			TileEditor.SetWall(x, y, wall);
			carved++;
		}
		for (int depth = 0; depth < 3; depth++) {
			if (CanReplace(x, floorY + depth)) {
				TileEditor.SetTerrain(x, floorY + depth, depth == 0 ? TileID.Sunplate : TileID.Cloud);
			}
		}
		return carved;
	}

	private static int BuildSkyLake(SkyHighlandPlan plan, int left, int right, int[] surface)
	{
		int centerX = Math.Clamp(plan.CenterX + plan.Width / 5, left + 50, right - 50);
		int halfWidth = Math.Clamp(plan.Width / 11, 18, 30);
		int rimY = surface[centerX - left] + 1;
		int liquidCells = 0;
		for (int x = centerX - halfWidth; x <= centerX + halfWidth; x++) {
			int floorY = SkyLakeFloorY(x, centerX, halfWidth, rimY, plan.CenterX);
			for (int y = rimY; y < floorY; y++) {
				TileEditor.ClearTerrain(x, y);
				if (y >= rimY + 3) {
					TileEditor.SetLiquid(x, y, (byte)LiquidID.Water, byte.MaxValue);
					liquidCells++;
				}
			}
			TileEditor.SetTerrain(x, floorY, TileID.Sunplate);
			TileEditor.SetTerrain(x, floorY + 1, TileID.Cloud);
		}
		return liquidCells;
	}

	private static int SkyLakeFloorY(int x, int centerX, int halfWidth, int rimY, int seed)
	{
		double t = (double)(x - centerX + halfWidth) / Math.Max(1, halfWidth * 2);
		int irregularFloor = OrganicBoundary.Profile(
			x,
			seed ^ centerX ^ 0x4C41_4B45,
			17,
			5,
			3,
			1);
		return rimY + 6 + (int)Math.Round(Math.Sin(Math.PI * t) * 10d) + irregularFloor;
	}

	private static int BuildSatellites(SkyHighlandPlan plan, int left, int right, UnifiedRandom random)
	{
		int cloudTiles = 0;
		int biomeOutcrops = Math.Min(plan.Style == SkyHighlandStyle.BrokenArchipelago ? 7 : 4, plan.SatelliteCount);
		for (int index = 0; index < biomeOutcrops; index++) {
			int direction = index % 2 == 0 ? -1 : 1;
			int centerX = direction < 0
				? left - random.Next(30, 78)
				: right + random.Next(30, 78);
			int centerY = plan.SurfaceY + random.Next(5, 22);
			int horizontalRadius = random.Next(28, 47);
			int verticalRadius = random.Next(14, 25);
			if (!WorldGen.InWorld(centerX, centerY, horizontalRadius + 10)) {
				continue;
			}

			for (int offsetX = -horizontalRadius; offsetX <= horizontalRadius; offsetX++) {
				for (int offsetY = -verticalRadius; offsetY <= verticalRadius; offsetY++) {
					double normalized =
						(double)(offsetX * offsetX) / (horizontalRadius * horizontalRadius)
						+ (double)(offsetY * offsetY) / (verticalRadius * verticalRadius);
					double edgeJitter = (OrganicBoundary.Field(
						centerX + offsetX,
						centerY + offsetY,
						plan.CenterX ^ index * 0x45D9_F3B,
						17,
						5) - 0.5d) * 0.32d;
					if (normalized > 1d + edgeJitter || !CanReplace(centerX + offsetX, centerY + offsetY)) {
						continue;
					}
					int capY = -verticalRadius + 4 + Math.Abs(offsetX) / Math.Max(6, horizontalRadius / 5)
						+ OrganicBoundary.Profile(
							offsetX,
							plan.CenterX ^ centerX ^ 0x4341_5059,
							19,
							5,
							3,
							1);
					int cloudBoundary = -verticalRadius / 4 + OrganicBoundary.Profile(
						offsetX,
						plan.CenterX ^ centerY ^ 0x434C_4F55,
						23,
						7,
						4,
						2);
					ushort tile = offsetY <= capY
						? (offsetY == capY ? TileID.Grass : TileID.Dirt)
						: offsetY < cloudBoundary ? TileID.Sunplate : TileID.Cloud;
					TileEditor.SetTerrain(centerX + offsetX, centerY + offsetY, tile);
					if (tile == TileID.Cloud) {
						cloudTiles++;
					}
				}
			}

			int bodyEdgeX = direction < 0 ? left : right;
			int outcropEdgeX = centerX - direction * horizontalRadius;
			int startY = plan.SurfaceY + 8;
			int endY = centerY - verticalRadius + 5;
			int span = Math.Max(1, Math.Abs(outcropEdgeX - bodyEdgeX));
			for (int step = 0; step <= span; step++) {
				double t = (double)step / span;
				int x = bodyEdgeX + direction * step;
				int topY = (int)Math.Round(startY + (endY - startY) * (t * t * (3d - 2d * t)));
				for (int y = topY - 5; y < topY; y++) {
					TileEditor.ClearTerrain(x, y);
				}
				int dirtDepth = Math.Clamp(3 + OrganicBoundary.Profile(
					step,
					plan.CenterX ^ index * unchecked((int)0x9E37_79B9u) ^ 0x4341_5553,
					31,
					7,
					2,
					1), 2, 5);
				for (int depth = 0; depth < 6; depth++) {
					TileEditor.SetTerrain(x, topY + depth, depth == 0 ? TileID.Grass : depth < dirtDepth ? TileID.Dirt : TileID.Cloud);
				}
			}
		}
		return cloudTiles;
	}

	private static void AddMountainCloudBelts(WorldPlan plan)
	{
		int skyLine = (int)Math.Round(Main.worldSurface * 0.35d);
		foreach (MountainRangePlan mountain in plan.Mountains) {
			if (mountain.HeightStyle != MountainHeightStyle.SkyPiercing) {
				continue;
			}
			WorldRegion region = plan.Regions[mountain.RegionId];
			for (int x = region.Left + 12; x <= region.Right - 12; x++) {
				int surfaceY = plan.SurfaceAt(x);
				if (surfaceY > skyLine + 28) {
					continue;
				}
				int depth = Math.Clamp(
					7 + OrganicBoundary.Profile(
						x,
						mountain.FeatureSeed ^ 0x434C_4F55,
						37,
						9,
						5,
						2),
					3,
					13);
				for (int y = surfaceY + 1; y <= surfaceY + depth; y++) {
					if (CanReplace(x, y)) {
						TileEditor.SetTerrain(
							x,
							y,
							OrganicBoundary.Field(x, y, mountain.FeatureSeed ^ 0x5241_494E, 17, 5) > 0.73d
								? TileID.RainCloud
								: TileID.Cloud);
					}
				}
			}
		}
	}

	private static bool CanReplace(int x, int y) =>
		WorldGen.InWorld(x, y, 8) && !TileEditor.IsProtectedTile(Main.tile[x, y]);

	internal static int CountStoneInAuthoredBody(WorldPlan worldPlan, int index)
	{
		SkyHighlandPlan plan = worldPlan.SkyHighlands[index];
		UnifiedRandom random = new(MixSeed(worldPlan.GenerationSeed, SkySeedSalt, index));
		int left = Math.Clamp(plan.CenterX - plan.Width / 2, 55, Main.maxTilesX - plan.Width - 55);
		int right = left + plan.Width - 1;
		int[] surface = BuildSurfaceProfile(plan, random, left, right);
		int bottom = Math.Min((int)Main.worldSurface - 12, plan.SurfaceY + plan.Depth);
		int stone = 0;
		for (int x = left; x <= right; x++) {
			int columnBottom = SkyBodyBottom(plan, left, x, surface[x - left], bottom);
			for (int y = surface[x - left]; y <= columnBottom; y++) {
				if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.Stone) {
					stone++;
				}
			}
		}
		return stone;
	}

	private static int SkyBodyBottom(SkyHighlandPlan plan, int left, int x, int surfaceY, int bottom)
	{
		double t = (double)(x - left) / Math.Max(1, plan.Width - 1);
		double taper = Math.Sin(Math.PI * t);
		int bottomJitter = OrganicBoundary.Profile(
			x,
			plan.CenterX ^ 0x534B_5942,
			43,
			11,
			8,
			3);
		return surfaceY + Math.Max(8, (int)Math.Round((bottom - surfaceY) * taper) + bottomJitter);
	}

	private static double SampleSkyNoise(int x, int y, int salt)
	{
		const int cellWidth = 19;
		const int cellHeight = 13;
		int cellX = FloorDiv(x, cellWidth);
		int cellY = FloorDiv(y, cellHeight);
		double localX = (double)(x - cellX * cellWidth) / cellWidth;
		double localY = (double)(y - cellY * cellHeight) / cellHeight;
		localX = localX * localX * (3d - 2d * localX);
		localY = localY * localY * (3d - 2d * localY);
		double top = Lerp(Noise01(cellX, cellY, salt), Noise01(cellX + 1, cellY, salt), localX);
		double bottom = Lerp(Noise01(cellX, cellY + 1, salt), Noise01(cellX + 1, cellY + 1, salt), localX);
		return Lerp(top, bottom, localY);
	}

	private static double Noise01(int x, int y, int salt) =>
		HashNoise(x, y, salt) / (double)int.MaxValue;

	private static int HashNoise(int x, int y, int salt)
	{
		unchecked {
			uint value = (uint)(x * 0x45D9F3B) ^ (uint)(y * 0x119DE1F3) ^ (uint)salt;
			value ^= value >> 16;
			value *= 0x7FEB352D;
			value ^= value >> 15;
			return (int)(value & 0x7FFFFFFF);
		}
	}

	private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : (value - divisor + 1) / divisor;

	private static double Lerp(double left, double right, double amount) => left + (right - left) * amount;

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
}
