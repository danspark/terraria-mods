using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace RicherBiomes.WorldGeneration;

internal static class WorldValidator
{
	public static GenerationReport Validate(WorldPlan plan, SurfaceMinePlan surfaceMinePlan, GenerationManifest manifest)
	{
		List<string> errors = [];
		int relief = ValidateSurface(plan, errors);
		int mountainCount = ValidateMountains(plan, manifest, errors);
		int connectedCaves = ValidateCaves(plan, errors);
		ValidateTerraces(plan, manifest, errors);
		ValidateLandmarks(manifest, errors);
		ValidateBridgesAndValleys(plan, manifest, errors);
		ValidateSkyHighlands(plan, manifest, errors);
		int mineTrackTiles = ValidateSurfaceMine(surfaceMinePlan, manifest, errors);
		ValidateProgressionSites(errors);

		int accentCount = manifest.AccentCounts.Values.Sum();
		int minimumAccents = Math.Max(36, Main.maxTilesX / 90);
		if (accentCount < minimumAccents) {
			errors.Add($"only {accentCount} contextual accents survived; expected at least {minimumAccents}");
		}

		if (errors.Count > 0) {
			throw new InvalidOperationException("Richer Biomes world validation failed: " + string.Join("; ", errors));
		}

		return new GenerationReport(
			true,
			plan.Regions.Count,
			relief,
			mountainCount,
			connectedCaves,
			manifest.Terraces.Count,
			manifest.Landmarks.Count,
			accentCount,
			manifest.Bridges.Count,
			manifest.SkyHighlands.Count,
			mineTrackTiles);
	}

	private static int ValidateSurface(WorldPlan plan, List<string> errors)
	{
		int minimumY = int.MaxValue;
		int maximumY = int.MinValue;
		int missingSamples = 0;
		for (int x = plan.LeftBoundary; x <= plan.RightBoundary; x += 8) {
			if (!BiomeClassifier.TryFindGroundSupport(x, out int y)) {
				missingSamples++;
				continue;
			}

			minimumY = Math.Min(minimumY, y);
			maximumY = Math.Max(maximumY, y);
		}

		int relief = maximumY > minimumY ? maximumY - minimumY : 0;
		int expectedRelief = Math.Min(100, Math.Max(65, (int)Main.worldSurface / 3));
		if (relief < expectedRelief) {
			errors.Add($"surface relief was {relief} tiles; expected at least {expectedRelief}");
		}
		if (missingSamples > 4) {
			errors.Add($"{missingSamples} inland surface samples had no natural support");
		}

		return relief;
	}

	private static int ValidateMountains(WorldPlan plan, GenerationManifest manifest, List<string> errors)
	{
		int validMountains = 0;
		int spaceThreshold = (int)Math.Floor(Main.worldSurface * 0.35d);
		foreach (WorldRegion region in plan.Regions.Where(region => region.Landform == LandformKind.Mountain)) {
			int peakY = int.MaxValue;
			int summitBandWidth = 0;
			int[] groundedSurface = MeasureGroundedMountainSurface(region);
			for (int x = region.Left; x <= region.Right; x++) {
				int y = groundedSurface[x - region.Left];
				if (y != int.MaxValue) {
					peakY = Math.Min(peakY, y);
					if (y <= spaceThreshold) {
						summitBandWidth++;
					}
				}
			}

			MountainRecord? record = null;
			foreach (MountainRecord mountain in manifest.Mountains) {
				if (mountain.RegionId == region.Id) {
					record = mountain;
					break;
				}
			}
			if (peakY <= spaceThreshold && summitBandWidth >= 32) {
				validMountains++;
			}
			else {
				errors.Add(
					$"mountain region {region.Id} had ground peak y={peakY} and {summitBandWidth} Space-band columns; "
					+ $"expected y<={spaceThreshold} across at least 32 columns");
			}
			if (record is MountainRecord final && final.EntranceCount < 2) {
				errors.Add($"mountain region {region.Id} retained only {final.EntranceCount} visible entrances");
			}
			if (record is MountainRecord cloudRecord && cloudRecord.CloudTiles < 24) {
				errors.Add($"mountain region {region.Id} retained only {cloudRecord.CloudTiles} cloud-belt tiles");
			}
		}

		if (validMountains == 0) {
			errors.Add("no planned mountain retained a Space-height summit");
		}
		return validMountains;
	}

	internal static int[] MeasureGroundedMountainSurface(WorldRegion region)
	{
		int top = 40;
		int bottom = Math.Min(Main.maxTilesY - 50, (int)Main.worldSurface + 75);
		int width = region.Width;
		int groundingBandTop = Math.Max(top, (int)Main.worldSurface + 45);
		int[] surface = Enumerable.Repeat(int.MaxValue, width).ToArray();
		for (int x = region.Left; x <= region.Right; x++) {
			int candidateY = int.MaxValue;
			for (int y = top; y <= bottom; y++) {
				if (IsMountainGround(x, y)) {
					candidateY = y;
					break;
				}
			}
			if (candidateY == int.MaxValue) {
				continue;
			}

			int longestGap = 0;
			int currentGap = 0;
			bool reachedGroundingBand = false;
			for (int y = candidateY; y <= bottom; y++) {
				if (IsMountainGround(x, y)) {
					currentGap = 0;
					if (y >= groundingBandTop) {
						reachedGroundingBand = true;
					}
				}
				else {
					currentGap++;
					longestGap = Math.Max(longestGap, currentGap);
				}
			}
			// Authored galleries are ten tiles tall. A larger break indicates a truly
			// floating shelf rather than a tunnel passing through grounded terrain.
			if (reachedGroundingBand && longestGap <= 18) {
				surface[x - region.Left] = candidateY;
			}
		}
		return surface;
	}

	private static bool IsMountainGround(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 2)) {
			return false;
		}
		Tile tile = Main.tile[x, y];
		if (!tile.HasUnactuatedTile || Main.tileFrameImportant[tile.TileType]
			|| !Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]) {
			return false;
		}
		return tile.TileType is not (TileID.Cloud or TileID.RainCloud or TileID.SnowCloud or TileID.Sunplate);
	}

	private static int ValidateCaves(WorldPlan plan, List<string> errors)
	{
		int connected = 0;
		List<string> blockedRoutes = [];
		foreach (PlannedCave cave in plan.Caves.Where(cave => cave.RequiredRoute)) {
			if (HasAirRoute(cave, out string reason)) {
				connected++;
			}
			else {
				blockedRoutes.Add($"region {cave.RegionId}: {reason}");
			}
		}

		int plannedRequired = plan.Caves.Count(cave => cave.RequiredRoute);
		int minimumConnected = Math.Min(plannedRequired, Math.Max(3, (plannedRequired + 1) / 2));
		if (connected < minimumConnected) {
			errors.Add(
				$"only {connected} of {plannedRequired} protected regional cave routes connect to the Cavern layer; "
				+ $"expected at least {minimumConnected}. Blocked routes: {string.Join(" | ", blockedRoutes)}");
		}

		return connected;
	}

	private static bool HasAirRoute(PlannedCave cave, out string reason)
	{
		int left = Math.Max(10, Math.Min(cave.Start.X, Math.Min(cave.Midpoint.X, cave.End.X)) - cave.Radius - 12);
		int right = Math.Min(Main.maxTilesX - 11, Math.Max(cave.Start.X, Math.Max(cave.Midpoint.X, cave.End.X)) + cave.Radius + 12);
		int top = Math.Max(10, Math.Min(cave.Start.Y, Math.Min(cave.Midpoint.Y, cave.End.Y)) - cave.Radius - 12);
		int bottom = Math.Min(Main.maxTilesY - 11, Math.Max(cave.Start.Y, Math.Max(cave.Midpoint.Y, cave.End.Y)) + cave.Radius + 12);
		if (!TryFindPassable(cave.Start, left, right, top, bottom, out Point start)) {
			reason = $"no passable start near {cave.Start.X},{cave.Start.Y}";
			return false;
		}
		if (!TryFindPassable(cave.End, left, right, top, bottom, out Point end)) {
			reason = $"no passable end near {cave.End.X},{cave.End.Y}";
			return false;
		}

		int width = right - left + 1;
		int height = bottom - top + 1;
		bool[] visited = new bool[width * height];
		Queue<Point> queue = new();
		int visitedCount = 1;
		queue.Enqueue(start);
		visited[(start.X - left) + (start.Y - top) * width] = true;
		ReadOnlySpan<Point> directions = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

		while (queue.Count > 0) {
			Point current = queue.Dequeue();
			if (Math.Abs(current.X - end.X) <= 2 && Math.Abs(current.Y - end.Y) <= 2) {
				reason = string.Empty;
				return true;
			}

			foreach (Point direction in directions) {
				int x = current.X + direction.X;
				int y = current.Y + direction.Y;
				if (x < left || x > right || y < top || y > bottom || !IsPassable(x, y)) {
					continue;
				}

				int index = (x - left) + (y - top) * width;
				if (visited[index]) {
					continue;
				}

				visited[index] = true;
				visitedCount++;
				queue.Enqueue(new Point(x, y));
			}
		}

		reason = $"flood fill visited {visitedCount} cells from {start.X},{start.Y}; target {end.X},{end.Y}; bounds {left},{top}..{right},{bottom}";
		return false;
	}

	private static bool TryFindPassable(
		Point center,
		int left,
		int right,
		int top,
		int bottom,
		out Point passable)
	{
		for (int radius = 0; radius <= 14; radius++) {
			for (int x = Math.Max(left, center.X - radius); x <= Math.Min(right, center.X + radius); x++) {
				for (int y = Math.Max(top, center.Y - radius); y <= Math.Min(bottom, center.Y + radius); y++) {
					if (IsPassable(x, y)) {
						passable = new Point(x, y);
						return true;
					}
				}
			}
		}

		passable = default;
		return false;
	}

	private static bool IsPassable(int x, int y) =>
		!TileEditor.IsSolid(x, y) && !TileEditor.IsSolid(x, y - 1);

	private static void ValidateTerraces(WorldPlan plan, GenerationManifest manifest, List<string> errors)
	{
		int minimumTerraces = TerraceGenerator.MinimumRequiredCount;
		if (manifest.Terraces.Count < minimumTerraces) {
			errors.Add($"only {manifest.Terraces.Count} protected building terraces survived; expected at least {minimumTerraces}");
		}
		if (!manifest.Terraces.Any(terrace => terrace.SpawnTerrace)) {
			errors.Add("the spawn building terrace was not reserved");
		}

		foreach (BuildTerrace terrace in manifest.Terraces) {
			int minimumY = int.MaxValue;
			int maximumY = int.MinValue;
			for (int x = terrace.Area.Left; x < terrace.Area.Right; x += 2) {
				if (!BiomeClassifier.TryFindGroundSupport(x, out int y)) {
					errors.Add($"building terrace at x={terrace.Area.Left} lost its terrain support");
					break;
				}
				minimumY = Math.Min(minimumY, y);
				maximumY = Math.Max(maximumY, y);
			}
			if (maximumY - minimumY > 3) {
				errors.Add($"building terrace at x={terrace.Area.Left} has {maximumY - minimumY} tiles of relief");
			}
		}
	}

	private static void ValidateLandmarks(GenerationManifest manifest, List<string> errors)
	{
		BiomeKind[] required = [
			BiomeKind.Forest,
			BiomeKind.Snow,
			BiomeKind.Desert,
			BiomeKind.Jungle,
			BiomeKind.Evil,
			BiomeKind.Sky,
			BiomeKind.Mushroom,
			BiomeKind.Cavern,
			BiomeKind.Underworld
		];

		foreach (BiomeKind biome in required) {
			if (!manifest.Landmarks.Any(landmark => landmark.Biome == biome)) {
				errors.Add($"the {biome} biome has no landmark");
			}
		}

		if (manifest.Landmarks.Count(landmark => landmark.Biome == BiomeKind.Ocean) < 2) {
			errors.Add("both oceans did not receive a coastal landmark");
		}

		foreach (LandmarkRecord landmark in manifest.Landmarks) {
			if (!WorldGen.InWorld(landmark.Area.Left, landmark.Area.Top, 8)
				|| !WorldGen.InWorld(landmark.Area.Right - 1, landmark.Area.Bottom - 1, 8)) {
				errors.Add($"{landmark.Biome} landmark extends outside world padding");
			}
			if (landmark.Area.Width < 39 || landmark.RoomCount < 2) {
				errors.Add($"{landmark.Biome} landmark is not a multi-room structure ({landmark.Area.Width} tiles, {landmark.RoomCount} rooms)");
			}
			int doorTiles = CountTiles(landmark.Area, TileID.ClosedDoor);
			bool enclosedHouse = landmark.Biome is BiomeKind.Forest or BiomeKind.Snow or BiomeKind.Desert
				or BiomeKind.Jungle or BiomeKind.Evil or BiomeKind.Ocean or BiomeKind.Sky;
			if (enclosedHouse && doorTiles < 3) {
				errors.Add($"{landmark.Biome} landmark has no complete door footprint");
			}
			int furnitureTiles = CountFurnitureTiles(landmark.Area);
			int minimumFurnitureTiles = enclosedHouse ? 4 : 1;
			if (landmark.FurnitureCount < 6 || furnitureTiles < minimumFurnitureTiles) {
				errors.Add($"{landmark.Biome} landmark retained {landmark.FurnitureCount} placed objects across {furnitureTiles} furniture tiles");
			}
			if (enclosedHouse) {
				HashSet<ushort> wallTypes = CollectWallTypes(landmark.Area);
				if (wallTypes.Count < 2 || CountWalls(landmark.Area, WallID.Glass) < 12) {
					errors.Add($"{landmark.Biome} landmark has a flat interior palette ({wallTypes.Count} wall types, {CountWalls(landmark.Area, WallID.Glass)} glass wall cells)");
				}
				int furnitureFamilies = CountFurnitureFamilies(landmark.Area);
				if (furnitureFamilies < 3) {
					errors.Add($"{landmark.Biome} landmark retained only {furnitureFamilies} furniture families");
				}
			}
			int thickFloorColumns = 0;
			for (int x = landmark.Area.Left; x < landmark.Area.Right; x++) {
				if (TileEditor.IsSolid(x, landmark.AnchorY) && TileEditor.IsSolid(x, landmark.AnchorY + 1)) {
					thickFloorColumns++;
				}
			}
			if (thickFloorColumns < landmark.Area.Width * 9 / 10) {
				errors.Add($"{landmark.Biome} landmark has a thin or broken foundation in {landmark.Area.Width - thickFloorColumns} columns");
			}
			if (landmark.RoomCount >= 3) {
				int platforms = CountTiles(landmark.Area, TileID.Platforms);
				if (platforms < 10 || platforms > 48) {
					errors.Add($"{landmark.Biome} landmark has {platforms} platform tiles; expected bounded stairs and drop portals");
				}
			}
		}
	}

	private static void ValidateBridgesAndValleys(WorldPlan plan, GenerationManifest manifest, List<string> errors)
	{
		if (manifest.Bridges.Count != plan.Mountains.Count) {
			errors.Add($"only {manifest.Bridges.Count} bridges were recorded for {plan.Mountains.Count} mountain ranges");
		}
		if (manifest.Valleys.Count != plan.Mountains.Count) {
			errors.Add($"only {manifest.Valleys.Count} valley payloads were recorded for {plan.Mountains.Count} mountain ranges");
		}

		foreach (BridgeRecord bridge in manifest.Bridges) {
			int survivingColumns = 0;
			int thickColumns = 0;
			int clearColumns = 0;
			int platformPortals = 0;
			for (int x = bridge.Area.Left; x < bridge.Area.Right; x++) {
				int deckY = int.MaxValue;
				for (int y = bridge.Area.Top; y < bridge.Area.Bottom; y++) {
					Tile tile = Main.tile[x, y];
					if (tile.HasTile
						&& tile.TileType is TileID.Platforms or TileID.GrayBrick or TileID.LivingWood or TileID.MinecartTrack
						&& !TileEditor.IsSolid(x, y - 1)
						&& !TileEditor.IsSolid(x, y - 2)
						&& !TileEditor.IsSolid(x, y - 3)) {
						deckY = y;
					}
				}
				if (deckY != int.MaxValue) {
					survivingColumns++;
					Tile deck = Main.tile[x, deckY];
					if (deck.TileType == TileID.Platforms) {
						platformPortals++;
					}
					else if (TileEditor.IsSolid(x, deckY + 1) && TileEditor.IsSolid(x, deckY + 2)) {
						thickColumns++;
					}
					if (!TileEditor.IsSolid(x, deckY - 1) && !TileEditor.IsSolid(x, deckY - 2) && !TileEditor.IsSolid(x, deckY - 3)) {
						clearColumns++;
					}
				}
			}
			int minimumColumns = Math.Max(28, bridge.Area.Width * 3 / 5);
			if (survivingColumns < minimumColumns) {
				errors.Add($"{bridge.Style} bridge retained deck material in only {survivingColumns}/{bridge.Area.Width} columns");
			}
			if (thickColumns < bridge.Area.Width / 2) {
				errors.Add($"{bridge.Style} bridge has only {thickColumns}/{bridge.Area.Width} structurally thick deck columns");
			}
			if (clearColumns < survivingColumns * 9 / 10) {
				errors.Add($"{bridge.Style} bridge has blocked headroom in {survivingColumns - clearColumns} deck columns");
			}
			if (bridge.Style != BridgeStyle.StoneArch && platformPortals < 4) {
				errors.Add($"{bridge.Style} bridge has no intentional platform drop bays");
			}
			int actuators = CountActuators(bridge.Area);
			int backdrop = CountWallCells(bridge.Area);
			if (actuators < 16) {
				errors.Add($"{bridge.Style} bridge retained only {actuators} actuated portal blocks");
			}
			if (backdrop < bridge.Area.Width * 2) {
				errors.Add($"{bridge.Style} bridge retained only {backdrop} authored background-wall cells");
			}
		}

		foreach (ValleyRecord valley in manifest.Valleys) {
			int water = CountLiquid(valley.Area, LiquidID.Water);
			int lava = CountLiquid(valley.Area, LiquidID.Lava);
			if (valley.Theme == ValleyTheme.Lake && water < 80) {
				errors.Add($"lake valley at x={valley.Area.Center.X} retained only {water} water cells");
			}
			if (valley.Theme == ValleyTheme.Lava && lava < 50) {
				errors.Add($"lava valley at x={valley.Area.Center.X} retained only {lava} lava cells");
			}
			if (valley.Theme == ValleyTheme.SealedEvil && CountTiles(valley.Area, TileID.GrayBrick) < 60) {
				errors.Add($"sealed evil valley at x={valley.Area.Center.X} lost its quarantine shell");
			}
		}
	}

	private static void ValidateSkyHighlands(WorldPlan plan, GenerationManifest manifest, List<string> errors)
	{
		if (manifest.SkyHighlands.Count != plan.SkyHighlands.Count) {
			errors.Add($"only {manifest.SkyHighlands.Count} of {plan.SkyHighlands.Count} floating highlands were recorded");
		}

		for (int index = 0; index < manifest.SkyHighlands.Count; index++) {
			SkyHighlandRecord highland = manifest.SkyHighlands[index];
			int minimumWidth = Main.maxTilesX switch { <= 4200 => 280, <= 6400 => 360, _ => 440 };
			if (highland.Area.Width < minimumWidth) {
				errors.Add($"floating highland {index} spans only {highland.Area.Width} tiles; expected at least {minimumWidth}");
			}
			int mass = CountTiles(highland.Area, TileID.Cloud)
				+ CountTiles(highland.Area, TileID.RainCloud)
				+ CountTiles(highland.Area, TileID.Sunplate)
				+ CountTiles(highland.Area, TileID.Stone)
				+ CountTiles(highland.Area, TileID.Dirt);
			if (mass < minimumWidth * 12) {
				errors.Add($"floating highland {index} retained only {mass} terrain tiles");
			}
			int authoredStone = SkyHighlandGenerator.CountStoneInAuthoredBody(plan, index);
			if (authoredStone > minimumWidth / 2) {
				errors.Add($"floating highland {index} retained {authoredStone} stone tiles inside its authored sky body");
			}
			(int componentMass, int componentWidth, int componentHeight) = MeasureLargestHighlandComponent(highland.Area);
			int plannedDepth = index < plan.SkyHighlands.Count ? plan.SkyHighlands[index].Depth : highland.Area.Height - 62;
			if (componentWidth < minimumWidth * 3 / 4 || componentHeight < plannedDepth * 2 / 3) {
				errors.Add(
					$"floating highland {index} has no biome-scale connected body: largest component "
					+ $"is {componentWidth}x{componentHeight} ({componentMass} tiles)");
			}
			if (highland.InteriorRouteTiles < 350) {
				errors.Add($"floating highland {index} recorded only {highland.InteriorRouteTiles} carved route cells");
			}
			if (CountLiquid(highland.Area, LiquidID.Water) < 80) {
				errors.Add($"floating highland {index} lost its sealed sky lake");
			}
			int platformTiles = CountTiles(highland.Area, TileID.Platforms);
			if (platformTiles < 50) {
				errors.Add($"floating highland {index} retained only {platformTiles} platform cells in its vertical drop routes");
			}
			int thinSupports = CountThinWalkableSupports(highland.Area);
			if (thinSupports > minimumWidth / 5) {
				errors.Add($"floating highland {index} retained {thinSupports} unsupported one-tile walking cells");
			}
		}
	}

	private static int ValidateSurfaceMine(SurfaceMinePlan plan, GenerationManifest manifest, List<string> errors)
	{
		ValidateMinePlanTopology(plan, errors);
		if (manifest.SurfaceMine is not SurfaceMineRecord mine) {
			errors.Add("the guaranteed surface mine has no final manifest record");
			return 0;
		}

		MineSectionKind[] requiredKinds = [
			MineSectionKind.Workyard,
			MineSectionKind.Working,
			MineSectionKind.Collapsed,
			MineSectionKind.Flooded,
			MineSectionKind.SealedEvil,
			MineSectionKind.MountainRail
		];
		foreach (MineSectionKind kind in requiredKinds) {
			if (!manifest.MineSections.Any(section => section.Kind == kind)) {
				errors.Add($"the guaranteed surface mine has no {kind} section");
			}
		}

		int actualTracks = CountTiles(mine.Area, TileID.MinecartTrack);
		int minimumConnected = Main.maxTilesX switch { <= 4200 => 300, <= 6400 => 500, _ => 700 };
		HashSet<Point> entranceComponent = CollectConnectedTracks(plan.Entrance, mine.Area);
		int connectedTracks = entranceComponent.Count;
		if (connectedTracks < minimumConnected) {
			errors.Add($"surface mine entrance reaches only {connectedTracks} rail tiles; expected {minimumConnected}");
		}
		if (actualTracks < minimumConnected || mine.TrackTiles < minimumConnected) {
			errors.Add($"surface mine retained only {actualTracks} track tiles (manifest {mine.TrackTiles})");
		}
		int requiredAuthoredConnection = mine.TrackTiles * 9 / 10;
		if (connectedTracks < requiredAuthoredConnection) {
			errors.Add(
				$"surface mine rail graph is fragmented: the entrance component reaches {connectedTracks} "
				+ $"of {mine.TrackTiles} authored track tiles; expected at least {requiredAuthoredConnection}");
		}
		if (mine.SupportTiles < minimumConnected / 12) {
			errors.Add($"surface mine retained only {mine.SupportTiles} Wooden Beam support tiles");
		}
		if (mine.FurnitureCount < 12) {
			errors.Add($"surface mine retained only {mine.FurnitureCount} work-district furnishings");
		}
		if (mine.ConnectedRouteCount < mine.RequiredRouteCount) {
			errors.Add($"surface mine retained {mine.ConnectedRouteCount}/{mine.RequiredRouteCount} required rail edges");
		}
		if (mine.Entrance.Y > Main.worldSurface + 20 || !HasTile(plan.Entrance.X, plan.Entrance.Y, TileID.MinecartTrack)) {
			errors.Add("surface mine has no visible rail entrance in the surface band");
		}
		int openMouthColumns = 0;
		for (int x = mine.Entrance.X - 8; x <= mine.Entrance.X + 8; x++) {
			bool clear = true;
			for (int y = mine.Entrance.Y - 6; y < mine.Entrance.Y; y++) {
				clear &= !TileEditor.IsSolid(x, y);
			}
			if (clear) {
				openMouthColumns++;
			}
		}
		if (openMouthColumns < 12) {
			errors.Add($"surface mine entrance exposes only {openMouthColumns}/17 clear approach columns");
		}
		for (int routeIndex = 0; routeIndex < plan.Routes.Count; routeIndex++) {
			MineRoute route = plan.Routes[routeIndex];
			if (!route.HasTrack) {
				continue;
			}
			Point? failure = null;
			string reason = "";
			foreach (Point point in SurfaceMineGenerator.Rasterize(route)) {
				if (!HasTile(point.X, point.Y, TileID.MinecartTrack)) {
					failure = point;
					reason = "missing";
					break;
				}
				if (!entranceComponent.Contains(point)) {
					failure = point;
					reason = "disconnected";
					break;
				}
			}
			if (failure is Point failedPoint) {
				errors.Add($"mine rail edge {routeIndex} has a {reason} authored cell at {failedPoint}");
			}
		}

		MineSection? flooded = manifest.MineSections
			.Where(section => section.Kind == MineSectionKind.Flooded)
			.Select<MineSection, MineSection?>(section => section)
			.FirstOrDefault();
		if (flooded is MineSection wet && CountLiquid(wet.Area, LiquidID.Water) < 80) {
			errors.Add("the mine's flooded spur lost its water district");
		}
		MineSection? evil = manifest.MineSections
			.Where(section => section.Kind == MineSectionKind.SealedEvil)
			.Select<MineSection, MineSection?>(section => section)
			.FirstOrDefault();
		if (evil is MineSection quarantine && CountTiles(quarantine.Area, TileID.GrayBrick) < 250) {
			errors.Add("the mine's evil annex lost its five-tile quarantine shell");
		}
		if (evil is MineSection irregularQuarantine) {
			HashSet<int> shellTops = [];
			for (int x = irregularQuarantine.Area.Left; x < irregularQuarantine.Area.Right; x++) {
				for (int y = irregularQuarantine.Area.Top; y < irregularQuarantine.Area.Bottom; y++) {
					if (HasTile(x, y, TileID.GrayBrick)) {
						shellTops.Add(y);
						break;
					}
				}
			}
			if (shellTops.Count < 7) {
				errors.Add($"the mine's evil annex still has a rectangular shell profile ({shellTops.Count} distinct top rows)");
			}
			if (CountActuators(irregularQuarantine.Area) < 12) {
				errors.Add("the mine's evil annex has no passable actuated quarantine gate");
			}
		}

		return connectedTracks;
	}

	private static (int Mass, int Width, int Height) MeasureLargestHighlandComponent(Rectangle area)
	{
		int left = Math.Max(2, area.Left);
		int top = Math.Max(2, area.Top);
		int right = Math.Min(Main.maxTilesX - 2, area.Right);
		int bottom = Math.Min(Main.maxTilesY - 2, area.Bottom);
		int width = right - left;
		int height = bottom - top;
		bool[] visited = new bool[width * height];
		(int Mass, int Width, int Height) largest = default;
		Queue<Point> queue = new();

		for (int y = top; y < bottom; y++) {
			for (int x = left; x < right; x++) {
				int localIndex = x - left + (y - top) * width;
				if (visited[localIndex] || !IsHighlandMass(x, y)) {
					continue;
				}

				visited[localIndex] = true;
				queue.Enqueue(new Point(x, y));
				int mass = 0;
				int minimumX = x;
				int maximumX = x;
				int minimumY = y;
				int maximumY = y;
				while (queue.Count > 0) {
					Point current = queue.Dequeue();
					mass++;
					minimumX = Math.Min(minimumX, current.X);
					maximumX = Math.Max(maximumX, current.X);
					minimumY = Math.Min(minimumY, current.Y);
					maximumY = Math.Max(maximumY, current.Y);
					ReadOnlySpan<Point> directions = [
						new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
						new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
					];
					foreach (Point direction in directions) {
						Point next = current + direction;
						if (next.X < left || next.X >= right || next.Y < top || next.Y >= bottom) {
							continue;
						}
						int nextIndex = next.X - left + (next.Y - top) * width;
						if (visited[nextIndex] || !IsHighlandMass(next.X, next.Y)) {
							continue;
						}
						visited[nextIndex] = true;
						queue.Enqueue(next);
					}
				}

				(int Mass, int Width, int Height) component =
					(mass, maximumX - minimumX + 1, maximumY - minimumY + 1);
				if (component.Mass > largest.Mass) {
					largest = component;
				}
			}
		}

		return largest;
	}

	private static bool IsHighlandMass(int x, int y)
	{
		Tile tile = Main.tile[x, y];
		if (tile.HasUnactuatedTile && tile.TileType == TileID.Platforms) {
			return true;
		}
		if (!tile.HasUnactuatedTile || !Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]) {
			return false;
		}
		return tile.TileType is TileID.Cloud or TileID.RainCloud or TileID.SnowCloud or TileID.Sunplate
			or TileID.Dirt or TileID.Stone or TileID.Grass or TileID.Mud or TileID.JungleGrass
			or TileID.SnowBlock or TileID.IceBlock or TileID.HardenedSand or TileID.Sandstone;
	}

	private static HashSet<Point> CollectConnectedTracks(Point entrance, Rectangle bounds)
	{
		if (!HasTile(entrance.X, entrance.Y, TileID.MinecartTrack)) {
			return [];
		}

		HashSet<Point> visited = [entrance];
		Queue<Point> queue = new();
		queue.Enqueue(entrance);
		while (queue.Count > 0) {
			Point current = queue.Dequeue();
			for (int offsetX = -1; offsetX <= 1; offsetX++) {
				for (int offsetY = -1; offsetY <= 1; offsetY++) {
					if (offsetX == 0 && offsetY == 0) {
						continue;
					}
					Point next = new(current.X + offsetX, current.Y + offsetY);
					if (!bounds.Contains(next) || visited.Contains(next) || !HasTile(next.X, next.Y, TileID.MinecartTrack)) {
						continue;
					}
					visited.Add(next);
					queue.Enqueue(next);
				}
			}
		}
		return visited;
	}

	private static void ValidateMinePlanTopology(SurfaceMinePlan plan, List<string> errors)
	{
		Dictionary<Point, int> degree = [];
		HashSet<Point> vertices = [];
		int authoredEdges = 0;
		int horizontalEdges = 0;
		foreach (MineRoute route in plan.Routes.Where(route => route.HasTrack)) {
			authoredEdges++;
			vertices.Add(route.Start);
			vertices.Add(route.End);
			degree[route.Start] = degree.GetValueOrDefault(route.Start) + 1;
			degree[route.End] = degree.GetValueOrDefault(route.End) + 1;
			if (Math.Abs(route.End.Y - route.Start.Y) <= 10) {
				horizontalEdges++;
			}

			Point? previous = null;
			int previousGrade = 0;
			int gradeChanges = 0;
			foreach (Point point in SurfaceMineGenerator.Rasterize(route)) {
				if (previous is Point prior) {
					int grade = Math.Sign(point.Y - prior.Y);
					if (grade != previousGrade) {
						gradeChanges++;
						previousGrade = grade;
					}
				}
				previous = point;
			}
			if (gradeChanges > 2) {
				errors.Add($"mine rail edge {route.Start}->{route.End} has {gradeChanges} grade changes and would render as a wobble");
			}
		}

		int junctions = degree.Values.Count(value => value >= 3);
		int cycleRank = authoredEdges - vertices.Count + 1;
		if (authoredEdges < 12 || junctions < 3 || cycleRank < 2 || horizontalEdges < 4) {
			errors.Add(
				$"surface mine graph is too simple: edges={authoredEdges}, junctions={junctions}, "
				+ $"independentLoops={cycleRank}, horizontalEdges={horizontalEdges}");
		}
	}

	private static HashSet<ushort> CollectWallTypes(Rectangle area)
	{
		HashSet<ushort> types = [];
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				ushort wall = Main.tile[x, y].WallType;
				if (wall != WallID.None) {
					types.Add(wall);
				}
			}
		}
		return types;
	}

	private static int CountWalls(Rectangle area, ushort wallType)
	{
		int count = 0;
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				count += Main.tile[x, y].WallType == wallType ? 1 : 0;
			}
		}
		return count;
	}

	private static int CountWallCells(Rectangle area)
	{
		int count = 0;
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				count += Main.tile[x, y].WallType != WallID.None ? 1 : 0;
			}
		}
		return count;
	}

	private static int CountActuators(Rectangle area)
	{
		int count = 0;
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				count += Main.tile[x, y].HasActuator ? 1 : 0;
			}
		}
		return count;
	}

	private static int CountThinWalkableSupports(Rectangle area)
	{
		int count = 0;
		for (int x = Math.Max(3, area.Left); x < Math.Min(Main.maxTilesX - 3, area.Right); x++) {
			for (int y = Math.Max(4, area.Top); y < Math.Min(Main.maxTilesY - 4, area.Bottom); y++) {
				Tile tile = Main.tile[x, y];
				if (!tile.HasUnactuatedTile || !Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]) {
					continue;
				}
				if (!TileEditor.IsSolid(x, y - 1) && !TileEditor.IsSolid(x, y - 2) && !TileEditor.IsSolid(x, y - 3)
					&& !TileEditor.IsSolid(x, y + 1) && !TileEditor.IsSolid(x, y + 2)) {
					count++;
				}
			}
		}
		return count;
	}

	private static int CountFurnitureFamilies(Rectangle area)
	{
		ushort[] types = [
			TileID.WorkBenches, TileID.Tables, TileID.Chairs, TileID.Bookcases,
			TileID.Benches, TileID.Anvils, TileID.Chandeliers, TileID.SmallPiles
		];
		return types.Count(type => CountTiles(area, type) > 0);
	}

	private static int CountFurnitureTiles(Rectangle area)
	{
		ushort[] types = [
			TileID.WorkBenches, TileID.Tables, TileID.Chairs, TileID.Bookcases,
			TileID.Benches, TileID.Anvils, TileID.Chandeliers, TileID.SmallPiles
		];
		return types.Sum(type => CountTiles(area, type));
	}

	private static int CountTiles(Rectangle area, ushort tileType)
	{
		int count = 0;
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				if (HasTile(x, y, tileType)) {
					count++;
				}
			}
		}
		return count;
	}

	private static int CountLiquid(Rectangle area, int liquidType)
	{
		int count = 0;
		for (int x = Math.Max(2, area.Left); x < Math.Min(Main.maxTilesX - 2, area.Right); x++) {
			for (int y = Math.Max(2, area.Top); y < Math.Min(Main.maxTilesY - 2, area.Bottom); y++) {
				Tile tile = Main.tile[x, y];
				if (tile.LiquidAmount > 0 && tile.LiquidType == liquidType) {
					count++;
				}
			}
		}
		return count;
	}

	private static bool HasTile(int x, int y, ushort type) =>
		WorldGen.InWorld(x, y, 2) && Main.tile[x, y].HasTile && Main.tile[x, y].TileType == type;

	private static void ValidateProgressionSites(List<string> errors)
	{
		int dungeonTiles = 0;
		int templeTiles = 0;
		int evilObjects = 0;
		int shimmerCells = 0;
		int sampleStep = Main.maxTilesX > 7000 ? 2 : 1;
		for (int x = 40; x < Main.maxTilesX - 40; x += sampleStep) {
			for (int y = 40; y < Main.maxTilesY - 40; y++) {
				Tile tile = Main.tile[x, y];
				if (tile.HasTile) {
					if (tile.TileType is TileID.BlueDungeonBrick or TileID.GreenDungeonBrick or TileID.PinkDungeonBrick) {
						dungeonTiles++;
					}
					else if (tile.TileType == TileID.LihzahrdBrick) {
						templeTiles++;
					}
					else if (tile.TileType is TileID.ShadowOrbs or TileID.DemonAltar) {
						evilObjects++;
					}
				}
				if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Shimmer) {
					shimmerCells++;
				}
			}
		}

		if (dungeonTiles < 250) {
			errors.Add($"Dungeon integrity scan found only {dungeonTiles} brick samples");
		}
		if (templeTiles < 250) {
			errors.Add($"Jungle Temple integrity scan found only {templeTiles} brick samples");
		}
		if (evilObjects == 0) {
			errors.Add("no Shadow Orb, Crimson Heart, or Demon Altar tiles survived");
		}
		if (shimmerCells < 40) {
			errors.Add($"Aether integrity scan found only {shimmerCells} Shimmer cells");
		}
	}
}
