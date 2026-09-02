using System;
using System.Collections.Generic;
using System.Linq;
using VanillaWorldsOverhauled.WorldGeneration;
using Terraria.GameContent.Generation;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.WorldBuilding;

namespace VanillaWorldsOverhauled.Systems;

public sealed class VanillaWorldsOverhauledWorldSystem : ModSystem
{
	private WorldPlan? _plan;
	private SurfaceMinePlan? _surfaceMinePlan;
	private TorchGodTemplePlan? _torchGodTemplePlan;
	private GenerationManifest? _manifest;
	private GenerationReport? _report;
	private string? _savedValidationSummary;

	public override void ClearWorld()
	{
		_plan = null;
		_surfaceMinePlan = null;
		_torchGodTemplePlan = null;
		_manifest = null;
		_report = null;
		_savedValidationSummary = null;
	}

	public override void PreWorldGen()
	{
		_plan = null;
		_surfaceMinePlan = null;
		_torchGodTemplePlan = null;
		_manifest = new GenerationManifest();
		_report = null;
		_savedValidationSummary = null;
	}

	public override void ModifyWorldGenTasks(List<GenPass> tasks, ref double totalWeight)
	{
		InsertAfter(tasks, "Terrain", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: plan world skeleton", 8d, Plan), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: plan world skeleton", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: form world skeleton", 85d, ShapeTerrain), ref totalWeight);
		InsertBefore(tasks, "Floating Islands", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: reinforce mountain silhouettes", 55d, ReinforceMountains), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: reinforce mountain silhouettes", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: carve mountain crossings", 32d, CarveMountainCrossings), ref totalWeight);
		InsertAfter(tasks, "Floating Islands", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: form floating highlands", 38d, BuildSkyHighlands), ref totalWeight);
		InsertAfter(tasks, "Wavy Caves", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: carve regional cave routes", 45d, CarveCaves), ref totalWeight);
		InsertAfter(tasks, "Corruption", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: reopen regional cave routes", 24d, RepairCaves), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: reopen regional cave routes", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: reopen mountain crossings", 18d, RepairMountainCrossings), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: reopen mountain crossings", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: blend surface biome seams", 18d, BlendBiomeTransitions), ref totalWeight);
		InsertAfter(tasks, "Shimmer", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: reserve building terraces", 8d, ReserveTerraces), ref totalWeight);
		InsertAfter(tasks, "Hives", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: form mountain valleys", 18d, BuildMountainValleys), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: form mountain valleys", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: form forest lake crossings", 16d, BuildForestWaterCrossings), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: form forest lake crossings", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: form mountain interior waters", 14d, BuildMountainInteriorWaters), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: form mountain interior waters", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: reserve surface mine", 8d, ReserveSurfaceMine), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: reserve surface mine", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: excavate surface mine", 42d, ExcavateSurfaceMine), ref totalWeight);
		InsertAfter(tasks, "Smooth World", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: stabilize routes and terraces", 26d, StabilizeRoutesAndTerraces), ref totalWeight);
		InsertAfter(tasks, "Micro Biomes", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: stabilize summit buttresses", 22d, StabilizeMountainSummits), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: stabilize summit buttresses", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: reopen late regional routes", 18d, RepairLateCaves), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: reopen late regional routes", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: reopen late mountain crossings", 18d, RepairLateMountainCrossings), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: reopen late mountain crossings", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: join mountain ranges", 24d, BuildMountainBridges), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: join mountain ranges", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: build biome landmarks", 30d, PlaceLandmarks), ref totalWeight);
		InsertAfter(tasks, "Stalac", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: scatter biome accents", 12d, AddAccents), ref totalWeight);
		InsertAfter(tasks, "Final Cleanup", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: furnish biome landmarks", 20d, FurnishLandmarks), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: furnish biome landmarks", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: lay connected mine rails", 36d, FurnishSurfaceMine), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: lay connected mine rails", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: decorate mountain interiors", 18d, DecorateMountainInteriors), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: decorate mountain interiors", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: record final features", 8d, RecordFinalFeatures), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: record final features", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: plan Torch God temple", 8d, PlanTorchGodTemple), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: plan Torch God temple", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: build Torch God temple", 26d, BuildTorchGodTemple), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: build Torch God temple", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: light Torch God temple", 18d, FinishTorchGodTemple), ref totalWeight);
		InsertAfter(tasks, "Vanilla Worlds Overhauled: light Torch God temple", new VanillaWorldsOverhauledPass("Vanilla Worlds Overhauled: validate replacement world", 24d, Validate), ref totalWeight);
	}

	public override void PostWorldGen()
	{
		_ = RequirePlan();
		_ = RequireManifest();
		_ = _report ?? throw new InvalidOperationException("Vanilla Worlds Overhauled validation did not run.");
	}

	public override void SaveWorldData(TagCompound tag)
	{
		if (_manifest is null) {
			return;
		}
		string validationSummary = _report?.Summary ?? _savedValidationSummary ?? string.Empty;

		tag["manifestVersion"] = GenerationManifest.CurrentVersion;
		tag["generationSeed"] = _manifest.GenerationSeed;
		tag["validation"] = validationSummary;
		tag["terraces"] = _manifest.Terraces.Select(SerializeTerrace).ToList();
		tag["landmarks"] = _manifest.Landmarks.Select(SerializeLandmark).ToList();
		tag["mountains"] = _manifest.Mountains.Select(SerializeMountain).ToList();
		tag["valleys"] = _manifest.Valleys.Select(SerializeValley).ToList();
		tag["bridges"] = _manifest.Bridges.Select(SerializeBridge).ToList();
		tag["forestLakeBridges"] = _manifest.ForestLakeBridges.Select(SerializeForestLakeBridge).ToList();
		tag["mountainWaters"] = _manifest.MountainWaters.Select(SerializeMountainWater).ToList();
		tag["skyHighlands"] = _manifest.SkyHighlands.Select(SerializeSkyHighland).ToList();
		tag["biomeTransitions"] = _manifest.BiomeTransitions.Select(SerializeBiomeTransition).ToList();
		tag["mineSections"] = _manifest.MineSections.Select(SerializeMineSection).ToList();
		if (_manifest.SurfaceMine is SurfaceMineRecord surfaceMine) {
			tag["surfaceMine"] = SerializeSurfaceMine(surfaceMine);
		}
		if (_manifest.TorchGodTemple is TorchGodTempleRecord torchGodTemple) {
			tag["torchGodTemple"] = SerializeTorchGodTemple(torchGodTemple);
		}
		tag["accents"] = _manifest.AccentCounts
			.OrderBy(pair => pair.Key)
			.Select(pair => new TagCompound {
				["biome"] = (int)pair.Key,
				["count"] = pair.Value
			})
			.ToList();
	}

	public override void LoadWorldData(TagCompound tag)
	{
		GenerationManifest manifest = new() {
			GenerationSeed = tag.GetInt("generationSeed")
		};

		foreach (TagCompound saved in tag.GetList<TagCompound>("terraces")) {
			manifest.Terraces.Add(new BuildTerrace(
				new Microsoft.Xna.Framework.Rectangle(
					saved.GetInt("x"),
					saved.GetInt("y"),
					saved.GetInt("width"),
					saved.GetInt("height")),
				saved.GetInt("surfaceY"),
				saved.GetBool("spawn")));
		}

		foreach (TagCompound saved in tag.GetList<TagCompound>("landmarks")) {
			BiomeKind biome = (BiomeKind)saved.GetInt("biome");
			int layoutVariant = saved.ContainsKey("layoutVariant") ? saved.GetInt("layoutVariant") : 0;
			int roomCount = saved.ContainsKey("rooms") ? saved.GetInt("rooms") : 1;
			manifest.Landmarks.Add(new LandmarkRecord(
				biome,
				new Microsoft.Xna.Framework.Rectangle(
					saved.GetInt("x"),
					saved.GetInt("y"),
					saved.GetInt("width"),
					saved.GetInt("height")),
				saved.GetInt("anchorX"),
				saved.GetInt("anchorY"),
				saved.ContainsKey("archetype")
					? (LandmarkArchetype)saved.GetInt("archetype")
					: (LandmarkArchetype)((int)biome * 3 + Math.Abs(layoutVariant) % 3),
				roomCount,
				saved.ContainsKey("floors") ? saved.GetInt("floors") : Math.Min(2, roomCount),
				saved.ContainsKey("stairs") ? saved.GetInt("stairs") : Math.Max(1, roomCount / 3),
				saved.ContainsKey("furniture") ? saved.GetInt("furniture") : 0,
				layoutVariant));
		}

		foreach (TagCompound saved in tag.GetList<TagCompound>("mountains")) {
			manifest.Mountains.Add(new MountainRecord(
				saved.GetInt("regionId"),
				DeserializeRectangle(saved),
				saved.GetInt("peakY"),
				saved.GetInt("entrances"),
				saved.GetInt("cloudTiles"),
				saved.ContainsKey("interiorStyle") ? (MountainInteriorStyle)saved.GetInt("interiorStyle") : MountainInteriorStyle.BranchingGrottoes,
				saved.ContainsKey("caveAirTiles") ? saved.GetInt("caveAirTiles") : 0,
				saved.ContainsKey("wideCavityColumns") ? saved.GetInt("wideCavityColumns") : 0,
				saved.ContainsKey("potTiles") ? saved.GetInt("potTiles") : 0,
				saved.ContainsKey("vineTiles") ? saved.GetInt("vineTiles") : 0,
				saved.ContainsKey("climbAidTiles") ? saved.GetInt("climbAidTiles") : 0,
				saved.ContainsKey("waterCells") ? saved.GetInt("waterCells") : 0,
				saved.ContainsKey("waterBodies") ? saved.GetInt("waterBodies") : 0));
		}
		foreach (TagCompound saved in tag.GetList<TagCompound>("valleys")) {
			manifest.Valleys.Add(new ValleyRecord(
				(ValleyTheme)saved.GetInt("theme"),
				DeserializeRectangle(saved),
				saved.GetInt("liquidCells")));
		}
		foreach (TagCompound saved in tag.GetList<TagCompound>("bridges")) {
			manifest.Bridges.Add(new BridgeRecord(
				(BridgeStyle)saved.GetInt("style"),
				DeserializeRectangle(saved),
				saved.GetInt("deckTiles"),
				saved.ContainsKey("graveyard") && saved.GetBool("graveyard"),
				saved.ContainsKey("tombstoneTiles") ? saved.GetInt("tombstoneTiles") : 0));
		}
		foreach (TagCompound saved in tag.GetList<TagCompound>("forestLakeBridges")) {
			manifest.ForestLakeBridges.Add(new ForestLakeBridgeRecord(
				(ForestBridgeStyle)saved.GetInt("style"),
				DeserializeRectangle(saved),
				saved.GetInt("deckY"),
				saved.GetInt("waterlineY"),
				saved.ContainsKey("depth") ? saved.GetInt("depth") : Math.Max(8, DeserializeRectangle(saved).Bottom - saved.GetInt("waterlineY") - 7),
				saved.ContainsKey("featureSeed") ? saved.GetInt("featureSeed") : saved.GetInt("x") ^ saved.GetInt("y") ^ 0x464C_414B,
				saved.GetInt("waterCells"),
				saved.GetInt("deckTiles"),
				saved.GetInt("supportTiles"),
				saved.ContainsKey("graveyard") && saved.GetBool("graveyard"),
				saved.ContainsKey("tombstoneTiles") ? saved.GetInt("tombstoneTiles") : 0));
		}
		foreach (TagCompound saved in tag.GetList<TagCompound>("mountainWaters")) {
			manifest.MountainWaters.Add(new MountainWaterRecord(
				saved.GetInt("regionId"),
				(MountainWaterStyle)saved.GetInt("style"),
				DeserializeRectangle(saved),
				saved.GetInt("waterlineY"),
				saved.ContainsKey("depth") ? saved.GetInt("depth") : 8,
				saved.ContainsKey("featureSeed") ? saved.GetInt("featureSeed") : saved.GetInt("x") ^ saved.GetInt("y") ^ 0x4D57_4154,
				saved.GetInt("waterCells")));
		}
		foreach (TagCompound saved in tag.GetList<TagCompound>("skyHighlands")) {
			manifest.SkyHighlands.Add(new SkyHighlandRecord(
				DeserializeRectangle(saved),
				saved.GetInt("surfaceTiles"),
				saved.GetInt("routeTiles"),
				saved.GetInt("cloudTiles"),
				saved.GetInt("liquidCells"),
				saved.ContainsKey("style") ? (SkyHighlandStyle)saved.GetInt("style") : SkyHighlandStyle.TerracedMeadow,
				saved.ContainsKey("mountainAttached") && saved.GetBool("mountainAttached")));
		}
		foreach (TagCompound saved in tag.GetList<TagCompound>("biomeTransitions")) {
			manifest.BiomeTransitions.Add(new BiomeTransitionRecord(
				(BiomeKind)saved.GetInt("leftBiome"),
				(BiomeKind)saved.GetInt("rightBiome"),
				DeserializeRectangle(saved),
				saved.GetInt("modifiedCells")));
		}
		foreach (TagCompound saved in tag.GetList<TagCompound>("mineSections")) {
			Microsoft.Xna.Framework.Rectangle area = DeserializeRectangle(saved);
				manifest.MineSections.Add(new MineSection(
					saved.GetInt("id"),
					(MineSectionKind)saved.GetInt("kind"),
					area,
					new Microsoft.Xna.Framework.Point(saved.GetInt("centerX"), saved.GetInt("centerY")),
					saved.ContainsKey("theme") ? (BiomeKind)saved.GetInt("theme") : BiomeKind.Cavern));
		}
		if (tag.ContainsKey("surfaceMine")) {
			TagCompound saved = tag.GetCompound("surfaceMine");
			manifest.SurfaceMine = new SurfaceMineRecord(
				DeserializeRectangle(saved),
				new Microsoft.Xna.Framework.Point(saved.GetInt("entranceX"), saved.GetInt("entranceY")),
				saved.GetInt("trackTiles"),
				saved.GetInt("supportTiles"),
				saved.GetInt("furniture"),
				saved.GetInt("requiredRoutes"),
				saved.GetInt("connectedRoutes"),
				saved.ContainsKey("kind") ? (MinePlanKind)saved.GetInt("kind") : MinePlanKind.Complete,
				saved.ContainsKey("detouredRoutes") ? saved.GetInt("detouredRoutes") : 0,
				saved.ContainsKey("brokenRoutes") ? saved.GetInt("brokenRoutes") : 0);
		}
		if (tag.ContainsKey("torchGodTemple")) {
			TagCompound saved = tag.GetCompound("torchGodTemple");
			manifest.TorchGodTemple = new TorchGodTempleRecord(
				DeserializeRectangle(saved),
				new Microsoft.Xna.Framework.Point(saved.GetInt("activationX"), saved.GetInt("activationY")),
				new Microsoft.Xna.Framework.Point(saved.GetInt("chestX"), saved.GetInt("chestY")),
				new Microsoft.Xna.Framework.Point(saved.GetInt("socketX"), saved.GetInt("socketY")),
				(TorchTempleLayout)saved.GetInt("layout"),
				(TorchTempleTheme)saved.GetInt("theme"),
				saved.GetInt("torches"),
				saved.GetInt("furniture"),
				saved.GetInt("entrances"),
				saved.GetInt("brickTiles"));
		}

		foreach (TagCompound saved in tag.GetList<TagCompound>("accents")) {
			manifest.AccentCounts[(BiomeKind)saved.GetInt("biome")] = saved.GetInt("count");
		}
		_manifest = manifest;
		_savedValidationSummary = tag.ContainsKey("validation") ? tag.GetString("validation") : null;
		int loadedTorchTempleTorches = manifest.TorchGodTemple is TorchGodTempleRecord loadedTemple
			? TorchGodTempleGenerator.CountLitTorches(TorchGodTempleGenerator.ActivationArea(loadedTemple.ActivationPoint))
			: 0;
		int loadedTorchTempleChestTorches = manifest.TorchGodTemple is TorchGodTempleRecord loadedTempleWithChest
			? TorchGodTempleGenerator.CountOrdinaryTorchesInChest(loadedTempleWithChest.ChestTopLeft)
			: -1;
		Mod.Logger.Info(
			$"Loaded Vanilla Worlds Overhauled manifest v{tag.GetInt("manifestVersion")}: "
			+ $"landmarks={manifest.Landmarks.Count}; mountains={manifest.Mountains.Count}; "
			+ $"bridges={manifest.Bridges.Count}; forestLakeBridges={manifest.ForestLakeBridges.Count}; "
			+ $"mountainWaters={manifest.MountainWaters.Count}; skyHighlands={manifest.SkyHighlands.Count}; "
			+ $"mine={(manifest.SurfaceMine is null ? "missing" : "present")}; "
			+ $"mineKind={manifest.SurfaceMine?.Kind.ToString() ?? "missing"}; "
			+ $"mineDetours={manifest.SurfaceMine?.DetouredRouteCount ?? -1}; "
			+ $"mineBrokenRoutes={manifest.SurfaceMine?.BrokenRouteCount ?? -1}; "
			+ $"torchGodTemple={(manifest.TorchGodTemple is null ? "missing" : "present")}; "
			+ $"torchGodTempleTorches={loadedTorchTempleTorches}; "
			+ $"torchGodTempleChestTorches={loadedTorchTempleChestTorches}; "
			+ $"validation={_savedValidationSummary ?? "missing"}");
	}

	public override void SaveWorldHeader(TagCompound tag)
	{
		if (_manifest is not null) {
			tag["richerBiomesManifestVersion"] = GenerationManifest.CurrentVersion;
		}
	}

	private void Plan(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Planning the world-scale landform and route network";
		_plan = WorldPlanner.Create();
		_manifest = new GenerationManifest { GenerationSeed = _plan.GenerationSeed };
		Mod.Logger.Info(
			"Vanilla Worlds Overhauled mountain plan: "
			+ string.Join(", ", _plan.Mountains.Select(mountain =>
				$"region {mountain.RegionId}={mountain.HeightStyle} "
				+ $"peaks {mountain.LeftPeakY}/{mountain.RightPeakY} interior {mountain.InteriorStyle}")));
	}

	private void ShapeTerrain(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Forming valleys, plateaus, mountains, and quiet ground";
		LandformGenerator.Apply(RequirePlan(), progress);
	}

	private void CarveCaves(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Connecting regional caves to the Cavern layer";
		RegionalCaveGenerator.Apply(RequirePlan(), progress);
	}

	private void BuildSkyHighlands(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Expanding the sky into walkable highland biomes";
		SkyHighlandGenerator.Apply(RequirePlan(), RequireManifest(), progress);
	}

	private void ReinforceMountains(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Reinforcing biome-aware peaks after desert and snow shaping";
		LandformGenerator.ReinforceMountains(RequirePlan(), progress);
	}

	private void CarveMountainCrossings(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Opening foothill entrances, halls, and summit routes";
		MountainBiomeGenerator.CarveInteriors(RequirePlan(), protectSensitiveTiles: false, reserveRoutes: false);
		progress.Set(1d);
	}

	private void RepairMountainCrossings(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Reopening mountain crossings around the world evil";
		MountainBiomeGenerator.CarveInteriors(RequirePlan(), protectSensitiveTiles: true, reserveRoutes: true);
		progress.Set(1d);
	}

	private void BlendBiomeTransitions(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Breaking up straight biome seams with interleaved terrain";
		BiomeTransitionGenerator.Apply(RequirePlan(), RequireManifest(), progress);
	}

	private void ReserveTerraces(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Protecting calm ground for building";
		TerraceGenerator.Reserve(RequirePlan(), RequireManifest(), progress);
		Mod.Logger.Info($"Vanilla Worlds Overhauled reserved {RequireManifest().Terraces.Count} building terraces before surface structures and decoration.");
	}

	private void RepairCaves(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Reopening biome cave routes without touching progression objects";
		RegionalCaveGenerator.RepairRequiredRoutes(RequirePlan(), progress);
	}

	private void ReserveSurfaceMine(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Planning a guaranteed connected surface mine";
		_surfaceMinePlan = SurfaceMineGenerator.PlanAndReserve(RequirePlan(), RequireManifest());
		Mod.Logger.Info(
			$"Vanilla Worlds Overhauled reserved a {RequireMinePlan().Kind} surface mine with "
			+ $"{RequireMinePlan().DetouredRouteCount} obstacle detour(s), "
			+ $"{RequireMinePlan().BrokenRouteCount} broken branch(es), "
			+ $"{RequireMinePlan().Sections.Count} district(s), and {RequireMinePlan().Routes.Count} rail edge(s).");
		Mod.Logger.Info(
			"Vanilla Worlds Overhauled mine routes: "
			+ string.Join("; ", RequireMinePlan().Routes.Select((route, index) =>
				$"{index}:{route.Start}->{route.End}/cells={route.Centerline.Count}"
				+ (route.HasJumpTransfer ? $"/jump={route.JumpStartIndex}+{route.JumpGapLength}" : string.Empty)
				+ (route.HasGravityTransfer ? $"/drop={route.DropStartIndex}+{route.DropGapLength}x{route.DropDepth}" : string.Empty))));
		progress.Set(1d);
	}

	private void BuildMountainValleys(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Shaping reservoirs, grottoes, and valley faults";
		MountainBiomeGenerator.BuildValleys(RequirePlan(), RequireManifest());
		SkyHighlandGenerator.RefillLakes(RequirePlan(), RequireManifest());
		progress.Set(1d);
	}

	private void BuildForestWaterCrossings(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Fitting forest footbridges over natural lake basins";
		ForestWaterBridgeGenerator.Apply(RequirePlan(), RequireManifest(), progress);
		Mod.Logger.Info($"Vanilla Worlds Overhauled formed {RequireManifest().ForestLakeBridges.Count} optional forest lake crossings.");
	}

	private void BuildMountainInteriorWaters(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Sealing ponds and lakes into mountain chambers";
		MountainBiomeGenerator.BuildInteriorWaters(RequirePlan(), RequireManifest(), progress);
		Mod.Logger.Info($"Vanilla Worlds Overhauled formed {RequireManifest().MountainWaters.Count} protected mountain water bodies.");
	}

	private void ExcavateSurfaceMine(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Excavating mine stations, branches, and liquid sections";
		SurfaceMineGenerator.Excavate(RequireMinePlan(), RequirePlan(), RequireManifest(), progress);
	}

	private void PlanTorchGodTemple(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Finding a natural cavern for the Torch God's temple";
		_torchGodTemplePlan = TorchGodTempleGenerator.Plan(RequirePlan(), RequireMinePlan(), RequireManifest());
		Mod.Logger.Info(
			$"Vanilla Worlds Overhauled planned a {RequireTorchGodTemplePlan().Theme} "
			+ $"{RequireTorchGodTemplePlan().Layout} Torch God temple at "
			+ $"{RequireTorchGodTemplePlan().ActivationPoint} with "
			+ $"{RequireTorchGodTemplePlan().Entrances.Count} cavern entrance(s).");
		progress.Set(1d);
	}

	private void BuildTorchGodTemple(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Building the underground Torch God temple";
		TorchGodTempleGenerator.BuildAndProtect(RequireTorchGodTemplePlan());
		progress.Set(1d);
	}

	private void PlaceLandmarks(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Building landmarks from each biome's materials";
		LandmarkGenerator.Apply(RequirePlan(), RequireMinePlan(), RequireManifest(), progress);
	}

	private void BuildMountainBridges(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Joining mountain ledges with regional bridges";
		MountainBiomeGenerator.BuildBridges(RequirePlan(), RequireManifest());
		progress.Set(1d);
	}

	private void FurnishSurfaceMine(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Laying the mine's connected rail graph and work districts";
		SurfaceMineGenerator.FurnishAndLayTrack(RequireMinePlan(), RequireManifest(), progress);
	}

	private void DecorateMountainInteriors(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Adding pots, rubble, vines, light, and climb routes inside mountains";
		MountainBiomeGenerator.DecorateInteriors(RequirePlan(), RequireManifest(), progress);
	}

	private void RepairLateCaves(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Reopening protected regional routes after destructive micro-biomes";
		RegionalCaveGenerator.RepairRequiredRoutes(
			RequirePlan(),
			progress,
			reserveRoutes: true,
			respectStructureMap: false);
		SkyHighlandGenerator.RepairKeels(RequirePlan());
	}

	private void RepairLateMountainCrossings(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Reopening foothill entrances and summit routes after micro-biomes";
		MountainBiomeGenerator.CarveInteriors(RequirePlan(), protectSensitiveTiles: true, reserveRoutes: true);
		progress.Set(1d);
	}

	private void StabilizeMountainSummits(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Stabilizing ground-connected mountain summit buttresses";
		LandformGenerator.StabilizeSummits(RequirePlan(), progress);
	}

	private void FurnishLandmarks(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Furnishing open biome workshops, ruins, and lookouts";
		BiomeTransitionGenerator.Repair(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RepairValleyStructures(RequirePlan());
		TerraceGenerator.RepairReserved(RequireManifest());
		LandmarkGenerator.Furnish(RequireManifest(), progress);
	}

	private void StabilizeRoutesAndTerraces(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Stabilizing regional routes and buildable ground";
		RegionalCaveGenerator.RepairRequiredRoutes(RequirePlan(), progress);
		int beforeRepair = RequireManifest().Terraces.Count;
		TerraceGenerator.RepairReserved(RequireManifest());
		Mod.Logger.Info($"Vanilla Worlds Overhauled stabilized {RequireManifest().Terraces.Count} of {beforeRepair} reserved building terraces after Smooth World.");
	}

	private void AddAccents(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Adding rubble clusters and quiet gaps";
		BiomeAccentGenerator.Apply(RequirePlan(), RequireManifest(), progress);
	}

	private void RecordFinalFeatures(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Checking mountain grounding and recording final feature state";
		// Mountain crossings, landmark rebuilds, and vanilla cleanup all run after the
		// first sky repair. Reapply the three-tile keel at the final ownership boundary
		// so a highland remains one biome-scale body instead of isolated shelves.
		SkyHighlandGenerator.RepairKeels(RequirePlan());
		SkyHighlandGenerator.RepairVerticalRoutes(RequirePlan(), RequireManifest());
		SkyHighlandGenerator.RepairOrganicMaterialSeams(RequirePlan(), RequireManifest());
		SkyHighlandGenerator.RefillLakes(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RepairBridgePortals(RequirePlan(), RequireManifest());
		LandformGenerator.FinishMountainMaterials(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RepairEntrances(RequirePlan(), RequireManifest());
		RegionalCaveGenerator.RepairRequiredRoutes(
			RequirePlan(),
			progress,
			respectStructureMap: false,
			naturalTilesOnly: true);
		TerraceGenerator.RepairReserved(RequireManifest());
		BiomeTransitionGenerator.Repair(RequirePlan(), RequireManifest());
		int occludedTransitions = BiomeTransitionGenerator.RetainObservable(RequirePlan(), RequireManifest());
		if (occludedTransitions > 0) {
			Mod.Logger.Info($"Vanilla Worlds Overhauled omitted {occludedTransitions} surface seams occluded by final feature ownership.");
		}
		MountainBiomeGenerator.FinishInteriorWalls(RequirePlan(), RequireManifest());
		BiomeTransitionGenerator.Repair(RequirePlan(), RequireManifest());
		LandmarkGenerator.RepairTraversal(RequireManifest());
		int lateOccludedTransitions = BiomeTransitionGenerator.RetainObservable(RequirePlan(), RequireManifest());
		if (lateOccludedTransitions > 0) {
			Mod.Logger.Info($"Vanilla Worlds Overhauled omitted {lateOccludedTransitions} surface seams after final traversal repair.");
		}
		MountainBiomeGenerator.RepairInteriorDecorations(RequirePlan(), RequireManifest());
		ForestWaterBridgeGenerator.RepairAndRefill(RequireManifest());
		MountainBiomeGenerator.RefillInteriorWaters(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RefillValleyLiquids(RequireManifest());
		// Liquids, transition blending, and traversal repair can all touch the
		// mountain slopes after the first bridge repair. Rebuild the galleries,
		// reconnect natural summit mass below them, finish the landmarks and
		// graveyard crossings, then give the mine the final structural write so
		// its rail frames, transfer space, and quarantine shell cannot be cut.
		MountainBiomeGenerator.RepairBridgePortals(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RepairGroundingSpines(RequirePlan(), RequireManifest());
		LandmarkGenerator.RepairFinalGeometry(RequireManifest());
		MountainBiomeGenerator.RepairGraveyardBridges(RequirePlan(), RequireManifest());
		SurfaceMineGenerator.RepairTrackGraph(RequireMinePlan(), RequireManifest());
		// Converge the overlapping late repairs before recording or validating the
		// world. Each operation respects final feature ownership, and the order
		// leaves traversal and decoration repairs after material normalization.
		SkyHighlandGenerator.RepairAuthoredBodyMaterials(RequirePlan(), RequireManifest());
		LandformGenerator.FinishMountainMaterials(RequirePlan(), RequireManifest());
		LandformGenerator.RepairMountainMaterialSeams(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.FinishInteriorWalls(RequirePlan(), RequireManifest());
		BiomeTransitionGenerator.Repair(RequirePlan(), RequireManifest());
		SkyHighlandGenerator.RepairOrganicMaterialSeams(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RepairGroundingSpines(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RepairBridgePortals(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RepairInteriorDecorations(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RepairNaturalVineRoots(RequirePlan(), RequireManifest());
		ForestWaterBridgeGenerator.RepairAndRefill(RequireManifest());
		MountainBiomeGenerator.RefillInteriorWaters(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RefillValleyLiquids(RequireManifest());
		SurfaceMineGenerator.RepairTrackGraph(RequireMinePlan(), RequireManifest());
		MountainBiomeGenerator.RepairBridgePortals(RequirePlan(), RequireManifest());
		LandformGenerator.FinishMountainMaterials(RequirePlan(), RequireManifest());
		LandformGenerator.RepairMountainMaterialSeams(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.FinishInteriorWalls(RequirePlan(), RequireManifest());
		BiomeTransitionGenerator.Repair(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RepairInteriorDecorations(RequirePlan(), RequireManifest());
		ForestWaterBridgeGenerator.RepairAndRefill(RequireManifest());
		MountainBiomeGenerator.RefillInteriorWaters(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RefillValleyLiquids(RequireManifest());
		foreach (MountainWaterRecord water in RequireManifest().MountainWaters.Where(water => water.WaterCells < 80)) {
			Mod.Logger.Warn(
				$"Vanilla Worlds Overhauled retained a constrained {water.Style} in mountain {water.RegionId}: "
				+ $"area={water.Area}; waterline={water.WaterlineY}; depth={water.Depth}; cells={water.WaterCells}.");
		}
		MountainBiomeGenerator.RepairNaturalVineRoots(RequirePlan(), RequireManifest());
		MountainBiomeGenerator.RecordFinalState(RequirePlan(), RequireManifest());
		progress.Set(1d);
	}

	private void FinishTorchGodTemple(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Furnishing the altar and leaving its final flame unlit";
		TorchGodTempleGenerator.FinishAndArm(RequireTorchGodTemplePlan(), RequireManifest());
		progress.Set(1d);
	}

	private void Validate(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Inspecting terrain, routes, landmarks, and progression sites";
		_report = WorldValidator.Validate(
			RequirePlan(),
			RequireMinePlan(),
			RequireTorchGodTemplePlan(),
			RequireManifest());
		_savedValidationSummary = _report.Summary;
		Mod.Logger.Info("Vanilla Worlds Overhauled validation passed. " + _report.Summary);
	}

	private static void InsertAfter(
		List<GenPass> tasks,
		string anchorName,
		GenPass pass,
		ref double totalWeight)
	{
		int anchorIndex = tasks.FindIndex(candidate => candidate.Name == anchorName);
		if (anchorIndex < 0) {
			throw new InvalidOperationException($"Vanilla Worlds Overhauled requires the '{anchorName}' world-generation pass, but it is missing.");
		}

		tasks.Insert(anchorIndex + 1, pass);
		totalWeight += pass.Weight;
	}

	private static void InsertBefore(
		List<GenPass> tasks,
		string anchorName,
		GenPass pass,
		ref double totalWeight)
	{
		int anchorIndex = tasks.FindIndex(candidate => candidate.Name == anchorName);
		if (anchorIndex < 0) {
			throw new InvalidOperationException($"Vanilla Worlds Overhauled requires the '{anchorName}' world-generation pass, but it is missing.");
		}

		tasks.Insert(anchorIndex, pass);
		totalWeight += pass.Weight;
	}

	private static TagCompound SerializeTerrace(BuildTerrace terrace) => new() {
		["x"] = terrace.Area.X,
		["y"] = terrace.Area.Y,
		["width"] = terrace.Area.Width,
		["height"] = terrace.Area.Height,
		["surfaceY"] = terrace.SurfaceY,
		["spawn"] = terrace.SpawnTerrace
	};

	private static TagCompound SerializeLandmark(LandmarkRecord landmark) => new() {
		["biome"] = (int)landmark.Biome,
		["x"] = landmark.Area.X,
		["y"] = landmark.Area.Y,
		["width"] = landmark.Area.Width,
		["height"] = landmark.Area.Height,
		["anchorX"] = landmark.AnchorX,
		["anchorY"] = landmark.AnchorY,
		["archetype"] = (int)landmark.Archetype,
		["rooms"] = landmark.RoomCount,
		["floors"] = landmark.FloorCount,
		["stairs"] = landmark.StairCount,
		["furniture"] = landmark.FurnitureCount,
		["layoutVariant"] = landmark.LayoutVariant
	};

	private static TagCompound SerializeMountain(MountainRecord mountain) => WithRectangle(mountain.Area, new TagCompound {
		["regionId"] = mountain.RegionId,
		["peakY"] = mountain.PeakY,
		["entrances"] = mountain.EntranceCount,
		["cloudTiles"] = mountain.CloudTiles,
		["interiorStyle"] = (int)mountain.InteriorStyle,
		["caveAirTiles"] = mountain.CaveAirTiles,
		["wideCavityColumns"] = mountain.WideCavityColumns,
		["potTiles"] = mountain.PotTiles,
		["vineTiles"] = mountain.VineTiles,
		["climbAidTiles"] = mountain.ClimbAidTiles,
		["waterCells"] = mountain.WaterCells,
		["waterBodies"] = mountain.WaterBodyCount
	});

	private static TagCompound SerializeValley(ValleyRecord valley) => WithRectangle(valley.Area, new TagCompound {
		["theme"] = (int)valley.Theme,
		["liquidCells"] = valley.LiquidCells
	});

	private static TagCompound SerializeBridge(BridgeRecord bridge) => WithRectangle(bridge.Area, new TagCompound {
		["style"] = (int)bridge.Style,
		["deckTiles"] = bridge.DeckTiles,
		["graveyard"] = bridge.Graveyard,
		["tombstoneTiles"] = bridge.TombstoneTiles
	});

	private static TagCompound SerializeForestLakeBridge(ForestLakeBridgeRecord bridge) => WithRectangle(bridge.Area, new TagCompound {
		["style"] = (int)bridge.Style,
		["deckY"] = bridge.DeckY,
		["waterlineY"] = bridge.WaterlineY,
		["depth"] = bridge.Depth,
		["featureSeed"] = bridge.FeatureSeed,
		["waterCells"] = bridge.WaterCells,
		["deckTiles"] = bridge.DeckTiles,
		["supportTiles"] = bridge.SupportTiles,
		["graveyard"] = bridge.Graveyard,
		["tombstoneTiles"] = bridge.TombstoneTiles
	});

	private static TagCompound SerializeMountainWater(MountainWaterRecord water) => WithRectangle(water.Area, new TagCompound {
		["regionId"] = water.RegionId,
		["style"] = (int)water.Style,
		["waterlineY"] = water.WaterlineY,
		["depth"] = water.Depth,
		["featureSeed"] = water.FeatureSeed,
		["waterCells"] = water.WaterCells
	});

	private static TagCompound SerializeSkyHighland(SkyHighlandRecord highland) => WithRectangle(highland.Area, new TagCompound {
		["surfaceTiles"] = highland.WalkableSurfaceTiles,
		["routeTiles"] = highland.InteriorRouteTiles,
		["cloudTiles"] = highland.CloudTiles,
		["liquidCells"] = highland.LiquidCells,
		["style"] = (int)highland.Style,
		["mountainAttached"] = highland.MountainAttached
	});

	private static TagCompound SerializeBiomeTransition(BiomeTransitionRecord transition) => WithRectangle(transition.Area, new TagCompound {
		["leftBiome"] = (int)transition.LeftBiome,
		["rightBiome"] = (int)transition.RightBiome,
		["modifiedCells"] = transition.ModifiedCells
	});

	private static TagCompound SerializeMineSection(MineSection section) => WithRectangle(section.Area, new TagCompound {
		["id"] = section.Id,
		["kind"] = (int)section.Kind,
		["centerX"] = section.Center.X,
		["centerY"] = section.Center.Y,
		["theme"] = (int)section.Theme
	});

	private static TagCompound SerializeSurfaceMine(SurfaceMineRecord mine) => WithRectangle(mine.Area, new TagCompound {
		["entranceX"] = mine.Entrance.X,
		["entranceY"] = mine.Entrance.Y,
		["trackTiles"] = mine.TrackTiles,
		["supportTiles"] = mine.SupportTiles,
		["furniture"] = mine.FurnitureCount,
		["requiredRoutes"] = mine.RequiredRouteCount,
		["connectedRoutes"] = mine.ConnectedRouteCount,
		["kind"] = (int)mine.Kind,
		["detouredRoutes"] = mine.DetouredRouteCount,
		["brokenRoutes"] = mine.BrokenRouteCount
	});

	private static TagCompound SerializeTorchGodTemple(TorchGodTempleRecord temple) => WithRectangle(temple.Area, new TagCompound {
		["activationX"] = temple.ActivationPoint.X,
		["activationY"] = temple.ActivationPoint.Y,
		["chestX"] = temple.ChestTopLeft.X,
		["chestY"] = temple.ChestTopLeft.Y,
		["socketX"] = temple.MissingTorch.X,
		["socketY"] = temple.MissingTorch.Y,
		["layout"] = (int)temple.Layout,
		["theme"] = (int)temple.Theme,
		["torches"] = temple.TorchCount,
		["furniture"] = temple.FurnitureCount,
		["entrances"] = temple.EntranceCount,
		["brickTiles"] = temple.BrickTiles
	});

	private static TagCompound WithRectangle(Microsoft.Xna.Framework.Rectangle area, TagCompound tag)
	{
		tag["x"] = area.X;
		tag["y"] = area.Y;
		tag["width"] = area.Width;
		tag["height"] = area.Height;
		return tag;
	}

	private static Microsoft.Xna.Framework.Rectangle DeserializeRectangle(TagCompound tag) => new(
		tag.GetInt("x"),
		tag.GetInt("y"),
		tag.GetInt("width"),
		tag.GetInt("height"));

	private WorldPlan RequirePlan() =>
		_plan ?? throw new InvalidOperationException("Vanilla Worlds Overhauled world planning did not run before a dependent pass.");

	private GenerationManifest RequireManifest() =>
		_manifest ?? throw new InvalidOperationException("Vanilla Worlds Overhauled generation manifest is unavailable.");

	private SurfaceMinePlan RequireMinePlan() =>
		_surfaceMinePlan ?? throw new InvalidOperationException("Vanilla Worlds Overhauled surface-mine planning did not run before a dependent pass.");

	private TorchGodTemplePlan RequireTorchGodTemplePlan() =>
		_torchGodTemplePlan ?? throw new InvalidOperationException("Vanilla Worlds Overhauled Torch God temple planning did not run before a dependent pass.");

	private sealed class VanillaWorldsOverhauledPass : GenPass
	{
		private readonly Action<GenerationProgress, GameConfiguration> _apply;

		public VanillaWorldsOverhauledPass(
			string name,
			double weight,
			Action<GenerationProgress, GameConfiguration> apply)
			: base(name, weight)
		{
			_apply = apply;
		}

		protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration)
		{
			_apply(progress, configuration);
		}
	}
}
