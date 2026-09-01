using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.WorldBuilding;

namespace RicherBiomes.WorldGeneration;

internal static class MountainBiomeGenerator
{
	private const int RouteHalfHeight = 5;

	public static void CarveInteriors(WorldPlan plan, bool protectSensitiveTiles, bool reserveRoutes)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			WorldRegion region = plan.Regions[mountain.RegionId];
			int leftX = region.Left + 22;
			int rightX = region.Right - 22;
			int hallY = Math.Min((int)Main.worldSurface + 34, mountain.SaddleY + 78);
			Point leftEntrance = new(leftX, plan.SurfaceAt(leftX) + 1);
			int leftAlternateX = region.Left + 44;
			Point leftAlternateEntrance = new(leftAlternateX, plan.SurfaceAt(leftAlternateX) + 1);
			Point hall = new(mountain.SaddleX, hallY);
			Point rightEntrance = new(rightX, plan.SurfaceAt(rightX) + 1);
			int rightAlternateX = region.Right - 44;
			Point rightAlternateEntrance = new(rightAlternateX, plan.SurfaceAt(rightAlternateX) + 1);

			CarveRoute(leftEntrance, hall, protectSensitiveTiles);
			CarveRoute(leftAlternateEntrance, hall, protectSensitiveTiles);
			CarveRoute(hall, rightEntrance, protectSensitiveTiles);
			CarveRoute(hall, rightAlternateEntrance, protectSensitiveTiles);
			CarveChamber(new Point(mountain.LeftPeakX, hallY - 18), 22, 12, protectSensitiveTiles);
			CarveChamber(new Point(mountain.RightPeakX, hallY + 8), 26, 14, protectSensitiveTiles);
			CarveChimney(plan, mountain.LeftPeakX, hallY - 18, protectSensitiveTiles);
			CarveChimney(plan, mountain.RightPeakX, hallY + 8, protectSensitiveTiles);

			if (reserveRoutes) {
				ProtectRoute(leftEntrance, hall);
				ProtectRoute(leftAlternateEntrance, hall);
				ProtectRoute(hall, rightEntrance);
				ProtectRoute(hall, rightAlternateEntrance);
			}
		}
	}

	public static void BuildValleys(WorldPlan plan, GenerationManifest manifest)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			ValleyRecord valley = BuildValley(plan, mountain);
			manifest.Valleys.Add(valley);
		}
	}

	public static void BuildBridges(WorldPlan plan, GenerationManifest manifest)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			BridgeRecord bridge = BuildBridge(plan, mountain);
			manifest.Bridges.Add(bridge);
			GenVars.structures.AddProtectedStructure(bridge.Area, padding: 5);
		}
	}

	public static void RecordFinalState(WorldPlan plan, GenerationManifest manifest)
	{
		manifest.Mountains.Clear();
		foreach (MountainRangePlan mountain in plan.Mountains) {
			WorldRegion region = plan.Regions[mountain.RegionId];
			int[] groundedSurface = WorldValidator.MeasureGroundedMountainSurface(region);
			int peakY = int.MaxValue;
			int cloudTiles = 0;
			for (int x = region.Left; x <= region.Right; x++) {
				int surfaceY = groundedSurface[x - region.Left];
				if (surfaceY != int.MaxValue) {
					peakY = Math.Min(peakY, surfaceY);
				}
				for (int y = 45; y < Math.Min((int)Main.worldSurface, 220); y++) {
					Tile tile = Main.tile[x, y];
					if (tile.HasTile && tile.TileType is TileID.Cloud or TileID.RainCloud or TileID.SnowCloud) {
						cloudTiles++;
					}
				}
			}

			if (peakY == int.MaxValue) {
				peakY = (int)Main.worldSurface;
			}
			Rectangle area = new(
				region.Left,
				Math.Max(45, peakY - 8),
				region.Width,
				Math.Min(Main.maxTilesY - 45, (int)Main.worldSurface + 70) - Math.Max(45, peakY - 8));
			manifest.Mountains.Add(new MountainRecord(
				mountain.RegionId,
				area,
				peakY,
				CountEntrances(plan, mountain),
				cloudTiles));
		}
	}

	public static void RepairBridgePortals(WorldPlan plan)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			int leftX = (mountain.LeftPeakX + mountain.SaddleX) / 2;
			int rightX = (mountain.SaddleX + mountain.RightPeakX) / 2;
			int leftDeckY = plan.SurfaceAt(leftX) - 2;
			int rightDeckY = plan.SurfaceAt(rightX) - 2;
			BuildBridgeApproach(leftX, leftDeckY, direction: -1, style: mountain.BridgeStyle);
			BuildBridgeApproach(rightX, rightDeckY, direction: 1, style: mountain.BridgeStyle);
		}
	}

	private static void CarveRoute(Point start, Point end, bool protectSensitiveTiles)
	{
		int steps = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
		for (int step = 0; step <= steps; step++) {
			double t = steps == 0 ? 0d : (double)step / steps;
			double eased = t * t * (3d - 2d * t);
			int x = (int)Math.Round(start.X + (end.X - start.X) * t);
			int floorY = (int)Math.Round(start.Y + (end.Y - start.Y) * eased);
			CarveEllipse(x, floorY - RouteHalfHeight, 4, RouteHalfHeight, protectSensitiveTiles);
			for (int depth = 0; depth < 3; depth++) {
				if (WorldGen.InWorld(x, floorY + depth, 8) && CanMutate(x, floorY + depth, protectSensitiveTiles)) {
					TileEditor.SetTerrain(x, floorY + depth, depth == 0 ? TileID.StoneSlab : TileID.Stone);
				}
			}
		}
	}

	private static void CarveChamber(Point center, int horizontalRadius, int verticalRadius, bool protectSensitiveTiles)
	{
		CarveEllipse(center.X, center.Y, horizontalRadius, verticalRadius, protectSensitiveTiles);
		int shelfY = center.Y + verticalRadius - 1;
		for (int x = center.X - horizontalRadius + 3; x <= center.X + horizontalRadius - 3; x++) {
			for (int depth = 0; depth < 3; depth++) {
				if (CanMutate(x, shelfY + depth, protectSensitiveTiles)) {
					TileEditor.SetTerrain(x, shelfY + depth, depth == 0 ? TileID.StoneSlab : TileID.Stone);
				}
			}
		}
	}

	private static void CarveChimney(WorldPlan plan, int x, int bottomY, bool protectSensitiveTiles)
	{
		int topY = plan.SurfaceAt(x) + 1;
		if (topY >= bottomY - 12) {
			return;
		}

		for (int offset = -3; offset <= 3; offset++) {
			TileEditor.TryPlacePlatformForced(x + offset, topY - 1);
		}
		for (int y = topY; y <= bottomY; y++) {
			for (int offset = -3; offset <= 3; offset++) {
				if (CanMutate(x + offset, y, protectSensitiveTiles)) {
					TileEditor.ClearTerrain(x + offset, y);
					TileEditor.SetWall(x + offset, y, (y - topY) % 30 < 15 ? WallID.Stone : WallID.Planked);
				}
			}
			if (CanMutate(x, y, protectSensitiveTiles)) {
				TileEditor.SetTerrain(x, y, TileID.Rope);
			}
			if ((y - topY) % 12 == 0) {
				for (int offset = -3; offset <= 3; offset++) {
					if (offset != 0) {
						TileEditor.TryPlacePlatformForced(x + offset, y);
					}
				}
			}
		}
	}

	private static void CarveEllipse(
		int centerX,
		int centerY,
		int horizontalRadius,
		int verticalRadius,
		bool protectSensitiveTiles)
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
				if (!CanMutate(x, y, protectSensitiveTiles)) {
					continue;
				}
				TileEditor.ClearTerrain(x, y);
				if (Main.tile[x, y].WallType == WallID.None) {
					TileEditor.SetWall(x, y, WallID.Stone);
				}
			}
		}
	}

	private static bool CanMutate(int x, int y, bool protectSensitiveTiles)
	{
		if (!WorldGen.InWorld(x, y, 8)) {
			return false;
		}
		return !protectSensitiveTiles || !TileEditor.IsProtectedTile(Main.tile[x, y]);
	}

	private static void ProtectRoute(Point start, Point end)
	{
		int steps = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
		for (int step = 0; step <= steps; step += 14) {
			double t = steps == 0 ? 0d : (double)step / steps;
			int x = (int)Math.Round(start.X + (end.X - start.X) * t);
			int y = (int)Math.Round(start.Y + (end.Y - start.Y) * t);
			GenVars.structures.AddProtectedStructure(new Rectangle(x - 6, y - 8, 13, 16), padding: 1);
		}
	}

	private static ValleyRecord BuildValley(WorldPlan plan, MountainRangePlan mountain)
	{
		int halfWidth = Math.Clamp((mountain.RightPeakX - mountain.LeftPeakX) / 5, 24, 42);
		int left = mountain.SaddleX - halfWidth;
		int right = mountain.SaddleX + halfWidth;
		int surfaceY = plan.SurfaceAt(mountain.SaddleX);
		int depth = mountain.ValleyTheme switch {
			ValleyTheme.Wooded => 8,
			ValleyTheme.SealedEvil => 16,
			_ => 13
		};
		Rectangle area = new(left - 3, surfaceY - 3, right - left + 7, depth + 12);
		if (!TileEditor.IsSafeForTerrainFeature(area)) {
			return new ValleyRecord(ValleyTheme.Wooded, area, 0);
		}

		int liquidCells = 0;
		int previousFloor = surfaceY + 3;
		for (int x = left; x <= right; x++) {
			double t = (double)(x - left) / Math.Max(1, right - left);
			double bowl = Math.Sin(Math.PI * t);
			int jitter = HashNoise(x, mountain.RegionId * 97) % 5 - 2;
			int targetFloor = surfaceY + 3 + (int)Math.Round(depth * bowl) + jitter;
			int floorY = Math.Clamp(targetFloor, previousFloor - 1, previousFloor + 1);
			previousFloor = floorY;
			for (int y = surfaceY - 1; y < floorY; y++) {
				TileEditor.ClearTerrain(x, y);
			}

			ushort liner = mountain.ValleyTheme == ValleyTheme.Lava ? TileID.ObsidianBrick : TileID.Stone;
			for (int linerDepth = 0; linerDepth < 4; linerDepth++) {
				TileEditor.SetTerrain(x, floorY + linerDepth, liner);
			}
			if (mountain.ValleyTheme is (ValleyTheme.Lake or ValleyTheme.Lava) && floorY >= surfaceY + 6) {
				byte liquidType = (byte)(mountain.ValleyTheme == ValleyTheme.Lava ? LiquidID.Lava : LiquidID.Water);
				for (int y = surfaceY + 5; y < floorY; y++) {
					TileEditor.SetLiquid(x, y, liquidType, byte.MaxValue);
					liquidCells++;
				}
			}
		}

		if (mountain.ValleyTheme == ValleyTheme.SealedEvil) {
			BuildSealedEvilGrotto(mountain.SaddleX, surfaceY + 10);
		}
		TileEditor.Frame(area);
		GenVars.structures.AddProtectedStructure(area, padding: 3);
		return new ValleyRecord(mountain.ValleyTheme, area, liquidCells);
	}

	private static void BuildSealedEvilGrotto(int centerX, int centerY)
	{
		const int radiusX = 23;
		const int radiusY = 13;
		Rectangle outer = new(centerX - radiusX - 2, centerY - radiusY - 2, radiusX * 2 + 5, radiusY * 2 + 5);
		ushort evilStone = WorldGen.crimson ? TileID.Crimstone : TileID.Ebonstone;
		ushort evilWall = WorldGen.crimson ? WallID.CrimstoneUnsafe : WallID.EbonstoneUnsafe;
		for (int x = centerX - radiusX; x <= centerX + radiusX; x++) {
			double normalizedX = (double)(x - centerX) / radiusX;
			int halfHeight = Math.Max(3, (int)Math.Round(radiusY * Math.Sqrt(Math.Max(0d, 1d - normalizedX * normalizedX))));
			int topJitter = HashNoise(x, centerY) % 3 - 1;
			int bottomJitter = HashNoise(x, centerY + 113) % 5 - 2;
			int outerTop = centerY - halfHeight + topJitter;
			int outerBottom = centerY + halfHeight + bottomJitter;
			int shellThickness = 4 + HashNoise(x, centerY + 227) % 3;
			for (int y = outerTop; y <= outerBottom; y++) {
				TileEditor.SetTerrain(x, y, TileID.GrayBrick);
			}
			int innerTop = outerTop + shellThickness;
			int innerBottom = outerBottom - shellThickness;
			for (int y = innerTop; y <= innerBottom; y++) {
				if (y >= innerBottom - 3) {
					TileEditor.SetTerrain(x, y, evilStone);
				}
				else {
					TileEditor.ClearTerrain(x, y);
					TileEditor.SetWall(x, y, evilWall);
				}
			}
		}
		TileEditor.Frame(outer, border: 3);
	}

	private static BridgeRecord BuildBridge(WorldPlan plan, MountainRangePlan mountain)
	{
		int leftX = (mountain.LeftPeakX + mountain.SaddleX) / 2;
		int rightX = (mountain.SaddleX + mountain.RightPeakX) / 2;
		int leftY = plan.SurfaceAt(leftX) - 2;
		int rightY = plan.SurfaceAt(rightX) - 2;
		int top = Math.Min(leftY, rightY) - 20;
		int bottom = Math.Max(plan.SurfaceAt(mountain.SaddleX), Math.Max(leftY, rightY)) + 4;
		Rectangle area = new(leftX - 12, top, rightX - leftX + 25, bottom - top + 1);
		int deckTiles = 0;
		int leftDeckY = leftY;
		int rightDeckY = rightY;
		for (int x = leftX; x <= rightX; x++) {
			double t = (double)(x - leftX) / Math.Max(1, rightX - leftX);
			int baseY = (int)Math.Round(leftY + (rightY - leftY) * t);
			int deckY = baseY + (int)Math.Round(Math.Sin(Math.PI * t) * 4d);
			if (x == leftX) {
				leftDeckY = deckY;
			}
			if (x == rightX) {
				rightDeckY = deckY;
			}
			for (int y = top; y <= deckY - 1; y++) {
				if (Main.tile[x, y].HasTile && !TileEditor.IsProtectedTile(Main.tile[x, y])) {
					TileEditor.ClearTerrain(x, y);
				}
			}

			ushort backdrop = mountain.BridgeStyle == BridgeStyle.StoneArch ? WallID.GrayBrick : WallID.Planked;
			if ((x - leftX) % 28 is >= 3 and <= 23) {
				for (int y = deckY - 5; y < deckY; y++) {
					TileEditor.SetWall(x, y, backdrop);
				}
			}

			int bay = Math.Abs(x - leftX) % 46;
			bool dropPortal = mountain.BridgeStyle != BridgeStyle.StoneArch
				&& x > leftX + 18 && x < rightX - 18 && bay is >= 20 and <= 25;
			if (dropPortal) {
				if (TileEditor.TryPlacePlatformForced(x, deckY)) {
					deckTiles++;
				}
				TileEditor.ClearTerrain(x, deckY + 1);
				TileEditor.ClearTerrain(x, deckY + 2);
			}
			else {
				ushort topMaterial = mountain.BridgeStyle == BridgeStyle.StoneArch ? TileID.GrayBrick : TileID.LivingWood;
				ushort coreMaterial = mountain.BridgeStyle == BridgeStyle.StoneArch ? TileID.StoneSlab : TileID.LivingWood;
				TileEditor.SetTerrain(x, deckY, topMaterial);
				TileEditor.SetTerrain(x, deckY + 1, coreMaterial);
				TileEditor.SetTerrain(x, deckY + 2, topMaterial);
				deckTiles++;
			}

			int trussPosition = Math.Abs(x - leftX) % 14;
			int trussDepth = Math.Min(trussPosition, 14 - trussPosition) / 2;
			if (!dropPortal) {
				ushort trussMaterial = mountain.BridgeStyle == BridgeStyle.StoneArch ? TileID.StoneSlab : TileID.WoodenBeam;
				TileEditor.SetTerrain(x, deckY + 3 + trussDepth, trussMaterial);
				if (mountain.BridgeStyle != BridgeStyle.RailTrestle) {
					TileEditor.SetTerrain(x, deckY + 4 + trussDepth, trussMaterial);
				}
			}
			if (mountain.BridgeStyle == BridgeStyle.StoneArch) {
				double archX = Math.Abs(2d * t - 1d);
				int archRingY = deckY + 5 + (int)Math.Round(12d * (1d - Math.Sqrt(Math.Max(0d, 1d - archX * archX))));
				TileEditor.SetTerrain(x, archRingY, TileID.GrayBrick);
				TileEditor.SetTerrain(x, archRingY + 1, TileID.StoneSlab);
			}

			if ((x - leftX) % 14 == 0) {
				int supportBottom = Math.Min(bottom - 1, deckY + 14 + HashNoise(x, mountain.RegionId) % 6);
				for (int y = deckY + 3; y <= supportBottom; y++) {
					TileEditor.SetTerrain(x, y, mountain.BridgeStyle == BridgeStyle.StoneArch ? TileID.StoneSlab : TileID.WoodenBeam);
				}
				TileEditor.TryPlaceTorch(x + 1, deckY - 3);
			}
		}

		BuildBridgeTower(leftX, leftDeckY, top, mountain.BridgeStyle);
		BuildBridgeTower(rightX, rightDeckY, top, mountain.BridgeStyle);
		TileEditor.Frame(area);
		BuildBridgeApproach(leftX, leftDeckY, direction: -1, style: mountain.BridgeStyle);
		BuildBridgeApproach(rightX, rightDeckY, direction: 1, style: mountain.BridgeStyle);
		return new BridgeRecord(mountain.BridgeStyle, area, deckTiles);
	}

	private static void BuildBridgeTower(int x, int deckY, int top, BridgeStyle style)
	{
		ushort material = style == BridgeStyle.StoneArch ? TileID.GrayBrick : TileID.LivingWood;
		int towerTop = Math.Max(top + 2, deckY - 12);
		for (int y = towerTop; y <= deckY + 2; y++) {
			for (int thickness = 0; thickness < 2; thickness++) {
				TileEditor.SetTerrain(x - 5 - thickness, y, material);
				TileEditor.SetTerrain(x + 5 + thickness, y, material);
			}
		}
		for (int columnX = x - 6; columnX <= x + 6; columnX++) {
			TileEditor.SetTerrain(columnX, towerTop, material);
			TileEditor.SetTerrain(columnX, towerTop + 1, material);
		}
	}

	private static void BuildBridgeApproach(int endpointX, int deckY, int direction, BridgeStyle style)
	{
		ushort material = style == BridgeStyle.StoneArch ? TileID.GrayBrick : TileID.LivingWood;
		ushort wall = style == BridgeStyle.StoneArch ? WallID.GrayBrick : WallID.Planked;
		for (int step = 0; step <= 11; step++) {
			int x = endpointX + direction * step;
			for (int y = deckY - 6; y < deckY; y++) {
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, wall);
			}
			for (int depth = 0; depth < 3; depth++) {
				TileEditor.SetTerrain(x, deckY + depth, depth == 1 ? TileID.StoneSlab : material);
			}
		}

		int gateX = endpointX + direction * 8;
		for (int gateWidth = 0; gateWidth < 2; gateWidth++) {
			for (int y = deckY - 5; y < deckY; y++) {
				TileEditor.SetActuatedTerrain(gateX + direction * gateWidth, y, material);
			}
		}
	}

	private static int HashNoise(int x, int salt)
	{
		unchecked {
			uint value = (uint)(x * 0x45D9F3B) ^ (uint)salt;
			value ^= value >> 16;
			value *= 0x7FEB352D;
			value ^= value >> 15;
			return (int)(value & 0x7FFFFFFF);
		}
	}

	private static int CountEntrances(WorldPlan plan, MountainRangePlan mountain)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		int entrances = 0;
		int[][] entranceCandidates = [
			[region.Left + 22, region.Left + 44],
			[region.Right - 22, region.Right - 44]
		];
		foreach (int[] sideCandidates in entranceCandidates) {
			bool clear = false;
			foreach (int entranceX in sideCandidates) {
				for (int x = entranceX - 7; x <= entranceX + 7 && !clear; x++) {
					int y = plan.SurfaceAt(x);
					for (int offsetY = -3; offsetY <= 4; offsetY++) {
						if (!TileEditor.IsSolid(x, y + offsetY)) {
							clear = true;
							break;
						}
					}
				}
				if (clear) {
					break;
				}
			}
			if (clear) {
				entrances++;
			}
		}
		return entrances;
	}
}
