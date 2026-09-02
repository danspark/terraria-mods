using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace VanillaWorldsOverhauled.WorldGeneration;

internal static class SurfaceMineGenerator
{
	private const int MineSeedSalt = 0x4D49_4E45;
	private const int CorridorHeadroom = 6;
	private const int CoastalLandmarkClearance = 380;
	private const short FlatOpenLeftFrame = 14;
	private const short FlatOpenRightFrame = 15;
	private const short FirstLaunchRampFrame = 16;
	private const short LastLaunchRampFrame = 19;

	public static SurfaceMinePlan PlanAndReserve(WorldPlan worldPlan, GenerationManifest manifest)
	{
		int preferredHalfWidth = Main.maxTilesX switch {
			<= 4200 => 260,
			<= 6400 => 330,
			_ => 400
		};
		int[] halfWidths = [
			preferredHalfWidth,
			Math.Max(210, preferredHalfWidth * 9 / 10),
			Math.Max(210, preferredHalfWidth * 4 / 5)
		];
		Dictionary<string, int> rejected = [];
		for (int tier = 0; tier < halfWidths.Length; tier++) {
			int halfWidth = halfWidths[tier];
			if (tier > 0 && halfWidth == halfWidths[tier - 1]) {
				continue;
			}
			if (TryReserveMineTier(
				worldPlan,
				manifest,
				halfWidth,
				tier,
				includeDenseScan: tier > 0,
				allowShortening: tier == halfWidths.Length - 1,
				rejected,
				out SurfaceMinePlan accepted)) {
				return accepted;
			}
		}

		string rejectionSummary = string.Join(", ", rejected.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key}={pair.Value}"));
		throw new InvalidOperationException(
			"Vanilla Worlds Overhauled could not reserve a progression-safe site for the guaranteed surface mine "
			+ $"after adaptive widths {string.Join("/", halfWidths.Distinct())}. "
			+ rejectionSummary);
	}

	private static bool TryReserveMineTier(
		WorldPlan worldPlan,
		GenerationManifest manifest,
		int halfWidth,
		int tier,
		bool includeDenseScan,
		bool allowShortening,
		Dictionary<string, int> rejected,
		out SurfaceMinePlan accepted)
	{
		UnifiedRandom random = new(MixSeed(worldPlan.GenerationSeed, MineSeedSalt ^ tier * 0x2C92_77B5));
		int leftMineCenter = worldPlan.CoastMargin + halfWidth + CoastalLandmarkClearance;
		int rightMineCenter = Main.maxTilesX - worldPlan.CoastMargin - halfWidth - CoastalLandmarkClearance - 1;
		if (leftMineCenter > rightMineCenter) {
			accepted = null!;
			return false;
		}

		List<int> candidates = worldPlan.Mountains
			.Select(mountain => worldPlan.Regions[mountain.RegionId].CenterX)
			.Concat(worldPlan.Regions
				.Where(region => region.Landform is LandformKind.Plateau or LandformKind.RollingHills)
				.OrderByDescending(region => Math.Abs(region.CenterX - worldPlan.SpawnX))
				.Select(region => region.CenterX))
			.ToList();
		for (int attempt = 0; attempt < 480; attempt++) {
			candidates.Add(random.Next(leftMineCenter, rightMineCenter + 1));
		}
		if (includeDenseScan) {
			int stride = tier == 1 ? 12 : 8;
			int offset = random.Next(stride);
			for (int centerX = leftMineCenter + offset; centerX <= rightMineCenter; centerX += stride) {
				candidates.Add(centerX);
			}
		}

		foreach (int rawCenterX in candidates) {
			int centerX = Math.Clamp(rawCenterX, leftMineCenter, rightMineCenter);
			if (Math.Abs(centerX - worldPlan.SpawnX) < halfWidth + 180 || Math.Abs(centerX - GenVars.dungeonX) < halfWidth + 140) {
				continue;
			}

			int layoutAttempts = includeDenseScan ? 2 : 1;
			for (int layoutAttempt = 0; layoutAttempt < layoutAttempts; layoutAttempt++) {
				SurfaceMinePlan template = CreatePlan(worldPlan, centerX, halfWidth, random.Next());
				if (!TryNavigateAroundObstacles(
					template,
					worldPlan,
					manifest,
					allowShortening,
					out SurfaceMinePlan candidate,
					out string navigationReason)) {
					rejected[navigationReason] = rejected.GetValueOrDefault(navigationReason) + 1;
					continue;
				}
				if (!CanPlace(candidate, worldPlan, manifest, out string reason)) {
					rejected[reason] = rejected.GetValueOrDefault(reason) + 1;
					continue;
				}

				Reserve(candidate);
				accepted = candidate;
				return true;
			}
		}

		accepted = null!;
		return false;
	}

	private static bool TryNavigateAroundObstacles(
		SurfaceMinePlan template,
		WorldPlan worldPlan,
		GenerationManifest manifest,
		bool allowShortening,
		out SurfaceMinePlan accepted,
		out string reason)
	{
		List<MineSection> sections = [];
		Dictionary<Point, Point> movedSectionCenters = [];
		HashSet<Point> omittedSectionCenters = [];
		bool geometryChanged = false;

		foreach (MineSection section in template.Sections.OrderBy(section => section.Id)) {
			if (TryFitSection(
				section,
				worldPlan,
				manifest,
				sections,
				allowShortening,
				template.FeatureSeed,
				out MineSection fitted)) {
				sections.Add(fitted);
				movedSectionCenters[section.Center] = fitted.Center;
				geometryChanged |= fitted.Area != section.Area || fitted.Center != section.Center;
				continue;
			}

			if (!allowShortening || section.Kind == MineSectionKind.Workyard) {
				accepted = null!;
				reason = $"section {section.Id} obstacle exhaustion";
				return false;
			}
			omittedSectionCenters.Add(section.Center);
			geometryChanged = true;
		}

		if (sections.Count(section => section.Kind == MineSectionKind.Working) < 1
			|| sections.Count < 4) {
			accepted = null!;
			reason = "shortened district exhaustion";
			return false;
		}

		Rectangle navigationBounds = template.Area;
		foreach (MineSection section in sections) {
			navigationBounds = Rectangle.Union(navigationBounds, section.Area);
		}
		navigationBounds.Inflate(190, 230);
		navigationBounds = ClampToWorld(navigationBounds, 24);
		MineObstacleField obstacles = new(navigationBounds, worldPlan, manifest, sections);

		Dictionary<Point, Point> movedNodes = new(movedSectionCenters);
		foreach (Point node in template.Routes
			.SelectMany(route => new[] { route.Start, route.End })
			.Distinct()) {
			if (movedNodes.ContainsKey(node)) {
				continue;
			}
			if (!TryFitRouteNode(node, obstacles, out Point fittedNode)) {
				if (allowShortening && omittedSectionCenters.Contains(node)) {
					continue;
				}
				accepted = null!;
				reason = "rail node obstacle exhaustion";
				return false;
			}
			movedNodes[node] = fittedNode;
			geometryChanged |= fittedNode != node;
		}

		List<MineRoute> routes = [];
		int detouredRoutes = 0;
		int brokenRoutes = 0;
		HashSet<Point> disconnectedSectionCenters = [];
		for (int routeIndex = 0; routeIndex < template.Routes.Count; routeIndex++) {
			MineRoute routeTemplate = template.Routes[routeIndex];
			if (!movedNodes.TryGetValue(routeTemplate.Start, out Point start)
				|| !movedNodes.TryGetValue(routeTemplate.End, out Point end)) {
				if (allowShortening && !routeTemplate.Required) {
					disconnectedSectionCenters.Add(routeTemplate.End);
					continue;
				}
				accepted = null!;
				reason = "rail endpoint obstacle exhaustion";
				return false;
			}

			IReadOnlyList<Rectangle> routeExclusions = BuildRouteExclusions(
				routeIndex,
				start,
				end,
				sections,
				manifest);
			if (TryBuildNavigatedRoute(
				routeTemplate,
				start,
				end,
				obstacles,
				routeExclusions,
				out MineRoute route,
				out bool detoured)) {
				routes.Add(route);
				detouredRoutes += detoured ? 1 : 0;
				geometryChanged |= detoured || start != routeTemplate.Start || end != routeTemplate.End;
				continue;
			}

			if (routeTemplate.Required || !allowShortening
				|| !TryBuildBrokenRoute(
					routeTemplate,
					start,
					end,
					obstacles,
					routeExclusions,
					out MineRoute brokenRoute)) {
				accepted = null!;
				reason = routeTemplate.Required
					? "required rail obstacle exhaustion"
					: "optional rail obstacle exhaustion";
				return false;
			}

			routes.Add(brokenRoute);
			brokenRoutes++;
			disconnectedSectionCenters.Add(routeTemplate.End);
			geometryChanged = true;
		}

		sections.RemoveAll(section => disconnectedSectionCenters.Contains(
			template.Sections.First(original => original.Id == section.Id).Center));
		int gravityTransfers = routes.Count(route => route.HasGravityTransfer);
		int launchTransfers = routes.Count(route => route.HasJumpTransfer);
		if ((!allowShortening && (gravityTransfers < 3 || launchTransfers < 1))
			|| allowShortening && (gravityTransfers < 1 || launchTransfers < 1)) {
			accepted = null!;
			reason = "rail transfer obstacle exhaustion";
			return false;
		}

		Rectangle area = MineArea(sections, routes);
		MinePlanKind kind = brokenRoutes > 0 || sections.Count < template.Sections.Count
			? MinePlanKind.Shortened
			: geometryChanged ? MinePlanKind.Rerouted : MinePlanKind.Complete;
		Point entrance = movedNodes[template.Entrance];
		accepted = new SurfaceMinePlan(
			template.FeatureSeed,
			area,
			entrance,
			sections,
			routes,
			CaptureRouteThemes(routes),
			kind,
			detouredRoutes,
			brokenRoutes);
		reason = string.Empty;
		return true;
	}

	private static bool TryFitSection(
		MineSection template,
		WorldPlan worldPlan,
		GenerationManifest manifest,
		IReadOnlyList<MineSection> acceptedSections,
		bool allowShortening,
		int featureSeed,
		out MineSection accepted)
	{
		ReadOnlySpan<int> scales = allowShortening ? [100, 90, 82] : [100, 94, 88];
		int horizontalSign = Noise(featureSeed, template.Id, 0x5345_4358) % 2 == 0 ? 1 : -1;
		int verticalSign = Noise(featureSeed, template.Id, 0x5345_4359) % 2 == 0 ? 1 : -1;
		foreach (int scale in scales) {
			int width = Math.Max(68, template.Area.Width * scale / 100);
			int height = Math.Max(34, template.Area.Height * scale / 100);
			int maximumRing = template.Kind == MineSectionKind.Workyard ? 0 : allowShortening ? 7 : 5;
			for (int ring = 0; ring <= maximumRing; ring++) {
				for (int gridY = -ring; gridY <= ring; gridY++) {
					for (int gridX = -ring; gridX <= ring; gridX++) {
						if (Math.Max(Math.Abs(gridX), Math.Abs(gridY)) != ring) {
							continue;
						}
						int offsetX = gridX * 22 * horizontalSign;
						int offsetY = gridY * 16 * verticalSign;
						Point center = template.Center + new Point(offsetX, offsetY);
						Rectangle area = template.Kind == MineSectionKind.Workyard
							? new Rectangle(template.Area.X, template.Area.Y, width, height)
							: Centered(center.X, center.Y, width, height);
						MineSection candidate = CreateSection(template.Id, template.Kind, area, center);
						if (IsSectionPlacementSafe(candidate, worldPlan, manifest, acceptedSections)) {
							accepted = candidate;
							return true;
						}
					}
				}
			}
		}

		accepted = default;
		return false;
	}

	private static bool IsSectionPlacementSafe(
		MineSection section,
		WorldPlan worldPlan,
		GenerationManifest manifest,
		IReadOnlyList<MineSection> acceptedSections)
	{
		Rectangle spacing = section.Area;
		spacing.Inflate(10, 8);
		if (acceptedSections.Any(other => spacing.Intersects(other.Area))
			|| !TileEditor.IsSafeForTerrainFeature(section.Area)
			|| !TileEditor.IsClearOfTempleAndDungeon(section.Area, margin: 36)
			|| MountainBiomeGenerator.IntersectsBridgePassage(worldPlan, section.Area)) {
			return false;
		}

		if (manifest.ForestLakeBridges.Any(bridge => bridge.Area.Intersects(section.Area))) {
			return false;
		}
		if (manifest.MountainWaters.Any(water => {
			Rectangle clearance = water.Area;
			clearance.Inflate(5, CorridorHeadroom + 5);
			return clearance.Intersects(section.Area);
		})) {
			return false;
		}

		if (section.Kind != MineSectionKind.Workyard) {
			return true;
		}
		Rectangle workyardBuffer = section.Area;
		workyardBuffer.Inflate(24, 12);
		return !manifest.BiomeTransitions.Any(transition => transition.Area.Intersects(workyardBuffer))
			&& !manifest.Terraces.Any(terrace => {
				Rectangle terraceBuffer = terrace.Area;
				terraceBuffer.Inflate(12, 10);
				return terraceBuffer.Intersects(workyardBuffer);
			});
	}

	private static bool TryFitRouteNode(Point desired, MineObstacleField obstacles, out Point accepted)
	{
		for (int ring = 0; ring <= 8; ring++) {
			for (int gridY = -ring; gridY <= ring; gridY++) {
				for (int gridX = -ring; gridX <= ring; gridX++) {
					if (Math.Max(Math.Abs(gridX), Math.Abs(gridY)) != ring) {
						continue;
					}
					Point candidate = desired + new Point(gridX * 8, gridY * 8);
					if (obstacles.IsRouteCenterSafe(candidate, [])) {
						accepted = candidate;
						return true;
					}
				}
			}
		}

		accepted = default;
		return false;
	}

	private static IReadOnlyList<Rectangle> BuildRouteExclusions(
		int routeIndex,
		Point start,
		Point end,
		IReadOnlyList<MineSection> sections,
		GenerationManifest manifest)
	{
		List<Rectangle> exclusions = [];
		MineSection? quarantine = sections
			.Where(section => section.Kind == MineSectionKind.SealedEvil)
			.Select<MineSection, MineSection?>(section => section)
			.FirstOrDefault();
		if (quarantine is MineSection sealedSection
			&& start != sealedSection.Center
			&& end != sealedSection.Center) {
			Rectangle clearance = sealedSection.Area;
			clearance.Inflate(3, 3);
			exclusions.Add(clearance);
		}
		if (routeIndex == 0) {
			foreach (BuildTerrace terrace in manifest.Terraces) {
				Rectangle clearance = terrace.Area;
				clearance.Inflate(12, 10);
				exclusions.Add(clearance);
			}
		}
		return exclusions;
	}

	private static bool TryBuildNavigatedRoute(
		MineRoute template,
		Point start,
		Point end,
		MineObstacleField obstacles,
		IReadOnlyList<Rectangle> exclusions,
		out MineRoute accepted,
		out bool detoured)
	{
		if (!TryCreateRouteShape(template, start, end, includeTransfers: true, out MineRoute nominal)) {
			accepted = default;
			detoured = false;
			return false;
		}
		if (HasValidTrackGrade(nominal.Centerline)
			&& obstacles.IsRouteSafe(nominal.Centerline, exclusions)) {
			accepted = nominal;
			detoured = start != template.Start || end != template.End;
			return true;
		}

		if (!TryCreateRouteShape(template, start, end, includeTransfers: false, out MineRoute baseRoute)
			|| !TryFindObstacleAwarePath(baseRoute.Centerline, obstacles, exclusions, out IReadOnlyList<Point> path)) {
			accepted = default;
			detoured = false;
			return false;
		}

		int jumpStart = -1;
		int jumpGap = 0;
		int dropStart = -1;
		int dropGap = 0;
		int dropDepth = 0;
		if (template.HasJumpTransfer) {
			if (!TryInstallLaunchTransfer(
				path,
				ScaleRouteIndex(template.JumpStartIndex, template.Centerline.Count, path.Count),
				template.JumpGapLength,
				obstacles,
				exclusions,
				out path,
				out jumpStart)) {
				accepted = default;
				detoured = false;
				return false;
			}
			jumpGap = template.JumpGapLength;
		}
		else if (template.HasGravityTransfer
			&& TryInstallGravityTransfer(
				path,
				ScaleRouteIndex(template.DropStartIndex, template.Centerline.Count, path.Count),
				template.DropGapLength,
				template.DropDepth,
				obstacles,
				exclusions,
				out IReadOnlyList<Point> gravityPath,
				out int installedDropStart)) {
			path = gravityPath;
			dropStart = installedDropStart;
			dropGap = template.DropGapLength;
			dropDepth = template.DropDepth;
		}

		accepted = new MineRoute(
			start,
			end,
			true,
			template.Required,
			template.Profile,
			template.VariationSeed,
			path,
			jumpStart,
			jumpGap,
			dropStart,
			dropGap,
			dropDepth);
		detoured = true;
		return true;
	}

	private static bool TryCreateRouteShape(
		MineRoute template,
		Point start,
		Point end,
		bool includeTransfers,
		out MineRoute route)
	{
		int steps = Math.Abs(end.X - start.X);
		if (steps < 2 || Math.Abs(end.Y - start.Y) > steps) {
			route = default;
			return false;
		}
		int jumpGap = includeTransfers && template.HasJumpTransfer ? template.JumpGapLength : 0;
		int jumpStart = jumpGap > 0
			? Math.Clamp(ScaleRouteIndex(template.JumpStartIndex, template.Centerline.Count, steps + 1), 24, steps - jumpGap - 24)
			: -1;
		int dropGap = includeTransfers && template.HasGravityTransfer ? template.DropGapLength : 0;
		int dropDepth = dropGap > 0 ? template.DropDepth : 0;
		int dropStart = dropGap > 0
			? Math.Clamp(ScaleRouteIndex(template.DropStartIndex, template.Centerline.Count, steps + 1), 18, steps - dropGap - 20)
			: -1;
		try {
			IReadOnlyList<Point> centerline = BuildRouteCenterline(
				start,
				end,
				template.Profile,
				template.VariationSeed,
				jumpStart,
				jumpGap,
				dropStart,
				dropGap,
				dropDepth);
			route = new MineRoute(
				start,
				end,
				true,
				template.Required,
				template.Profile,
				template.VariationSeed,
				centerline,
				jumpStart,
				jumpGap,
				dropStart,
				dropGap,
				dropDepth);
			return true;
		}
		catch (InvalidOperationException) {
			route = default;
			return false;
		}
	}

	private static int ScaleRouteIndex(int index, int oldCount, int newCount) =>
		index < 0 ? -1 : Math.Clamp(index * Math.Max(1, newCount - 1) / Math.Max(1, oldCount - 1), 0, newCount - 1);

	private static bool TryFindObstacleAwarePath(
		IReadOnlyList<Point> nominal,
		MineObstacleField obstacles,
		IReadOnlyList<Rectangle> exclusions,
		out IReadOnlyList<Point> accepted)
	{
		if (TryFindObstacleAwarePath(nominal, obstacles, exclusions, maximumDetour: 96, out accepted)) {
			return true;
		}
		return TryFindObstacleAwarePath(nominal, obstacles, exclusions, maximumDetour: 180, out accepted);
	}

	private static bool TryFindObstacleAwarePath(
		IReadOnlyList<Point> nominal,
		MineObstacleField obstacles,
		IReadOnlyList<Rectangle> exclusions,
		int maximumDetour,
		out IReadOnlyList<Point> accepted)
	{
		Point start = nominal[0];
		Point end = nominal[^1];
		int steps = nominal.Count - 1;
		int top = Math.Max(obstacles.Bounds.Top + CorridorHeadroom + 3, nominal.Min(point => point.Y) - maximumDetour);
		int bottom = Math.Min(obstacles.Bounds.Bottom - 5, nominal.Max(point => point.Y) + maximumDetour);
		if (start.Y < top || start.Y > bottom || end.Y < top || end.Y > bottom) {
			accepted = [];
			return false;
		}

		const int gradeStates = 3;
		const int unreachable = int.MaxValue / 8;
		int height = bottom - top + 1;
		int stateCount = height * gradeStates;
		int[] previousCosts = new int[stateCount];
		int[] currentCosts = new int[stateCount];
		Array.Fill(previousCosts, unreachable);
		sbyte[] previousGrades = new sbyte[(steps + 1) * stateCount];
		Array.Fill(previousGrades, (sbyte)-1);
		previousCosts[(start.Y - top) * gradeStates + 1] = 0;

		int direction = Math.Sign(end.X - start.X);
		for (int step = 1; step <= steps; step++) {
			Array.Fill(currentCosts, unreachable);
			int x = start.X + direction * step;
			int minimumReachableY = Math.Max(top, end.Y - (steps - step));
			int maximumReachableY = Math.Min(bottom, end.Y + (steps - step));
			for (int y = minimumReachableY; y <= maximumReachableY; y++) {
				Point candidate = new(x, y);
				if ((step != steps || candidate != end)
					&& !obstacles.IsRouteCenterSafe(candidate, exclusions)) {
					continue;
				}
				if (step == steps && candidate != end) {
					continue;
				}

				for (int gradeIndex = 0; gradeIndex < gradeStates; gradeIndex++) {
					int grade = gradeIndex - 1;
					int priorY = y - grade;
					if (priorY < top || priorY > bottom) {
						continue;
					}
					int bestCost = unreachable;
					int bestPriorGrade = -1;
					for (int priorGradeIndex = 0; priorGradeIndex < gradeStates; priorGradeIndex++) {
						int priorCost = previousCosts[(priorY - top) * gradeStates + priorGradeIndex];
						if (priorCost >= unreachable) {
							continue;
						}
						int priorGrade = priorGradeIndex - 1;
						int turnCost = priorGrade == grade ? 0 : priorGrade == -grade ? 44 : 16;
						int cost = priorCost + turnCost;
						if (cost < bestCost) {
							bestCost = cost;
							bestPriorGrade = priorGradeIndex;
						}
					}
					if (bestPriorGrade < 0) {
						continue;
					}
					int deviation = Math.Abs(y - nominal[step].Y);
					int state = (y - top) * gradeStates + gradeIndex;
					currentCosts[state] = bestCost + deviation * 5 + (grade == 0 ? 0 : 2);
					previousGrades[step * stateCount + state] = (sbyte)bestPriorGrade;
				}
			}
			(previousCosts, currentCosts) = (currentCosts, previousCosts);
		}

		int endStateBase = (end.Y - top) * gradeStates;
		int finalGrade = -1;
		int finalCost = unreachable;
		for (int gradeIndex = 0; gradeIndex < gradeStates; gradeIndex++) {
			if (previousCosts[endStateBase + gradeIndex] < finalCost) {
				finalCost = previousCosts[endStateBase + gradeIndex];
				finalGrade = gradeIndex;
			}
		}
		if (finalGrade < 0) {
			accepted = [];
			return false;
		}

		Point[] path = new Point[steps + 1];
		int currentY = end.Y;
		int currentGrade = finalGrade;
		for (int step = steps; step >= 1; step--) {
			path[step] = new Point(start.X + direction * step, currentY);
			int state = (currentY - top) * gradeStates + currentGrade;
			int priorGrade = previousGrades[step * stateCount + state];
			if (priorGrade < 0) {
				accepted = [];
				return false;
			}
			currentY -= currentGrade - 1;
			currentGrade = priorGrade;
		}
		path[0] = start;
		accepted = path;
		return obstacles.IsRouteSafe(path, exclusions);
	}

	private static bool TryInstallLaunchTransfer(
		IReadOnlyList<Point> path,
		int preferredStart,
		int gapLength,
		MineObstacleField obstacles,
		IReadOnlyList<Rectangle> exclusions,
		out IReadOnlyList<Point> accepted,
		out int acceptedStart)
	{
		foreach (int start in TransferCandidateIndexes(path.Count, preferredStart, 24, gapLength + 25)) {
			List<Point> candidate = path.ToList();
			try {
				ApplyLaunchTransfer(candidate, start, gapLength);
			}
			catch (InvalidOperationException) {
				continue;
			}
			if (HasValidTrackGrade(candidate) && obstacles.IsRouteSafe(candidate, exclusions)) {
				accepted = candidate;
				acceptedStart = start;
				return true;
			}
		}

		accepted = [];
		acceptedStart = -1;
		return false;
	}

	private static bool TryInstallGravityTransfer(
		IReadOnlyList<Point> path,
		int preferredStart,
		int gapLength,
		int dropDepth,
		MineObstacleField obstacles,
		IReadOnlyList<Rectangle> exclusions,
		out IReadOnlyList<Point> accepted,
		out int acceptedStart)
	{
		foreach (int start in TransferCandidateIndexes(path.Count, preferredStart, 18, gapLength + 21)) {
			List<Point> candidate = path.ToList();
			try {
				ApplyGravityTransfer(candidate, start, gapLength, dropDepth);
			}
			catch (InvalidOperationException) {
				continue;
			}
			if (HasValidTrackGrade(candidate) && obstacles.IsRouteSafe(candidate, exclusions)) {
				accepted = candidate;
				acceptedStart = start;
				return true;
			}
		}

		accepted = [];
		acceptedStart = -1;
		return false;
	}

	private static IEnumerable<int> TransferCandidateIndexes(
		int pathCount,
		int preferred,
		int minimum,
		int trailingClearance)
	{
		int maximum = pathCount - trailingClearance - 1;
		if (maximum < minimum) {
			yield break;
		}
		preferred = Math.Clamp(preferred, minimum, maximum);
		yield return preferred;
		for (int distance = 12; distance <= Math.Max(preferred - minimum, maximum - preferred); distance += 12) {
			if (preferred - distance >= minimum) {
				yield return preferred - distance;
			}
			if (preferred + distance <= maximum) {
				yield return preferred + distance;
			}
		}
	}

	private static bool HasValidTrackGrade(IReadOnlyList<Point> path)
	{
		for (int index = 1; index < path.Count; index++) {
			if (Math.Abs(path[index].X - path[index - 1].X) != 1
				|| Math.Abs(path[index].Y - path[index - 1].Y) > 1) {
				return false;
			}
		}
		return true;
	}

	private static bool TryBuildBrokenRoute(
		MineRoute template,
		Point start,
		Point end,
		MineObstacleField obstacles,
		IReadOnlyList<Rectangle> exclusions,
		out MineRoute accepted)
	{
		if (!TryCreateRouteShape(template, start, end, includeTransfers: false, out MineRoute nominal)) {
			accepted = default;
			return false;
		}
		List<Point> prefix = [];
		foreach (Point point in nominal.Centerline) {
			if (!obstacles.IsRouteCenterSafe(point, exclusions)) {
				break;
			}
			prefix.Add(point);
		}
		if (prefix.Count < 36) {
			accepted = default;
			return false;
		}

		Point brokenEnd = prefix[^1];
		accepted = new MineRoute(
			start,
			brokenEnd,
			true,
			Required: false,
			MineRailProfile.RollingGrades,
			template.VariationSeed,
			prefix,
			JumpStartIndex: -1,
			JumpGapLength: 0,
			DropStartIndex: -1,
			DropGapLength: 0,
			DropDepth: 0);
		return true;
	}

	private static Rectangle MineArea(IReadOnlyList<MineSection> sections, IReadOnlyList<MineRoute> routes)
	{
		int left = Math.Max(30, Math.Min(sections.Min(section => section.Area.Left), routes.Min(route => route.Centerline.Min(point => point.X))) - 20);
		int right = Math.Min(Main.maxTilesX - 31, Math.Max(sections.Max(section => section.Area.Right), routes.Max(route => route.Centerline.Max(point => point.X) + 1)) + 20);
		int top = Math.Max(40, Math.Min(sections.Min(section => section.Area.Top), routes.Min(route => route.Centerline.Min(point => point.Y))) - 20);
		int bottom = Math.Min(Main.UnderworldLayer - 80, Math.Max(sections.Max(section => section.Area.Bottom), routes.Max(route => route.Centerline.Max(point => point.Y) + 1)) + 24);
		return new Rectangle(left, top, right - left, bottom - top);
	}

	private static Rectangle ClampToWorld(Rectangle area, int padding)
	{
		int left = Math.Max(padding, area.Left);
		int top = Math.Max(padding, area.Top);
		int right = Math.Min(Main.maxTilesX - padding, area.Right);
		int bottom = Math.Min(Main.maxTilesY - padding, area.Bottom);
		return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
	}

	private sealed class MineObstacleField
	{
		private readonly WorldPlan _worldPlan;
		private readonly int _stride;
		private readonly int[] _unsafePrefix;
		private readonly int[] _templeDungeonPrefix;
		private readonly IReadOnlyList<Rectangle> _commonExclusions;

		public Rectangle Bounds { get; }

		public MineObstacleField(
			Rectangle bounds,
			WorldPlan worldPlan,
			GenerationManifest manifest,
			IReadOnlyList<MineSection> mineSections)
		{
			Bounds = bounds;
			_worldPlan = worldPlan;
			_stride = bounds.Width + 1;
			_unsafePrefix = new int[(bounds.Width + 1) * (bounds.Height + 1)];
			_templeDungeonPrefix = new int[_unsafePrefix.Length];
			for (int localY = 1; localY <= bounds.Height; localY++) {
				int unsafeRow = 0;
				int templeRow = 0;
				int worldY = bounds.Top + localY - 1;
				for (int localX = 1; localX <= bounds.Width; localX++) {
					int worldX = bounds.Left + localX - 1;
					Tile tile = Main.tile[worldX, worldY];
					bool ownedSection = mineSections.Any(section => section.Area.Contains(worldX, worldY));
					bool wired = tile.RedWire || tile.BlueWire || tile.GreenWire || tile.YellowWire || tile.HasActuator;
					bool unsafeTile = TileEditor.IsProgressionTile(tile)
						|| wired
						|| !ownedSection && tile.HasTile && Main.tileFrameImportant[tile.TileType];
					bool templeDungeon = TileEditor.IsTempleOrDungeonCell(tile);
					unsafeRow += unsafeTile ? 1 : 0;
					templeRow += templeDungeon ? 1 : 0;
					int index = localY * _stride + localX;
					_unsafePrefix[index] = _unsafePrefix[index - _stride] + unsafeRow;
					_templeDungeonPrefix[index] = _templeDungeonPrefix[index - _stride] + templeRow;
				}
			}

			List<Rectangle> exclusions = manifest.ForestLakeBridges
				.Select(bridge => bridge.Area)
				.ToList();
			foreach (MountainWaterRecord water in manifest.MountainWaters) {
				Rectangle clearance = water.Area;
				clearance.Inflate(5, CorridorHeadroom + 5);
				exclusions.Add(clearance);
			}
			if (GenVars.tRight > GenVars.tLeft && GenVars.tBottom > GenVars.tTop) {
				exclusions.Add(new Rectangle(
					GenVars.tLeft - 28,
					GenVars.tTop - 28,
					GenVars.tRight - GenVars.tLeft + 57,
					GenVars.tBottom - GenVars.tTop + 57));
			}
			_commonExclusions = exclusions;
		}

		public bool IsRouteSafe(IReadOnlyList<Point> path, IReadOnlyList<Rectangle> exclusions)
		{
			foreach (Point point in path) {
				if (!IsRouteCenterSafe(point, exclusions)) {
					return false;
				}
			}
			return true;
		}

		public bool IsRouteCenterSafe(Point center, IReadOnlyList<Rectangle> exclusions)
		{
			Rectangle corridor = new(
				center.X - 3,
				center.Y - CorridorHeadroom - 2,
				7,
				CorridorHeadroom + 6);
			if (!Bounds.Contains(corridor.Left, corridor.Top)
				|| !Bounds.Contains(corridor.Right - 1, corridor.Bottom - 1)
				|| Count(_unsafePrefix, corridor) > 0) {
				return false;
			}

			Rectangle broadTempleCheck = corridor;
			broadTempleCheck.Inflate(28, 28);
			if (Count(_templeDungeonPrefix, broadTempleCheck) > 0
				|| _commonExclusions.Any(area => area.Intersects(corridor))
				|| exclusions.Any(area => area.Intersects(corridor))
				|| MountainBiomeGenerator.IntersectsBridgePassage(_worldPlan, corridor)) {
				return false;
			}
			return true;
		}

		private int Count(int[] prefix, Rectangle worldArea)
		{
			Rectangle clipped = Rectangle.Intersect(worldArea, Bounds);
			if (clipped.Width <= 0 || clipped.Height <= 0) {
				return 0;
			}
			int left = clipped.Left - Bounds.Left;
			int right = clipped.Right - Bounds.Left;
			int top = clipped.Top - Bounds.Top;
			int bottom = clipped.Bottom - Bounds.Top;
			return prefix[bottom * _stride + right]
				- prefix[top * _stride + right]
				- prefix[bottom * _stride + left]
				+ prefix[top * _stride + left];
		}
	}

	public static void Excavate(
		SurfaceMinePlan plan,
		WorldPlan worldPlan,
		GenerationManifest manifest,
		GenerationProgress progress)
	{
		if (!CanPlace(plan, worldPlan, manifest, out _)) {
			throw new InvalidOperationException("The reserved surface mine became unsafe before excavation; a progression structure entered its clearance envelope.");
		}
		for (int index = 0; index < plan.Sections.Count; index++) {
			BuildSection(plan.Sections[index]);
			progress.Set((double)(index + 1) / (plan.Sections.Count + plan.Routes.Count));
		}

		for (int index = 0; index < plan.Routes.Count; index++) {
			CarveRoute(plan, plan.Routes[index]);
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

		OwnTrackGraph(plan, plannedTrack);
		foreach (MineSection section in plan.Sections) {
			int displayFurniture = BuildLowerWorkDisplay(section, plannedTrack);
			if (displayFurniture < 2 && section.Kind is MineSectionKind.Working or MineSectionKind.MountainRail) {
				displayFurniture = BuildCompactWorkAlcove(section, plannedTrack);
			}
			furnitureCount += displayFurniture;
		}
		MineSection workyard = plan.Sections.First(section => section.Kind == MineSectionKind.Workyard);
		furnitureCount += BuildWorkyardLoft(workyard);

		RefillFloodedSection(plan);
		// Furnishings, quarantine gates, and display decks are authored after the
		// first rail pass. Give the connected graph final ownership of its exact
		// cells and clearance envelope so no district can sever a branch.
		OwnTrackGraph(plan, plannedTrack);
		BuildQuarantineGates(plan);
		RepairTrackClearance(plannedTrack);
		RestoreGravityTransferClearance(plan);
		RestoreTrackCells(plan, plannedTrack);

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
			connectedRoutes,
			plan.Kind,
			plan.DetouredRouteCount,
			plan.BrokenRouteCount);
	}

	public static void RepairTrackGraph(SurfaceMinePlan plan, GenerationManifest manifest)
	{
		HashSet<Point> plannedTrack = plan.Routes
			.Where(route => route.HasTrack)
			.SelectMany(Rasterize)
			.ToHashSet();
		// Vanilla cleanup and late micro-biomes can refill the broad surface mouth
		// after its first excavation. Recut the headframe approach at the final mine
		// ownership boundary, then let the rail graph reclaim the entrance track.
		BuildHeadframe(plan.Entrance);
		OwnTrackGraph(plan, plannedTrack);
		foreach (MineSection section in plan.Sections.Where(section =>
			section.Kind is MineSectionKind.Working or MineSectionKind.MountainRail)) {
			if (BuildLowerWorkDisplay(section, plannedTrack) < 2) {
				BuildCompactWorkAlcove(section, plannedTrack);
			}
		}
		BuildQuarantineGates(plan);
		RepairTrackClearance(plannedTrack);
		RestoreGravityTransferClearance(plan);
		RestoreTrackCells(plan, plannedTrack);

		SurfaceMineRecord existing = manifest.SurfaceMine
			?? throw new InvalidOperationException("The surface mine cannot be repaired before it is furnished.");
		manifest.SurfaceMine = existing with {
			TrackTiles = plannedTrack.Count(point => HasTile(point.X, point.Y, TileID.MinecartTrack)),
			SupportTiles = CountTiles(plan.Area, TileID.WoodenBeam),
			ConnectedRouteCount = plan.Routes.Count(route => route.Required && RouteSurvived(route))
		};
	}

	private static void OwnTrackGraph(SurfaceMinePlan plan, HashSet<Point> plannedTrack)
	{
		FinishBiomeWalls(plan);
		foreach (MineRoute route in plan.Routes.Where(route => route.HasJumpTransfer)) {
			CarveJumpTransfer(plan, route);
		}
		foreach (MineRoute route in plan.Routes.Where(route => route.HasGravityTransfer)) {
			CarveGravityTransfer(plan, route);
		}
		foreach (Point point in plannedTrack) {
			PrepareTrackCell(plan, point, plannedTrack);
		}
		foreach (Point point in plannedTrack) {
			TileEditor.TryPlaceMinecartTrack(point.X, point.Y);
		}
		foreach (Point point in plannedTrack) {
			if (HasTile(point.X, point.Y, TileID.MinecartTrack)) {
				Minecart.FrameTrack(point.X, point.Y, pound: false, mute: true);
			}
		}
		ConfigureRailEndpoints(plan);
		BuildRouteSupports(plan, plannedTrack);
	}

	private static void RestoreTrackCells(SurfaceMinePlan plan, HashSet<Point> plannedTrack)
	{
		foreach (Point point in plannedTrack) {
			if (!HasTile(point.X, point.Y, TileID.MinecartTrack)) {
				TileEditor.ClearTerrain(point.X, point.Y);
				TileEditor.TryPlaceMinecartTrack(point.X, point.Y);
			}
		}
		foreach (Point point in plannedTrack) {
			if (HasTile(point.X, point.Y, TileID.MinecartTrack)) {
				Minecart.FrameTrack(point.X, point.Y, pound: false, mute: true);
			}
		}
		ConfigureRailEndpoints(plan);
	}

	private static void RepairTrackClearance(HashSet<Point> plannedTrack)
	{
		foreach (Point point in plannedTrack) {
			for (int offsetX = -1; offsetX <= 1; offsetX++) {
				for (int offsetY = -CorridorHeadroom; offsetY < 0; offsetY++) {
					int x = point.X + offsetX;
					int y = point.Y + offsetY;
					if (TileEditor.IsSolid(x, y)) {
						TileEditor.ClearTerrain(x, y);
					}
				}
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
			CreateRoute(random, p0, upperEast, required: true, forcedProfile: MineRailProfile.DipAndRise),
			CreateRoute(random, upperEast, upperJunction, required: true),
			CreateRoute(random, upperJunction, upperWest, required: true),
			CreateRoute(random, upperWest, middleJunction, required: true, forcedProfile: MineRailProfile.RollingGrades),
			CreateRoute(random, upperJunction, middleEast, required: true),
			CreateRoute(random, middleEast, middleJunction, required: true),
			CreateRoute(random, middleJunction, middleWest, required: true),
			CreateRoute(random, middleWest, deepJunction, required: true, forcedProfile: MineRailProfile.DipAndRise),
			CreateRoute(random, middleJunction, deepEast, required: true),
			CreateRoute(random, deepEast, deepJunction, required: true),
			CreateRoute(random, deepJunction, deepWest, required: true)
		];

		List<MineSection> sections = [
			CreateSection(0, MineSectionKind.Workyard, Centered(p0.X + 20, p0.Y - 8, random.Next(68, 83), random.Next(34, 42)), p0),
			CreateSection(1, MineSectionKind.Working, Centered(upperEast.X, upperEast.Y, random.Next(74, 91), random.Next(38, 47)), upperEast),
			CreateSection(2, MineSectionKind.MountainRail, Centered(upperWest.X, upperWest.Y, random.Next(82, 101), random.Next(40, 51)), upperWest),
			CreateSection(3, MineSectionKind.Working, Centered(middleJunction.X, middleJunction.Y, random.Next(88, 107), random.Next(42, 53)), middleJunction),
			CreateSection(4, MineSectionKind.Working, Centered(deepJunction.X, deepJunction.Y, random.Next(96, 117), random.Next(44, 57)), deepJunction)
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
		sections.Add(CreateSection(5, MineSectionKind.Flooded, Centered(floodedCenter.X, floodedCenter.Y, random.Next(78, 95), random.Next(40, 51)), floodedCenter));
		sections.Add(CreateSection(6, MineSectionKind.Collapsed, Centered(collapsedCenter.X, collapsedCenter.Y, random.Next(82, 101), random.Next(39, 50)), collapsedCenter));
		sections.Add(new MineSection(7, MineSectionKind.SealedEvil, Centered(evilCenter.X, evilCenter.Y, random.Next(90, 109), random.Next(47, 59)), evilCenter, BiomeKind.Evil));
		routes.Add(CreateRoute(random, floodedOrigin, floodedCenter, required: false, forcedProfile: MineRailProfile.RollingGrades));
		// The collapsed spur always contains one short launch-and-landing transfer.
		// It is optional to the progression graph, but is guaranteed as a mine motif.
		routes.Add(CreateRoute(random, collapsedOrigin, collapsedCenter, required: false, forcedProfile: MineRailProfile.LaunchTransfer));
		routes.Add(CreateRoute(
			random,
			evilOrigin,
			evilCenter,
			required: false,
			forcedProfile: MineRailProfile.TerracedGrades,
			allowGravityTransfer: false));

		int left = Math.Max(30, Math.Min(sections.Min(section => section.Area.Left), routes.Min(route => route.Centerline.Min(point => point.X))) - 20);
		int right = Math.Min(Main.maxTilesX - 31, Math.Max(sections.Max(section => section.Area.Right), routes.Max(route => route.Centerline.Max(point => point.X) + 1)) + 20);
		int top = Math.Max(40, Math.Min(sections.Min(section => section.Area.Top), routes.Min(route => route.Centerline.Min(point => point.Y))) - 20);
		int bottom = Math.Min(Main.UnderworldLayer - 80, Math.Max(sections.Max(section => section.Area.Bottom), routes.Max(route => route.Centerline.Max(point => point.Y) + 1)) + 24);
		Rectangle area = new(left, top, right - left, bottom - top);
		return new SurfaceMinePlan(
			featureSeed,
			area,
			p0,
			sections,
			routes,
			CaptureRouteThemes(routes),
			MinePlanKind.Complete,
			DetouredRouteCount: 0,
			BrokenRouteCount: 0);
	}

	private static MineSection CreateSection(int id, MineSectionKind kind, Rectangle area, Point center) =>
		new(id, kind, area, center, BiomeClassifier.ClassifyAreaTheme(center.X, center.Y));

	private static MineRoute CreateRoute(
		UnifiedRandom random,
		Point start,
		Point end,
		bool required,
		MineRailProfile? forcedProfile = null,
		bool allowGravityTransfer = true)
	{
		MineRailProfile profile = forcedProfile ?? (MineRailProfile)random.Next(3);
		int variationSeed = random.Next();
		int steps = Math.Abs(end.X - start.X);
		int jumpGapLength = profile == MineRailProfile.LaunchTransfer ? random.Next(4, 7) : 0;
		int jumpStartIndex = profile == MineRailProfile.LaunchTransfer
			? Math.Clamp(steps * random.Next(43, 63) / 100, 24, steps - jumpGapLength - 24)
			: -1;
		bool descendsToAnotherLevel = allowGravityTransfer
			&& profile != MineRailProfile.LaunchTransfer
			&& end.Y - start.Y >= 24
			&& steps >= 64;
		int dropGapLength = descendsToAnotherLevel ? random.Next(2, 4) : 0;
		int dropDepth = descendsToAnotherLevel ? random.Next(3, 5) : 0;
		int dropStartIndex = descendsToAnotherLevel
			? Math.Clamp(steps * random.Next(42, 69) / 100, 18, steps - dropGapLength - 20)
			: -1;
		IReadOnlyList<Point> centerline = BuildRouteCenterline(
			start,
			end,
			profile,
			variationSeed,
			jumpStartIndex,
			jumpGapLength,
			dropStartIndex,
			dropGapLength,
			dropDepth);
		return new MineRoute(
			start,
			end,
			true,
			required,
			profile,
			variationSeed,
			centerline,
			jumpStartIndex,
			jumpGapLength,
			dropStartIndex,
			dropGapLength,
			dropDepth);
	}

	private static IReadOnlyList<Point> BuildRouteCenterline(
		Point start,
		Point end,
		MineRailProfile profile,
		int variationSeed,
		int jumpStartIndex,
		int jumpGapLength,
		int dropStartIndex,
		int dropGapLength,
		int dropDepth)
	{
		int deltaX = end.X - start.X;
		int steps = Math.Abs(deltaX);
		if (steps == 0 || Math.Abs(end.Y - start.Y) > steps) {
			throw new InvalidOperationException($"Mine rail edge {start}->{end} exceeds the one-tile track grade.");
		}

		int segmentCount = Math.Clamp(steps / 54, 4, 8);
		SortedDictionary<int, int> desiredControls = new() {
			[0] = start.Y,
			[steps] = end.Y
		};
		for (int index = 1; index < segmentCount; index++) {
			int nominalStep = steps * index / segmentCount;
			int jitter = Noise(variationSeed, index, 0x5241_494C) % 13 - 6;
			int step = Math.Clamp(nominalStep + jitter, 12, steps - 12);
			int baseline = InterpolateY(start.Y, end.Y, step, steps);
			int amplitude = profile switch {
				MineRailProfile.TerracedGrades => 5 + Noise(variationSeed, index, 0x5445_5252) % 5,
				MineRailProfile.DipAndRise => 10 + Noise(variationSeed, index, 0x4449_5052) % 9,
				MineRailProfile.LaunchTransfer => 8 + Noise(variationSeed, index, 0x4C41_554E) % 7,
				_ => 7 + Noise(variationSeed, index, 0x524F_4C4C) % 8
			};
			int sign = profile switch {
				MineRailProfile.DipAndRise => index <= segmentCount / 2 ? 1 : -1,
				MineRailProfile.TerracedGrades => (index / 2) % 2 == 0 ? 1 : -1,
				_ => index % 2 == 0 ? -1 : 1
			};
			int fineJitter = Noise(variationSeed, index, 0x4649_4E45) % 7 - 3;
			desiredControls[step] = baseline + sign * amplitude + fineJitter;
		}

		if (profile == MineRailProfile.LaunchTransfer && jumpStartIndex >= 0 && jumpGapLength > 0) {
			int landingIndex = jumpStartIndex + jumpGapLength + 1;
			int launchY = InterpolateY(start.Y, end.Y, jumpStartIndex, steps)
				- 4 - Noise(variationSeed, jumpStartIndex, 0x4A55_4D50) % 3;
			desiredControls[Math.Max(1, jumpStartIndex - 7)] = launchY + 5;
			desiredControls[jumpStartIndex] = launchY;
			desiredControls[landingIndex] = launchY + 2;
			desiredControls[Math.Min(steps - 1, landingIndex + 8)] =
				InterpolateY(start.Y, end.Y, Math.Min(steps - 1, landingIndex + 8), steps) + 5;
		}

		List<RouteControl> controls = desiredControls
			.Select(pair => new RouteControl(pair.Key, pair.Value))
			.ToList();
		for (int pass = 0; pass < 2; pass++) {
			for (int index = 1; index < controls.Count - 1; index++) {
				RouteControl previous = controls[index - 1];
				RouteControl current = controls[index];
				int reach = current.Step - previous.Step;
				controls[index] = current with {
					Y = Math.Clamp(current.Y, previous.Y - reach, previous.Y + reach)
				};
			}
			for (int index = controls.Count - 2; index > 0; index--) {
				RouteControl current = controls[index];
				RouteControl next = controls[index + 1];
				int reach = next.Step - current.Step;
				controls[index] = current with {
					Y = Math.Clamp(current.Y, next.Y - reach, next.Y + reach)
				};
			}
		}

		int direction = Math.Sign(deltaX);
		List<Point> path = new(steps + 1) { start };
		for (int controlIndex = 0; controlIndex < controls.Count - 1; controlIndex++) {
			RouteControl from = controls[controlIndex];
			RouteControl to = controls[controlIndex + 1];
			int horizontalSteps = to.Step - from.Step;
			int verticalSteps = Math.Abs(to.Y - from.Y);
			int verticalDirection = Math.Sign(to.Y - from.Y);
			int flatSteps = horizontalSteps - verticalSteps;
			int slopeStart = SelectSlopeStart(
				profile,
				variationSeed,
				controlIndex,
				flatSteps,
				to.Step == jumpStartIndex,
				from.Step == jumpStartIndex + jumpGapLength + 1);
			for (int localStep = 1; localStep <= horizontalSteps; localStep++) {
				int verticalProgress = Math.Clamp(localStep - slopeStart, 0, verticalSteps);
				int absoluteStep = from.Step + localStep;
				path.Add(new Point(
					start.X + direction * absoluteStep,
					from.Y + verticalDirection * verticalProgress));
			}
		}
		if (profile == MineRailProfile.LaunchTransfer && jumpStartIndex >= 0 && jumpGapLength > 0) {
			ApplyLaunchTransfer(path, jumpStartIndex, jumpGapLength);
		}
		else if (dropStartIndex >= 0) {
			ApplyGravityTransfer(path, dropStartIndex, dropGapLength, dropDepth);
		}
		return path;
	}

	private static void ApplyLaunchTransfer(List<Point> path, int jumpStartIndex, int jumpGapLength)
	{
		const int approachLength = 5;
		const int landingRun = 4;
		int approachStart = jumpStartIndex - approachLength;
		int landingIndex = jumpStartIndex + jumpGapLength + 1;
		int landingEnd = landingIndex + landingRun;
		if (approachStart < 1 || landingEnd + 4 >= path.Count) {
			throw new InvalidOperationException("Mine launch transfer does not have enough route on both sides.");
		}

		int approachY = path[approachStart].Y;
		for (int offset = 1; offset <= approachLength; offset++) {
			int index = approachStart + offset;
			path[index] = new Point(path[index].X, approachY - offset);
		}
		int launchY = path[jumpStartIndex].Y;
		for (int offset = 1; offset <= jumpGapLength + 1; offset++) {
			int index = jumpStartIndex + offset;
			int drop = (int)Math.Round(2d * offset / (jumpGapLength + 1));
			path[index] = new Point(path[index].X, launchY + drop);
		}
		for (int index = landingIndex; index <= landingEnd; index++) {
			path[index] = new Point(path[index].X, launchY + 2);
		}

		for (int pass = 0; pass < 3; pass++) {
			for (int index = landingEnd + 1; index < path.Count - 1; index++) {
				int previousY = path[index - 1].Y;
				path[index] = new Point(
					path[index].X,
					Math.Clamp(path[index].Y, previousY - 1, previousY + 1));
			}
			for (int index = path.Count - 2; index > landingEnd; index--) {
				int nextY = path[index + 1].Y;
				path[index] = new Point(
					path[index].X,
					Math.Clamp(path[index].Y, nextY - 1, nextY + 1));
			}
		}
	}

	private static void ApplyGravityTransfer(List<Point> path, int dropStartIndex, int dropGapLength, int dropDepth)
	{
		const int flatRun = 4;
		int approachStart = dropStartIndex - flatRun;
		int landingIndex = dropStartIndex + dropGapLength + 1;
		int landingEnd = landingIndex + flatRun;
		if (approachStart < 1 || landingEnd >= path.Count - 1) {
			throw new InvalidOperationException("Mine gravity transfer does not have enough route on both sides.");
		}

		int upperY = path[approachStart].Y;
		for (int index = approachStart; index <= dropStartIndex; index++) {
			path[index] = new Point(path[index].X, upperY);
		}
		for (int offset = 1; offset <= dropGapLength; offset++) {
			int index = dropStartIndex + offset;
			int fall = (int)Math.Round(dropDepth * offset / (double)(dropGapLength + 1));
			path[index] = new Point(path[index].X, upperY + fall);
		}
		int lowerY = upperY + dropDepth;
		for (int index = landingIndex; index <= landingEnd; index++) {
			path[index] = new Point(path[index].X, lowerY);
		}

		// Reconcile the lower shelf with the remaining route while preserving the
		// one-tile rail grade and the exact authored endpoint.
		for (int pass = 0; pass < 4; pass++) {
			for (int index = landingEnd + 1; index < path.Count - 1; index++) {
				int previousY = path[index - 1].Y;
				path[index] = new Point(path[index].X, Math.Clamp(path[index].Y, previousY - 1, previousY + 1));
			}
			for (int index = path.Count - 2; index > landingEnd; index--) {
				int nextY = path[index + 1].Y;
				path[index] = new Point(path[index].X, Math.Clamp(path[index].Y, nextY - 1, nextY + 1));
			}
		}
	}

	private static int SelectSlopeStart(
		MineRailProfile profile,
		int variationSeed,
		int controlIndex,
		int flatSteps,
		bool endsAtLaunch,
		bool startsAtLanding)
	{
		if (flatSteps <= 0) {
			return 0;
		}
		if (endsAtLaunch) {
			return flatSteps;
		}
		if (startsAtLanding) {
			return 0;
		}
		if (profile == MineRailProfile.TerracedGrades) {
			return controlIndex % 2 == 0 ? flatSteps / 5 : flatSteps * 4 / 5;
		}
		return (Noise(variationSeed, controlIndex, 0x534C_4F50) % 3) switch {
			0 => flatSteps / 5,
			1 => flatSteps / 2,
			_ => flatSteps * 4 / 5
		};
	}

	private static int InterpolateY(int startY, int endY, int step, int steps) =>
		startY + (int)Math.Round((endY - startY) * step / (double)Math.Max(1, steps));

	private static IReadOnlyDictionary<Point, BiomeKind> CaptureRouteThemes(IReadOnlyList<MineRoute> routes)
	{
		Dictionary<Point, BiomeKind> themes = [];
		foreach (MineRoute route in routes) {
			BiomeKind[] rawThemes = route.Centerline
				.Select(point => BiomeClassifier.ClassifyAreaTheme(point.X, point.Y))
				.ToArray();
			for (int index = 0; index < route.Centerline.Count; index++) {
				Dictionary<BiomeKind, int> scores = [];
				for (int sample = Math.Max(0, index - 12); sample <= Math.Min(rawThemes.Length - 1, index + 12); sample++) {
					BiomeKind theme = rawThemes[sample];
					scores[theme] = scores.GetValueOrDefault(theme) + (theme is BiomeKind.Forest or BiomeKind.Cavern ? 1 : 3);
				}
				BiomeKind smoothedTheme = scores
					.OrderByDescending(pair => pair.Value)
					.ThenBy(pair => pair.Key)
					.First().Key;
				themes.TryAdd(route.Centerline[index], smoothedTheme);
			}
		}
		return themes;
	}

	private readonly record struct RouteControl(int Step, int Y);

	private static Rectangle Centered(int x, int y, int width, int height) =>
		new(x - width / 2, y - height / 2, width, height);

	private static bool CanPlace(
		SurfaceMinePlan plan,
		WorldPlan worldPlan,
		GenerationManifest manifest,
		out string reason)
	{
		if (!WorldGen.InWorld(plan.Area.Left, plan.Area.Top, 30)
			|| !WorldGen.InWorld(plan.Area.Right - 1, plan.Area.Bottom - 1, 30)) {
			reason = "world padding";
			return false;
		}
		if (GenVars.tRight > GenVars.tLeft && GenVars.tBottom > GenVars.tTop) {
			Rectangle templeClearance = new(
				GenVars.tLeft - 28,
				GenVars.tTop - 28,
				GenVars.tRight - GenVars.tLeft + 57,
				GenVars.tBottom - GenVars.tTop + 57);
			if (plan.Sections.Any(section => section.Area.Intersects(templeClearance))
				|| plan.Routes.SelectMany(RasterizeCenterline).Any(templeClearance.Contains)) {
				reason = "Jungle Temple envelope";
				return false;
			}
		}
		MineSection workyard = plan.Sections.First(section => section.Kind == MineSectionKind.Workyard);
		Rectangle workyardBuffer = new(
			workyard.Area.X - 24,
			workyard.Area.Y - 12,
			workyard.Area.Width + 48,
			workyard.Area.Height + 24);
		if (manifest.BiomeTransitions.Any(transition => transition.Area.Intersects(workyardBuffer))) {
			reason = "surface transition";
			return false;
		}
		if (manifest.ForestLakeBridges.Any(bridge => bridge.Area.Intersects(workyardBuffer)
			|| plan.Sections.Any(section => section.Area.Intersects(bridge.Area))
			|| plan.Routes.SelectMany(RasterizeCenterline).Any(bridge.Area.Contains))) {
			reason = "forest lake bridge";
			return false;
		}
		if (manifest.MountainWaters.Any(water => {
			Rectangle waterClearance = water.Area;
			waterClearance.Inflate(5, CorridorHeadroom + 5);
			return plan.Sections.Any(section => section.Area.Intersects(waterClearance))
				|| plan.Routes.SelectMany(RasterizeCenterline).Any(waterClearance.Contains);
		})) {
			reason = "mountain interior water";
			return false;
		}
		if (plan.Sections.Any(section => MountainBiomeGenerator.IntersectsBridgePassage(worldPlan, section.Area))) {
			reason = "mountain bridge gallery";
			return false;
		}
		MineSection? quarantine = plan.Sections
			.Where(section => section.Kind == MineSectionKind.SealedEvil)
			.Select<MineSection, MineSection?>(section => section)
			.FirstOrDefault();
		if (quarantine is MineSection sealedSection) {
			Rectangle quarantineClearance = sealedSection.Area;
			quarantineClearance.Inflate(3, 3);
			if (plan.Routes
				.Where(route => route.Start != sealedSection.Center && route.End != sealedSection.Center)
				.SelectMany(RasterizeCenterline)
				.Any(quarantineClearance.Contains)) {
				reason = "evil annex route overlap";
				return false;
			}
		}
		foreach (MineRoute route in plan.Routes) {
			int sampleIndex = 0;
			foreach (Point point in RasterizeCenterline(route)) {
				if (sampleIndex++ % 6 != 0) {
					continue;
				}
				Rectangle corridor = new(
					point.X - 3,
					point.Y - CorridorHeadroom - 2,
					7,
					CorridorHeadroom + 6);
				if (MountainBiomeGenerator.IntersectsBridgePassage(worldPlan, corridor)) {
					reason = "mountain bridge gallery";
					return false;
				}
			}
		}
		MineRoute surfaceDescent = plan.Routes[0];
		foreach (BuildTerrace terrace in manifest.Terraces) {
			Rectangle terraceBuffer = new(
				terrace.Area.X - 12,
				terrace.Area.Y - 10,
				terrace.Area.Width + 24,
				terrace.Area.Height + 20);
			if (terraceBuffer.Intersects(workyardBuffer)
				|| RasterizeCenterline(surfaceDescent).Any(terraceBuffer.Contains)) {
				reason = "building terrace";
				return false;
			}
		}

		foreach (MineSection section in plan.Sections) {
			// The mine is itself a major world structure and may replace ordinary
			// terrain reservations. Tile-level checks still reject chests, altars,
			// dungeon/temple blocks, Shimmer, wiring, and other progression state.
			if (!TileEditor.IsSafeForTerrainFeature(section.Area)) {
				reason = $"section {section.Id} object";
				return false;
			}
			if (!TileEditor.IsClearOfTempleAndDungeon(section.Area, margin: 36)) {
				reason = $"section {section.Id} temple/dungeon";
				return false;
			}
		}

		foreach (MineRoute route in plan.Routes) {
			int routeIndex = 0;
			foreach (Point point in RasterizeCenterline(route)) {
				for (int offsetX = -3; offsetX <= 3; offsetX++) {
					for (int offsetY = -CorridorHeadroom - 2; offsetY <= 3; offsetY++) {
						if (!WorldGen.InWorld(point.X + offsetX, point.Y + offsetY, 18)
							|| TileEditor.IsProgressionTile(Main.tile[point.X + offsetX, point.Y + offsetY])
							|| TileEditor.IsTempleOrDungeonCell(Main.tile[point.X + offsetX, point.Y + offsetY])) {
							reason = "route object";
							return false;
						}
					}
				}
				if (routeIndex % 12 == 0
					&& !TileEditor.IsClearOfTempleAndDungeon(new Rectangle(point.X - 3, point.Y - CorridorHeadroom - 2, 7, CorridorHeadroom + 6), margin: 28)) {
					reason = "route temple/dungeon";
					return false;
				}
				routeIndex++;
			}
		}

		reason = string.Empty;
		return true;
	}

	private static void Reserve(SurfaceMinePlan plan)
	{
		foreach (MineSection section in plan.Sections) {
			GenVars.structures.AddProtectedStructure(section.Area, padding: 4);
		}
		foreach (MineRoute route in plan.Routes) {
			int index = 0;
			foreach (Point point in RasterizeCenterline(route)) {
				if (index++ % 20 == 0) {
					GenVars.structures.AddProtectedStructure(new Rectangle(point.X - 7, point.Y - 8, 15, 13), padding: 1);
				}
			}
		}
	}

	private static void BuildSection(MineSection section)
	{
		MinePalette palette = ResolvePalette(section.Theme);
		if (section.Kind == MineSectionKind.Workyard) {
			BuildOpenWorkyard(section, palette);
			return;
		}

		Rectangle area = section.Area;
		int shell = section.Kind == MineSectionKind.SealedEvil ? 5 : 2;
		for (int x = area.Left + 1; x < area.Right - 1; x++) {
			SectionVerticalBounds(
				section,
				x,
				shell,
				out int outerTop,
				out int outerBottom,
				out int innerTop,
				out int innerBottom);
			for (int y = outerTop; y <= outerBottom; y++) {
				TileEditor.SetTerrain(x, y, section.Kind == MineSectionKind.SealedEvil ? TileID.GrayBrick : palette.Masonry);
				if (section.Kind == MineSectionKind.SealedEvil) {
					TileEditor.SetWall(x, y, WallID.GrayBrick);
				}
			}

			for (int y = innerTop; y <= innerBottom; y++) {
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, SelectSectionWall(section, palette, x, y));
			}
			for (int floorDepth = 0; floorDepth < 3 && innerBottom - floorDepth >= innerTop; floorDepth++) {
				TileEditor.SetTerrain(x, innerBottom - floorDepth, section.Kind == MineSectionKind.SealedEvil
					? (WorldGen.crimson ? TileID.Crimstone : TileID.Ebonstone)
					: floorDepth == 0 ? TileID.WoodenBeam : palette.Timber);
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

		BuildDistrictPlatforms(section, palette);
	}

	private static void BuildDistrictPlatforms(MineSection section, MinePalette palette)
	{
		int deckY = section.Center.Y + Math.Clamp(section.Area.Height / 4, 8, 13);
		int centerGap = section.Kind == MineSectionKind.Flooded ? 11 : 7;
		for (int x = section.Area.Left + 8; x < section.Area.Right - 8; x++) {
			bool gap = Math.Abs(x - section.Center.X) <= centerGap;
			for (int y = deckY - 7; y < deckY; y++) {
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, SelectSectionWall(section, palette, x, y));
			}
			if (gap) {
				TileEditor.TryPlacePlatformForced(x, deckY, palette.PlatformStyle);
			}
			else {
				TileEditor.SetTerrain(x, deckY, TileID.WoodenBeam);
				TileEditor.SetTerrain(x, deckY + 1, palette.Timber);
			}
		}

		int supportGap = 13 + Noise(section.Id, section.Center.X, 307) % 8;
		for (int x = section.Area.Left + 10; x < section.Area.Right - 9; x += supportGap) {
			for (int y = deckY + 2; y <= Math.Min(section.Area.Bottom - 4, deckY + 10); y++) {
				TileEditor.SetTerrain(x, y, TileID.WoodenBeam);
			}
		}
	}

	private static void BuildOpenWorkyard(MineSection section, MinePalette palette)
	{
		Rectangle area = section.Area;
		int floorY = section.Center.Y + 1;
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < floorY; y++) {
				TileEditor.ClearTerrain(x, y);
				if (IsInsideWorkyardWall(section, x, y)) {
					TileEditor.SetWall(x, y, palette.PrimaryWall);
				}
			}
			for (int depth = 0; depth < 3; depth++) {
				TileEditor.SetTerrain(x, floorY + depth, depth == 0 ? TileID.WoodenBeam : palette.Timber);
			}
		}
		for (int x = area.Center.X + 5; x < area.Right - 2; x++) {
			int canopyY = floorY - 11 + Math.Abs(x - (area.Center.X + 14)) / 8;
			for (int depth = 0; depth < 2; depth++) {
				TileEditor.SetTerrain(x, canopyY + depth, palette.Timber);
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

	private static void CarveRoute(SurfaceMinePlan plan, MineRoute route)
	{
		int index = 0;
		foreach (Point point in RasterizeCenterline(route)) {
			int ceilingExtra = SampleCeilingExtra(route, index);
			int horizontalRadius = ceilingExtra >= 3 ? 3 : 2;
			for (int offsetX = -horizontalRadius; offsetX <= horizontalRadius; offsetX++) {
				int archLift = Math.Max(0, horizontalRadius - Math.Abs(offsetX)) / 2;
				int ceilingHeight = CorridorHeadroom + 1 + ceilingExtra + archLift;
				for (int offsetY = -ceilingHeight; offsetY <= 0; offsetY++) {
					int x = point.X + offsetX;
					int y = point.Y + offsetY;
					MinePalette palette = ResolveRoutePalette(plan, route, index, x, y);
					TileEditor.ClearTerrain(x, y);
					TileEditor.SetWall(x, y, palette.PrimaryWall);
				}
				for (int depth = 1; depth <= 3; depth++) {
					int x = point.X + offsetX;
					int y = point.Y + depth;
					MinePalette palette = ResolveRoutePalette(plan, route, index, x, y);
					TileEditor.SetTerrain(x, y, palette.Masonry);
				}
			}

			index++;
		}
		CarveJumpTransfer(plan, route);
	}

	private static void CarveJumpTransfer(SurfaceMinePlan plan, MineRoute route)
	{
		if (GetJumpTransfer(route) is not MineRailJump jump) {
			return;
		}

		for (int index = route.JumpStartIndex + 1;
			index <= route.JumpStartIndex + route.JumpGapLength;
			index++) {
			Point point = route.Centerline[index];
			for (int offsetX = -2; offsetX <= 2; offsetX++) {
				for (int offsetY = -5; offsetY <= 4; offsetY++) {
					int x = point.X + offsetX;
					int y = point.Y + offsetY;
					MinePalette palette = ResolveRoutePalette(plan, route, index, x, y);
					TileEditor.ClearTerrain(x, y);
					TileEditor.SetWall(x, y, palette.PrimaryWall);
				}
			}
		}

		MinePalette launchPalette = ResolvePalette(plan.ThemeAt(jump.Launch));
		MinePalette landingPalette = ResolvePalette(plan.ThemeAt(jump.Landing));
		for (int depth = 1; depth <= 3; depth++) {
			TileEditor.SetTerrain(jump.Launch.X, jump.Launch.Y + depth, launchPalette.Masonry);
			TileEditor.SetTerrain(jump.Landing.X, jump.Landing.Y + depth, landingPalette.Masonry);
		}
	}

	private static void CarveGravityTransfer(SurfaceMinePlan plan, MineRoute route)
	{
		if (GetGravityTransfer(route) is not MineRailDrop drop) {
			return;
		}

		int landingIndex = route.DropStartIndex + route.DropGapLength + 1;
		for (int index = route.DropStartIndex + 1; index < landingIndex; index++) {
			Point point = route.Centerline[index];
			for (int y = drop.Gap.Top; y < drop.Gap.Bottom; y++) {
				MinePalette palette = ResolveRoutePalette(plan, route, index, point.X, y);
				TileEditor.ClearTerrain(point.X, y);
				TileEditor.SetWall(point.X, y, palette.PrimaryWall);
			}
		}
	}

	private static void RestoreGravityTransferClearance(SurfaceMinePlan plan)
	{
		foreach (MineRoute route in plan.Routes.Where(route => route.HasGravityTransfer)) {
			CarveGravityTransfer(plan, route);
		}
	}

	private static void BuildRouteSupports(SurfaceMinePlan plan, HashSet<Point> plannedTrack)
	{
		foreach (MineRoute route in plan.Routes.Where(route => route.HasTrack)) {
			int spacing = 8 + Noise(route.VariationSeed, route.Centerline.Count, 0x4245_414D) % 6;
			int index = 8 + Noise(route.VariationSeed, spacing, 0x5354_4152) % spacing;
			for (; index < route.Centerline.Count - 8; index += spacing) {
				bool nearJump = route.HasJumpTransfer
					&& Math.Abs(index - route.JumpStartIndex) < route.JumpGapLength + 10;
				bool nearDrop = route.HasGravityTransfer
					&& Math.Abs(index - route.DropStartIndex) < route.DropGapLength + 10;
				if (nearJump || nearDrop) {
					continue;
				}
				Point point = route.Centerline[index];
				// A shallow tunnel still needs visible structural rhythm. Its crossbeam
				// can be keyed into the ceiling while the posts hang into the passage;
				// omitting the entire bent made compact mine plans look unfinished.
				int topY = point.Y - CorridorHeadroom - 4;
				int leftPostX = point.X - 3;
				int rightPostX = point.X + 3;
				int hangingPostBottom = topY + 2;
				bool leftPost = CanPlaceSupportColumn(leftPostX, topY, hangingPostBottom, plannedTrack);
				bool rightPost = CanPlaceSupportColumn(rightPostX, topY, hangingPostBottom, plannedTrack);
				if (!leftPost && !rightPost) {
					continue;
				}

				for (int x = leftPostX; x <= rightPostX; x++) {
					Point beam = new(x, topY);
					if (CanPlaceSupportCell(beam, plannedTrack)) {
						TileEditor.SetTerrain(x, topY, TileID.WoodenBeam);
					}
				}
				if (leftPost) {
					for (int y = topY + 1; y <= hangingPostBottom; y++) {
						TileEditor.SetTerrain(leftPostX, y, TileID.WoodenBeam);
					}
				}
				if (rightPost) {
					for (int y = topY + 1; y <= hangingPostBottom; y++) {
						TileEditor.SetTerrain(rightPostX, y, TileID.WoodenBeam);
					}
				}
				for (int depth = 1; depth <= 3; depth++) {
					Point foundation = new(point.X, point.Y + depth);
					if (CanPlaceSupportCell(foundation, plannedTrack)) {
						TileEditor.SetTerrain(foundation.X, foundation.Y, TileID.WoodenBeam);
					}
				}
				int torchX = rightPost ? rightPostX - 1 : leftPostX + 1;
				if (Noise(route.VariationSeed, index, 0x544F_5243) % 3 == 0) {
					TileEditor.TryPlaceTorch(torchX, hangingPostBottom);
				}
			}
		}
	}

	private static bool CanPlaceSupportColumn(int x, int topY, int bottomY, HashSet<Point> plannedTrack)
	{
		for (int y = topY; y <= bottomY; y++) {
			if (!CanPlaceSupportCell(new Point(x, y), plannedTrack)) {
				return false;
			}
		}
		return true;
	}

	private static bool CanPlaceSupportCell(Point cell, HashSet<Point> plannedTrack)
	{
		if (!WorldGen.InWorld(cell.X, cell.Y, 4)
			|| plannedTrack.Contains(cell)
			|| IsReservedTrackHeadroom(cell, plannedTrack)) {
			return false;
		}

		Tile tile = Main.tile[cell.X, cell.Y];
		return !TileEditor.IsProgressionTile(tile)
			&& !TileEditor.IsTempleOrDungeonCell(tile)
			&& (!tile.HasTile || !Main.tileFrameImportant[tile.TileType] || tile.TileType == TileID.WoodenBeam)
			&& !tile.RedWire && !tile.BlueWire && !tile.GreenWire && !tile.YellowWire
			&& !tile.HasActuator;
	}

	private static void FinishBiomeWalls(SurfaceMinePlan plan)
	{
		foreach (MineSection section in plan.Sections) {
			if (section.Kind == MineSectionKind.SealedEvil) {
				continue;
			}
			MinePalette palette = ResolvePalette(section.Theme);
			if (section.Kind == MineSectionKind.Workyard) {
				int floorY = section.Center.Y + 1;
				for (int x = section.Area.Left; x < section.Area.Right; x++) {
					for (int y = Math.Max(section.Area.Top, floorY - 14); y < floorY; y++) {
						if (IsInsideWorkyardWall(section, x, y) && !TileEditor.IsSolid(x, y)) {
							TileEditor.SetWall(x, y, palette.PrimaryWall);
						}
					}
				}
				continue;
			}

			int shell = 2;
			for (int x = section.Area.Left + 1; x < section.Area.Right - 1; x++) {
				SectionVerticalBounds(
					section,
					x,
					shell,
					out _,
					out _,
					out int innerTop,
					out int innerBottom);
				for (int y = innerTop; y <= innerBottom; y++) {
					if (!TileEditor.IsSolid(x, y) && Main.tile[x, y].WallType != WallID.GrayBrick) {
						TileEditor.SetWall(x, y, SelectSectionWall(section, palette, x, y));
					}
				}
			}
		}

		foreach (MineRoute route in plan.Routes) {
			for (int index = 0; index < route.Centerline.Count; index++) {
				Point point = route.Centerline[index];
				int ceilingExtra = SampleCeilingExtra(route, index);
				int horizontalRadius = ceilingExtra >= 3 ? 3 : 2;
				for (int offsetX = -horizontalRadius; offsetX <= horizontalRadius; offsetX++) {
					int archLift = Math.Max(0, horizontalRadius - Math.Abs(offsetX)) / 2;
					int ceilingHeight = CorridorHeadroom + 1 + ceilingExtra + archLift;
					for (int offsetY = -ceilingHeight; offsetY <= 0; offsetY++) {
						int x = point.X + offsetX;
						int y = point.Y + offsetY;
						if (!TileEditor.IsSolid(x, y) && Main.tile[x, y].WallType != WallID.GrayBrick) {
							MinePalette palette = ResolveRoutePalette(plan, route, index, x, y);
							TileEditor.SetWall(x, y, palette.PrimaryWall);
						}
					}
				}
			}
		}
	}

	private static int SampleCeilingExtra(MineRoute route, int index)
	{
		const int span = 29;
		int cell = index / span;
		double local = (index % span) / (double)span;
		local = local * local * (3d - 2d * local);
		int routeKey = route.Start.X * 31 ^ route.Start.Y * 17 ^ route.End.X * 13 ^ route.End.Y * 7;
		int left = Noise(routeKey, cell, 0x4341_5645) % 6;
		int right = Noise(routeKey, cell + 1, 0x4341_5645) % 6;
		int extra = (int)Math.Round(left + (right - left) * local);

		// A minority of macro cells swell into broad natural pockets rather than
		// making every rail run a constant-height timber tube.
		if (Noise(routeKey, cell, 0x504F_434B) % 100 < 32) {
			extra += (int)Math.Round(Math.Sin(local * Math.PI) * 3d);
		}
		return Math.Clamp(extra, 0, 8);
	}

	private static void BuildJunctionStations(SurfaceMinePlan plan)
	{
		Dictionary<Point, int> degree = [];
		foreach (MineRoute route in plan.Routes.Where(route => route.HasTrack)) {
			degree[route.Start] = degree.GetValueOrDefault(route.Start) + 1;
			degree[route.End] = degree.GetValueOrDefault(route.End) + 1;
		}
		foreach ((Point center, int routeDegree) in degree.Where(pair => pair.Value >= 3)) {
			MinePalette palette = ResolvePalette(plan.ThemeAt(center));
			for (int offsetX = -12; offsetX <= 12; offsetX++) {
				for (int offsetY = -8; offsetY <= 3; offsetY++) {
					double normalized = (double)(offsetX * offsetX) / 144d + (double)(offsetY * offsetY) / 64d;
					if (normalized > 1d) {
						continue;
					}
					int x = center.X + offsetX;
					int y = center.Y + offsetY;
					if (offsetY >= 1) {
						TileEditor.SetTerrain(x, y, offsetY == 1 ? TileID.WoodenBeam : palette.Timber);
					}
					else {
						TileEditor.ClearTerrain(x, y);
						TileEditor.SetWall(x, y, palette.PrimaryWall);
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

	private static void PrepareTrackCell(SurfaceMinePlan plan, Point point, HashSet<Point> plannedTrack)
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
			TileEditor.SetTerrain(point.X, point.Y + 1, ResolvePalette(plan.ThemeAt(point)).Timber);
		}
	}

	private static void ConfigureRailEndpoints(SurfaceMinePlan plan)
	{
		Dictionary<Point, int> routeDegree = [];
		foreach (MineRoute route in plan.Routes.Where(route => route.HasTrack)) {
			routeDegree[route.Start] = routeDegree.GetValueOrDefault(route.Start) + 1;
			routeDegree[route.End] = routeDegree.GetValueOrDefault(route.End) + 1;
		}

		foreach (Point terminal in routeDegree
			.Where(pair => pair.Value == 1)
			.Select(pair => pair.Key)) {
			MineRoute incident = plan.Routes.First(route => route.HasTrack
				&& (route.Start == terminal || route.End == terminal));
			Point inwardNeighbor = incident.Start == terminal
				? incident.Centerline[1]
				: incident.Centerline[^2];
			RemoveStrayTerminalTracks(terminal, inwardNeighbor);
			HammerTrackUntil(terminal, Minecart.DrawBouncyBumper);
			if (!Minecart.DrawBouncyBumper(Main.tile[terminal.X, terminal.Y].TileFrameX)) {
				SetBouncyTerminalFrame(terminal, inwardNeighbor);
			}
		}

		foreach (MineRoute route in plan.Routes.Where(route => route.HasJumpTransfer)) {
			if (GetJumpTransfer(route) is MineRailJump jump) {
				HammerTrackUntil(jump.Launch, IsLaunchRampFrame);
				HammerTrackUntil(jump.Landing, IsFlatOpenEndFrame);
			}
		}
		foreach (MineRoute route in plan.Routes.Where(route => route.HasGravityTransfer)) {
			if (GetGravityTransfer(route) is MineRailDrop drop) {
				HammerTrackUntil(drop.UpperLip, IsFlatOpenEndFrame);
				HammerTrackUntil(drop.LowerLanding, IsFlatOpenEndFrame);
			}
		}
	}

	private static void RemoveStrayTerminalTracks(Point terminal, Point inwardNeighbor)
	{
		for (int offsetX = -1; offsetX <= 1; offsetX += 2) {
			for (int offsetY = -1; offsetY <= 1; offsetY++) {
				Point neighbor = new(terminal.X + offsetX, terminal.Y + offsetY);
				if (neighbor != inwardNeighbor && HasTile(neighbor.X, neighbor.Y, TileID.MinecartTrack)) {
					TileEditor.ClearTerrain(neighbor.X, neighbor.Y);
				}
			}
		}
	}

	private static void SetBouncyTerminalFrame(Point terminal, Point inwardNeighbor)
	{
		int horizontalDirection = Math.Sign(inwardNeighbor.X - terminal.X);
		int verticalOffset = inwardNeighbor.Y - terminal.Y;
		short frame = (horizontalDirection, verticalOffset) switch {
			(1, 0) => 24,
			(-1, 0) => 25,
			(-1, 1) => 26,
			(1, 1) => 27,
			(-1, -1) => 28,
			(1, -1) => 29,
			_ => throw new InvalidOperationException(
				$"Mine terminal {terminal} has invalid inward rail neighbor {inwardNeighbor}.")
		};
		Tile track = Main.tile[terminal.X, terminal.Y];
		track.TileFrameX = frame;
		track.TileFrameY = -1;
	}

	private static void HammerTrackUntil(Point point, Func<int, bool> acceptsFrame)
	{
		if (!HasTile(point.X, point.Y, TileID.MinecartTrack)) {
			return;
		}
		Minecart.FrameTrack(point.X, point.Y, pound: false, mute: true);
		for (int attempt = 0; attempt < 8; attempt++) {
			if (acceptsFrame(Main.tile[point.X, point.Y].TileFrameX)) {
				return;
			}
			if (!Minecart.FrameTrack(point.X, point.Y, pound: true, mute: true)) {
				return;
			}
		}
	}

	internal static bool IsLaunchRampFrame(int frame) =>
		frame is >= FirstLaunchRampFrame and <= LastLaunchRampFrame;

	internal static bool IsFlatOpenEndFrame(int frame) =>
		frame is FlatOpenLeftFrame or FlatOpenRightFrame;

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
		MinePalette palette = ResolvePalette(section.Theme);
		int count = 0;
		count += TryPlaceFurniture(section.Area.Left + 10, floorY, TileID.WorkBenches, palette.WorkbenchStyle) ? 1 : 0;
		count += TryPlaceFurniture(section.Area.Left + 18, floorY, palette.TableTile, palette.TableStyle) ? 1 : 0;
		count += TryPlaceFurniture(section.Area.Left + 23, floorY, TileID.Chairs, palette.ChairStyle) ? 1 : 0;
		count += TryPlaceFurniture(section.Area.Right - 12, floorY, TileID.Anvils) ? 1 : 0;
		count += TryPlaceFurniture(section.Area.Right - 20, floorY, TileID.Bookcases, palette.BookcaseStyle) ? 1 : 0;
		count += TileEditor.TryPlaceSmallPile(section.Area.Right - 7, floorY, section.Id % 6, 0) ? 1 : 0;
		count += TileEditor.TryPlaceSmallPile(section.Area.Center.X + 13, floorY, (section.Id + 3) % 6, 0) ? 1 : 0;
		TileEditor.TryPlaceTorch(section.Area.Center.X, section.Area.Top + 5);
		return count;
	}

	private static bool TryPlaceFurniture(int x, int y, ushort tileType, int style = 0)
	{
		for (int clearX = x - 2; clearX <= x + 2; clearX++) {
			for (int clearY = y - 4; clearY <= y; clearY++) {
				TileEditor.ClearTerrain(clearX, clearY);
			}
		}
		WorldGen.PlaceTile(x, y, tileType, mute: true, forced: true, plr: -1, style: style);
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
		MinePalette palette = ResolvePalette(section.Theme);
		for (int x = displayCenterX - 10; x <= displayCenterX + 10; x++) {
			for (int y = section.Center.Y + 2; y <= displayFloorY; y++) {
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, palette.PrimaryWall);
			}
			TileEditor.SetTerrain(x, displayFloorY + 1, palette.Timber);
			TileEditor.SetTerrain(x, displayFloorY + 2, palette.Timber);
		}

		int portalX = displayCenterX - 8;
		for (int x = portalX - 2; x <= portalX + 2; x++) {
			TileEditor.TryPlacePlatformForced(x, section.Center.Y + 1, palette.PlatformStyle);
		}
		for (int y = section.Center.Y + 2; y <= displayFloorY; y++) {
			TileEditor.SetTerrain(portalX, y, TileID.Rope);
		}

		int placed = 0;
		placed += PlaceWorkbenchFootprint(displayCenterX - 7, displayFloorY, palette) ? 1 : 0;
		placed += PlaceTableFootprint(displayCenterX, displayFloorY, palette) ? 1 : 0;
		placed += PlaceChairFootprint(displayCenterX + 7, displayFloorY, palette) ? 1 : 0;
		TileEditor.TryPlaceTorch(displayCenterX + 9, displayFloorY - 4);
		return placed;
	}

	private static int BuildCompactWorkAlcove(MineSection section, HashSet<Point> plannedTrack)
	{
		MinePalette palette = ResolvePalette(section.Theme);
		for (int floorY = section.Area.Top + 12; floorY < section.Area.Bottom - 5; floorY += 7) {
			for (int centerX = section.Area.Left + 12; centerX < section.Area.Right - 12; centerX += 10) {
				Rectangle alcove = new(centerX - 10, floorY - 7, 21, 10);
				if (plannedTrack.Any(alcove.Contains)) {
					continue;
				}

				for (int x = alcove.Left; x < alcove.Right; x++) {
					for (int y = alcove.Top; y < floorY; y++) {
						TileEditor.ClearTerrain(x, y);
						TileEditor.SetWall(x, y, palette.PrimaryWall);
					}
					TileEditor.SetTerrain(x, floorY, palette.Timber);
					TileEditor.SetTerrain(x, floorY + 1, palette.Timber);
				}
				int placed = 0;
				placed += PlaceWorkbenchFootprint(centerX - 7, floorY - 1, palette) ? 1 : 0;
				placed += PlaceTableFootprint(centerX, floorY - 1, palette) ? 1 : 0;
				placed += PlaceChairFootprint(centerX + 7, floorY - 1, palette) ? 1 : 0;
				TileEditor.TryPlaceTorch(centerX + 9, floorY - 4);
				return placed;
			}
		}
		return 0;
	}

	private static int BuildWorkyardLoft(MineSection workyard)
	{
		Point entrance = workyard.Center;
		MinePalette palette = ResolvePalette(workyard.Theme);
		int left = entrance.X + 12;
		int right = entrance.X + 30;
		int floorY = entrance.Y - 8;
		for (int x = left; x <= right; x++) {
			for (int y = floorY - 6; y < floorY; y++) {
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, palette.PrimaryWall);
			}
			if (x >= left + 2 && x <= left + 5) {
				TileEditor.TryPlacePlatformForced(x, floorY, palette.PlatformStyle);
			}
			else {
				TileEditor.SetTerrain(x, floorY, palette.Timber);
				TileEditor.SetTerrain(x, floorY + 1, palette.Timber);
			}
			int roofY = floorY - 8 + Math.Abs(x - (left + right) / 2) / 6;
			TileEditor.SetTerrain(x, roofY, palette.Masonry);
			TileEditor.SetTerrain(x, roofY + 1, palette.Timber);
		}
		for (int y = floorY - 1; y <= entrance.Y - 1; y++) {
			TileEditor.SetTerrain(left + 3, y, TileID.Rope);
		}
		int placed = 0;
		placed += PlaceWorkbenchFootprint(left + 8, floorY - 1, palette) ? 1 : 0;
		placed += PlaceTableFootprint(left + 13, floorY - 1, palette) ? 1 : 0;
		placed += PlaceChairFootprint(left + 17, floorY - 1, palette) ? 1 : 0;
		TileEditor.TryPlaceTorch(right - 2, floorY - 4);
		return placed;
	}

	private static bool PlaceWorkbenchFootprint(int leftX, int bottomY, MinePalette palette)
	{
		WorldGen.PlaceTile(leftX, bottomY, TileID.WorkBenches, mute: true, forced: false, plr: -1, style: palette.WorkbenchStyle);
		return HasNearbyTile(leftX, bottomY, TileID.WorkBenches);
	}

	private static bool PlaceTableFootprint(int centerX, int bottomY, MinePalette palette)
	{
		WorldGen.PlaceTile(centerX, bottomY, palette.TableTile, mute: true, forced: false, plr: -1, style: palette.TableStyle);
		return HasNearbyTile(centerX, bottomY, palette.TableTile);
	}

	private static bool PlaceChairFootprint(int x, int bottomY, MinePalette palette)
	{
		WorldGen.PlaceTile(x, bottomY, TileID.Chairs, mute: true, forced: false, plr: -1, style: palette.ChairStyle);
		return HasNearbyTile(x, bottomY, TileID.Chairs);
	}

	private static bool HasNearbyTile(int x, int y, ushort type)
	{
		for (int offsetX = -3; offsetX <= 3; offsetX++) {
			for (int offsetY = -3; offsetY <= 1; offsetY++) {
				if (HasTile(x + offsetX, y + offsetY, type)) {
					return true;
				}
			}
		}
		return false;
	}

	internal static IEnumerable<Point> Rasterize(MineRoute route)
	{
		for (int index = 0; index < route.Centerline.Count; index++) {
			bool insideJump = route.HasJumpTransfer
				&& index > route.JumpStartIndex
				&& index <= route.JumpStartIndex + route.JumpGapLength;
			bool insideDrop = route.HasGravityTransfer
				&& index > route.DropStartIndex
				&& index <= route.DropStartIndex + route.DropGapLength;
			if (insideJump || insideDrop) {
				continue;
			}
			yield return route.Centerline[index];
		}
	}

	internal static IEnumerable<Point> RasterizeCenterline(MineRoute route) => route.Centerline;

	internal static MineRailJump? GetJumpTransfer(MineRoute route)
	{
		if (!route.HasJumpTransfer) {
			return null;
		}

		int landingIndex = route.JumpStartIndex + route.JumpGapLength + 1;
		if (route.JumpStartIndex < 0 || landingIndex >= route.Centerline.Count) {
			return null;
		}

		Point launch = route.Centerline[route.JumpStartIndex];
		Point landing = route.Centerline[landingIndex];
		IReadOnlyList<Point> missing = route.Centerline
			.Skip(route.JumpStartIndex + 1)
			.Take(route.JumpGapLength)
			.ToArray();
		int left = missing.Min(point => point.X);
		int right = missing.Max(point => point.X);
		int top = Math.Min(launch.Y, landing.Y) - 5;
		int bottom = Math.Max(launch.Y, landing.Y) + 5;
		return new MineRailJump(
			launch,
			landing,
			new Rectangle(left, top, right - left + 1, bottom - top + 1));
	}

	internal static MineRailDrop? GetGravityTransfer(MineRoute route)
	{
		if (!route.HasGravityTransfer) {
			return null;
		}

		int landingIndex = route.DropStartIndex + route.DropGapLength + 1;
		if (route.DropStartIndex < 0 || landingIndex >= route.Centerline.Count) {
			return null;
		}

		Point upperLip = route.Centerline[route.DropStartIndex];
		Point lowerLanding = route.Centerline[landingIndex];
		IReadOnlyList<Point> missing = route.Centerline
			.Skip(route.DropStartIndex + 1)
			.Take(route.DropGapLength)
			.ToArray();
		int left = missing.Min(point => point.X);
		int right = missing.Max(point => point.X);
		int top = upperLip.Y - CorridorHeadroom;
		int bottom = lowerLanding.Y + 4;
		return new MineRailDrop(
			upperLip,
			lowerLanding,
			new Rectangle(left, top, right - left + 1, bottom - top + 1));
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
			ReinforceQuarantineShell(section, direction);
			int gateX = section.Center.X + direction * (section.Area.Width / 2 - 8);
			int tunnelEndX = gateX + direction * 14;
			int tunnelLeft = Math.Min(section.Center.X + direction * 8, tunnelEndX);
			int tunnelRight = Math.Max(section.Center.X + direction * 8, tunnelEndX);
			for (int x = tunnelLeft; x <= tunnelRight; x++) {
				int trackY = RouteYAt(route, x, section.Center.Y);
				for (int depth = 1; depth <= 4; depth++) {
					TileEditor.SetTerrain(x, trackY + depth, TileID.GrayBrick);
					TileEditor.SetTerrain(x, trackY - CorridorHeadroom - 1 - depth, TileID.GrayBrick);
					TileEditor.SetWall(x, trackY + depth, WallID.GrayBrick);
					TileEditor.SetWall(x, trackY - CorridorHeadroom - 1 - depth, WallID.GrayBrick);
				}
				for (int y = trackY - CorridorHeadroom - 1; y <= trackY; y++) {
					TileEditor.SetWall(x, y, WallID.GrayBrick);
				}
			}

			for (int width = 0; width < 4; width++) {
				int columnX = gateX + direction * width;
				int trackY = RouteYAt(route, columnX, section.Center.Y);
				for (int y = trackY - 6; y < trackY; y++) {
					TileEditor.SetActuatedTerrain(columnX, y, TileID.GrayBrick);
					TileEditor.SetWall(columnX, y, WallID.GrayBrick);
				}
			}

			int innerGateX = section.Center.X + direction * 10;
			int innerTrackY = Math.Clamp(section.Center.Y, section.Area.Top + 10, section.Area.Bottom - 3);
			for (int width = 0; width < 4; width++) {
				int columnX = innerGateX + direction * width;
				for (int y = innerTrackY - 8; y < innerTrackY; y++) {
					TileEditor.SetActuatedTerrain(columnX, y, TileID.GrayBrick);
					TileEditor.SetWall(columnX, y, WallID.GrayBrick);
				}
			}
		}
	}

	private static void ReinforceQuarantineShell(MineSection section, int portalDirection)
	{
		Rectangle area = section.Area;
		int radiusX = area.Width / 2 - 2;
		int radiusY = area.Height / 2 - 2;
		for (int x = area.Left + 1; x < area.Right - 1; x++) {
			int offsetX = x - area.Center.X;
			int portalBandStart = Math.Max(8, area.Width / 2 - 18);
			if (Math.Sign(offsetX) == portalDirection && Math.Abs(offsetX) >= portalBandStart) {
				continue;
			}
			double normalizedX = (double)offsetX / Math.Max(1, radiusX);
			int halfHeight = Math.Max(3, (int)Math.Round(radiusY * Math.Sqrt(Math.Max(0d, 1d - normalizedX * normalizedX))));
			int outerTop = area.Center.Y - halfHeight + Noise(section.Id, x, 17) % 5 - 2;
			int outerBottom = area.Center.Y + halfHeight + Noise(section.Id, x, 43) % 5 - 2;
			for (int depth = 0; depth < 4 && outerTop + depth <= outerBottom - depth; depth++) {
				SetSafeQuarantineLayer(x, outerTop + depth);
				SetSafeQuarantineLayer(x, outerBottom - depth);
			}
		}

		int oppositeDirection = -portalDirection;
		int oppositeEdgeX = oppositeDirection < 0 ? area.Left + 1 : area.Right - 2;
		for (int depth = 0; depth < 4; depth++) {
			int x = oppositeEdgeX - oppositeDirection * depth;
			int offsetX = x - area.Center.X;
			double normalizedX = (double)offsetX / Math.Max(1, radiusX);
			int halfHeight = Math.Max(3, (int)Math.Round(radiusY * Math.Sqrt(Math.Max(0d, 1d - normalizedX * normalizedX))));
			int outerTop = area.Center.Y - halfHeight + Noise(section.Id, x, 17) % 5 - 2;
			int outerBottom = area.Center.Y + halfHeight + Noise(section.Id, x, 43) % 5 - 2;
			for (int y = outerTop; y <= outerBottom; y++) {
				SetSafeQuarantineLayer(x, y);
			}
		}
	}

	private static void SetSafeQuarantineLayer(int x, int y)
	{
		TileEditor.SetTerrain(x, y, TileID.GrayBrick);
		TileEditor.SetWall(x, y, WallID.GrayBrick);
	}

	internal static bool HasFourTileQuarantine(SurfaceMinePlan plan, MineSection section, out string reason)
	{
		MineRoute? spur = plan.Routes
			.Where(route => route.Start == section.Center || route.End == section.Center)
			.Select<MineRoute, MineRoute?>(route => route)
			.FirstOrDefault();
		if (spur is not MineRoute route) {
			reason = "no route reaches the annex";
			return false;
		}
		Point outside = route.Start == section.Center ? route.End : route.Start;
		int direction = Math.Sign(outside.X - section.Center.X);
		int gateX = section.Center.X + direction * (section.Area.Width / 2 - 8);
		for (int width = 0; width < 4; width++) {
			int x = gateX + direction * width;
			int trackY = RouteYAt(route, x, section.Center.Y);
			for (int y = trackY - 6; y < trackY; y++) {
				Tile tile = Main.tile[x, y];
				if (!tile.HasTile || tile.TileType != TileID.GrayBrick || !tile.HasActuator
					|| tile.WallType != WallID.GrayBrick) {
					reason = $"gate column {x} is not an actuated Gray Brick and safe-wall barrier";
					return false;
				}
			}
			for (int depth = 1; depth <= 4; depth++) {
				Tile floor = Main.tile[x, trackY + depth];
				if (!floor.HasTile || floor.TileType != TileID.GrayBrick || floor.WallType != WallID.GrayBrick) {
					reason = $"gate column {x} has only {depth - 1}/4 safe floor blocks";
					return false;
				}
				int ceilingY = trackY - CorridorHeadroom - 1 - depth;
				Tile ceiling = Main.tile[x, ceilingY];
				if (!ceiling.HasTile || ceiling.TileType != TileID.GrayBrick || ceiling.WallType != WallID.GrayBrick) {
					reason = $"gate column {x} has only {depth - 1}/4 safe ceiling blocks";
					return false;
				}
			}
		}

		int shellCenterY = section.Area.Center.Y;
		int oppositeDirection = -direction;
		int oppositeShellX = oppositeDirection < 0 ? section.Area.Left + 1 : section.Area.Right - 2;
		for (int depth = 0; depth < 4; depth++) {
			int x = oppositeShellX - oppositeDirection * depth;
			if (!IsSafeQuarantineLayer(x, shellCenterY)) {
				reason = $"annex opposite shell at {x},{shellCenterY} lacks four Gray Brick and safe-wall layers";
				return false;
			}
		}

		int shellCenterX = section.Area.Center.X;
		for (int verticalDirection = -1; verticalDirection <= 1; verticalDirection += 2) {
			int edgeY = verticalDirection < 0 ? section.Area.Top : section.Area.Bottom - 1;
			int safeRun = 0;
			for (int y = edgeY;; y -= verticalDirection) {
				safeRun = IsSafeQuarantineLayer(shellCenterX, y) ? safeRun + 1 : 0;
				if (safeRun >= 4) {
					break;
				}
				if (y == shellCenterY) {
					reason = $"annex {(verticalDirection < 0 ? "ceiling" : "floor")} shell at x={shellCenterX} "
						+ "has no continuous four-layer Gray Brick and safe-wall barrier";
					return false;
				}
			}
		}
		reason = string.Empty;
		return true;
	}

	private static bool IsSafeQuarantineLayer(int x, int y)
	{
		Tile tile = Main.tile[x, y];
		return tile.HasTile && tile.TileType == TileID.GrayBrick && tile.WallType == WallID.GrayBrick;
	}

	private static int RouteYAt(MineRoute route, int x, int fallbackY)
	{
		foreach (Point point in RasterizeCenterline(route)) {
			if (point.X == x) {
				return point.Y;
			}
		}
		return fallbackY;
	}

	private static ushort SelectSectionWall(MineSection section, MinePalette palette, int x, int y)
	{
		int motif = Noise(section.Id, section.Center.X, 0x5741_4C4C) % 4;
		int seed = section.Id * 193 ^ section.Center.X ^ section.Center.Y * 31;
		int horizontal = x - section.Center.X + OrganicBoundary.Profile(
			y,
			seed ^ 0x4857_4152,
			17,
			5,
			5,
			2);
		int vertical = y - section.Center.Y + OrganicBoundary.Profile(
			x,
			seed ^ 0x5657_4152,
			23,
			7,
			4,
			2);
		double field = OrganicBoundary.Field(x, y, seed ^ 0x5041_5443, 17, 5);
		bool accent = motif switch {
			0 => vertical > section.Area.Height / 8 && field > 0.33d,
			1 => horizontal < -section.Area.Width / 6
				&& Math.Abs(vertical) < section.Area.Height / 3 && field > 0.38d,
			2 => Math.Abs(horizontal) < section.Area.Width / 5
				&& Math.Abs(vertical) < section.Area.Height / 4 && field > 0.36d,
			_ => (long)horizontal * horizontal * 9 + (long)vertical * vertical * 16
				< (long)section.Area.Width * section.Area.Width * (field > 0.42d ? 1 : 0)
		};
		return accent ? palette.SecondaryWall : palette.PrimaryWall;
	}

	private static void SectionVerticalBounds(
		MineSection section,
		int x,
		int shell,
		out int outerTop,
		out int outerBottom,
		out int innerTop,
		out int innerBottom)
	{
		int radiusX = section.Area.Width / 2 - 2;
		int radiusY = section.Area.Height / 2 - 2;
		int offsetX = x - section.Area.Center.X;
		double normalizedX = offsetX / (double)Math.Max(1, radiusX);
		int halfHeight = Math.Max(3, (int)Math.Round(radiusY * Math.Sqrt(Math.Max(0d, 1d - normalizedX * normalizedX))));
		int seed = section.Id * 193 ^ section.Center.X ^ section.Center.Y * 31;
		int topJitter = OrganicBoundary.Profile(x, seed ^ 0x544F_5059, 31, 7, 4, 2);
		int bottomJitter = OrganicBoundary.Profile(x, seed ^ 0x424F_5459, 37, 9, 4, 2);
		outerTop = section.Area.Center.Y - halfHeight + topJitter;
		outerBottom = section.Area.Center.Y + halfHeight + bottomJitter;
		int shellJitter = OrganicBoundary.Profile(x, seed ^ 0x5348_454C, 19, 7, 1, 1);
		int localShell = Math.Clamp(shell + shellJitter, Math.Max(1, shell - 1), shell + 2);
		innerTop = outerTop + localShell;
		innerBottom = outerBottom - localShell;
	}

	internal static bool IsInsideSectionInterior(MineSection section, int x, int y)
	{
		if (section.Kind == MineSectionKind.Workyard
			|| x <= section.Area.Left || x >= section.Area.Right - 1) {
			return false;
		}
		int shell = section.Kind == MineSectionKind.SealedEvil ? 5 : 2;
		SectionVerticalBounds(section, x, shell, out _, out _, out int innerTop, out int innerBottom);
		return y >= innerTop && y <= innerBottom;
	}

	private static bool IsInsideWorkyardWall(MineSection section, int x, int y)
	{
		int floorY = section.Center.Y + 1;
		int seed = section.Id * 193 ^ section.Center.X ^ section.Center.Y * 31;
		int leftBoundary = section.Center.X + 5 + OrganicBoundary.Profile(
			y,
			seed ^ 0x574C_4546,
			13,
			4,
			4,
			1);
		int topBoundary = floorY - 10 + OrganicBoundary.Profile(
			x,
			seed ^ 0x5754_4F50,
			19,
			5,
			3,
			1);
		return x > leftBoundary && x < section.Area.Right - 3 && y > topBoundary && y < floorY;
	}

	private static MinePalette ResolveRoutePalette(
		SurfaceMinePlan plan,
		MineRoute route,
		int centerlineIndex,
		int x,
		int y)
	{
		int shift = OrganicBoundary.Profile(
			y,
			route.VariationSeed ^ 0x5254_4859,
			19,
			5,
			5,
			2);
		shift += OrganicBoundary.Profile(
			x,
			route.VariationSeed ^ 0x5254_4858,
			31,
			7,
			2,
			1);
		int probeIndex = Math.Clamp(centerlineIndex + shift, 0, route.Centerline.Count - 1);
		return ResolvePalette(plan.ThemeAt(route.Centerline[probeIndex]));
	}

	internal static bool IsBiomeWall(BiomeKind biome, ushort wallType)
	{
		MinePalette palette = ResolvePalette(biome);
		return wallType == palette.PrimaryWall || wallType == palette.SecondaryWall;
	}

	private static MinePalette ResolvePalette(BiomeKind biome) => biome switch {
		BiomeKind.Snow => new MinePalette(
			TileID.BorealWood, TileID.BorealWood, WallID.IceUnsafe, WallID.SnowWallUnsafe,
			19, TileID.Tables, 28, 30, 23, 25),
		BiomeKind.Desert => new MinePalette(
			TileID.PalmWood, TileID.SandstoneBrick, WallID.Sandstone, WallID.HardenedSand,
			42, TileID.Tables2, 7, 43, 39, 39),
		BiomeKind.Jungle => new MinePalette(
			TileID.RichMahogany, TileID.LivingMahogany, WallID.JungleUnsafe, WallID.JungleUnsafe2,
			2, TileID.Tables, 2, 3, 2, 12),
		BiomeKind.Evil when WorldGen.crimson => new MinePalette(
			TileID.LivingWood, TileID.CrimstoneBrick, WallID.CrimstoneUnsafe, WallID.CrimsonUnsafe2,
			0, TileID.Tables, 0, 0, 0, 0),
		BiomeKind.Evil => new MinePalette(
			TileID.LivingWood, TileID.EbonstoneBrick, WallID.EbonstoneUnsafe, WallID.CorruptionUnsafe2,
			0, TileID.Tables, 0, 0, 0, 0),
		BiomeKind.Mushroom => new MinePalette(
			TileID.MushroomBlock, TileID.MushroomBlock, WallID.MushroomUnsafe, WallID.MushroomUnsafe,
			18, TileID.Tables, 27, 9, 7, 24),
		BiomeKind.Underworld => new MinePalette(
			TileID.AshWood, TileID.AshWood, WallID.HellstoneBrickUnsafe, WallID.ObsidianBrickUnsafe,
			0, TileID.Tables, 0, 0, 0, 0),
		BiomeKind.Cavern => new MinePalette(
			TileID.LivingWood, TileID.GrayBrick, WallID.CaveUnsafe, WallID.Cave2Unsafe,
			0, TileID.Tables, 0, 0, 0, 0),
		_ => new MinePalette(
			TileID.LivingWood, TileID.WoodBlock, WallID.DirtUnsafe, WallID.LivingWoodUnsafe,
			0, TileID.Tables, 0, 0, 0, 0)
	};

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

	private readonly record struct MinePalette(
		ushort Timber,
		ushort Masonry,
		ushort PrimaryWall,
		ushort SecondaryWall,
		int PlatformStyle,
		ushort TableTile,
		int TableStyle,
		int ChairStyle,
		int WorkbenchStyle,
		int BookcaseStyle);
}
