using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace VanillaWorldsOverhauled.WorldGeneration;

internal enum LandformKind
{
	QuietLowland,
	RollingHills,
	Valley,
	Plateau,
	Mountain,
	Basin
}

internal enum BiomeKind
{
	Forest,
	Snow,
	Desert,
	Jungle,
	Evil,
	Ocean,
	Sky,
	Mushroom,
	Cavern,
	Underworld
}

internal enum ValleyTheme
{
	Wooded,
	Lake,
	Lava,
	SealedEvil
}

internal enum BridgeStyle
{
	TimberSuspension,
	StoneArch,
	RailTrestle
}

internal enum MountainInteriorStyle
{
	BranchingGrottoes,
	SwitchbackClimb,
	SplitLevelCaves,
	OpenFault
}

internal enum MountainHeightStyle
{
	Highland,
	Alpine,
	SkyPiercing
}

internal enum SkyHighlandStyle
{
	TerracedMeadow,
	CloudBasin,
	BrokenArchipelago
}

internal enum LandmarkArchetype
{
	ForestRangerLodge,
	ForestSplitHall,
	ForestWatchHouse,
	SnowChalet,
	SnowIceWatch,
	SnowBuriedIgloo,
	DesertCourtyard,
	DesertCaravanserai,
	DesertSunTower,
	JungleCanopyLodge,
	JungleStiltHall,
	JungleOvergrownTower,
	EvilRiftChapel,
	EvilQuarantineKeep,
	EvilBrokenSpire,
	OceanStiltHouse,
	OceanHarborHall,
	OceanLighthouse,
	SkyObservatory,
	SkySunplateAerie,
	SkyCloudMonastery,
	MushroomCapHouse,
	MushroomSporeTower,
	MushroomMyceliumHall,
	CavernStoneDepot,
	CavernArchVault,
	CavernShaftHouse,
	UnderworldAshForge,
	UnderworldObsidianKeep,
	UnderworldHangingFort
}

internal enum ForestBridgeStyle
{
	TimberFootbridge,
	LivingWoodCauseway,
	StoneAndTimber
}

internal enum MountainWaterStyle
{
	SpringPond,
	CavernLake,
	HangingPool
}

internal enum MineSectionKind
{
	Workyard,
	Working,
	Collapsed,
	Flooded,
	SealedEvil,
	MountainRail
}

internal enum MineRailProfile
{
	RollingGrades,
	TerracedGrades,
	DipAndRise,
	LaunchTransfer
}

internal readonly record struct WorldRegion(
	int Id,
	int Left,
	int Right,
	LandformKind Landform,
	int LandmarkBudget,
	int QuietBudget)
{
	public int Width => Right - Left + 1;
	public int CenterX => Left + Width / 2;
	public bool Contains(int x) => x >= Left && x <= Right;
}

internal readonly record struct TerraceRequest(int PreferredX, int Width, bool Required);

internal readonly record struct PlannedCave(
	int RegionId,
	Point Start,
	Point Midpoint,
	Point End,
	int Radius,
	bool RequiredRoute);

internal readonly record struct MountainRangePlan(
	int RegionId,
	int LeftPeakX,
	int LeftPeakY,
	int SaddleX,
	int SaddleY,
	int RightPeakX,
	int RightPeakY,
	MountainHeightStyle HeightStyle,
	ValleyTheme ValleyTheme,
	BridgeStyle BridgeStyle,
	MountainInteriorStyle InteriorStyle,
	int FeatureSeed);

internal readonly record struct SkyHighlandPlan(
	int? AttachedMountainRegionId,
	int CenterX,
	int SurfaceY,
	int Width,
	int Depth,
	int SatelliteCount,
	SkyHighlandStyle Style,
	bool HasLake);

internal readonly record struct MineRoute(
	Point Start,
	Point End,
	bool HasTrack,
	bool Required,
	MineRailProfile Profile,
	int VariationSeed,
	IReadOnlyList<Point> Centerline,
	int JumpStartIndex,
	int JumpGapLength,
	int DropStartIndex,
	int DropGapLength,
	int DropDepth)
{
	public bool HasJumpTransfer => JumpStartIndex >= 0 && JumpGapLength > 0;
	public bool HasGravityTransfer => DropStartIndex >= 0 && DropGapLength > 0 && DropDepth > 0;
}

internal readonly record struct MineRailJump(Point Launch, Point Landing, Rectangle Gap);

internal readonly record struct MineRailDrop(Point UpperLip, Point LowerLanding, Rectangle Gap);

internal readonly record struct MineSection(
	int Id,
	MineSectionKind Kind,
	Rectangle Area,
	Point Center,
	BiomeKind Theme);

internal sealed record SurfaceMinePlan(
	int FeatureSeed,
	Rectangle Area,
	Point Entrance,
	IReadOnlyList<MineSection> Sections,
	IReadOnlyList<MineRoute> Routes,
	IReadOnlyDictionary<Point, BiomeKind> RouteThemes)
{
	public BiomeKind ThemeAt(Point point) =>
		RouteThemes.TryGetValue(point, out BiomeKind theme) ? theme : BiomeKind.Cavern;
}

internal sealed record WorldPlan(
	int GenerationSeed,
	int SpawnX,
	int CoastMargin,
	int[] SurfaceY,
	IReadOnlyList<WorldRegion> Regions,
	IReadOnlyList<TerraceRequest> TerraceRequests,
	IReadOnlyList<PlannedCave> Caves,
	IReadOnlyList<MountainRangePlan> Mountains,
	IReadOnlyList<SkyHighlandPlan> SkyHighlands)
{
	public int LeftBoundary => CoastMargin;
	public int RightBoundary => SurfaceY.Length - CoastMargin - 1;

	public int SurfaceAt(int x) => SurfaceY[Math.Clamp(x, 0, SurfaceY.Length - 1)];

	public WorldRegion RegionAt(int x)
	{
		foreach (WorldRegion region in Regions) {
			if (region.Contains(x)) {
				return region;
			}
		}

		return x < Regions[0].Left ? Regions[0] : Regions[^1];
	}
}

internal readonly record struct BuildTerrace(Rectangle Area, int SurfaceY, bool SpawnTerrace);

internal readonly record struct LandmarkRecord(
	BiomeKind Biome,
	Rectangle Area,
	int AnchorX,
	int AnchorY,
	LandmarkArchetype Archetype,
	int RoomCount,
	int FloorCount,
	int StairCount,
	int FurnitureCount,
	int LayoutVariant);

internal readonly record struct MountainRecord(
	int RegionId,
	Rectangle Area,
	int PeakY,
	int EntranceCount,
	int CloudTiles,
	MountainInteriorStyle InteriorStyle,
	int CaveAirTiles,
	int WideCavityColumns,
	int PotTiles,
	int VineTiles,
	int ClimbAidTiles,
	int WaterCells,
	int WaterBodyCount);

internal readonly record struct ValleyRecord(ValleyTheme Theme, Rectangle Area, int LiquidCells);

internal readonly record struct BridgeRecord(
	BridgeStyle Style,
	Rectangle Area,
	int DeckTiles,
	bool Graveyard,
	int TombstoneTiles);

internal readonly record struct ForestLakeBridgeRecord(
	ForestBridgeStyle Style,
	Rectangle Area,
	int DeckY,
	int WaterlineY,
	int Depth,
	int FeatureSeed,
	int WaterCells,
	int DeckTiles,
	int SupportTiles,
	bool Graveyard,
	int TombstoneTiles);

internal readonly record struct MountainWaterRecord(
	int RegionId,
	MountainWaterStyle Style,
	Rectangle Area,
	int WaterlineY,
	int Depth,
	int FeatureSeed,
	int WaterCells);

internal readonly record struct SkyHighlandRecord(
	Rectangle Area,
	int WalkableSurfaceTiles,
	int InteriorRouteTiles,
	int CloudTiles,
	int LiquidCells,
	SkyHighlandStyle Style,
	bool MountainAttached);

internal readonly record struct BiomeTransitionRecord(
	BiomeKind LeftBiome,
	BiomeKind RightBiome,
	Rectangle Area,
	int ModifiedCells);

internal readonly record struct SurfaceMineRecord(
	Rectangle Area,
	Point Entrance,
	int TrackTiles,
	int SupportTiles,
	int FurnitureCount,
	int RequiredRouteCount,
	int ConnectedRouteCount);

internal sealed class GenerationManifest
{
	public const int CurrentVersion = 7;

	public int GenerationSeed { get; init; }
	public List<BuildTerrace> Terraces { get; } = [];
	public List<LandmarkRecord> Landmarks { get; } = [];
	public List<MountainRecord> Mountains { get; } = [];
	public List<ValleyRecord> Valleys { get; } = [];
	public List<BridgeRecord> Bridges { get; } = [];
	public List<ForestLakeBridgeRecord> ForestLakeBridges { get; } = [];
	public List<MountainWaterRecord> MountainWaters { get; } = [];
	public List<SkyHighlandRecord> SkyHighlands { get; } = [];
	public List<BiomeTransitionRecord> BiomeTransitions { get; } = [];
	public List<MineSection> MineSections { get; } = [];
	public SurfaceMineRecord? SurfaceMine { get; set; }
	public Dictionary<BiomeKind, int> AccentCounts { get; } = [];
}

internal sealed record GenerationReport(
	bool Valid,
	int RegionCount,
	int Relief,
	int MountainCount,
	int ConnectedCaveRoutes,
	int BuildTerraces,
	int LandmarkCount,
	int AccentCount,
	int BridgeCount,
	int ForestLakeBridgeCount,
	int GraveyardBridgeCount,
	int TombstoneTiles,
	int MountainWaterCount,
	int SkyHighlandCount,
	int MineTrackTiles)
{
	public string Summary =>
		$"valid={Valid}; regions={RegionCount}; relief={Relief}; mountains={MountainCount}; " +
		$"caveRoutes={ConnectedCaveRoutes}; terraces={BuildTerraces}; " +
		$"landmarks={LandmarkCount}; accents={AccentCount}; bridges={BridgeCount}; " +
		$"forestLakeBridges={ForestLakeBridgeCount}; graveyardBridges={GraveyardBridgeCount}; " +
		$"tombstoneTiles={TombstoneTiles}; mountainWaters={MountainWaterCount}; " +
		$"skyHighlands={SkyHighlandCount}; mineTrackTiles={MineTrackTiles}";
}
