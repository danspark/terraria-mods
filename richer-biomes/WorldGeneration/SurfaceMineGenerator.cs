using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace RicherBiomes.WorldGeneration;

internal static class SurfaceMineGenerator
{
	private const int MineSeedSalt = 0x4D49_4E45;
	private const int CorridorHeadroom = 6;

	public static SurfaceMinePlan PlanAndReserve(WorldPlan worldPlan, GenerationManifest manifest)
	{
		UnifiedRandom random = new(MixSeed(worldPlan.GenerationSeed, MineSeedSalt));
		int halfWidth = Main.maxTilesX switch {
			<= 4200 => 260,
			<= 6400 => 330,
			_ => 400
		};

		List<int> candidates = worldPlan.Mountains
			.Select(mountain => worldPlan.Regions[mountain.RegionId].CenterX)
			.Concat(worldPlan.Regions
				.Where(region => region.Landform is LandformKind.Plateau or LandformKind.RollingHills)
				.OrderByDescending(region => Math.Abs(region.CenterX - worldPlan.SpawnX))
				.Select(region => region.CenterX))
			.ToList();
		for (int attempt = 0; attempt < 90; attempt++) {
			candidates.Add(random.Next(worldPlan.CoastMargin + halfWidth + 40, Main.maxTilesX - worldPlan.CoastMargin - halfWidth - 40));
		}

		foreach (int rawCenterX in candidates) {
			int centerX = Math.Clamp(rawCenterX, worldPlan.CoastMargin + halfWidth + 30, Main.maxTilesX - worldPlan.CoastMargin - halfWidth - 31);
			if (Math.Abs(centerX - worldPlan.SpawnX) < halfWidth + 180 || Math.Abs(centerX - GenVars.dungeonX) < halfWidth + 140) {
				continue;
			}

			SurfaceMinePlan candidate = CreatePlan(worldPlan, centerX, halfWidth, random.Next());
			if (!CanPlace(candidate, manifest)) {
				continue;
			}

			Reserve(candidate);
			return candidate;
		}

		throw new InvalidOperationException("Richer Biomes could not reserve a progression-safe site for the guaranteed surface mine.");
	}

	public static void Excavate(SurfaceMinePlan plan, GenerationManifest manifest, GenerationProgress progress)
	{
		for (int index = 0; index < plan.Sections.Count; index++) {
			BuildSection(plan.Sections[index]);
			progress.Set((double)(index + 1) / (plan.Sections.Count + plan.Routes.Count));
		}

		for (int index = 0; index < plan.Routes.Count; index++) {
			CarveRoute(plan.Routes[index]);
			progress.Set((double)(plan.Sections.Count + index + 1) / (plan.Sections.Count + plan.Routes.Count));
		}
		BuildJunctionStations(plan);
		RefillFloodedSection(plan);

		BuildHeadframe(plan.Entrance);
		manifest.MineSections.Clear();
		manifest.MineSections.AddRange(plan.Sections);
		TileEditor.Frame(plan.Area, border: 3);
	}

	public static void FurnishAndLayTrack(SurfaceMinePlan plan, GenerationManifest manifest, GenerationProgress progress)
	{
		int furnitureCount = 0;
		for (int index = 0; index < plan.Sections.Count; index++) {
			furnitureCount += FurnishSection(plan.Sections[index]);
			progress.Set((double)(index + 1) / (plan.Routes.Count + plan.Sections.Count));
		}

		HashSet<Point> plannedTrack = [];
		for (int index = 0; index < plan.Routes.Count; index++) {
			MineRoute route = plan.Routes[index];
			if (!route.HasTrack) {
				continue;
			}

			foreach (Point point in Rasterize(route)) {
				plannedTrack.Add(point);
			}
			progress.Set((double)(plan.Sections.Count + index + 1) / (plan.Routes.Count + plan.Sections.Count));
		}

		OwnTrackGraph(plannedTrack);
		foreach (MineSection section in plan.Sections) {
			int displayFurniture = BuildLowerWorkDisplay(section, plannedTrack);
			if (displayFurniture == 0 && section.Kind is MineSectionKind.Working or MineSectionKind.MountainRail) {
				displayFurniture = BuildCompactWorkAlcove(section, plannedTrack);
			}
			furnitureCount += displayFurniture;
		}
		furnitureCount += BuildWorkyardLoft(plan.Entrance);

		RefillFloodedSection(plan);
		// Furnishings, quarantine gates, and display decks are authored after the
		// first rail pass. Give the connected graph final ownership of its exact
		// cells and clearance envelope so no district can sever a branch.
		OwnTrackGraph(plannedTrack);
		BuildQuarantineGates(plan);

		int trackTiles = plannedTrack.Count(point => HasTile(point.X, point.Y, TileID.MinecartTrack));
		int supportTiles = CountTiles(plan.Area, TileID.WoodenBeam);
		int connectedRoutes = plan.Routes.Count(route => route.Required && RouteSurvived(route));
		manifest.SurfaceMine = new SurfaceMineRecord(
			plan.Area,
			plan.Entrance,
			trackTiles,
			supportTiles,
			furnitureCount,
			plan.Routes.Count(route => route.Required),
			connectedRoutes);
	}

	public static void RepairTrackGraph(SurfaceMinePlan plan, GenerationManifest manifest)
	{
		HashSet<Point> plannedTrack = plan.Routes
			.Where(route => route.HasTrack)
			.SelectMany(Rasterize)
			.ToHashSet();
		OwnTrackGraph(plannedTrack);
		BuildQuarantineGates(plan);

		SurfaceMineRecord existing = manifest.SurfaceMine
			?? throw new InvalidOperationException("The surface mine cannot be repaired before it is furnished.");
		manifest.SurfaceMine = existing with {
			TrackTiles = plannedTrack.Count(point => HasTile(point.X, point.Y, TileID.MinecartTrack)),
			SupportTiles = CountTiles(plan.Area, TileID.WoodenBeam),
			ConnectedRouteCount = plan.Routes.Count(route => route.Required && RouteSurvived(route))
		};
	}

	private static void OwnTrackGraph(HashSet<Point> plannedTrack)
	{
		foreach (Point point in plannedTrack) {
			PrepareTrackCell(point, plannedTrack);
		}
		foreach (Point point in plannedTrack) {
			TileEditor.TryPlaceMinecartTrack(point.X, point.Y);
		}
		foreach (Point point in plannedTrack) {
			if (HasTile(point.X, point.Y, TileID.MinecartTrack)) {
				Minecart.FrameTrack(point.X, point.Y, pound: false, mute: true);
			}
		}
	}

	private static SurfaceMinePlan CreatePlan(WorldPlan worldPlan, int centerX, int halfWidth, int featureSeed)
	{
		UnifiedRandom random = new(featureSeed);
		int surfaceY = worldPlan.SurfaceAt(centerX - halfWidth + 35) - 1;
		int wing = halfWidth * random.Next(66, 79) / 100;
		int levelDrop = Math.Clamp(random.Next(104, 136), 96, wing - 32);
		int upperY = (int)Main.worldSurface + random.Next(68, 108);
		int middleY = Math.Min((int)Main.rockLayer + 44, upperY + levelDrop);
		int deepY = Math.Min(
			Math.Min(Main.UnderworldLayer - 170, (int)Main.rockLayer + 275),
			middleY + levelDrop);
		Point p0 = new(centerX - halfWidth + 35, surfaceY);
		Point upperWest = new(centerX - wing, upperY + random.Next(-4, 5));
		Point upperJunction = new(centerX + random.Next(-12, 13), upperY + random.Next(-3, 6));
		Point upperEast = new(centerX + wing, upperY + random.Next(-2, 7));
		Point middleWest = new(centerX - wing + random.Next(-8, 9), middleY + random.Next(-4, 7));
		Point middleJunction = new(centerX + random.Next(-12, 13), middleY + random.Next(-3, 6));
		Point middleEast = new(centerX + wing + random.Next(-8, 9), middleY + random.Next(-4, 7));
		Point deepWest = new(centerX - wing + random.Next(-8, 9), deepY + random.Next(-4, 7));
		Point deepJunction = new(centerX + random.Next(-12, 13), deepY + random.Next(-3, 6));
		Point deepEast = new(centerX + wing + random.Next(-8, 9), deepY + random.Next(-4, 7));

		List<MineRoute> routes = [
			new(p0, upperEast, HasTrack: true, Required: true),
			new(upperEast, upperJunction, HasTrack: true, Required: true),
			new(upperJunction, upperWest, HasTrack: true, Required: true),
			new(upperWest, middleJunction, HasTrack: true, Required: true),
			new(upperJunction, middleEast, HasTrack: true, Required: true),
			new(middleEast, middleJunction, HasTrack: true, Required: true),
			new(middleJunction, middleWest, HasTrack: true, Required: true),
			new(middleWest, deepJunction, HasTrack: true, Required: true),
			new(middleJunction, deepEast, HasTrack: true, Required: true),
			new(deepEast, deepJunction, HasTrack: true, Required: true),
			new(deepJunction, deepWest, HasTrack: true, Required: true)
		];

		List<MineSection> sections = [
			new(0, MineSectionKind.Workyard, Centered(p0.X + 20, p0.Y - 8, random.Next(68, 83), random.Next(34, 42)), p0),
			new(1, MineSectionKind.Working, Centered(upperEast.X, upperEast.Y, random.Next(74, 91), random.Next(38, 47)), upperEast),
			new(2, MineSectionKind.MountainRail, Centered(upperWest.X, upperWest.Y, random.Next(82, 101), random.Next(40, 51)), upperWest),
			new(3, MineSectionKind.Working, Centered(middleJunction.X, middleJunction.Y, random.Next(88, 107), random.Next(42, 53)), middleJunction),
			new(4, MineSectionKind.Working, Centered(deepJunction.X, deepJunction.Y, random.Next(96, 117), random.Next(44, 57)), deepJunction)
		];

		bool floodedOnEast = random.NextBool();
		Point floodedOrigin = floodedOnEast ? middleEast : middleWest;
		Point evilOrigin = floodedOnEast ? middleWest : middleEast;
		Point collapsedOrigin = random.NextBool() ? deepWest : deepEast;
		int floodedDirection = Math.Sign(floodedOrigin.X - centerX);
		int evilDirection = Math.Sign(evilOrigin.X - centerX);
		int collapsedDirection = Math.Sign(collapsedOrigin.X - centerX);
		Point floodedCenter = new(floodedOrigin.X + floodedDirection * random.Next(70, 101), floodedOrigin.Y + random.Next(27, 44));
		Point collapsedCenter = new(collapsedOrigin.X + collapsedDirection * random.Next(72, 106), collapsedOrigin.Y + random.Next(31, 49));
		Point evilCenter = new(evilOrigin.X + evilDirection * random.Next(82, 118), evilOrigin.Y + random.Next(30, 49));
		sections.Add(new MineSection(5, MineSectionKind.Flooded, Centered(floodedCenter.X, floodedCenter.Y, random.Next(78, 95), random.Next(40, 51)), floodedCenter));
		sections.Add(new MineSection(6, MineSectionKind.Collapsed, Centered(collapsedCenter.X, collapsedCenter.Y, random.Next(82, 101), random.Next(39, 50)), collapsedCenter));
		sections.Add(new MineSection(7, MineSectionKind.SealedEvil, Centered(evilCenter.X, evilCenter.Y, random.Next(90, 109), random.Next(47, 59)), evilCenter));
		routes.Add(new MineRoute(floodedOrigin, floodedCenter, HasTrack: true, Required: false));
		routes.Add(new MineRoute(collapsedOrigin, collapsedCenter, HasTrack: true, Required: false));
		routes.Add(new MineRoute(evilOrigin, evilCenter, HasTrack: true, Required: false));

		int left = Math.Max(30, Math.Min(sections.Min(section => section.Area.Left), routes.Min(route => Math.Min(route.Start.X, route.End.X))) - 20);
		int right = Math.Min(Main.maxTilesX - 31, Math.Max(sections.Max(section => section.Area.Right), routes.Max(route => Math.Max(route.Start.X, route.End.X) + 1)) + 20);
		int top = Math.Max(40, Math.Min(sections.Min(section => section.Area.Top), routes.Min(route => Math.Min(route.Start.Y, route.End.Y))) - 20);
		int bottom = Math.Min(Main.UnderworldLayer - 80, Math.Max(sections.Max(section => section.Area.Bottom), routes.Max(route => Math.Max(route.Start.Y, route.End.Y) + 1)) + 24);
		Rectangle area = new(left, top, right - left, bottom - top);
		return new SurfaceMinePlan(featureSeed, area, p0, sections, routes);
	}

	private static Rectangle Centered(int x, int y, int width, int height) =>
		new(x - width / 2, y - height / 2, width, height);

	private static bool CanPlace(SurfaceMinePlan plan, GenerationManifest manifest)
	{
		if (!WorldGen.InWorld(plan.Area.Left, plan.Area.Top, 30)
			|| !WorldGen.InWorld(plan.Area.Right - 1, plan.Area.Bottom - 1, 30)) {
			return false;
		}
		MineSection workyard = plan.Sections.First(section => section.Kind == MineSectionKind.Workyard);
		Rectangle workyardBuffer = new(
			workyard.Area.X - 24,
			workyard.Area.Y - 12,
			workyard.Area.Width + 48,
			workyard.Area.Height + 24);
		if (manifest.BiomeTransitions.Any(transition => transition.Area.Intersects(workyardBuffer))) {
			return false;
		}
		MineRoute surfaceDescent = plan.Routes[0];
		foreach (BuildTerrace terrace in manifest.Terraces) {
			Rectangle terraceBuffer = new(
				terrace.Area.X - 12,
				terrace.Area.Y - 10,
				terrace.Area.Width + 24,
				terrace.Area.Height + 20);
			if (terraceBuffer.Intersects(workyardBuffer)
				|| Rasterize(surfaceDescent).Any(terraceBuffer.Contains)) {
				return false;
			}
		}

		foreach (MineSection section in plan.Sections) {
			// The mine is itself a major world structure and may replace ordinary
			// terrain reservations. Tile-level checks still reject chests, altars,
			// dungeon/temple blocks, Shimmer, wiring, and other progression state.
			if (!TileEditor.IsSafeForTerrainFeature(section.Area)) {
				return false;
			}
		}

		foreach (MineRoute route in plan.Routes) {
			foreach (Point point in Rasterize(route)) {
				for (int offsetY = -CorridorHeadroom; offsetY <= 2; offsetY++) {
					if (!WorldGen.InWorld(point.X, point.Y + offsetY, 18)
						|| TileEditor.IsProgressionTile(Main.tile[point.X, point.Y + offsetY])) {
						return false;
					}
				}
			}
		}

		return true;
	}

	private static void Reserve(SurfaceMinePlan plan)
	{
		foreach (MineSection section in plan.Sections) {
			GenVars.structures.AddProtectedStructure(section.Area, padding: 4);
		}
		foreach (MineRoute route in plan.Routes) {
			int index = 0;
			foreach (Point point in Rasterize(route)) {
				if (index++ % 20 == 0) {
					GenVars.structures.AddProtectedStructure(new Rectangle(point.X - 7, point.Y - 8, 15, 13), padding: 1);
				}
			}
		}
	}

	private static void BuildSection(MineSection section)
	{
		if (section.Kind == MineSectionKind.Workyard) {
			BuildOpenWorkyard(section);
			return;
		}

		Rectangle area = section.Area;
		int shell = section.Kind == MineSectionKind.SealedEvil ? 5 : 2;
		ushort wall = section.Kind == MineSectionKind.SealedEvil
			? (WorldGen.crimson ? WallID.CrimstoneUnsafe : WallID.EbonstoneUnsafe)
			: WallID.Planked;
		int radiusX = area.Width / 2 - 2;
		int radiusY = area.Height / 2 - 2;
		for (int x = area.Left + 1; x < area.Right - 1; x++) {
			int offsetX = x - area.Center.X;
			double normalizedX = (double)offsetX / Math.Max(1, radiusX);
			int halfHeight = Math.Max(3, (int)Math.Round(radiusY * Math.Sqrt(Math.Max(0d, 1d - normalizedX * normalizedX))));
			int topJitter = Noise(section.Id, x, 17) % 5 - 2;
			int bottomJitter = Noise(section.Id, x, 43) % 5 - 2;
			int outerTop = area.Center.Y - halfHeight + topJitter;
			int outerBottom = area.Center.Y + halfHeight + bottomJitter;
			for (int y = outerTop; y <= outerBottom; y++) {
				TileEditor.SetTerrain(x, y, section.Kind == MineSectionKind.SealedEvil ? TileID.GrayBrick : TileID.LivingWood);
			}

			int localShell = shell + Noise(section.Id, x, 71) % 2;
			int innerTop = outerTop + localShell;
			int innerBottom = outerBottom - localShell;
			for (int y = innerTop; y <= innerBottom; y++) {
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, Noise(section.Id, x / 7 + y / 9, 137) % 100 < 58 ? wall : WallID.Planked);
			}
			for (int floorDepth = 0; floorDepth < 3 && innerBottom - floorDepth >= innerTop; floorDepth++) {
				TileEditor.SetTerrain(x, innerBottom - floorDepth, section.Kind == MineSectionKind.SealedEvil
					? (WorldGen.crimson ? TileID.Crimstone : TileID.Ebonstone)
					: floorDepth == 0 ? TileID.WoodenBeam : TileID.LivingWood);
			}
		}

		if (section.Kind == MineSectionKind.Flooded) {
			int waterline = area.Center.Y + 4;
			for (int x = area.Left + shell + 2; x < area.Right - shell - 2; x++) {
				for (int y = waterline; y < area.Bottom - shell - 3; y++) {
					TileEditor.SetLiquid(x, y, (byte)LiquidID.Water, byte.MaxValue);
				}
			}
		}
		else if (section.Kind == MineSectionKind.Collapsed) {
			for (int x = area.Center.X; x < area.Right - shell; x += 3) {
				int height = 2 + Noise(section.Id, x, 97) % 6;
				int floorY = section.Center.Y + 3 + Math.Abs(x - area.Center.X) / 5;
				for (int y = floorY - height; y < floorY; y++) {
					TileEditor.SetTerrain(x, y, y % 2 == 0 ? TileID.Stone : TileID.Dirt);
				}
			}
		}

		BuildDistrictPlatforms(section);
	}

	private static void BuildDistrictPlatforms(MineSection section)
	{
		int deckY = section.Center.Y + Math.Clamp(section.Area.Height / 4, 8, 13);
		int centerGap = section.Kind == MineSectionKind.Flooded ? 11 : 7;
		for (int x = section.Area.Left + 8; x < section.Area.Right - 8; x++) {
			bool gap = Math.Abs(x - section.Center.X) <= centerGap;
			for (int y = deckY - 7; y < deckY; y++) {
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, Noise(section.Id, x / 5 + y / 7, 241) % 100 < 62 ? WallID.Planked : WallID.Stone);
			}
			if (gap) {
				TileEditor.TryPlacePlatformForced(x, deckY);
			}
			else {
				TileEditor.SetTerrain(x, deckY, TileID.WoodenBeam);
				TileEditor.SetTerrain(x, deckY + 1, TileID.LivingWood);
			}
		}

		int supportGap = 13 + Noise(section.Id, section.Center.X, 307) % 8;
		for (int x = section.Area.Left + 10; x < section.Area.Right - 9; x += supportGap) {
			for (int y = deckY + 2; y <= Math.Min(section.Area.Bottom - 4, deckY + 10); y++) {
				TileEditor.SetTerrain(x, y, TileID.WoodenBeam);
			}
		}
	}

	private static void BuildOpenWorkyard(MineSection section)
	{
		Rectangle area = section.Area;
		int floorY = section.Center.Y + 1;
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < floorY; y++) {
				TileEditor.ClearTerrain(x, y);
				if (x > area.Center.X + 5 && x < area.Right - 3 && y > floorY - 10) {
					TileEditor.SetWall(x, y, (x - area.Left) % 10 < 5 ? WallID.Planked : WallID.LivingWoodUnsafe);
				}
			}
			for (int depth = 0; depth < 3; depth++) {
				TileEditor.SetTerrain(x, floorY + depth, depth == 0 ? TileID.WoodenBeam : TileID.LivingWood);
			}
		}
		for (int x = area.Center.X + 5; x < area.Right - 2; x++) {
			int canopyY = floorY - 11 + Math.Abs(x - (area.Center.X + 14)) / 8;
			for (int depth = 0; depth < 2; depth++) {
				TileEditor.SetTerrain(x, canopyY + depth, TileID.LivingWood);
			}
		}
	}

	private static void RefillFloodedSection(SurfaceMinePlan plan)
	{
		foreach (MineSection section in plan.Sections.Where(section => section.Kind == MineSectionKind.Flooded)) {
			int waterline = section.Area.Center.Y + 4;
			for (int x = section.Area.Left + 2; x < section.Area.Right - 2; x++) {
				for (int y = waterline; y < section.Area.Bottom - 1; y++) {
					if (!Main.tile[x, y].HasTile) {
						TileEditor.SetLiquid(x, y, (byte)LiquidID.Water, byte.MaxValue);
					}
				}
			}
		}
	}

	private static void CarveRoute(MineRoute route)
	{
		int index = 0;
		foreach (Point point in Rasterize(route)) {
			for (int offsetX = -2; offsetX <= 2; offsetX++) {
				for (int offsetY = -CorridorHeadroom - 1; offsetY <= 0; offsetY++) {
					TileEditor.ClearTerrain(point.X + offsetX, point.Y + offsetY);
					ushort wall = Noise(route.Start.X + route.End.X, (point.X + offsetX) / 9 + (point.Y + offsetY) / 7, 401) % 100 < 57
						? WallID.Planked
						: WallID.Stone;
					TileEditor.SetWall(point.X + offsetX, point.Y + offsetY, wall);
				}
				for (int depth = 1; depth <= 3; depth++) {
					TileEditor.SetTerrain(point.X + offsetX, point.Y + depth, depth == 1 ? TileID.WoodenBeam : TileID.LivingWood);
				}
			}

			if (index % (11 + Noise(route.Start.X, route.End.X, 457) % 8) == 0) {
				TileEditor.SetTerrain(point.X, point.Y - CorridorHeadroom - 1, TileID.WoodenBeam);
				TileEditor.TryPlaceTorch(point.X + 1, point.Y - 3);
			}
			index++;
		}
	}

	private static void BuildJunctionStations(SurfaceMinePlan plan)
	{
		Dictionary<Point, int> degree = [];
		foreach (MineRoute route in plan.Routes.Where(route => route.HasTrack)) {
			degree[route.Start] = degree.GetValueOrDefault(route.Start) + 1;
			degree[route.End] = degree.GetValueOrDefault(route.End) + 1;
		}
		foreach ((Point center, int routeDegree) in degree.Where(pair => pair.Value >= 3)) {
			for (int offsetX = -12; offsetX <= 12; offsetX++) {
				for (int offsetY = -8; offsetY <= 3; offsetY++) {
					double normalized = (double)(offsetX * offsetX) / 144d + (double)(offsetY * offsetY) / 64d;
					if (normalized > 1d) {
						continue;
					}
					int x = center.X + offsetX;
					int y = center.Y + offsetY;
					if (offsetY >= 1) {
						TileEditor.SetTerrain(x, y, offsetY == 1 ? TileID.WoodenBeam : TileID.LivingWood);
					}
					else {
						TileEditor.ClearTerrain(x, y);
						TileEditor.SetWall(x, y, offsetX % 8 < 4 ? WallID.Planked : WallID.Stone);
					}
				}
			}
			for (int x = center.X - 10; x <= center.X + 10; x += 10) {
				for (int y = center.Y - 6; y <= center.Y + 2; y++) {
					if (y != center.Y) {
						TileEditor.SetTerrain(x, y, TileID.WoodenBeam);
					}
				}
			}
			TileEditor.TryPlaceTorch(center.X + 2, center.Y - 4);
		}
	}

	private static void PrepareTrackCell(Point point, HashSet<Point> plannedTrack)
	{
		for (int offsetX = -2; offsetX <= 2; offsetX++) {
			for (int y = point.Y - CorridorHeadroom - 1; y < point.Y; y++) {
				if (!plannedTrack.Contains(new Point(point.X + offsetX, y))) {
					TileEditor.ClearTerrain(point.X + offsetX, y);
				}
			}
		}
		// A support from a crossing branch may occupy the rail cell. Rails own this
		// exact coordinate; beams remain in the headroom and below the track.
		TileEditor.ClearTerrain(point.X, point.Y);
		Point support = new(point.X, point.Y + 1);
		if (!plannedTrack.Contains(support)
			&& !IsReservedTrackHeadroom(support, plannedTrack)
			&& !TileEditor.IsSolid(point.X, point.Y + 1)) {
			TileEditor.SetTerrain(point.X, point.Y + 1, TileID.LivingWood);
		}
	}

	private static bool IsReservedTrackHeadroom(Point cell, HashSet<Point> plannedTrack)
	{
		for (int offsetX = -2; offsetX <= 2; offsetX++) {
			for (int depth = 1; depth <= CorridorHeadroom + 1; depth++) {
				if (plannedTrack.Contains(new Point(cell.X + offsetX, cell.Y + depth))) {
					return true;
				}
			}
		}
		return false;
	}

	private static void BuildHeadframe(Point entrance)
	{
		int groundY = entrance.Y + 1;
		for (int x = entrance.X - 10; x <= entrance.X + 12; x++) {
			for (int y = groundY - 9; y < groundY; y++) {
				TileEditor.ClearTerrain(x, y);
			}
			for (int depth = 0; depth < 3; depth++) {
				TileEditor.SetTerrain(x, groundY + depth, depth == 0 ? TileID.WoodenBeam : TileID.LivingWood);
			}
		}
		for (int offsetX = -7; offsetX <= 7; offsetX += 14) {
			for (int y = groundY - 16; y <= groundY; y++) {
				TileEditor.SetTerrain(entrance.X + offsetX, y, TileID.WoodenBeam);
			}
		}
		for (int x = entrance.X - 10; x <= entrance.X + 10; x++) {
			int roofY = groundY - 18 + Math.Abs(x - entrance.X) / 4;
			TileEditor.SetTerrain(x, roofY, TileID.LivingWood);
			TileEditor.SetTerrain(x, roofY + 1, TileID.LivingWood);
		}
		for (int y = groundY - 15; y < groundY - 2; y++) {
			TileEditor.SetTerrain(entrance.X - 2, y, TileID.Rope);
		}
		TileEditor.TryPlaceTorch(entrance.X + 3, groundY - 5);
	}

	private static int FurnishSection(MineSection section)
	{
		if (section.Kind is MineSectionKind.Flooded or MineSectionKind.Collapsed or MineSectionKind.SealedEvil) {
			return 0;
		}

		int floorY = section.Center.Y + Math.Clamp(section.Area.Height / 4, 8, 13) - 1;
		int count = 0;
		count += TryPlaceFurniture(section.Area.Left + 10, floorY, TileID.WorkBenches) ? 1 : 0;
		count += TryPlaceFurniture(section.Area.Left + 18, floorY, TileID.Tables) ? 1 : 0;
		count += TryPlaceFurniture(section.Area.Left + 23, floorY, TileID.Chairs) ? 1 : 0;
		count += TryPlaceFurniture(section.Area.Right - 12, floorY, TileID.Anvils) ? 1 : 0;
		count += TryPlaceFurniture(section.Area.Right - 20, floorY, TileID.Bookcases) ? 1 : 0;
		count += TileEditor.TryPlaceSmallPile(section.Area.Right - 7, floorY, section.Id % 6, 0) ? 1 : 0;
		count += TileEditor.TryPlaceSmallPile(section.Area.Center.X + 13, floorY, (section.Id + 3) % 6, 0) ? 1 : 0;
		TileEditor.TryPlaceTorch(section.Area.Center.X, section.Area.Top + 5);
		return count;
	}

	private static bool TryPlaceFurniture(int x, int y, ushort tileType)
	{
		for (int clearX = x - 2; clearX <= x + 2; clearX++) {
			for (int clearY = y - 4; clearY <= y; clearY++) {
				TileEditor.ClearTerrain(clearX, clearY);
			}
		}
		WorldGen.PlaceTile(x, y, tileType, mute: true, forced: true);
		for (int offsetX = -2; offsetX <= 2; offsetX++) {
			for (int offsetY = -3; offsetY <= 1; offsetY++) {
				if (HasTile(x + offsetX, y + offsetY, tileType)) {
					return true;
				}
			}
		}
		return false;
	}

	private static int BuildLowerWorkDisplay(MineSection section, HashSet<Point> plannedTrack)
	{
		if (section.Kind is MineSectionKind.Workyard or MineSectionKind.Flooded
			or MineSectionKind.Collapsed or MineSectionKind.SealedEvil) {
			return 0;
		}

		Point? displayCenter = null;
		Point[] candidates = [
			new(section.Center.X + 10, section.Center.Y + 8),
			new(section.Center.X - 10, section.Center.Y + 8),
			new(section.Center.X + 16, section.Center.Y + 7),
			new(section.Center.X - 16, section.Center.Y + 7)
		];
		foreach (Point candidate in candidates) {
			Rectangle displayArea = new(candidate.X - 10, section.Center.Y + 2, 21, candidate.Y - section.Center.Y + 1);
			if (!plannedTrack.Any(displayArea.Contains)) {
				displayCenter = candidate;
				break;
			}
		}
		if (displayCenter is not Point display) {
			return 0;
		}

		int displayCenterX = display.X;
		int displayFloorY = display.Y;
		for (int x = displayCenterX - 10; x <= displayCenterX + 10; x++) {
			for (int y = section.Center.Y + 2; y <= displayFloorY; y++) {
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, (x - displayCenterX) % 8 < 4 ? WallID.Planked : WallID.LivingWoodUnsafe);
			}
			TileEditor.SetTerrain(x, displayFloorY + 1, TileID.LivingWood);
			TileEditor.SetTerrain(x, displayFloorY + 2, TileID.LivingWood);
		}

		int portalX = displayCenterX - 8;
		for (int x = portalX - 2; x <= portalX + 2; x++) {
			TileEditor.TryPlacePlatformForced(x, section.Center.Y + 1);
		}
		for (int y = section.Center.Y + 2; y <= displayFloorY; y++) {
			TileEditor.SetTerrain(portalX, y, TileID.Rope);
		}

		PlaceWorkbenchFootprint(displayCenterX - 7, displayFloorY);
		PlaceTableFootprint(displayCenterX, displayFloorY);
		PlaceChairFootprint(displayCenterX + 7, displayFloorY);
		TileEditor.TryPlaceTorch(displayCenterX + 9, displayFloorY - 4);
		return 3;
	}

	private static int BuildCompactWorkAlcove(MineSection section, HashSet<Point> plannedTrack)
	{
		for (int floorY = section.Area.Top + 12; floorY < section.Area.Bottom - 5; floorY += 7) {
			for (int centerX = section.Area.Left + 12; centerX < section.Area.Right - 12; centerX += 10) {
				Rectangle alcove = new(centerX - 10, floorY - 7, 21, 10);
				if (plannedTrack.Any(alcove.Contains)) {
					continue;
				}

				for (int x = alcove.Left; x < alcove.Right; x++) {
					for (int y = alcove.Top; y < floorY; y++) {
						TileEditor.ClearTerrain(x, y);
						TileEditor.SetWall(x, y, (x - alcove.Left) % 8 < 4 ? WallID.Planked : WallID.LivingWoodUnsafe);
					}
					TileEditor.SetTerrain(x, floorY, TileID.LivingWood);
					TileEditor.SetTerrain(x, floorY + 1, TileID.LivingWood);
				}
				PlaceWorkbenchFootprint(centerX - 7, floorY - 1);
				PlaceTableFootprint(centerX, floorY - 1);
				PlaceChairFootprint(centerX + 7, floorY - 1);
				TileEditor.TryPlaceTorch(centerX + 9, floorY - 4);
				return 3;
			}
		}
		return 0;
	}

	private static int BuildWorkyardLoft(Point entrance)
	{
		int left = entrance.X + 12;
		int right = entrance.X + 30;
		int floorY = entrance.Y - 8;
		for (int x = left; x <= right; x++) {
			for (int y = floorY - 6; y < floorY; y++) {
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, (x - left) % 8 < 4 ? WallID.Planked : WallID.LivingWoodUnsafe);
			}
			if (x >= left + 2 && x <= left + 5) {
				TileEditor.TryPlacePlatformForced(x, floorY);
			}
			else {
				TileEditor.SetTerrain(x, floorY, TileID.LivingWood);
				TileEditor.SetTerrain(x, floorY + 1, TileID.LivingWood);
			}
			int roofY = floorY - 8 + Math.Abs(x - (left + right) / 2) / 6;
			TileEditor.SetTerrain(x, roofY, TileID.LivingWood);
			TileEditor.SetTerrain(x, roofY + 1, TileID.LivingWood);
		}
		for (int y = floorY - 1; y <= entrance.Y - 1; y++) {
			TileEditor.SetTerrain(left + 3, y, TileID.Rope);
		}
		PlaceWorkbenchFootprint(left + 8, floorY - 1);
		PlaceTableFootprint(left + 13, floorY - 1);
		PlaceChairFootprint(left + 17, floorY - 1);
		TileEditor.TryPlaceTorch(right - 2, floorY - 4);
		return 3;
	}

	private static void PlaceWorkbenchFootprint(int leftX, int bottomY)
	{
		SetFurnitureTile(leftX, bottomY, TileID.WorkBenches, frameX: 0, frameY: 0);
		SetFurnitureTile(leftX + 1, bottomY, TileID.WorkBenches, frameX: 18, frameY: 0);
	}

	private static void PlaceTableFootprint(int centerX, int bottomY)
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
	}

	private static void PlaceChairFootprint(int x, int bottomY)
	{
		SetFurnitureTile(x, bottomY - 1, TileID.Chairs, frameX: 0, frameY: 0);
		SetFurnitureTile(x, bottomY, TileID.Chairs, frameX: 0, frameY: 18);
	}

	private static void SetFurnitureTile(int x, int y, ushort type, short frameX, short frameY)
	{
		TileEditor.SetTerrain(x, y, type);
		Main.tile[x, y].TileFrameX = frameX;
		Main.tile[x, y].TileFrameY = frameY;
	}

	internal static IEnumerable<Point> Rasterize(MineRoute route)
	{
		int deltaX = route.End.X - route.Start.X;
		int steps = Math.Abs(deltaX);
		if (steps == 0 || Math.Abs(route.End.Y - route.Start.Y) > steps) {
			throw new InvalidOperationException($"Mine rail edge {route.Start}->{route.End} exceeds the one-tile track grade.");
		}

		int direction = Math.Sign(deltaX);
		int verticalDelta = route.End.Y - route.Start.Y;
		int verticalSteps = Math.Abs(verticalDelta);
		int verticalDirection = Math.Sign(verticalDelta);
		int flatSteps = steps - verticalSteps;
		int leadFlat = flatSteps / 2;
		for (int step = 0; step <= steps; step++) {
			int x = route.Start.X + direction * step;
			int verticalProgress = Math.Clamp(step - leadFlat, 0, verticalSteps);
			int y = route.Start.Y + verticalDirection * verticalProgress;
			yield return new Point(x, y);
		}
	}

	private static void BuildQuarantineGates(SurfaceMinePlan plan)
	{
		foreach (MineSection section in plan.Sections.Where(section => section.Kind == MineSectionKind.SealedEvil)) {
			MineRoute? spur = plan.Routes
				.Where(route => route.Start == section.Center || route.End == section.Center)
				.Select<MineRoute, MineRoute?>(route => route)
				.FirstOrDefault();
			if (spur is not MineRoute route) {
				continue;
			}
			Point outside = route.Start == section.Center ? route.End : route.Start;
			int direction = Math.Sign(outside.X - section.Center.X);
			int gateX = section.Center.X + direction * (section.Area.Width / 2 - 8);
			for (int width = 0; width < 3; width++) {
				int columnX = gateX + direction * width;
				int trackY = section.Center.Y;
				foreach (Point point in Rasterize(route)) {
					if (point.X == columnX) {
						trackY = point.Y;
						break;
					}
				}
				for (int y = trackY - 6; y < trackY; y++) {
					TileEditor.SetActuatedTerrain(columnX, y, TileID.GrayBrick);
				}
			}

			int innerGateX = section.Center.X + direction * 10;
			int innerTrackY = Math.Clamp(section.Center.Y, section.Area.Top + 10, section.Area.Bottom - 3);
			for (int width = 0; width < 4; width++) {
				int columnX = innerGateX + direction * width;
				for (int y = innerTrackY - 8; y < innerTrackY; y++) {
					TileEditor.SetActuatedTerrain(columnX, y, TileID.GrayBrick);
				}
			}
		}
	}

	private static int Noise(int id, int x, int salt)
	{
		unchecked {
			uint value = (uint)(id * 0x9E3779B) ^ (uint)(x * 0x45D9F3B) ^ (uint)salt;
			value ^= value >> 16;
			value *= 0x7FEB352D;
			value ^= value >> 15;
			return (int)(value & 0x7FFFFFFF);
		}
	}

	private static bool RouteSurvived(MineRoute route)
	{
		int total = 0;
		int tracks = 0;
		foreach (Point point in Rasterize(route)) {
			total++;
			if (HasTile(point.X, point.Y, TileID.MinecartTrack)) {
				tracks++;
			}
		}
		return tracks == total;
	}

	private static int CountTiles(Rectangle area, ushort tileType)
	{
		int count = 0;
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				if (HasTile(x, y, tileType)) {
					count++;
				}
			}
		}
		return count;
	}

	private static bool HasTile(int x, int y, ushort type) =>
		WorldGen.InWorld(x, y, 2) && Main.tile[x, y].HasTile && Main.tile[x, y].TileType == type;

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
}
