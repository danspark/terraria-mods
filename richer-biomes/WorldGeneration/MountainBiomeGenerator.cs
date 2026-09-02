using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace RicherBiomes.WorldGeneration;

internal static class MountainBiomeGenerator
{
	private const int MinimumRouteRadius = 5;

	public static void CarveInteriors(WorldPlan plan, bool protectSensitiveTiles, bool reserveRoutes)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			MountainInteriorLayout layout = BuildInteriorLayout(plan, mountain);
			for (int routeIndex = 0; routeIndex < layout.Routes.Count; routeIndex++) {
				CarveRoute(plan, mountain, layout.Routes[routeIndex], routeIndex, protectSensitiveTiles);
			}
			for (int chamberIndex = 0; chamberIndex < layout.Chambers.Count; chamberIndex++) {
				CarveChamber(plan, mountain, layout.Chambers[chamberIndex], chamberIndex, protectSensitiveTiles);
			}
			foreach (MountainShaft shaft in layout.Shafts) {
				CarveShaft(plan, mountain, shaft, protectSensitiveTiles);
			}
			if (layout.WallClimb is MountainWallClimb wallClimb) {
				CarveWallClimb(plan, mountain, wallClimb, protectSensitiveTiles);
			}

			if (reserveRoutes) {
				foreach (Point[] route in layout.Routes) {
					ProtectRoute(route);
				}
				foreach (MountainChamber chamber in layout.Chambers) {
					Rectangle area = new(
						chamber.Center.X - chamber.RadiusX - 4,
						chamber.Center.Y - chamber.RadiusY - 4,
						chamber.RadiusX * 2 + 9,
						chamber.RadiusY * 2 + 9);
					GenVars.structures.AddProtectedStructure(area, padding: 1);
				}
				if (layout.WallClimb is MountainWallClimb reservedClimb) {
					GenVars.structures.AddProtectedStructure(WallClimbArea(reservedClimb), padding: 3);
				}
			}
		}
	}

	public static void DecorateInteriors(WorldPlan plan, GenerationManifest manifest, GenerationProgress progress)
	{
		for (int mountainIndex = 0; mountainIndex < plan.Mountains.Count; mountainIndex++) {
			MountainRangePlan mountain = plan.Mountains[mountainIndex];
			MountainInteriorLayout layout = BuildInteriorLayout(plan, mountain);
			UnifiedRandom random = new(MixSeed(mountain.FeatureSeed, 0x4D44_4543));
			PlaceClimbAids(layout, random, manifest);
			PlaceFloatingInclusions(plan, mountain, layout, random, manifest);
			PlaceChamberVignettes(layout, random, manifest);
			PlaceInteriorObjects(plan, mountain, random, manifest);
			EnsureInteriorDecorationMinimums(plan, mountain, manifest);
			TileEditor.Frame(MountainArea(plan, mountain), border: 3);
			progress.Set((double)(mountainIndex + 1) / Math.Max(1, plan.Mountains.Count));
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
			int[] groundedSurface = WorldValidator.MeasureGroundedMountainSurface(plan, region);
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
			(int caveAirTiles, int wideCavityColumns) = MeasureCavities(area);
			manifest.Mountains.Add(new MountainRecord(
				mountain.RegionId,
				area,
				peakY,
				CountEntrances(plan, mountain),
				cloudTiles,
				mountain.InteriorStyle,
				caveAirTiles,
				wideCavityColumns,
				CountTiles(area, TileID.Pots),
				CountVines(area),
				CountTiles(area, TileID.Platforms) + CountTiles(area, TileID.Rope)));
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
			ActuateBridgeTowerPassage(leftX, leftDeckY, mountain.BridgeStyle);
			ActuateBridgeTowerPassage(rightX, rightDeckY, mountain.BridgeStyle);
			RepairBridgeEndpointCorridor(leftX, leftDeckY, mountain.BridgeStyle);
			RepairBridgeEndpointCorridor(rightX, rightDeckY, mountain.BridgeStyle);
		}
	}

	public static void FinishInteriorWalls(WorldPlan plan, GenerationManifest manifest)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			Rectangle area = MountainArea(plan, mountain);
			for (int x = area.Left + 3; x < area.Right - 3; x++) {
				int surfaceY = plan.SurfaceAt(x);
				for (int y = Math.Max(area.Top + 3, surfaceY + 4); y < area.Bottom - 3; y++) {
					if (IsDecorationExcluded(manifest, x, y) || IsMineRailEnvelope(x, y)) {
						continue;
					}
					Tile tile = Main.tile[x, y];
					if (tile.HasTile && Main.tileFrameImportant[tile.TileType]) {
						continue;
					}
					if (!TileEditor.IsSolid(x, y) && ShouldLeaveWallVoid(plan, mountain, x, y)) {
						TileEditor.SetWall(x, y, WallID.None);
						continue;
					}
					if (tile.WallType == WallID.None && !TileEditor.IsSolid(x, y)) {
						continue;
					}
					if (tile.WallType != WallID.None && !IsNaturalMountainWall(tile.WallType)) {
						continue;
					}
					TileEditor.SetWall(x, y, MountainWallAt(plan, mountain, x, y, manifest));
				}
			}
			BreakLongWallSeams(area, manifest);
			TileEditor.Frame(area, border: 2);
		}
	}

	private static void BreakLongWallSeams(Rectangle area, GenerationManifest manifest)
	{
		for (int x = area.Left + 2; x < area.Right - 3; x++) {
			int runStart = -1;
			for (int y = area.Top + 2; y <= area.Bottom - 2; y++) {
				bool boundary = y < area.Bottom - 2
					&& IsOpenNaturalWallCell(manifest, x, y)
					&& IsOpenNaturalWallCell(manifest, x + 1, y)
					&& Main.tile[x, y].WallType != Main.tile[x + 1, y].WallType;
				if (boundary && runStart < 0) {
					runStart = y;
				}
				if (boundary) {
					continue;
				}
				if (runStart >= 0 && y - runStart > 28) {
					for (int seamY = runStart; seamY < y; seamY++) {
						ushort leftWall = Main.tile[x, seamY].WallType;
						ushort rightWall = Main.tile[x + 1, seamY].WallType;
						if (((seamY - runStart) / 7) % 2 == 0) {
							TileEditor.SetWall(x + 1, seamY, leftWall);
						}
						else {
							TileEditor.SetWall(x, seamY, rightWall);
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
					&& IsOpenNaturalWallCell(manifest, x, y)
					&& IsOpenNaturalWallCell(manifest, x, y + 1)
					&& Main.tile[x, y].WallType != Main.tile[x, y + 1].WallType;
				if (boundary && runStart < 0) {
					runStart = x;
				}
				if (boundary) {
					continue;
				}
				if (runStart >= 0 && x - runStart > 28) {
					for (int seamX = runStart; seamX < x; seamX++) {
						ushort upperWall = Main.tile[seamX, y].WallType;
						ushort lowerWall = Main.tile[seamX, y + 1].WallType;
						if (((seamX - runStart) / 7) % 2 == 0) {
							TileEditor.SetWall(seamX, y + 1, upperWall);
						}
						else {
							TileEditor.SetWall(seamX, y, lowerWall);
						}
					}
				}
				runStart = -1;
			}
		}
	}

	private static bool IsOpenNaturalWallCell(GenerationManifest manifest, int x, int y) =>
		!IsDecorationExcluded(manifest, x, y)
		&& !TileEditor.IsSolid(x, y)
		&& IsNaturalMountainWall(Main.tile[x, y].WallType);

	public static void RepairInteriorDecorations(WorldPlan plan, GenerationManifest manifest)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			EnsureInteriorDecorationMinimums(plan, mountain, manifest);
			TileEditor.Frame(MountainArea(plan, mountain), border: 2);
		}
	}

	public static void RepairValleyStructures(WorldPlan plan)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			if (mountain.ValleyTheme == ValleyTheme.SealedEvil) {
				int surfaceY = plan.SurfaceAt(mountain.SaddleX);
				BuildSealedEvilGrotto(mountain.SaddleX, surfaceY + 10);
			}
			else if (mountain.ValleyTheme is ValleyTheme.Lake or ValleyTheme.Lava) {
				_ = BuildValley(plan, mountain);
			}
		}
	}

	public static void RefillValleyLiquids(GenerationManifest manifest)
	{
		for (int index = 0; index < manifest.Valleys.Count; index++) {
			ValleyRecord valley = manifest.Valleys[index];
			if (valley.Theme is not (ValleyTheme.Lake or ValleyTheme.Lava)) {
				continue;
			}
			byte liquidType = (byte)(valley.Theme == ValleyTheme.Lava ? LiquidID.Lava : LiquidID.Water);
			int liquidCells = 0;
			int waterline = valley.Area.Top + 8;
			for (int x = valley.Area.Left + 3; x < valley.Area.Right - 3; x++) {
				for (int y = waterline; y < valley.Area.Bottom - 3; y++) {
					Tile tile = Main.tile[x, y];
					if (tile.HasTile) {
						continue;
					}
					TileEditor.SetLiquid(x, y, liquidType, byte.MaxValue);
					liquidCells++;
				}
			}
			manifest.Valleys[index] = valley with { LiquidCells = liquidCells };
		}
	}

	public static void RepairGroundingSpines(WorldPlan plan)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			SkyHighlandPlan? attachedHighland = plan.SkyHighlands
				.Where(highland => highland.AttachedMountainRegionId == mountain.RegionId)
				.Select<SkyHighlandPlan, SkyHighlandPlan?>(highland => highland)
				.FirstOrDefault();
			int peakX = attachedHighland is SkyHighlandPlan highland
				&& Math.Abs(highland.CenterX - mountain.LeftPeakX) < Math.Abs(highland.CenterX - mountain.RightPeakX)
					? mountain.RightPeakX
					: mountain.LeftPeakX;
			foreach (int spineX in new[] { peakX - 12, peakX + 12 }) {
				int topY = plan.SurfaceAt(spineX);
				int bottomY = Math.Min(Main.maxTilesY - 50, (int)Main.worldSurface + 55);
				for (int x = spineX - 4; x <= spineX + 4; x++) {
					for (int y = plan.SurfaceAt(x); y <= bottomY; y++) {
						if (TileEditor.IsProtectedTile(Main.tile[x, y]) || IsMineRailEnvelope(x, y)) {
							continue;
						}
						int depth = y - plan.SurfaceAt(x);
						ushort terrain = LandformGenerator.MountainTerrainAt(x, plan.SurfaceAt(x), depth);
						TileEditor.SetTerrain(x, y, terrain);
						if (depth >= 4) {
							TileEditor.SetWall(x, y, depth < 18 ? WallID.DirtUnsafe : WallID.Stone);
						}
					}
				}
				for (int portalY = topY + 20; portalY < bottomY - 8; portalY += 27) {
					for (int x = spineX - 2; x <= spineX + 2; x++) {
						for (int y = portalY - 7; y < portalY; y++) {
							if (!TileEditor.IsProtectedTile(Main.tile[x, y]) && !IsMineRailEnvelope(x, y)) {
								TileEditor.ClearTerrain(x, y);
								TileEditor.SetWall(x, y, WallID.Stone);
							}
						}
					}
					for (int length = 0; length < 4; length++) {
						int vineY = portalY - 7 + length;
						if (Main.tile[spineX, vineY].HasTile || IsMineRailEnvelope(spineX, vineY)) {
							break;
						}
						TileEditor.SetTerrain(spineX, vineY, VineTypeAt(spineX, portalY - 8));
					}
				}
			}
		}
	}

	public static void RepairEntrances(WorldPlan plan, GenerationManifest manifest)
	{
		foreach (MountainRangePlan mountain in plan.Mountains) {
			MountainInteriorLayout layout = BuildInteriorLayout(plan, mountain);
			foreach (Point entrance in layout.Entrances) {
				for (int offsetX = -6; offsetX <= 6; offsetX++) {
					for (int offsetY = -5; offsetY <= 6; offsetY++) {
						double normalized = (double)(offsetX * offsetX) / 36d + (double)(offsetY * offsetY) / 36d;
						if (normalized > 1.08d) {
							continue;
						}
						int x = entrance.X + offsetX;
						int y = entrance.Y + offsetY;
						if (!WorldGen.InWorld(x, y, 10) || IsDecorationExcluded(manifest, x, y)
							|| TileEditor.IsProgressionTile(Main.tile[x, y]) || IsMineRailEnvelope(x, y)) {
							continue;
						}
						TileEditor.ClearTerrain(x, y);
						if (y >= plan.SurfaceAt(x) + 2) {
							TileEditor.SetWall(x, y, MountainWallAt(plan, mountain, x, y, manifest));
						}
					}
				}
			}
			TileEditor.Frame(MountainArea(plan, mountain), border: 2);
		}
	}

	private static bool IsMineRailEnvelope(int x, int y)
	{
		for (int offsetX = -1; offsetX <= 1; offsetX++) {
			for (int offsetY = 0; offsetY <= 7; offsetY++) {
				Tile tile = Main.tile[x + offsetX, y + offsetY];
				if (tile.HasTile && tile.TileType == TileID.MinecartTrack) {
					return true;
				}
			}
		}
		return false;
	}

	private static MountainInteriorLayout BuildInteriorLayout(WorldPlan plan, MountainRangePlan mountain)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		UnifiedRandom random = new(mountain.FeatureSeed);
		int leftEntranceX = region.Left + random.Next(18, Math.Min(68, region.Width / 5));
		int rightEntranceX = region.Right - random.Next(18, Math.Min(68, region.Width / 5));
		Point leftEntrance = new(leftEntranceX, plan.SurfaceAt(leftEntranceX) + 2);
		Point rightEntrance = new(rightEntranceX, plan.SurfaceAt(rightEntranceX) + 2);
		Point leftUpper = Inside(plan, mountain.LeftPeakX + random.Next(-28, 29), mountain.LeftPeakY + random.Next(64, 116));
		Point rightUpper = Inside(plan, mountain.RightPeakX + random.Next(-28, 29), mountain.RightPeakY + random.Next(64, 116));
		Point central = Inside(plan, mountain.SaddleX + random.Next(-24, 25), mountain.SaddleY + random.Next(46, 92));
		Point lower = Inside(plan, mountain.SaddleX + random.Next(-55, 56), (int)Main.worldSurface + random.Next(26, 62));
		Point leftLower = Inside(plan, region.Left + region.Width * random.Next(28, 42) / 100, (int)Main.worldSurface + random.Next(5, 48));
		Point rightLower = Inside(plan, region.Left + region.Width * random.Next(60, 75) / 100, (int)Main.worldSurface + random.Next(5, 48));
		Point leftSummit = new(mountain.LeftPeakX + random.Next(-10, 11), plan.SurfaceAt(mountain.LeftPeakX) + 3);
		Point rightSummit = new(mountain.RightPeakX + random.Next(-10, 11), plan.SurfaceAt(mountain.RightPeakX) + 3);

		List<Point[]> routes = [];
		List<MountainShaft> shafts = [];
		switch (mountain.InteriorStyle) {
			case MountainInteriorStyle.BranchingGrottoes:
				routes.Add([leftEntrance, leftLower, leftUpper, central]);
				routes.Add([central, rightUpper, rightLower, rightEntrance]);
				routes.Add([leftUpper, lower, rightUpper]);
				routes.Add([central, leftSummit]);
				shafts.Add(new MountainShaft(leftSummit, central, random.Next(3, 6)));
				break;
			case MountainInteriorStyle.SwitchbackClimb:
				routes.Add([leftEntrance, leftLower, central, rightUpper, rightSummit]);
				routes.Add([rightEntrance, rightLower, lower, central]);
				routes.Add([leftLower, leftUpper, rightUpper]);
				shafts.Add(new MountainShaft(rightSummit, rightUpper, random.Next(4, 7)));
				shafts.Add(new MountainShaft(leftUpper, leftLower, random.Next(3, 6)));
				break;
			case MountainInteriorStyle.SplitLevelCaves:
				routes.Add([leftEntrance, leftUpper, central, rightUpper, rightEntrance]);
				routes.Add([leftLower, lower, rightLower]);
				routes.Add([leftUpper, leftLower]);
				routes.Add([rightUpper, rightLower]);
				shafts.Add(new MountainShaft(leftUpper, leftLower, random.Next(3, 6)));
				shafts.Add(new MountainShaft(rightUpper, rightLower, random.Next(3, 6)));
				break;
			case MountainInteriorStyle.OpenFault:
				routes.Add([leftEntrance, leftLower, lower]);
				routes.Add([rightEntrance, rightLower, lower]);
				routes.Add([leftUpper, central, rightUpper]);
				routes.Add([leftLower, leftUpper]);
				routes.Add([rightLower, rightUpper]);
				Point faultTop = new(mountain.SaddleX, plan.SurfaceAt(mountain.SaddleX) + 1);
				shafts.Add(new MountainShaft(faultTop, lower, random.Next(7, 11)));
				break;
		}

		List<Point> centers = [leftLower, leftUpper, central, lower, rightUpper, rightLower];
		int extraChambers = mountain.InteriorStyle == MountainInteriorStyle.BranchingGrottoes ? 4 : 2;
		for (int index = 0; index < extraChambers; index++) {
			int x = random.Next(region.Left + 70, region.Right - 69);
			int y = random.Next(plan.SurfaceAt(x) + 24, Math.Min((int)Main.worldSurface + 58, plan.SurfaceAt(x) + 190));
			centers.Add(new Point(x, y));
		}

		List<MountainChamber> chambers = [];
		for (int index = 0; index < centers.Count; index++) {
			int radiusX = random.Next(20, mountain.InteriorStyle == MountainInteriorStyle.BranchingGrottoes ? 49 : 42);
			int radiusY = random.Next(12, mountain.InteriorStyle == MountainInteriorStyle.OpenFault ? 31 : 25);
			chambers.Add(new MountainChamber(centers[index], radiusX, radiusY, OpenToSurface: index == centers.Count - 1 && random.NextBool(3)));
		}

		MountainWallClimb? wallClimb = null;
		bool includeWallClimb = mountain.InteriorStyle is MountainInteriorStyle.SwitchbackClimb or MountainInteriorStyle.OpenFault
			|| HashNoise(mountain.RegionId, mountain.FeatureSeed ^ 0x434C_494D) % 100 < 38;
		if (includeWallClimb) {
			int climbX = mountain.InteriorStyle == MountainInteriorStyle.OpenFault
				? mountain.SaddleX + random.Next(-18, 19)
				: random.NextBool() ? leftUpper.X : rightUpper.X;
			climbX = Math.Clamp(climbX, region.Left + 45, region.Right - 45);
			int topY = plan.SurfaceAt(climbX) + random.Next(16, 29);
			int bottomY = Math.Min((int)Main.worldSurface + 58, topY + random.Next(58, 91));
			if (bottomY - topY >= 46) {
				wallClimb = new MountainWallClimb(climbX, topY, bottomY, random.Next(15, 23));
			}
		}

		return new MountainInteriorLayout(routes, chambers, shafts, [leftEntrance, rightEntrance], wallClimb);
	}

	private static Point Inside(WorldPlan plan, int x, int desiredY)
	{
		int clampedX = Math.Clamp(x, plan.LeftBoundary + 20, plan.RightBoundary - 20);
		return new Point(clampedX, Math.Clamp(desiredY, plan.SurfaceAt(clampedX) + 12, (int)Main.worldSurface + 62));
	}

	private static void CarveRoute(
		WorldPlan plan,
		MountainRangePlan mountain,
		IReadOnlyList<Point> points,
		int routeIndex,
		bool protectSensitiveTiles)
	{
		for (int segment = 1; segment < points.Count; segment++) {
			Point start = points[segment - 1];
			Point end = points[segment];
			int steps = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
			if (steps == 0) {
				continue;
			}
			double length = Math.Sqrt((double)(end.X - start.X) * (end.X - start.X) + (double)(end.Y - start.Y) * (end.Y - start.Y));
			double normalX = -(end.Y - start.Y) / Math.Max(1d, length);
			double normalY = (end.X - start.X) / Math.Max(1d, length);
			int bend = 4 + HashNoise(routeIndex * 37 + segment, mountain.FeatureSeed) % 10;
			for (int step = 0; step <= steps; step++) {
				double t = (double)step / steps;
				double wobble = Math.Sin(Math.PI * t) * Math.Sin((t * 2.2d + routeIndex * 0.31d) * Math.PI) * bend;
				int x = (int)Math.Round(start.X + (end.X - start.X) * t + normalX * wobble);
				int y = (int)Math.Round(start.Y + (end.Y - start.Y) * t + normalY * wobble);
				int radius = MinimumRouteRadius + HashNoise(step / 13 + routeIndex * 19, mountain.FeatureSeed + segment * 101) % 4;
				CarveEllipse(plan, mountain, x, y, radius + 1, radius, routeIndex * 101 + segment, protectSensitiveTiles, allowSurfaceOpening: step < 12 || step > steps - 12);
			}
		}
	}

	private static void CarveChamber(
		WorldPlan plan,
		MountainRangePlan mountain,
		MountainChamber chamber,
		int chamberIndex,
		bool protectSensitiveTiles)
	{
		CarveEllipse(
			plan,
			mountain,
			chamber.Center.X,
			chamber.Center.Y,
			chamber.RadiusX,
			chamber.RadiusY,
			chamberIndex * 211,
			protectSensitiveTiles,
			chamber.OpenToSurface);
	}

	private static void CarveShaft(
		WorldPlan plan,
		MountainRangePlan mountain,
		MountainShaft shaft,
		bool protectSensitiveTiles)
	{
		int topY = Math.Min(shaft.Top.Y, shaft.Bottom.Y);
		int bottomY = Math.Max(shaft.Top.Y, shaft.Bottom.Y);
		int centerX = (shaft.Top.X + shaft.Bottom.X) / 2;
		for (int y = topY; y <= bottomY; y++) {
			double t = (double)(y - topY) / Math.Max(1, bottomY - topY);
			int x = centerX + (int)Math.Round(Math.Sin(t * Math.PI * 3d + mountain.RegionId) * 3d);
			CarveEllipse(plan, mountain, x, y, shaft.HalfWidth, 4, y / 9, protectSensitiveTiles, allowSurfaceOpening: y < topY + 10);
		}
	}

	private static void CarveWallClimb(
		WorldPlan plan,
		MountainRangePlan mountain,
		MountainWallClimb climb,
		bool protectSensitiveTiles)
	{
		int centerY = (climb.TopY + climb.BottomY) / 2;
		int halfHeight = Math.Max(1, (climb.BottomY - climb.TopY) / 2);
		for (int x = climb.CenterX - climb.HalfWidth; x <= climb.CenterX + climb.HalfWidth; x++) {
			for (int y = climb.TopY; y <= climb.BottomY; y++) {
				double normalizedX = (double)(x - climb.CenterX) * (x - climb.CenterX)
					/ Math.Max(1, climb.HalfWidth * climb.HalfWidth);
				double normalizedY = (double)(y - centerY) * (y - centerY)
					/ Math.Max(1, halfHeight * halfHeight);
				double jitter = (HashNoise(x * 5 + y * 3, mountain.FeatureSeed ^ 0x5743_4156) % 1000 / 1000d - 0.5d) * 0.18d;
				if (normalizedX + normalizedY > 1d + jitter || !CanMutate(x, y, protectSensitiveTiles)) {
					continue;
				}
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, MountainWallAt(plan, mountain, x, y));
			}
		}

		int ledgeIndex = 0;
		for (int y = climb.BottomY - 11; y > climb.TopY + 9; y -= 12 + HashNoise(y, mountain.FeatureSeed) % 6) {
			bool fromLeft = ledgeIndex++ % 2 == 0;
			int length = climb.HalfWidth + HashNoise(y, mountain.FeatureSeed ^ 0x4C45_4447) % Math.Max(5, climb.HalfWidth / 2);
			int startX = fromLeft ? climb.CenterX - climb.HalfWidth + 2 : climb.CenterX + climb.HalfWidth - 1;
			int direction = fromLeft ? 1 : -1;
			for (int step = 0; step < length; step++) {
				int x = startX + direction * step;
				if (!CanMutate(x, y, protectSensitiveTiles) || Math.Abs(x - climb.CenterX) < 4) {
					continue;
				}
				ushort material = step % 5 == 0
					? TileID.StoneSlab
					: LandformGenerator.MountainTerrainAt(x, plan.SurfaceAt(x), y - plan.SurfaceAt(x));
				TileEditor.SetTerrain(x, y, material);
				if (step < 4 && CanMutate(x, y + 1, protectSensitiveTiles)) {
					TileEditor.SetTerrain(x, y + 1, material);
				}
			}
		}
	}

	internal static bool HasUsableWallClimb(WorldPlan plan, MountainRangePlan mountain, out string reason)
	{
		MountainWallClimb? planned = BuildInteriorLayout(plan, mountain).WallClimb;
		if (planned is not MountainWallClimb climb) {
			reason = string.Empty;
			return true;
		}
		Rectangle area = WallClimbArea(climb);
		int wallBackedAir = 0;
		int staggeredLedgeRows = 0;
		for (int y = area.Top + 4; y < area.Bottom - 4; y++) {
			int solidCells = 0;
			for (int x = area.Left + 3; x < area.Right - 3; x++) {
				Tile tile = Main.tile[x, y];
				wallBackedAir += !TileEditor.IsSolid(x, y) && tile.WallType != WallID.None ? 1 : 0;
				solidCells += TileEditor.IsSolid(x, y) ? 1 : 0;
			}
			if (solidCells is >= 6 && solidCells <= 24) {
				staggeredLedgeRows++;
			}
		}
		int minimumAir = area.Width * area.Height / 3;
		if (wallBackedAir < minimumAir || staggeredLedgeRows < 3) {
			reason = $"{wallBackedAir} wall-backed air cells and {staggeredLedgeRows} staggered ledge rows";
			return false;
		}
		reason = string.Empty;
		return true;
	}

	private static Rectangle WallClimbArea(MountainWallClimb climb) => new(
		climb.CenterX - climb.HalfWidth - 3,
		climb.TopY - 3,
		climb.HalfWidth * 2 + 7,
		climb.BottomY - climb.TopY + 7);

	private static void CarveEllipse(
		WorldPlan plan,
		MountainRangePlan mountain,
		int centerX,
		int centerY,
		int horizontalRadius,
		int verticalRadius,
		int salt,
		bool protectSensitiveTiles,
		bool allowSurfaceOpening)
	{
		for (int offsetX = -horizontalRadius; offsetX <= horizontalRadius; offsetX++) {
			for (int offsetY = -verticalRadius; offsetY <= verticalRadius; offsetY++) {
				double normalized =
					(double)(offsetX * offsetX) / Math.Max(1, horizontalRadius * horizontalRadius)
					+ (double)(offsetY * offsetY) / Math.Max(1, verticalRadius * verticalRadius);
				double edgeJitter = (HashNoise(centerX + offsetX * 3, centerY + offsetY * 5 + salt) % 1000 / 1000d - 0.5d) * 0.22d;
				if (normalized > 1d + edgeJitter) {
					continue;
				}

				int x = centerX + offsetX;
				int y = centerY + offsetY;
				if ((!allowSurfaceOpening && y < plan.SurfaceAt(x) + 4) || !CanMutate(x, y, protectSensitiveTiles)) {
					continue;
				}
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, MountainWallAt(plan, mountain, x, y));
			}
		}
	}

	private static ushort MountainWallAt(
		WorldPlan plan,
		MountainRangePlan mountain,
		int x,
		int y,
		GenerationManifest? manifest = null)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		int materialSampleX = Math.Clamp(
			x + (int)Math.Round((ValueNoise(x, y, mountain.FeatureSeed ^ 0x4249_4F4D, 67) - 0.5d) * 64d),
			region.Left + 4,
			region.Right - 4);
		int sampleY = Math.Clamp(
			Math.Max((int)Main.worldSurface + 70, plan.SurfaceAt(materialSampleX) + 150),
			4,
			Main.maxTilesY - 5);
		ushort material = FindMountainSupportMaterial(materialSampleX, sampleY);
		BiomeKind biome = BiomeClassifier.ClassifySupport(material, materialSampleX, sampleY);
		ushort accent = biome switch {
			BiomeKind.Snow => WallID.SnowWallUnsafe,
			BiomeKind.Desert => WallID.Sandstone,
			BiomeKind.Jungle => WallID.JungleUnsafe,
			BiomeKind.Evil => WorldGen.crimson ? WallID.CrimstoneUnsafe : WallID.EbonstoneUnsafe,
			_ => WallID.Stone
		};
		int warpedX = x + (int)Math.Round((ValueNoise(x, y, mountain.FeatureSeed ^ 0x5741_5058, 57) - 0.5d) * 30d);
		int warpedY = y + (int)Math.Round((ValueNoise(x, y, mountain.FeatureSeed ^ 0x5741_5059, 53) - 0.5d) * 24d);
		double broad = ValueNoise(warpedX, warpedY, mountain.FeatureSeed ^ 0x5741_4C4C, 37);
		double detail = ValueNoise(x, y, mountain.FeatureSeed ^ 0x5041_5443, 17);
		double edgeGrain = ValueNoise(x, y, mountain.FeatureSeed ^ 0x4544_4745, 7);
		double depthBias = Math.Clamp((y - plan.SurfaceAt(x) - 18) / 130d, 0d, 1d) * 0.12d;
		// Keep every term two-dimensional. Per-row or per-column offsets create
		// long axis-aligned wall boundaries even when the underlying field is smooth.
		double score = broad * 0.52d + detail * 0.25d + edgeGrain * 0.23d + depthBias;
		double accentField = ValueNoise(x, y, mountain.FeatureSeed ^ 0x4143_4345, 11);
		if (accent != WallID.Stone && accentField > 0.86d && score is > 0.47d and < 0.63d) {
			return accent;
		}
		return score > 0.54d ? WallID.DirtUnsafe : WallID.Stone;
	}

	private static ushort FindMountainSupportMaterial(int x, int startY)
	{
		for (int offset = 0; offset <= 64; offset++) {
			int y = Math.Min(Main.maxTilesY - 5, startY + offset);
			Tile tile = Main.tile[x, y];
			if (tile.HasUnactuatedTile && !Main.tileFrameImportant[tile.TileType]
				&& tile.TileType is not (TileID.Cloud or TileID.RainCloud or TileID.SnowCloud or TileID.Sunplate)) {
				return tile.TileType;
			}
		}
		return TileID.Stone;
	}

	private static double ValueNoise(int x, int y, int seed, int cellSize)
	{
		int cellX = FloorDivide(x, cellSize);
		int cellY = FloorDivide(y, cellSize);
		double localX = (x - cellX * cellSize) / (double)cellSize;
		double localY = (y - cellY * cellSize) / (double)cellSize;
		double blendX = localX * localX * (3d - 2d * localX);
		double blendY = localY * localY * (3d - 2d * localY);
		double top = Lerp(UnitHash(cellX, cellY, seed), UnitHash(cellX + 1, cellY, seed), blendX);
		double bottom = Lerp(UnitHash(cellX, cellY + 1, seed), UnitHash(cellX + 1, cellY + 1, seed), blendX);
		return Lerp(top, bottom, blendY);
	}

	private static int FloorDivide(int value, int divisor) =>
		value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

	private static double UnitHash(int x, int y, int seed) =>
		HashNoise(x * 73_856_093 ^ y * 19_349_663, seed) / (double)int.MaxValue;

	private static double Lerp(double left, double right, double amount) => left + (right - left) * amount;

	private static bool IsNaturalMountainWall(ushort wall) =>
		wall is WallID.DirtUnsafe or WallID.Stone or WallID.SnowWallUnsafe or WallID.JungleUnsafe
			or WallID.Sandstone or WallID.EbonstoneUnsafe or WallID.CrimstoneUnsafe;

	private static void PlaceClimbAids(MountainInteriorLayout layout, UnifiedRandom random, GenerationManifest manifest)
	{
		foreach (MountainShaft shaft in layout.Shafts) {
			int topY = Math.Min(shaft.Top.Y, shaft.Bottom.Y);
			int bottomY = Math.Max(shaft.Top.Y, shaft.Bottom.Y);
			int x = (shaft.Top.X + shaft.Bottom.X) / 2;
			for (int y = topY + 2; y < bottomY - 1; y++) {
				if (IsDecorationExcluded(manifest, x, y)) {
					continue;
				}
				if (!Main.tile[x, y].HasTile) {
					TileEditor.SetTerrain(x, y, TileID.Rope);
				}
			}
			for (int y = topY + random.Next(9, 14); y < bottomY - 5; y += random.Next(10, 17)) {
				for (int offset = -shaft.HalfWidth; offset <= shaft.HalfWidth; offset++) {
					if (offset != 0 && !IsDecorationExcluded(manifest, x + offset, y)) {
						TileEditor.TryPlacePlatformForced(x + offset, y);
					}
				}
			}
		}
	}

	private static void PlaceInteriorObjects(
		WorldPlan plan,
		MountainRangePlan mountain,
		UnifiedRandom random,
		GenerationManifest manifest)
	{
		Rectangle area = MountainArea(plan, mountain);
		int potTarget = 12 + area.Width / 90 + random.Next(3, 9);
		int potsPlaced = 0;
		for (int attempt = 0; attempt < potTarget * 90 && potsPlaced < potTarget; attempt++) {
			int x = random.Next(area.Left + 12, area.Right - 13);
			int y = random.Next(Math.Max(area.Top + 10, plan.SurfaceAt(x) + 8), area.Bottom - 5);
			if (!CanPlacePot(x, y, manifest)) {
				continue;
			}
			int style = Math.Clamp((y - (int)Main.worldSurface) / 90 + random.Next(0, 2), 0, 11);
			if (WorldGen.PlacePot(x, y, TileID.Pots, style)) {
				potsPlaced++;
			}
		}

		int pileTarget = 18 + area.Width / 75 + random.Next(4, 11);
		for (int attempt = 0, placed = 0; attempt < pileTarget * 70 && placed < pileTarget; attempt++) {
			int x = random.Next(area.Left + 10, area.Right - 11);
			int y = random.Next(Math.Max(area.Top + 8, plan.SurfaceAt(x) + 7), area.Bottom - 4);
			if (IsDecorationExcluded(manifest, x, y) || !HasClearFloor(x, y)) {
				continue;
			}
			if (TileEditor.TryPlaceSmallPile(x, y, random.Next(0, 6), 0)) {
				placed++;
			}
		}

		int vineTarget = 35 + area.Width / 45 + random.Next(12, 29);
		for (int attempt = 0, vines = 0; attempt < vineTarget * 90 && vines < vineTarget; attempt++) {
			int x = random.Next(area.Left + 8, area.Right - 9);
			int y = random.Next(Math.Max(area.Top + 7, plan.SurfaceAt(x) + 6), area.Bottom - 16);
			if (IsDecorationExcluded(manifest, x, y) || !TileEditor.IsSolid(x, y - 1)
				|| Main.tile[x, y].HasTile || Main.tile[x, y].WallType == WallID.None) {
				continue;
			}

			ushort vineType = VineTypeAt(x, y - 1);
			int length = random.Next(3, 13);
			for (int offset = 0; offset < length; offset++) {
				int vineY = y + offset;
				if (IsDecorationExcluded(manifest, x, vineY) || Main.tile[x, vineY].HasTile || Main.tile[x, vineY].LiquidAmount > 0) {
					break;
				}
				TileEditor.SetTerrain(x, vineY, vineType);
				Main.tile[x, vineY].TileFrameX = 0;
				Main.tile[x, vineY].TileFrameY = 0;
				vines++;
			}
		}

		int torchTarget = 18 + area.Width / 120;
		for (int attempt = 0, torches = 0; attempt < torchTarget * 70 && torches < torchTarget; attempt++) {
			int x = random.Next(area.Left + 8, area.Right - 9);
			int y = random.Next(Math.Max(area.Top + 8, plan.SurfaceAt(x) + 8), area.Bottom - 6);
			if (!IsDecorationExcluded(manifest, x, y) && Main.tile[x, y].WallType != WallID.None
				&& TileEditor.TryPlaceTorch(x, y)) {
				torches++;
			}
		}
	}

	private static void PlaceFloatingInclusions(
		WorldPlan plan,
		MountainRangePlan mountain,
		MountainInteriorLayout layout,
		UnifiedRandom random,
		GenerationManifest manifest)
	{
		int target = Math.Clamp(layout.Chambers.Count / 3, 2, 4);
		int placed = 0;
		for (int attempt = 0; attempt < target * 18 && placed < target; attempt++) {
			MountainChamber chamber = layout.Chambers[random.Next(layout.Chambers.Count)];
			int radiusX = random.Next(7, Math.Min(16, Math.Max(8, chamber.RadiusX / 2)));
			int radiusY = random.Next(3, Math.Min(7, Math.Max(4, chamber.RadiusY / 3)));
			int centerX = chamber.Center.X + random.Next(-Math.Max(3, chamber.RadiusX / 2), Math.Max(4, chamber.RadiusX / 2 + 1));
			int verticalDirection = random.NextBool() ? -1 : 1;
			int centerY = chamber.Center.Y + verticalDirection * random.Next(
				Math.Max(6, chamber.RadiusY / 3),
				Math.Max(7, chamber.RadiusY * 2 / 3 + 1));
			Rectangle bounds = new(centerX - radiusX - 3, centerY - radiusY - 3, radiusX * 2 + 7, radiusY * 2 + 14);
			if (!WorldGen.InWorld(bounds.Left, bounds.Top, 10)
				|| !WorldGen.InWorld(bounds.Right - 1, bounds.Bottom - 1, 10)
				|| ContainsExcludedDecoration(manifest, bounds)
				|| layout.Routes.Any(route => RoutePassesNear(route, centerX, centerY, radiusY + 8))) {
				continue;
			}

			int authoredTiles = 0;
			for (int offsetX = -radiusX; offsetX <= radiusX; offsetX++) {
				double normalizedX = (double)(offsetX * offsetX) / Math.Max(1, radiusX * radiusX);
				int halfHeight = Math.Max(1, (int)Math.Round(radiusY * Math.Sqrt(Math.Max(0d, 1d - normalizedX))));
				halfHeight += HashNoise(centerX + offsetX, mountain.FeatureSeed ^ attempt) % 3 - 1;
				halfHeight = Math.Max(1, halfHeight);
				int topY = centerY - halfHeight;
				int bottomY = centerY + halfHeight;
				for (int y = topY; y <= bottomY; y++) {
					int x = centerX + offsetX;
					if (IsDecorationExcluded(manifest, x, y) || IsMineRailEnvelope(x, y)
						|| layout.Routes.Any(route => RoutePassesNear(route, x, y, 5))) {
						continue;
					}
					int depth = y - topY;
					TileEditor.SetTerrain(x, y, LandformGenerator.MountainTerrainAt(x, plan.SurfaceAt(x), depth));
					authoredTiles++;
				}

				if (Math.Abs(offsetX) >= radiusX - 1 && TileEditor.IsSolid(centerX + offsetX, topY)) {
					SlopeType slope = offsetX < 0 ? SlopeType.SlopeDownRight : SlopeType.SlopeDownLeft;
					Tile edge = Main.tile[centerX + offsetX, topY];
					edge.Slope = slope;
				}
				if ((offsetX + radiusX) % random.Next(3, 6) != 0) {
					continue;
				}
				int vineX = centerX + offsetX;
				int vineY = bottomY + 1;
				ushort vineType = VineTypeAt(vineX, bottomY);
				int vineLength = random.Next(4, 13);
				for (int length = 0; length < vineLength; length++, vineY++) {
					if (Main.tile[vineX, vineY].HasTile || Main.tile[vineX, vineY].LiquidAmount > 0
						|| IsDecorationExcluded(manifest, vineX, vineY) || IsMineRailEnvelope(vineX, vineY)) {
						break;
					}
					TileEditor.SetTerrain(vineX, vineY, vineType);
				}
			}
			if (authoredTiles >= 30) {
				placed++;
			}
		}
	}

	private static bool RoutePassesNear(IReadOnlyList<Point> route, int x, int y, int clearance)
	{
		double limit = clearance * clearance;
		for (int index = 1; index < route.Count; index++) {
			Point start = route[index - 1];
			Point end = route[index];
			double deltaX = end.X - start.X;
			double deltaY = end.Y - start.Y;
			double lengthSquared = deltaX * deltaX + deltaY * deltaY;
			double amount = lengthSquared <= 0d
				? 0d
				: Math.Clamp(((x - start.X) * deltaX + (y - start.Y) * deltaY) / lengthSquared, 0d, 1d);
			double nearestX = start.X + deltaX * amount;
			double nearestY = start.Y + deltaY * amount;
			double distanceX = x - nearestX;
			double distanceY = y - nearestY;
			if (distanceX * distanceX + distanceY * distanceY <= limit) {
				return true;
			}
		}
		return false;
	}

	private static bool ShouldLeaveWallVoid(WorldPlan plan, MountainRangePlan mountain, int x, int y)
	{
		if (y < plan.SurfaceAt(x) + 14) {
			return false;
		}
		double broad = ValueNoise(x, y, mountain.FeatureSeed ^ 0x564F_4944, 41);
		double edge = ValueNoise(x, y, mountain.FeatureSeed ^ 0x454D_5054, 13);
		return broad > 0.68d && edge > 0.43d;
	}

	private static void PlaceChamberVignettes(
		MountainInteriorLayout layout,
		UnifiedRandom random,
		GenerationManifest manifest)
	{
		int target = Math.Clamp(layout.Chambers.Count / 3, 2, 4);
		int placed = 0;
		for (int index = 0; index < layout.Chambers.Count && placed < target; index++) {
			MountainChamber chamber = layout.Chambers[(index * 3 + random.Next(layout.Chambers.Count)) % layout.Chambers.Count];
			int floorY = chamber.Center.Y + Math.Max(5, chamber.RadiusY - 5);
			int centerX = chamber.Center.X + random.Next(-Math.Max(1, chamber.RadiusX / 4), Math.Max(2, chamber.RadiusX / 4 + 1));
			Rectangle scene = new(centerX - 10, floorY - 6, 21, 11);
			if (!WorldGen.InWorld(scene.Left, scene.Top, 8) || !WorldGen.InWorld(scene.Right - 1, scene.Bottom - 1, 8)
				|| ContainsExcludedDecoration(manifest, scene)) {
				continue;
			}

			for (int x = centerX - 8; x <= centerX + 8; x++) {
				for (int y = floorY - 5; y < floorY; y++) {
					TileEditor.ClearTerrain(x, y);
				}
				TileEditor.SetTerrain(x, floorY, TileID.StoneSlab);
				if ((x - centerX) % 5 == 0) {
					for (int y = floorY + 1; y <= floorY + 3; y++) {
						TileEditor.SetTerrain(x, y, TileID.WoodenBeam);
					}
				}
			}
			TileEditor.SetSlopedTerrain(centerX - 8, floorY, TileID.StoneSlab, SlopeType.SlopeDownLeft);
			TileEditor.SetSlopedTerrain(centerX + 8, floorY, TileID.StoneSlab, SlopeType.SlopeDownRight);
			WorldGen.PlaceTile(centerX - 6, floorY - 1, TileID.WorkBenches, mute: true, forced: false, plr: -1, style: 0);
			WorldGen.PlaceTile(centerX, floorY - 1, TileID.Campfire, mute: true, forced: false, plr: -1, style: 0);
			WorldGen.PlaceTile(centerX + 6, floorY - 1, TileID.Benches, mute: true, forced: false, plr: -1, style: 0);
			TileEditor.TryPlaceSmallPile(centerX + 9, floorY - 1, (index + 2) % 6, 0);
			TileEditor.TryPlaceTorch(centerX - 9, floorY - 4);
			TileEditor.TryPlaceTorch(centerX + 9, floorY - 4);
			placed++;
		}
	}

	private static bool ContainsExcludedDecoration(GenerationManifest manifest, Rectangle area)
	{
		for (int x = area.Left; x < area.Right; x += 3) {
			for (int y = area.Top; y < area.Bottom; y += 3) {
				if (IsDecorationExcluded(manifest, x, y)) {
					return true;
				}
			}
		}
		return false;
	}

	private static void EnsureInteriorDecorationMinimums(
		WorldPlan plan,
		MountainRangePlan mountain,
		GenerationManifest manifest)
	{
		Rectangle area = MountainArea(plan, mountain);
		int vineTiles = CountVines(area);
		for (int x = area.Left + 10; x < area.Right - 10 && vineTiles < 28; x += 3) {
			for (int y = Math.Max(area.Top + 8, plan.SurfaceAt(x) + 7); y < area.Bottom - 12 && vineTiles < 28; y++) {
				if (IsDecorationExcluded(manifest, x, y) || !TileEditor.IsSolid(x, y - 1)
					|| Main.tile[x, y].HasTile || Main.tile[x, y].WallType == WallID.None) {
					continue;
				}
				ushort vineType = VineTypeAt(x, y - 1);
				for (int length = 0; length < 7 && vineTiles < 28; length++) {
					int vineY = y + length;
					if (IsDecorationExcluded(manifest, x, vineY) || Main.tile[x, vineY].HasTile
						|| Main.tile[x, vineY].LiquidAmount > 0) {
						break;
					}
					TileEditor.SetTerrain(x, vineY, vineType);
					vineTiles++;
				}
			}
		}

		int climbTiles = CountTiles(area, TileID.Platforms) + CountTiles(area, TileID.Rope);
		for (int x = area.Left + 14; x < area.Right - 14 && climbTiles < 28; x += 7) {
			int run = 0;
			for (int y = Math.Max(area.Top + 8, plan.SurfaceAt(x) + 8); y < area.Bottom - 5 && climbTiles < 28; y++) {
				if (IsDecorationExcluded(manifest, x, y) || Main.tile[x, y].HasTile || Main.tile[x, y].WallType == WallID.None) {
					run = 0;
					continue;
				}
				run++;
				if (run >= 4) {
					TileEditor.SetTerrain(x, y, TileID.Rope);
					climbTiles++;
				}
			}
		}
	}

	private static bool CanPlacePot(int x, int y, GenerationManifest manifest) =>
		!IsDecorationExcluded(manifest, x, y)
		&& !IsDecorationExcluded(manifest, x + 1, y)
		&& !Main.tile[x, y - 1].HasTile
		&& !Main.tile[x + 1, y - 1].HasTile
		&& !Main.tile[x, y].HasTile
		&& !Main.tile[x + 1, y].HasTile
		&& Main.tile[x, y].WallType != WallID.None
		&& Main.tile[x + 1, y].WallType != WallID.None
		&& TileEditor.IsSolid(x, y + 1)
		&& TileEditor.IsSolid(x + 1, y + 1);

	private static bool HasClearFloor(int x, int y) =>
		!Main.tile[x, y].HasTile && !Main.tile[x, y - 1].HasTile && TileEditor.IsSolid(x, y + 1);

	private static ushort VineTypeAt(int x, int supportY)
	{
		ushort support = Main.tile[x, supportY].TileType;
		if (support is TileID.JungleGrass or TileID.Mud or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass) {
			return TileID.JungleVines;
		}
		if (support is TileID.CorruptGrass or TileID.Ebonstone or TileID.CorruptHardenedSand or TileID.CorruptSandstone) {
			return TileID.CorruptVines;
		}
		if (support is TileID.CrimsonGrass or TileID.Crimstone or TileID.CrimsonHardenedSand or TileID.CrimsonSandstone) {
			return TileID.CrimsonVines;
		}
		if (support is TileID.MushroomGrass or TileID.MushroomBlock) {
			return TileID.MushroomVines;
		}
		if (support is TileID.Ash or TileID.AshGrass or TileID.AshWood) {
			return TileID.AshVines;
		}
		return support is TileID.Grass or TileID.Dirt ? TileID.Vines : TileID.VineRope;
	}

	private static bool IsDecorationExcluded(GenerationManifest manifest, int x, int y)
	{
		Point point = new(x, y);
		return manifest.Landmarks.Any(record => record.Area.Contains(point))
			|| manifest.Bridges.Any(record => record.Area.Contains(point))
			|| manifest.Valleys.Any(record => record.Area.Contains(point))
			|| manifest.SkyHighlands.Any(record => record.Area.Contains(point))
			|| manifest.MineSections.Any(record => record.Area.Contains(point));
	}

	private static Rectangle MountainArea(WorldPlan plan, MountainRangePlan mountain)
	{
		WorldRegion region = plan.Regions[mountain.RegionId];
		int top = Math.Max(45, Math.Min(mountain.LeftPeakY, mountain.RightPeakY) - 12);
		int bottom = Math.Min(Main.maxTilesY - 50, (int)Main.worldSurface + 70);
		return new Rectangle(region.Left, top, region.Width, bottom - top);
	}

	private static (int CaveAirTiles, int WideCavityColumns) MeasureCavities(Rectangle area)
	{
		int caveAirTiles = 0;
		int wideColumns = 0;
		for (int x = area.Left; x < area.Right; x++) {
			int longestRun = 0;
			int currentRun = 0;
			for (int y = area.Top; y < area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				if (!TileEditor.IsSolid(x, y) && tile.WallType != WallID.None) {
					caveAirTiles++;
					currentRun++;
					longestRun = Math.Max(longestRun, currentRun);
				}
				else {
					currentRun = 0;
				}
			}
			if (longestRun >= 12) {
				wideColumns++;
			}
		}
		return (caveAirTiles, wideColumns);
	}

	private static int CountTiles(Rectangle area, ushort type)
	{
		int count = 0;
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				count += Main.tile[x, y].HasTile && Main.tile[x, y].TileType == type ? 1 : 0;
			}
		}
		return count;
	}

	private static int CountVines(Rectangle area)
	{
		int count = 0;
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType is TileID.Vines or TileID.JungleVines
					or TileID.CrimsonVines or TileID.CorruptVines or TileID.MushroomVines or TileID.AshVines
					or TileID.VineRope) {
					count++;
				}
			}
		}
		return count;
	}

	private static bool CanMutate(int x, int y, bool protectSensitiveTiles)
	{
		if (!WorldGen.InWorld(x, y, 8)) {
			return false;
		}
		return !protectSensitiveTiles || !TileEditor.IsProtectedTile(Main.tile[x, y]);
	}

	private static void ProtectRoute(IReadOnlyList<Point> points)
	{
		for (int segment = 1; segment < points.Count; segment++) {
			Point start = points[segment - 1];
			Point end = points[segment];
			int steps = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
			for (int step = 0; step <= steps; step += 14) {
				double t = steps == 0 ? 0d : (double)step / steps;
				int x = (int)Math.Round(start.X + (end.X - start.X) * t);
				int y = (int)Math.Round(start.Y + (end.Y - start.Y) * t);
				GenVars.structures.AddProtectedStructure(new Rectangle(x - 9, y - 9, 19, 19), padding: 1);
			}
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
		ActuateBridgeTowerPassage(x, deckY, style);
	}

	private static void ActuateBridgeTowerPassage(int x, int deckY, BridgeStyle style)
	{
		ushort material = style == BridgeStyle.StoneArch ? TileID.GrayBrick : TileID.LivingWood;
		foreach (int columnX in new[] { x - 6, x - 5, x + 5, x + 6 }) {
			for (int y = deckY - 6; y < deckY; y++) {
				if (!IsMineRailEnvelope(columnX, y)) {
					TileEditor.SetActuatedTerrain(columnX, y, material);
				}
			}
		}
	}

	private static void RepairBridgeEndpointCorridor(int endpointX, int deckY, BridgeStyle style)
	{
		ushort material = style == BridgeStyle.StoneArch ? TileID.GrayBrick : TileID.LivingWood;
		for (int x = endpointX - 7; x <= endpointX + 7; x++) {
			for (int y = deckY - 6; y < deckY; y++) {
				if (!TileEditor.IsSolid(x, y) || TileEditor.IsProgressionTile(Main.tile[x, y])) {
					continue;
				}
				if (Math.Abs(x - endpointX) is 5 or 6) {
					TileEditor.SetActuatedTerrain(x, y, material);
				}
				else {
					TileEditor.ClearTerrain(x, y);
				}
			}
		}
	}

	private static void BuildBridgeApproach(int endpointX, int deckY, int direction, BridgeStyle style)
	{
		ushort material = style == BridgeStyle.StoneArch ? TileID.GrayBrick : TileID.LivingWood;
		ushort wall = style == BridgeStyle.StoneArch ? WallID.GrayBrick : WallID.Planked;
		for (int step = 0; step <= 11; step++) {
			int x = endpointX + direction * step;
			for (int y = deckY - 6; y < deckY; y++) {
				if (IsMineRailEnvelope(x, y)) {
					continue;
				}
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, wall);
			}
			for (int depth = 0; depth < 3; depth++) {
				if (!IsMineRailEnvelope(x, deckY + depth)) {
					TileEditor.SetTerrain(x, deckY + depth, depth == 1 ? TileID.StoneSlab : material);
				}
			}
		}

		int gateX = endpointX + direction * 8;
		for (int gateWidth = 0; gateWidth < 2; gateWidth++) {
			for (int y = deckY - 5; y < deckY; y++) {
				int gateColumn = gateX + direction * gateWidth;
				if (!IsMineRailEnvelope(gateColumn, y)) {
					TileEditor.SetActuatedTerrain(gateColumn, y, material);
				}
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
		int entrances = 0;
		MountainInteriorLayout layout = BuildInteriorLayout(plan, mountain);
		foreach (Point entrance in layout.Entrances) {
			int clearCells = 0;
			for (int x = entrance.X - 5; x <= entrance.X + 5; x++) {
				for (int y = entrance.Y - 4; y <= entrance.Y + 5; y++) {
					if (!TileEditor.IsSolid(x, y)) {
						clearCells++;
					}
				}
			}
			if (clearCells >= 70) {
				entrances++;
			}
		}
		return entrances;
	}

	private static int MixSeed(int seed, int salt)
	{
		unchecked {
			uint value = (uint)seed ^ (uint)salt;
			value ^= value >> 16;
			value *= 0x7FEB_352Du;
			value ^= value >> 15;
			return (int)value;
		}
	}

	private sealed record MountainInteriorLayout(
		IReadOnlyList<Point[]> Routes,
		IReadOnlyList<MountainChamber> Chambers,
		IReadOnlyList<MountainShaft> Shafts,
		IReadOnlyList<Point> Entrances,
		MountainWallClimb? WallClimb);

	private readonly record struct MountainChamber(Point Center, int RadiusX, int RadiusY, bool OpenToSurface);

	private readonly record struct MountainShaft(Point Top, Point Bottom, int HalfWidth);

	private readonly record struct MountainWallClimb(int CenterX, int TopY, int BottomY, int HalfWidth);
}
