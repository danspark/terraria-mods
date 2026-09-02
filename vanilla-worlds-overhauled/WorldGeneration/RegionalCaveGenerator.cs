using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace VanillaWorldsOverhauled.WorldGeneration;

internal static class RegionalCaveGenerator
{
	private const int CaveSeedSalt = 0x43A5_9D17;

	public static void Apply(WorldPlan plan, GenerationProgress progress)
	{
		for (int index = 0; index < plan.Caves.Count; index++) {
			PlannedCave cave = plan.Caves[index];
			UnifiedRandom random = new(MixSeed(plan.GenerationSeed, CaveSeedSalt, cave.RegionId, index));
			CarveCurve(cave, random, protectSensitiveTiles: false, allTilesAllowed: null);
			CarveChamber(cave.Midpoint, cave.Radius + random.Next(5, 10), random);
			progress.Set((double)(index + 1) / plan.Caves.Count);
		}
	}

	public static void RepairRequiredRoutes(
		WorldPlan plan,
		GenerationProgress progress,
		bool reserveRoutes = false,
		bool respectStructureMap = true,
		bool naturalTilesOnly = false)
	{
		bool[]? allTilesAllowed = null;
		if (respectStructureMap) {
			allTilesAllowed = new bool[TileID.Sets.GeneralPlacementTiles.Length];
			Array.Fill(allTilesAllowed, true);
		}
		int requiredCount = 0;
		for (int index = 0; index < plan.Caves.Count; index++) {
			if (plan.Caves[index].RequiredRoute) {
				requiredCount++;
			}
		}

		int processed = 0;
		for (int originalIndex = 0; originalIndex < plan.Caves.Count; originalIndex++) {
			PlannedCave cave = plan.Caves[originalIndex];
			if (!cave.RequiredRoute) {
				continue;
			}

			UnifiedRandom random = new(MixSeed(plan.GenerationSeed, CaveSeedSalt, cave.RegionId, originalIndex));
			CarveCurve(cave, random, protectSensitiveTiles: true, allTilesAllowed, naturalTilesOnly);
			if (reserveRoutes) {
				ProtectCurve(cave);
			}
			processed++;
			progress.Set((double)processed / requiredCount);
		}
	}

	private static void CarveCurve(
		PlannedCave cave,
		UnifiedRandom random,
		bool protectSensitiveTiles,
		bool[]? allTilesAllowed,
		bool naturalTilesOnly = false)
	{
		double approximateLength = Vector2.Distance(cave.Start.ToVector2(), cave.Midpoint.ToVector2())
			+ Vector2.Distance(cave.Midpoint.ToVector2(), cave.End.ToVector2());
		int steps = Math.Max(24, (int)Math.Ceiling(approximateLength / 2d));
		double radius = cave.Radius;
		for (int step = 0; step <= steps; step++) {
			double t = (double)step / steps;
			double inverse = 1d - t;
			int x = (int)Math.Round(
				inverse * inverse * cave.Start.X
				+ 2d * inverse * t * cave.Midpoint.X
				+ t * t * cave.End.X);
			int y = (int)Math.Round(
				inverse * inverse * cave.Start.Y
				+ 2d * inverse * t * cave.Midpoint.Y
				+ t * t * cave.End.Y);

			radius = Math.Clamp(radius + random.NextFloat(-0.45f, 0.46f), cave.Radius - 1.5d, cave.Radius + 2.5d);
			CarveEllipse(
				x,
				y,
				Math.Max(3, (int)Math.Round(radius)),
				Math.Max(3, (int)Math.Round(radius * 0.72d)),
				protectSensitiveTiles,
				allTilesAllowed,
				naturalTilesOnly);
		}
	}

	private static void ProtectCurve(PlannedCave cave)
	{
		double approximateLength = Vector2.Distance(cave.Start.ToVector2(), cave.Midpoint.ToVector2())
			+ Vector2.Distance(cave.Midpoint.ToVector2(), cave.End.ToVector2());
		int steps = Math.Max(24, (int)Math.Ceiling(approximateLength / 2d));
		for (int step = 0; step <= steps; step += 8) {
			double t = (double)step / steps;
			double inverse = 1d - t;
			int x = (int)Math.Round(inverse * inverse * cave.Start.X + 2d * inverse * t * cave.Midpoint.X + t * t * cave.End.X);
			int y = (int)Math.Round(inverse * inverse * cave.Start.Y + 2d * inverse * t * cave.Midpoint.Y + t * t * cave.End.Y);
			int radius = cave.Radius + 2;
			GenVars.structures.AddProtectedStructure(
				new Rectangle(x - radius, y - radius, radius * 2 + 1, radius * 2 + 1),
				padding: 1);
		}
	}

	private static void CarveChamber(Point center, int radius, UnifiedRandom random)
	{
		int horizontalRadius = radius + random.Next(4, 10);
		int verticalRadius = Math.Max(6, radius - random.Next(0, 4));
		CarveEllipse(center.X, center.Y, horizontalRadius, verticalRadius, protectSensitiveTiles: false, allTilesAllowed: null);

		int shelfY = center.Y + verticalRadius - 2;
		for (int x = center.X - horizontalRadius / 2; x <= center.X + horizontalRadius / 2; x++) {
			if (!WorldGen.InWorld(x, shelfY, 10) || TileEditor.IsSolid(x, shelfY)) {
				continue;
			}

			TileEditor.SetTerrain(x, shelfY, TileID.Stone);
		}
	}

	private static void CarveEllipse(
		int centerX,
		int centerY,
		int horizontalRadius,
		int verticalRadius,
		bool protectSensitiveTiles,
		bool[]? allTilesAllowed,
		bool naturalTilesOnly = false)
	{
		for (int offsetX = -horizontalRadius; offsetX <= horizontalRadius; offsetX++) {
			for (int offsetY = -verticalRadius; offsetY <= verticalRadius; offsetY++) {
				double normalized =
					(double)(offsetX * offsetX) / (horizontalRadius * horizontalRadius)
					+ (double)(offsetY * offsetY) / (verticalRadius * verticalRadius);
				if (normalized > 1d) {
					continue;
				}

				int x = centerX + offsetX;
				int y = centerY + offsetY;
				if (!WorldGen.InWorld(x, y, 5)
					|| protectSensitiveTiles && (TileEditor.IsProtectedTile(Main.tile[x, y])
						|| allTilesAllowed is not null
							&& !GenVars.structures.CanPlace(new Rectangle(x, y, 1, 1), allTilesAllowed, padding: 0))
					|| naturalTilesOnly && !IsNaturalRouteMaterial(Main.tile[x, y])) {
					continue;
				}
				TileEditor.ClearTerrain(x, y);
				if (Main.tile[x, y].WallType == WallID.None) {
					TileEditor.SetWall(x, y, y < Main.rockLayer ? WallID.DirtUnsafe : WallID.Stone);
				}
			}
		}
	}

	private static bool IsNaturalRouteMaterial(Tile tile) => !tile.HasTile || tile.TileType is
		TileID.Dirt or TileID.Stone or TileID.Grass or TileID.ClayBlock
		or TileID.SnowBlock or TileID.IceBlock or TileID.BreakableIce
		or TileID.Sand or TileID.HardenedSand or TileID.Sandstone
		or TileID.Mud or TileID.JungleGrass or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass
		or TileID.Ebonstone or TileID.Crimstone or TileID.Ebonsand or TileID.Crimsand;

	private static int MixSeed(int seed, int salt, int feature, int index)
	{
		unchecked {
			uint value = (uint)seed ^ (uint)salt;
			value ^= (uint)feature * 0x9E37_79B9u;
			value ^= (uint)index * 0x85EB_CA6Bu;
			value ^= value >> 16;
			value *= 0x7FEB_352Du;
			value ^= value >> 15;
			value *= 0x846C_A68Bu;
			value ^= value >> 16;
			return (int)value;
		}
	}
}
