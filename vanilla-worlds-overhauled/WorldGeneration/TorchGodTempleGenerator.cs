using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace VanillaWorldsOverhauled.WorldGeneration;

internal static class TorchGodTempleGenerator
{
	internal const int RequiredLitTorches = 100;
	internal const int TriggerRadius = 40;
	internal const int MinimumPlatformTiles = 72;
	internal const int MinimumWideArenaRows = 12;
	internal const double MinimumArenaOpenRatio = 0.82d;
	internal const int MinimumUnsafeWallCells = 120;
	internal const double MinimumMasonryWallRatio = 0.65d;

	private const int TempleSeedSalt = 0x5447_4F44;
	private const int RandomCandidateBudget = 420;
	private const int CandidateVarietyTarget = 12;
	private const int MaximumEntranceReach = 38;

	public static TorchGodTemplePlan Plan(WorldPlan worldPlan, SurfaceMinePlan minePlan, GenerationManifest manifest)
	{
		UnifiedRandom random = new(MixSeed(worldPlan.GenerationSeed, TempleSeedSalt));
		Dictionary<string, int> rejected = [];
		int minimumX = worldPlan.CoastMargin + 150;
		int maximumX = Main.maxTilesX - worldPlan.CoastMargin - 151;
		int minimumY = Math.Max((int)Main.worldSurface + 72, (int)Main.rockLayer - 100);
		int maximumY = Math.Min(Main.UnderworldLayer - 150, (int)Main.rockLayer + 380);
		if (minimumX >= maximumX || minimumY >= maximumY) {
			throw new InvalidOperationException("Vanilla Worlds Overhauled could not define an underground search region for the Torch God temple.");
		}

		Dictionary<(TorchTempleTheme Theme, TorchTempleLayout Layout), TorchGodTemplePlan> variedCandidates = [];
		for (int attempt = 0; attempt < RandomCandidateBudget; attempt++) {
			int centerX = random.Next(minimumX, maximumX + 1);
			int centerY = random.Next(minimumY, maximumY + 1);
			TorchTempleLayout layout = (TorchTempleLayout)random.Next(4);
			int featureSeed = random.Next();
			if (TryCreateCandidate(
				worldPlan,
				minePlan,
				manifest,
				centerX,
				centerY,
				layout,
				featureSeed,
				out TorchGodTemplePlan accepted,
				out string reason)) {
				variedCandidates.TryAdd((accepted.Theme, accepted.Layout), accepted);
				if (variedCandidates.Count >= CandidateVarietyTarget) {
					break;
				}
				continue;
			}
			rejected[reason] = rejected.GetValueOrDefault(reason) + 1;
		}
		if (variedCandidates.Count > 0) {
			TorchTempleLayout preferredLayout = (TorchTempleLayout)(
				MixSeed(worldPlan.GenerationSeed, TempleSeedSalt) % 4);
			IEnumerable<TorchGodTemplePlan> preferredCandidates = variedCandidates.Values
				.Where(candidate => candidate.Layout == preferredLayout);
			if (!preferredCandidates.Any()) {
				preferredCandidates = variedCandidates.Values;
			}
			List<TorchGodTemplePlan> orderedCandidates = preferredCandidates
				.OrderBy(candidate => candidate.Theme)
				.ThenBy(candidate => candidate.Layout)
				.ToList();
			int selectedIndex = MixSeed(worldPlan.GenerationSeed, 0x5641_5259) % orderedCandidates.Count;
			return orderedCandidates[selectedIndex];
		}

		int xOffset = Math.Abs(MixSeed(worldPlan.GenerationSeed, 0x5847_5244)) % 47;
		int yOffset = Math.Abs(MixSeed(worldPlan.GenerationSeed, 0x5947_5244)) % 31;
		int layoutIndex = Math.Abs(MixSeed(worldPlan.GenerationSeed, 0x4C41_594F)) % 4;
		for (int centerY = minimumY + yOffset; centerY <= maximumY; centerY += 31) {
			for (int centerX = minimumX + xOffset; centerX <= maximumX; centerX += 47) {
				TorchTempleLayout layout = (TorchTempleLayout)layoutIndex;
				layoutIndex = (layoutIndex + 1) % 4;
				int featureSeed = MixSeed(worldPlan.GenerationSeed, centerX * 397 ^ centerY * 977 ^ (int)layout);
				if (TryCreateCandidate(
					worldPlan,
					minePlan,
					manifest,
					centerX,
					centerY,
					layout,
					featureSeed,
					out TorchGodTemplePlan accepted,
					out string reason)) {
					return accepted;
				}
				rejected[reason] = rejected.GetValueOrDefault(reason) + 1;
			}
		}

		string summary = string.Join(", ", rejected
			.OrderByDescending(pair => pair.Value)
			.Take(8)
			.Select(pair => $"{pair.Key}={pair.Value}"));
		throw new InvalidOperationException(
			"Vanilla Worlds Overhauled could not place the guaranteed underground Torch God temple. " + summary);
	}

	public static void BuildAndProtect(TorchGodTemplePlan plan)
	{
		BuildGeometry(plan);
		TileEditor.Frame(plan.ProtectedArea, border: 3);
		GenVars.structures.AddProtectedStructure(plan.ProtectedArea, padding: 4);
	}

	public static void FinishAndArm(TorchGodTemplePlan plan, GenerationManifest manifest)
	{
		if (HasLiquidIngressRisk(plan.BodyArea, plan.Entrances)) {
			throw new InvalidOperationException(
				$"A water or lava reservoir entered the dry buffer around the Torch God temple at {plan.ActivationPoint} after planning.");
		}
		RejectUnexpectedCompanionRecords(plan);
		ClearTorchTiles(ActivationArea(plan.ActivationPoint));
		BuildGeometry(plan);
		TemplePalette palette = ResolvePalette(plan.Theme);
		int furniture = Furnish(plan, palette);
		PlaceTorchChest(plan, palette);
		int torchCount = PlaceTorchArray(plan, palette);
		TileEditor.Frame(plan.ProtectedArea, border: 3);
		torchCount = CountLitTorches(ActivationArea(plan.ActivationPoint));
		if (torchCount != RequiredLitTorches) {
			throw new InvalidOperationException(
				$"Torch God temple at {plan.ActivationPoint} retained {torchCount} lit torches; expected exactly {RequiredLitTorches}.");
		}

		manifest.TorchGodTemple = new TorchGodTempleRecord(
			plan.ProtectedArea,
			plan.ActivationPoint,
			plan.ChestTopLeft,
			plan.MissingTorch,
			plan.Layout,
			plan.Theme,
			torchCount,
			furniture,
			plan.Entrances.Count,
			CountTempleBricks(plan.BodyArea, palette));
	}

	internal static Rectangle ActivationArea(Point activationPoint) => new(
		activationPoint.X - TriggerRadius,
		activationPoint.Y - TriggerRadius,
		TriggerRadius * 2 + 1,
		TriggerRadius * 2 + 1);

	internal static int CountLitTorches(Rectangle area)
	{
		int count = 0;
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				if (!WorldGen.InWorld(x, y, 2)) {
					continue;
				}
				Tile tile = Main.tile[x, y];
				if (tile.HasTile && TileID.Sets.Torch[tile.TileType] && tile.TileFrameX < 66) {
					count++;
				}
			}
		}
		return count;
	}

	internal static int CountOrdinaryTorchesInChest(Point chestTopLeft)
	{
		Chest? chest = Main.chest.FirstOrDefault(candidate =>
			candidate is not null
			&& candidate.x == chestTopLeft.X
			&& candidate.y == chestTopLeft.Y);
		return chest is null
			? -1
			: chest.item
				.Where(item => item is not null && item.type == ItemID.Torch)
				.Sum(item => item.stack);
	}

	internal static bool IsTempleBrick(TorchTempleTheme theme, ushort tileType)
	{
		TemplePalette palette = ResolvePalette(theme);
		return tileType == palette.PrimaryBrick || tileType == palette.SecondaryBrick;
	}

	internal static bool IsTempleMasonryWall(TorchTempleTheme theme, ushort wallType)
	{
		TemplePalette palette = ResolvePalette(theme);
		return wallType == palette.PrimaryWall || wallType == palette.SecondaryWall;
	}

	internal static bool IsTempleUnsafeWall(TorchTempleTheme theme, ushort wallType) =>
		wallType == ResolvePalette(theme).UnsafeWall;

	internal static bool HasValidWallSemantics(TorchTempleTheme theme)
	{
		TemplePalette palette = ResolvePalette(theme);
		return palette.PrimaryWall != WallID.None
			&& palette.SecondaryWall != WallID.None
			&& palette.UnsafeWall != WallID.None
			&& Main.wallHouse[palette.PrimaryWall]
			&& Main.wallHouse[palette.SecondaryWall]
			&& !Main.wallHouse[palette.UnsafeWall];
	}

	internal static bool ContainsBodyCell(TorchGodTemplePlan plan, int x, int y) =>
		IsInsideBodyShape(plan, x, y);

	internal static int ExpectedTorchStyle(TorchTempleTheme theme) => ResolvePalette(theme).TorchStyle;

	internal static Rectangle ArenaInterior(TorchGodTemplePlan plan)
	{
		Rectangle body = plan.BodyArea;
		int left = body.Left + 12;
		int top = body.Top + body.Height / 3;
		int right = body.Right - 12;
		int bottom = MainFloorY(plan);
		return new Rectangle(left, top, right - left, bottom - top);
	}

	private static bool TryCreateCandidate(
		WorldPlan worldPlan,
		SurfaceMinePlan minePlan,
		GenerationManifest manifest,
		int centerX,
		int centerY,
		TorchTempleLayout layout,
		int featureSeed,
		out TorchGodTemplePlan candidate,
		out string reason)
	{
		(int width, int height, int shellThickness) = ResolveDimensions(layout, featureSeed);
		Rectangle body = new(centerX - width / 2, centerY - height / 2, width, height);
		int mainFloorY = body.Bottom - 7;
		int altarSupportY = mainFloorY - 1;
		Point activationPoint = new(centerX, altarSupportY - 4);
		Point chestTopLeft = new(centerX - 1, altarSupportY - 2);
		Point missingTorch = new(centerX + 3, altarSupportY - 1);
		Rectangle triggerArea = ActivationArea(activationPoint);

		if (!WorldGen.InWorld(triggerArea.Left, triggerArea.Top, 30)
			|| !WorldGen.InWorld(triggerArea.Right - 1, triggerArea.Bottom - 1, 30)
			|| body.Top <= Main.worldSurface + 24
			|| body.Bottom >= Main.UnderworldLayer - 90) {
			candidate = null!;
			reason = "depth or world edge";
			return false;
		}
		if (Math.Abs(centerX - worldPlan.SpawnX) < 250 || Math.Abs(centerX - GenVars.dungeonX) < 190) {
			candidate = null!;
			reason = "spawn or Dungeon approach";
			return false;
		}

		List<TorchTempleEntrance> entrances = [];
		foreach (int direction in OrderedEntranceDirections(featureSeed)) {
			Point doorway = new(direction < 0 ? body.Left + 2 : body.Right - 3, mainFloorY - 4);
			if (TryFindCaveTarget(body, doorway, direction, featureSeed, out Point target)) {
				entrances.Add(new TorchTempleEntrance(
					doorway,
					target,
					MixSeed(featureSeed, direction < 0 ? 0x4C45_4654 : 0x5249_4748)));
			}
			if (entrances.Count == 1 && (featureSeed & 3) != 0) {
				break;
			}
		}
		if (entrances.Count == 0) {
			candidate = null!;
			reason = "no nearby natural cave";
			return false;
		}

		Rectangle protectedArea = Rectangle.Union(triggerArea, body);
		foreach (TorchTempleEntrance entrance in entrances) {
			protectedArea = Rectangle.Union(protectedArea, CenteredBetween(entrance.Doorway, entrance.CaveTarget, 7));
		}
		protectedArea.Inflate(3, 3);
		candidate = new TorchGodTemplePlan(
			featureSeed,
			body,
			protectedArea,
			activationPoint,
			chestTopLeft,
			missingTorch,
			layout,
			ResolveTheme(centerX, centerY),
			shellThickness,
			entrances);

		if (Inflated(minePlan.Area, 30).Intersects(protectedArea)
			|| manifest.Terraces.Any(terrace => Inflated(terrace.Area, 30).Intersects(protectedArea))
			|| manifest.Valleys.Any(valley => Inflated(valley.Area, 20).Intersects(protectedArea))
			|| manifest.ForestLakeBridges.Any(bridge => Inflated(bridge.Area, 24).Intersects(protectedArea))
			|| manifest.MountainWaters.Any(water => Inflated(water.Area, 20).Intersects(protectedArea))
			|| manifest.SkyHighlands.Any(highland => Inflated(highland.Area, 20).Intersects(protectedArea))) {
			reason = "owned feature overlap";
			return false;
		}
		if (!TileEditor.IsSafeForTerrainFeature(body)
			|| entrances.Any(entrance => !TileEditor.IsSafeForTerrainFeature(
				CenteredBetween(entrance.Doorway, entrance.CaveTarget, 5)))) {
			reason = "building or passage progression object";
			return false;
		}
		if (!IsSafeActivationEnvelope(triggerArea)) {
			reason = "activation envelope progression object, wiring, chest, or housing wall";
			return false;
		}
		if (!TileEditor.IsClearOfTempleAndDungeon(protectedArea, margin: 38)) {
			reason = "Dungeon or Jungle Temple envelope";
			return false;
		}
		if (HasLiquidIngressRisk(body, entrances)) {
			reason = "nearby liquid reservoir";
			return false;
		}
		if (ContainsExcludedVanillaFeature(protectedArea)) {
			reason = "vanilla landmark or hive";
			return false;
		}
		if (!GenVars.structures.CanPlace(body, padding: 8)) {
			reason = "StructureMap reservation";
			return false;
		}
		if (SolidRatio(body) < 0.34d) {
			reason = "insufficient underground host";
			return false;
		}

		reason = string.Empty;
		return true;
	}

	private static (int Width, int Height, int ShellThickness) ResolveDimensions(
		TorchTempleLayout layout,
		int featureSeed)
	{
		int widthJitter = Math.Abs(MixSeed(featureSeed, 0x5749_4454)) % 3 * 2;
		int heightJitter = Math.Abs(MixSeed(featureSeed, 0x4845_4947)) % 3 * 2;
		return layout switch {
			TorchTempleLayout.SunkenBasilica => (75 + widthJitter, 49 + heightJitter, 3),
			TorchTempleLayout.TwinSanctum => (79 + widthJitter, 51 + heightJitter, 3),
			TorchTempleLayout.SteppedReliquary => (73 + widthJitter, 53 + heightJitter, 2 + Math.Abs(featureSeed) % 2),
			TorchTempleLayout.CrucibleVault => (71 + widthJitter, 49 + heightJitter, 3),
			_ => throw new ArgumentOutOfRangeException(nameof(layout), layout, null)
		};
	}

	private static IEnumerable<int> OrderedEntranceDirections(int seed)
	{
		if ((seed & 1) == 0) {
			yield return -1;
			yield return 1;
		}
		else {
			yield return 1;
			yield return -1;
		}
	}

	private static bool TryFindCaveTarget(
		Rectangle body,
		Point doorway,
		int direction,
		int seed,
		out Point target)
	{
		Point? best = null;
		int bestScore = int.MaxValue;
		for (int distance = 7; distance <= MaximumEntranceReach; distance++) {
			int x = direction < 0 ? body.Left - distance : body.Right - 1 + distance;
			for (int offsetY = -13; offsetY <= 11; offsetY++) {
				int y = doorway.Y + offsetY;
				if (!IsOpenCavePocket(x, y)) {
					continue;
				}
				int score = distance * 5 + Math.Abs(offsetY) * 2
					+ Math.Abs(MixSeed(seed, x * 31 ^ y * 17)) % 7;
				if (score < bestScore) {
					best = new Point(x, y);
					bestScore = score;
				}
			}
		}
		target = best ?? default;
		return best is not null;
	}

	private static bool IsOpenCavePocket(int centerX, int centerY)
	{
		if (!WorldGen.InWorld(centerX, centerY, 45)) {
			return false;
		}
		int open = 0;
		int walled = 0;
		for (int x = centerX - 2; x <= centerX + 2; x++) {
			for (int y = centerY - 3; y <= centerY + 2; y++) {
				Tile tile = Main.tile[x, y];
				open += !TileEditor.IsSolid(x, y) && tile.LiquidAmount == 0 ? 1 : 0;
				walled += tile.WallType != WallID.None ? 1 : 0;
			}
		}
		return open >= 24 && walled >= 8;
	}

	private static void BuildGeometry(TorchGodTemplePlan plan)
	{
		TemplePalette palette = ResolvePalette(plan.Theme);
		BuildBody(plan, palette);
		BuildArchitecture(plan, palette);
		foreach (TorchTempleEntrance entrance in plan.Entrances) {
			BuildEntrancePassage(plan, entrance, palette);
		}
		BuildAltarAndEmptySocket(plan, palette);
		BuildInteriorApproaches(plan, palette);
	}

	private static void BuildBody(TorchGodTemplePlan plan, TemplePalette palette)
	{
		Rectangle body = plan.BodyArea;
		for (int x = body.Left; x < body.Right; x++) {
			for (int y = body.Top; y < body.Bottom; y++) {
				if (!IsInsideBodyShape(plan, x, y)) {
					continue;
				}

				if (IsInteriorCell(plan, x, y)) {
					TileEditor.ClearTerrain(x, y);
					TileEditor.SetWall(x, y, TempleWallAt(plan, palette, x, y));
				}
				else {
					ushort material = OrganicBoundary.Field(
						x,
						y,
						plan.FeatureSeed ^ 0x4252_4943,
						17,
						6) > 0.68d
						? palette.SecondaryBrick
						: palette.PrimaryBrick;
					TileEditor.SetTerrain(x, y, material);
					TileEditor.SetWall(x, y, TempleWallAt(plan, palette, x, y));
				}
			}
		}

		int floorY = MainFloorY(plan);
		for (int x = body.Left - 6; x <= body.Right + 5; x++) {
			int edgeDistance = Math.Min(Math.Abs(x - body.Left), Math.Abs(x - (body.Right - 1)));
			int depth = 3 + Math.Max(0, OrganicBoundary.Profile(
				x,
				plan.FeatureSeed ^ 0x464F_554E,
				19,
				7,
				4,
				2));
			if (x < body.Left || x >= body.Right) {
				depth = Math.Max(1, depth - Math.Max(0, 5 - edgeDistance));
			}
			for (int y = floorY + 2; y <= Math.Min(plan.ProtectedArea.Bottom - 2, floorY + 2 + depth); y++) {
				if (!WorldGen.InWorld(x, y, 4) || TileEditor.IsProgressionTile(Main.tile[x, y])) {
					continue;
				}
				double field = OrganicBoundary.Field(x, y, plan.FeatureSeed ^ 0x4642_4C44, 13, 5);
				if (field > 0.24d || x >= body.Left + 3 && x < body.Right - 3) {
					TileEditor.SetTerrain(x, y, field > 0.72d ? palette.SecondaryBrick : palette.PrimaryBrick);
				}
			}
		}
	}

	private static bool IsInsideBodyShape(TorchGodTemplePlan plan, int x, int y)
	{
		Rectangle body = plan.BodyArea;
		if (!body.Contains(x, y)) {
			return false;
		}
		int halfWidth = body.Width / 2;
		int dx = Math.Abs(x - body.Center.X);
		int row = y - body.Top;
		int height = body.Height;
		int broadHalfWidth = plan.Layout switch {
			TorchTempleLayout.SunkenBasilica => row < height / 3
				? 8 + (halfWidth - 9) * row / Math.Max(1, height / 3)
				: halfWidth - 2,
			TorchTempleLayout.TwinSanctum => row < height / 4
				? halfWidth - 9 + Math.Abs(x - body.Center.X) / 5
				: row < height / 2 ? halfWidth - 5 : halfWidth - 2,
			TorchTempleLayout.SteppedReliquary => row < height / 5
				? halfWidth - 11
				: row < height * 2 / 5 ? halfWidth - 7
				: row < height * 3 / 5 ? halfWidth - 4 : halfWidth - 2,
			TorchTempleLayout.CrucibleVault => Math.Max(
				10,
				halfWidth - 2 - Math.Abs(row - height * 3 / 5) * (halfWidth - 12) / Math.Max(1, height * 3 / 5)),
			_ => halfWidth - 2
		};
		int jitter = OrganicBoundary.Profile(
			y,
			plan.FeatureSeed ^ 0x5348_4150,
			11,
			4,
			2,
			1);
		return dx <= Math.Clamp(broadHalfWidth + jitter, 8, halfWidth - 1);
	}

	private static bool IsInteriorCell(TorchGodTemplePlan plan, int x, int y) =>
		IsInsideBodyInset(plan, x, y, plan.ShellThickness);

	private static bool IsInsideBodyInset(TorchGodTemplePlan plan, int x, int y, int inset)
	{
		for (int offsetX = -inset; offsetX <= inset; offsetX++) {
			for (int offsetY = -inset; offsetY <= inset; offsetY++) {
				if (!IsInsideBodyShape(plan, x + offsetX, y + offsetY)) {
					return false;
				}
			}
		}
		return true;
	}

	private static ushort TempleWallAt(TorchGodTemplePlan plan, TemplePalette palette, int x, int y)
	{
		if (!IsInsideBodyInset(plan, x, y, plan.ShellThickness + 2)) {
			return palette.UnsafeWall;
		}

		int safetySeamX = plan.BodyArea.Center.X + OrganicBoundary.Profile(
			y,
			plan.FeatureSeed ^ 0x5746_4953,
			19,
			7,
			4,
			2);
		double erosion = OrganicBoundary.Field(
			x,
			y,
			plan.FeatureSeed ^ 0x5745_524F,
			29,
			10);
		if (Math.Abs(x - safetySeamX) <= 1 || erosion < 0.2d) {
			return palette.UnsafeWall;
		}

		double material = OrganicBoundary.Field(
			x,
			y,
			plan.FeatureSeed ^ 0x574D_4154,
			17,
			6);
		return material > 0.68d ? palette.SecondaryWall : palette.PrimaryWall;
	}

	private static void BuildArchitecture(TorchGodTemplePlan plan, TemplePalette palette)
	{
		Rectangle body = plan.BodyArea;
		int centerX = plan.BodyArea.Center.X;
		int floorY = MainFloorY(plan);
		int arenaLeft = body.Left + plan.ShellThickness + 2;
		int arenaRight = body.Right - plan.ShellThickness - 3;
		BuildStructuralRun(plan, palette, body.Left + 5, body.Right - 6, floorY, 3);
		BuildColumn(plan, palette, arenaLeft, floorY - 1, floorY - 15, 3);
		BuildColumn(plan, palette, arenaRight - 2, floorY - 1, floorY - 15, 3);

		switch (plan.Layout) {
			case TorchTempleLayout.SunkenBasilica:
				BuildSplitPlatformTier(plan, palette, arenaLeft + 5, arenaRight - 5, floorY - 12, centerX, 7);
				BuildSplitPlatformTier(plan, palette, arenaLeft + 11, arenaRight - 11, floorY - 25, centerX, 4);
				BuildPlatformStair(plan, palette, arenaLeft + 4, floorY - 1, 1, 11);
				BuildPlatformStair(plan, palette, arenaRight - 4, floorY - 1, -1, 11);
				BuildPlatformStair(plan, palette, centerX - 10, floorY - 13, -1, 10);
				BuildPlatformStair(plan, palette, centerX + 10, floorY - 13, 1, 10);
				break;
			case TorchTempleLayout.TwinSanctum:
				BuildPlatformRun(plan, palette, arenaLeft + 5, centerX - 8, floorY - 10);
				BuildPlatformRun(plan, palette, centerX + 8, arenaRight - 5, floorY - 14);
				BuildPlatformRun(plan, palette, arenaLeft + 11, centerX - 6, floorY - 25);
				BuildPlatformRun(plan, palette, centerX + 6, arenaRight - 11, floorY - 25);
				BuildPlatformStair(plan, palette, arenaLeft + 4, floorY - 1, 1, 9);
				BuildPlatformStair(plan, palette, arenaRight - 4, floorY - 1, -1, 13);
				BuildPlatformStair(plan, palette, centerX - 9, floorY - 11, -1, 12);
				BuildPlatformStair(plan, palette, centerX + 9, floorY - 15, 1, 8);
				break;
			case TorchTempleLayout.SteppedReliquary:
				BuildPlatformRun(plan, palette, arenaLeft + 4, centerX - 5, floorY - 8);
				BuildPlatformRun(plan, palette, centerX + 5, arenaRight - 4, floorY - 16);
				BuildPlatformRun(plan, palette, arenaLeft + 10, centerX + 4, floorY - 25);
				BuildPlatformStair(plan, palette, arenaLeft + 3, floorY - 1, 1, 7);
				BuildPlatformStair(plan, palette, arenaRight - 3, floorY - 1, -1, 15);
				BuildPlatformStair(plan, palette, centerX - 7, floorY - 9, -1, 14);
				break;
			case TorchTempleLayout.CrucibleVault:
				BuildSplitPlatformTier(plan, palette, arenaLeft + 4, arenaRight - 4, floorY - 11, centerX, 10);
				BuildSplitPlatformTier(plan, palette, arenaLeft + 8, arenaRight - 8, floorY - 17, centerX, 4);
				BuildSplitPlatformTier(plan, palette, arenaLeft + 12, arenaRight - 12, floorY - 22, centerX, 6);
				BuildPlatformStair(plan, palette, arenaLeft + 4, floorY - 1, 1, 10);
				BuildPlatformStair(plan, palette, arenaRight - 4, floorY - 1, -1, 10);
				BuildPlatformStair(plan, palette, centerX - 12, floorY - 12, -1, 8);
				BuildPlatformStair(plan, palette, centerX + 12, floorY - 12, 1, 8);
				break;
		}

		foreach ((int x, int supportY, int _) in FurnitureLedges(plan)) {
			BuildStructuralRun(plan, palette, x - 4, x + 4, supportY, 2);
		}
	}

	private static void BuildSplitPlatformTier(
		TorchGodTemplePlan plan,
		TemplePalette palette,
		int left,
		int right,
		int y,
		int centerX,
		int centerHalfGap)
	{
		BuildPlatformRun(plan, palette, left, centerX - centerHalfGap - 1, y);
		BuildPlatformRun(plan, palette, centerX + centerHalfGap + 1, right, y);
	}

	private static void BuildPlatformRun(
		TorchGodTemplePlan plan,
		TemplePalette palette,
		int left,
		int right,
		int y)
	{
		for (int x = left; x <= right; x++) {
			if (!IsInteriorCell(plan, x, y)) {
				continue;
			}
			TileEditor.ClearTerrain(x, y);
			TileEditor.SetWall(x, y, TempleWallAt(plan, palette, x, y));
			TileEditor.TryPlacePlatformForced(x, y, palette.PlatformStyle);
		}
	}

	private static void BuildStructuralRun(
		TorchGodTemplePlan plan,
		TemplePalette palette,
		int left,
		int right,
		int top,
		int thickness)
	{
		for (int x = left; x <= right; x++) {
			for (int y = top; y < top + thickness; y++) {
				if (!IsInsideBodyShape(plan, x, y)) {
					continue;
				}
				double field = OrganicBoundary.Field(x, y, plan.FeatureSeed ^ 0x4C45_4447, 11, 4);
				TileEditor.SetTerrain(x, y, field > 0.73d ? palette.SecondaryBrick : palette.PrimaryBrick);
				TileEditor.SetWall(x, y, TempleWallAt(plan, palette, x, y));
			}
		}
	}

	private static void BuildColumn(
		TorchGodTemplePlan plan,
		TemplePalette palette,
		int left,
		int bottom,
		int top,
		int width)
	{
		for (int x = left; x < left + width; x++) {
			for (int y = top; y <= bottom; y++) {
				if (IsInsideBodyShape(plan, x, y)) {
					TileEditor.SetTerrain(x, y, (y + x) % 7 == 0 ? palette.SecondaryBrick : palette.PrimaryBrick);
					TileEditor.SetWall(x, y, TempleWallAt(plan, palette, x, y));
				}
			}
		}
	}

	private static void BuildPlatformStair(
		TorchGodTemplePlan plan,
		TemplePalette palette,
		int startX,
		int startY,
		int direction,
		int steps)
	{
		for (int step = 0; step < steps; step++) {
			int x = startX + direction * step;
			int y = startY - step;
			if (!IsInsideBodyShape(plan, x, y)) {
				continue;
			}
			TileEditor.ClearTerrain(x, y);
			TileEditor.SetWall(x, y, TempleWallAt(plan, palette, x, y));
			TileEditor.TryPlaceSlopedPlatform(
				x,
				y,
				palette.PlatformStyle,
				direction > 0 ? SlopeType.SlopeDownLeft : SlopeType.SlopeDownRight);
		}
	}

	private static void BuildAltarAndEmptySocket(TorchGodTemplePlan plan, TemplePalette palette)
	{
		int altarSupportY = plan.ChestTopLeft.Y + 2;
		int centerX = plan.BodyArea.Center.X;
		for (int x = centerX - 4; x <= centerX + 4; x++) {
			for (int y = altarSupportY; y < MainFloorY(plan); y++) {
				TileEditor.SetTerrain(x, y, Math.Abs(x - centerX) >= 3
					? palette.SecondaryBrick
					: palette.PrimaryBrick);
				TileEditor.SetWall(x, y, TempleWallAt(plan, palette, x, y));
			}
		}

		for (int x = plan.MissingTorch.X - 1; x <= plan.MissingTorch.X + 1; x++) {
			for (int y = plan.MissingTorch.Y - 1; y <= plan.MissingTorch.Y; y++) {
				bool socket = x == plan.MissingTorch.X && y == plan.MissingTorch.Y;
				if (socket) {
					TileEditor.ClearTerrain(x, y);
				}
				else {
					TileEditor.SetTerrain(x, y, palette.SecondaryBrick);
				}
				TileEditor.SetWall(x, y, TempleWallAt(plan, palette, x, y));
			}
		}
		TileEditor.ClearTerrain(plan.MissingTorch.X, plan.MissingTorch.Y);
		TileEditor.SetWall(plan.MissingTorch.X, plan.MissingTorch.Y, palette.UnsafeWall);
	}

	private static void BuildEntrancePassage(
		TorchGodTemplePlan plan,
		TorchTempleEntrance entrance,
		TemplePalette palette)
	{
		int deltaX = entrance.CaveTarget.X - entrance.Doorway.X;
		int steps = Math.Max(1, Math.Abs(deltaX));
		int direction = Math.Sign(deltaX);
		for (int step = 0; step <= steps; step++) {
			double amount = step / (double)steps;
			int x = entrance.Doorway.X + direction * step;
			int y = (int)Math.Round(MathHelper.Lerp(entrance.Doorway.Y, entrance.CaveTarget.Y, (float)amount));
			y += OrganicBoundary.Profile(
				x,
				entrance.VariationSeed,
				13,
				5,
				2,
				1);
			int radiusX = 3 + Math.Abs(MixSeed(entrance.VariationSeed, step * 17)) % 2;
			int radiusY = 3 + Math.Abs(MixSeed(entrance.VariationSeed, step * 31)) % 2;

			for (int offsetX = -radiusX - 2; offsetX <= radiusX + 2; offsetX++) {
				for (int offsetY = -radiusY - 2; offsetY <= radiusY + 2; offsetY++) {
					double distance = offsetX * offsetX / (double)(radiusX * radiusX)
						+ offsetY * offsetY / (double)(radiusY * radiusY);
					int tileX = x + offsetX;
					int tileY = y + offsetY;
					if (!WorldGen.InWorld(tileX, tileY, 4)) {
						continue;
					}
					if (distance <= 1d) {
						TileEditor.ClearTerrain(tileX, tileY);
						if (step < steps - 2) {
							TileEditor.SetWall(tileX, tileY, palette.UnsafeWall);
						}
					}
					else if (distance <= 2.35d && step < steps * 3 / 4
						&& !TileEditor.IsProgressionTile(Main.tile[tileX, tileY])) {
						ushort material = OrganicBoundary.Field(
							tileX,
							tileY,
							entrance.VariationSeed ^ 0x504F_5254,
							9,
							4) > 0.7d
							? palette.SecondaryBrick
							: palette.PrimaryBrick;
						TileEditor.SetTerrain(tileX, tileY, material);
						TileEditor.SetWall(tileX, tileY, palette.UnsafeWall);
					}
				}
			}
		}
		for (int step = 0; step <= steps; step++) {
			double amount = step / (double)steps;
			int x = entrance.Doorway.X + direction * step;
			int y = (int)Math.Round(MathHelper.Lerp(entrance.Doorway.Y, entrance.CaveTarget.Y, (float)amount));
			y += OrganicBoundary.Profile(
				x,
				entrance.VariationSeed,
				13,
				5,
				2,
				1);
			int radiusX = 3 + Math.Abs(MixSeed(entrance.VariationSeed, step * 17)) % 2;
			int radiusY = 3 + Math.Abs(MixSeed(entrance.VariationSeed, step * 31)) % 2;
			for (int offsetX = -radiusX; offsetX <= radiusX; offsetX++) {
				for (int offsetY = -radiusY; offsetY <= radiusY; offsetY++) {
					double distance = offsetX * offsetX / (double)(radiusX * radiusX)
						+ offsetY * offsetY / (double)(radiusY * radiusY);
					if (distance > 1d) {
						continue;
					}
					int tileX = x + offsetX;
					int tileY = y + offsetY;
					if (!WorldGen.InWorld(tileX, tileY, 4)) {
						continue;
					}
					TileEditor.ClearTerrain(tileX, tileY);
					if (step < steps - 2) {
						TileEditor.SetWall(tileX, tileY, palette.UnsafeWall);
					}
				}
			}
		}

		CarveEntrancePortal(entrance.Doorway, palette, addTempleWall: true);
		CarveEntrancePortal(entrance.CaveTarget, palette, addTempleWall: false);
	}

	private static void CarveEntrancePortal(Point center, TemplePalette palette, bool addTempleWall)
	{
		for (int offsetX = -2; offsetX <= 2; offsetX++) {
			for (int offsetY = -3; offsetY <= 2; offsetY++) {
				int x = center.X + offsetX;
				int y = center.Y + offsetY;
				if (!WorldGen.InWorld(x, y, 4)) {
					continue;
				}
				TileEditor.ClearTerrain(x, y);
				if (addTempleWall) {
					TileEditor.SetWall(x, y, palette.UnsafeWall);
				}
			}
		}
	}

	private static void BuildInteriorApproaches(TorchGodTemplePlan plan, TemplePalette palette)
	{
		int shrinePassageY = plan.MissingTorch.Y + 2;
		foreach (TorchTempleEntrance entrance in plan.Entrances) {
			int direction = entrance.Doorway.X < plan.BodyArea.Center.X ? 1 : -1;
			int destinationX = plan.BodyArea.Center.X - direction * 5;
			int steps = Math.Abs(destinationX - entrance.Doorway.X);
			for (int step = 0; step <= steps; step++) {
				double amount = step / (double)Math.Max(1, steps);
				int x = entrance.Doorway.X + direction * step;
				int y = (int)Math.Round(MathHelper.Lerp(entrance.Doorway.Y, shrinePassageY, (float)amount));
				for (int offsetX = -1; offsetX <= 1; offsetX++) {
					for (int offsetY = -2; offsetY <= 2; offsetY++) {
						int tileX = x + offsetX;
						int tileY = y + offsetY;
						if (!WorldGen.InWorld(tileX, tileY, 4) || !plan.BodyArea.Contains(tileX, tileY)) {
							continue;
						}
						TileEditor.ClearTerrain(tileX, tileY);
						TileEditor.SetWall(tileX, tileY, TempleWallAt(plan, palette, tileX, tileY));
					}
				}
			}
		}
	}

	private static int Furnish(TorchGodTemplePlan plan, TemplePalette palette)
	{
		int floorY = MainFloorY(plan);
		int left = plan.BodyArea.Left + plan.ShellThickness + 8;
		int right = plan.BodyArea.Right - plan.ShellThickness - 9;
		int placed = 0;
		placed += TryPlaceFurniture(left, floorY, TileID.WorkBenches, palette.WorkbenchStyle) ? 1 : 0;
		placed += TryPlaceFurniture(left + 8, floorY, TileID.Anvils, 0) ? 1 : 0;
		placed += TryPlaceFurniture(right, floorY, palette.TableTile, palette.TableStyle) ? 1 : 0;
		placed += TryPlaceFurniture(right - 7, floorY, TileID.Chairs, palette.ChairStyle) ? 1 : 0;
		placed += TryPlaceFurniture(right + 5, floorY, TileID.Chairs, palette.ChairStyle) ? 1 : 0;

		foreach ((int x, int supportY, int role) in FurnitureLedges(plan)) {
			(ushort tileType, int style) = (role % 5) switch {
				0 => (TileID.Bookcases, palette.BookcaseStyle),
				1 => (TileID.Benches, 0),
				2 => (TileID.Loom, 0),
				3 => (TileID.Pianos, palette.PianoStyle),
				_ => (TileID.WorkBenches, palette.WorkbenchStyle)
			};
			placed += TryPlaceFurniture(x, supportY, tileType, style) ? 1 : 0;
			if (TileEditor.TryPlaceSmallPile(x + (role % 2 == 0 ? 5 : -5), supportY - 1, Math.Abs(plan.FeatureSeed + role) % 6, 0)) {
				placed++;
			}
		}
		return placed;
	}

	private static IEnumerable<(int X, int SupportY, int Role)> FurnitureLedges(TorchGodTemplePlan plan)
	{
		int leftX = plan.BodyArea.Left + plan.ShellThickness + 8;
		int rightX = plan.BodyArea.Right - plan.ShellThickness - 9;
		int floorY = MainFloorY(plan);
		switch (plan.Layout) {
			case TorchTempleLayout.SunkenBasilica:
				yield return (leftX, floorY - 13, 0);
				yield return (rightX, floorY - 13, 1);
				yield return (leftX + 4, floorY - 26, 2);
				yield return (rightX - 4, floorY - 26, 3);
				break;
			case TorchTempleLayout.TwinSanctum:
				yield return (leftX, floorY - 11, 3);
				yield return (rightX, floorY - 15, 0);
				yield return (leftX + 4, floorY - 26, 1);
				yield return (rightX - 4, floorY - 26, 2);
				break;
			case TorchTempleLayout.SteppedReliquary:
				yield return (leftX, floorY - 9, 4);
				yield return (rightX, floorY - 17, 0);
				yield return (leftX + 4, floorY - 26, 3);
				break;
			case TorchTempleLayout.CrucibleVault:
				yield return (leftX, floorY - 12, 2);
				yield return (rightX, floorY - 12, 4);
				yield return (leftX + 5, floorY - 23, 0);
				yield return (rightX - 5, floorY - 23, 1);
				break;
		}
	}

	private static bool TryPlaceFurniture(int preferredX, int supportY, ushort tileType, int style)
	{
		ReadOnlySpan<int> offsets = [0, -3, 3, -6, 6];
		foreach (int offset in offsets) {
			int x = preferredX + offset;
			if (!WorldGen.InWorld(x, supportY, 5) || !TileEditor.IsSolid(x, supportY)) {
				continue;
			}
			for (int clearX = x - 2; clearX <= x + 2; clearX++) {
				for (int clearY = supportY - 5; clearY < supportY; clearY++) {
					TileEditor.ClearTerrain(clearX, clearY);
				}
			}
			WorldGen.PlaceTile(x, supportY - 1, tileType, mute: true, forced: true, plr: -1, style: style);
			if (HasNearbyTile(x, supportY - 2, tileType, 3, 3)) {
				return true;
			}
		}
		return false;
	}

	private static void PlaceTorchChest(TorchGodTemplePlan plan, TemplePalette palette)
	{
		Point topLeft = plan.ChestTopLeft;
		for (int x = topLeft.X; x <= topLeft.X + 1; x++) {
			for (int y = topLeft.Y; y <= topLeft.Y + 1; y++) {
				TileEditor.ClearTerrain(x, y);
				TileEditor.SetWall(x, y, TempleWallAt(plan, palette, x, y));
			}
			TileEditor.SetTerrain(x, topLeft.Y + 2, palette.PrimaryBrick);
		}
		int chestIndex = WorldGen.PlaceChest(
			topLeft.X,
			topLeft.Y + 1,
			palette.ChestTile,
			notNearOtherChests: false,
			palette.ChestStyle);
		if (chestIndex < 0 || Main.chest[chestIndex] is not Chest chest
			|| chest.x != topLeft.X || chest.y != topLeft.Y) {
			throw new InvalidOperationException($"Torch God temple chest failed to place at {topLeft}.");
		}

		chest.item[0].SetDefaults(ItemID.Torch);
		chest.item[0].stack = 1;
	}

	private static int PlaceTorchArray(TorchGodTemplePlan plan, TemplePalette palette)
	{
		List<Point> placed = [];
		foreach (Point point in ShrineTorchSites(plan)) {
			TryPlaceCountedTorch(plan, point, palette.TorchStyle, placed);
		}

		List<Point> candidates = [];
		for (int x = plan.BodyArea.Left + 4; x < plan.BodyArea.Right - 4; x++) {
			for (int y = plan.BodyArea.Top + 4; y < MainFloorY(plan) - 1; y++) {
				Point point = new(x, y);
				if (CanHoldCeremonialTorch(plan, point)) {
					candidates.Add(point);
				}
			}
		}
		candidates.Sort((left, right) => {
			int score = TorchMotifScore(plan, left).CompareTo(TorchMotifScore(plan, right));
			return score != 0
				? score
				: Math.Abs(MixSeed(plan.FeatureSeed, left.X * 397 ^ left.Y * 977))
					.CompareTo(Math.Abs(MixSeed(plan.FeatureSeed, right.X * 397 ^ right.Y * 977)));
		});

		foreach (int spacing in new[] { 2, 1, 0 }) {
			foreach (Point point in candidates) {
				if (placed.Count >= RequiredLitTorches) {
					break;
				}
				if (spacing > 0 && placed.Any(existing =>
					Math.Abs(existing.X - point.X) <= spacing
					&& Math.Abs(existing.Y - point.Y) <= spacing)) {
					continue;
				}
				TryPlaceCountedTorch(plan, point, palette.TorchStyle, placed);
			}
		}
		if (placed.Count != RequiredLitTorches) {
			throw new InvalidOperationException(
				$"Torch God temple {plan.Layout} at {plan.ActivationPoint} placed only {placed.Count}/{RequiredLitTorches} torches.");
		}
		return placed.Count;
	}

	private static IEnumerable<Point> ShrineTorchSites(TorchGodTemplePlan plan)
	{
		Point socket = plan.MissingTorch;
		Point[] offsets = [
			new(-4, -2), new(-2, -2), new(2, -2), new(4, -2),
			new(-4, 0), new(4, 0),
			new(-4, 2), new(-2, 2), new(2, 2), new(4, 2)
		];
		foreach (Point offset in offsets) {
			yield return socket + offset;
		}
	}

	private static bool CanHoldCeremonialTorch(TorchGodTemplePlan plan, Point point)
	{
		if (point == plan.MissingTorch
			|| !IsInsideBodyShape(plan, point.X, point.Y)
			|| Math.Abs(point.X - plan.ActivationPoint.X) > 34
			|| Math.Abs(point.Y - plan.ActivationPoint.Y) > 34
			|| Math.Abs(point.X - plan.ChestTopLeft.X) <= 4
				&& Math.Abs(point.Y - plan.ChestTopLeft.Y) <= 4) {
			return false;
		}
		Tile tile = Main.tile[point.X, point.Y];
		return !tile.HasTile && tile.LiquidAmount == 0 && tile.WallType != WallID.None;
	}

	private static int TorchMotifScore(TorchGodTemplePlan plan, Point point)
	{
		int dx = point.X - plan.ActivationPoint.X;
		int dy = point.Y - plan.ActivationPoint.Y;
		int hashJitter = Math.Abs(MixSeed(plan.FeatureSeed ^ 0x4D4F_5446, point.X * 31 ^ point.Y * 17)) % 9;
		int motif = plan.Layout switch {
			TorchTempleLayout.SunkenBasilica => Math.Abs(dx * dx * 3 + dy * dy * 7 - 1350) / 18,
			TorchTempleLayout.TwinSanctum => Math.Min(
				Math.Abs((dx - 17) * (dx - 17) * 2 + dy * dy * 5 - 720),
				Math.Abs((dx + 17) * (dx + 17) * 2 + dy * dy * 5 - 720)) / 15,
			TorchTempleLayout.SteppedReliquary => Math.Abs(Math.Abs(dy + 5) * 4 - Math.Abs(dx) * 3 - 18),
			TorchTempleLayout.CrucibleVault => Math.Abs(Math.Abs(dx) * 2 + Math.Abs(dy + 3) * 3 - 58),
			_ => Math.Abs(dx) + Math.Abs(dy)
		};
		return motif + hashJitter;
	}

	private static void TryPlaceCountedTorch(
		TorchGodTemplePlan plan,
		Point point,
		int style,
		List<Point> placed)
	{
		if (placed.Count >= RequiredLitTorches
			|| Main.tile[point.X, point.Y].HasTile
			|| !HasStableTorchAnchor(plan, point)) {
			return;
		}
		TileEditor.TryPlaceTorch(point.X, point.Y, style);
		Tile tile = Main.tile[point.X, point.Y];
		if (tile.HasTile && TileID.Sets.Torch[tile.TileType] && tile.TileFrameX < 66) {
			placed.Add(point);
		}
	}

	private static bool HasStableTorchAnchor(TorchGodTemplePlan plan, Point point) =>
		IsTempleAnchor(plan, point.X - 1, point.Y)
		|| IsTempleAnchor(plan, point.X + 1, point.Y)
		|| IsTempleAnchor(plan, point.X, point.Y + 1);

	private static bool IsTempleAnchor(TorchGodTemplePlan plan, int x, int y)
	{
		if (!plan.BodyArea.Contains(x, y)) {
			return false;
		}
		Tile tile = Main.tile[x, y];
		return (tile.HasUnactuatedTile && tile.TileType == TileID.Platforms)
			|| (TileEditor.IsSolid(x, y) && IsTempleBrick(plan.Theme, tile.TileType));
	}

	private static TorchTempleTheme ResolveTheme(int centerX, int centerY)
	{
		Dictionary<TorchTempleTheme, int> scores = [];
		for (int x = centerX - 28; x <= centerX + 28; x += 2) {
			for (int y = centerY - 22; y <= centerY + 22; y += 2) {
				Tile tile = Main.tile[x, y];
				if (!tile.HasTile) {
					continue;
				}
				TorchTempleTheme theme = ThemeForTile(tile.TileType, x, y);
				int weight = theme is TorchTempleTheme.ForestStone or TorchTempleTheme.Cavern ? 1 : 5;
				scores[theme] = scores.GetValueOrDefault(theme) + weight;
			}
		}
		TorchTempleTheme fallback = centerY > Main.rockLayer ? TorchTempleTheme.Cavern : TorchTempleTheme.ForestStone;
		return scores
			.OrderByDescending(pair => pair.Value)
			.ThenBy(pair => pair.Key)
			.Select(pair => pair.Key)
			.FirstOrDefault(fallback);
	}

	private static TorchTempleTheme ThemeForTile(ushort tileType, int x, int y)
	{
		if (tileType is TileID.Granite or TileID.GraniteBlock) {
			return TorchTempleTheme.Granite;
		}
		if (tileType is TileID.Marble or TileID.MarbleBlock) {
			return TorchTempleTheme.Marble;
		}
		if (tileType is TileID.Crimstone or TileID.CrimstoneBrick or TileID.CrimsonGrass
			or TileID.Crimsand or TileID.CrimsonSandstone or TileID.CrimsonHardenedSand) {
			return TorchTempleTheme.Crimson;
		}
		if (tileType is TileID.Ebonstone or TileID.EbonstoneBrick or TileID.CorruptGrass
			or TileID.Ebonsand or TileID.CorruptSandstone or TileID.CorruptHardenedSand) {
			return TorchTempleTheme.Corrupt;
		}
		return BiomeClassifier.ClassifySupport(tileType, x, y) switch {
			BiomeKind.Snow => TorchTempleTheme.Ice,
			BiomeKind.Desert => TorchTempleTheme.Desert,
			BiomeKind.Jungle => TorchTempleTheme.Jungle,
			BiomeKind.Mushroom => TorchTempleTheme.Mushroom,
			BiomeKind.Evil => WorldGen.crimson ? TorchTempleTheme.Crimson : TorchTempleTheme.Corrupt,
			BiomeKind.Cavern => TorchTempleTheme.Cavern,
			_ => TorchTempleTheme.ForestStone
		};
	}

	private static TemplePalette ResolvePalette(TorchTempleTheme theme) => theme switch
	{
		TorchTempleTheme.ForestStone => new(
			TileID.GrayBrick, TileID.StoneSlab, WallID.GrayBrick, WallID.StoneSlab, WallID.CaveUnsafe, 0, TorchID.Torch,
			TileID.Tables, 0, 0, 0, 0, 0, TileID.Containers, 1),
		TorchTempleTheme.Ice => new(
			TileID.IceBrick, TileID.SnowBrick, WallID.IceBrick, WallID.SnowBrick, WallID.IceUnsafe, 19, TorchID.Ice,
			TileID.Tables, 28, 30, 23, 25, 23, TileID.Containers, 11),
		TorchTempleTheme.Desert => new(
			TileID.SandstoneBrick, TileID.Mudstone, WallID.SandstoneBrick, WallID.MudstoneBrick, WallID.Sandstone, 42, TorchID.Desert,
			TileID.Tables2, 7, 43, 39, 39, 38, TileID.Containers2, 10),
		TorchTempleTheme.Jungle => new(
			TileID.Mudstone, TileID.RichMahogany, WallID.MudstoneBrick, WallID.RichMaogany, WallID.JungleUnsafe, 2, TorchID.Jungle,
			TileID.Tables, 2, 3, 2, 12, 2, TileID.Containers, 8),
		TorchTempleTheme.Corrupt => new(
			TileID.EbonstoneBrick, TileID.Ebonstone, WallID.EbonstoneBrick, WallID.EbonstoneBrick, WallID.EbonstoneUnsafe, 0, TorchID.Corrupt,
			TileID.Tables, 0, 0, 0, 0, 0, TileID.Containers, 1),
		TorchTempleTheme.Crimson => new(
			TileID.CrimstoneBrick, TileID.Crimstone, WallID.CrimstoneBrick, WallID.CrimstoneBrick, WallID.CrimstoneUnsafe, 0, TorchID.Crimson,
			TileID.Tables, 0, 0, 0, 0, 0, TileID.Containers, 1),
		TorchTempleTheme.Mushroom => new(
			TileID.MushroomBlock, TileID.GrayBrick, WallID.Mushroom, WallID.GrayBrick, WallID.MushroomUnsafe, 18, TorchID.Mushroom,
			TileID.Tables, 27, 9, 7, 24, 22, TileID.Containers, 32),
		TorchTempleTheme.Granite => new(
			TileID.GraniteBlock, TileID.GrayBrick, WallID.GraniteBlock, WallID.GrayBrick, WallID.GraniteUnsafe, 28, TorchID.Bone,
			TileID.Tables, 33, 34, 29, 30, 28, TileID.Containers, 50),
		TorchTempleTheme.Marble => new(
			TileID.MarbleBlock, TileID.StoneSlab, WallID.MarbleBlock, WallID.StoneSlab, WallID.MarbleUnsafe, 29, TorchID.Bone,
			TileID.Tables, 34, 35, 30, 31, 29, TileID.Containers, 51),
		TorchTempleTheme.Cavern => new(
			TileID.StoneSlab, TileID.GrayBrick, WallID.StoneSlab, WallID.GrayBrick, WallID.CaveUnsafe, 0, TorchID.Bone,
			TileID.Tables, 0, 0, 0, 0, 0, TileID.Containers, 1),
		_ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null)
	};

	private static bool ContainsExcludedVanillaFeature(Rectangle area)
	{
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				if (tile.HasTile && tile.TileType is TileID.Hive or TileID.LivingWood or TileID.LeafBlock
					or TileID.SandstoneBrick or TileID.DemonAltar or TileID.ShadowOrbs
					|| tile.WallType is WallID.HiveUnsafe or WallID.LivingWood or WallID.LivingWoodUnsafe
						or WallID.HellstoneBrickUnsafe or WallID.ObsidianBrickUnsafe
					|| tile.LiquidAmount > 0 && tile.LiquidType is LiquidID.Shimmer or LiquidID.Honey) {
					return true;
				}
			}
		}
		return false;
	}

	private static bool ContainsLiquidReservoir(Rectangle area)
	{
		int left = Math.Max(3, area.Left);
		int top = Math.Max(3, area.Top);
		int right = Math.Min(Main.maxTilesX - 3, area.Right);
		int bottom = Math.Min(Main.maxTilesY - 3, area.Bottom);
		for (int x = left; x < right; x++) {
			for (int y = top; y < bottom; y++) {
				if (Main.tile[x, y].LiquidAmount > 0) {
					return true;
				}
			}
		}
		return false;
	}

	private static bool HasLiquidIngressRisk(
		Rectangle body,
		IReadOnlyList<TorchTempleEntrance> entrances)
	{
		if (ContainsLiquidReservoir(Inflated(body, 6))) {
			return true;
		}
		foreach (TorchTempleEntrance entrance in entrances) {
			if (ContainsLiquidReservoir(CenteredBetween(entrance.Doorway, entrance.CaveTarget, 12))) {
				return true;
			}
		}
		return false;
	}

	private static bool IsSafeActivationEnvelope(Rectangle area)
	{
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				if (TileEditor.IsProgressionTile(tile)
					|| tile.RedWire || tile.BlueWire || tile.GreenWire || tile.YellowWire
					|| tile.HasActuator
					|| tile.WallType != WallID.None && Main.wallHouse[tile.WallType]) {
					return false;
				}
			}
		}
		return !Main.chest.Any(chest => chest is not null && area.Contains(chest.x, chest.y));
	}

	private static double SolidRatio(Rectangle area)
	{
		int solid = 0;
		int sampled = 0;
		for (int x = area.Left; x < area.Right; x += 2) {
			for (int y = area.Top; y < area.Bottom; y += 2) {
				sampled++;
				solid += TileEditor.IsSolid(x, y) ? 1 : 0;
			}
		}
		return sampled == 0 ? 0d : solid / (double)sampled;
	}

	private static void RejectUnexpectedCompanionRecords(TorchGodTemplePlan plan)
	{
		foreach (Chest chest in Main.chest) {
			if (chest is not null && plan.BodyArea.Contains(chest.x, chest.y)) {
				throw new InvalidOperationException(
					$"A chest entered the Torch God temple building at {chest.x},{chest.y} before its altar was furnished.");
			}
		}
	}

	private static void ClearTorchTiles(Rectangle area)
	{
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				if (tile.HasTile && TileID.Sets.Torch[tile.TileType]) {
					WorldGen.KillTile(x, y, fail: false, effectOnly: false, noItem: true);
				}
			}
		}
	}

	private static int CountTempleBricks(Rectangle area, TemplePalette palette)
	{
		int count = 0;
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				if (tile.HasTile && tile.TileType is var type
					&& (type == palette.PrimaryBrick || type == palette.SecondaryBrick)) {
					count++;
				}
			}
		}
		return count;
	}

	private static bool HasNearbyTile(int centerX, int centerY, ushort tileType, int radiusX, int radiusY)
	{
		for (int x = centerX - radiusX; x <= centerX + radiusX; x++) {
			for (int y = centerY - radiusY; y <= centerY + radiusY; y++) {
				if (WorldGen.InWorld(x, y, 3) && Main.tile[x, y].HasTile && Main.tile[x, y].TileType == tileType) {
					return true;
				}
			}
		}
		return false;
	}

	private static int MainFloorY(TorchGodTemplePlan plan) => plan.BodyArea.Bottom - 7;

	private static Rectangle CenteredBetween(Point first, Point second, int padding)
	{
		int left = Math.Min(first.X, second.X) - padding;
		int top = Math.Min(first.Y, second.Y) - padding;
		int right = Math.Max(first.X, second.X) + padding + 1;
		int bottom = Math.Max(first.Y, second.Y) + padding + 1;
		return new Rectangle(left, top, right - left, bottom - top);
	}

	private static Rectangle Inflated(Rectangle area, int amount)
	{
		area.Inflate(amount, amount);
		return area;
	}

	private static int MixSeed(int seed, int salt)
	{
		unchecked {
			uint hash = (uint)seed ^ (uint)salt;
			hash ^= hash >> 16;
			hash *= 0x7FEB_352Du;
			hash ^= hash >> 15;
			hash *= 0x846C_A68Bu;
			hash ^= hash >> 16;
			return (int)(hash & 0x7FFF_FFFFu);
		}
	}

	private readonly record struct TemplePalette(
		ushort PrimaryBrick,
		ushort SecondaryBrick,
		ushort PrimaryWall,
		ushort SecondaryWall,
		ushort UnsafeWall,
		int PlatformStyle,
		int TorchStyle,
		ushort TableTile,
		int TableStyle,
		int ChairStyle,
		int WorkbenchStyle,
		int BookcaseStyle,
		int PianoStyle,
		ushort ChestTile,
		int ChestStyle);
}
