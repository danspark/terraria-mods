using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace RicherBiomes.WorldGeneration;

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

internal enum MineSectionKind
{
	Workyard,
	Working,
	Collapsed,
	Flooded,
	SealedEvil,
	MountainRail
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
	ValleyTheme ValleyTheme,
	BridgeStyle BridgeStyle);

internal readonly record struct SkyHighlandPlan(
	int MountainRegionId,
	int CenterX,
	int SurfaceY,
	int Width,
	int Depth,
	int SatelliteCount);

internal readonly record struct MineRoute(Point Start, Point End, bool HasTrack, bool Required);

internal readonly record struct MineSection(
	int Id,
	MineSectionKind Kind,
	Rectangle Area,
	Point Center);

internal sealed record SurfaceMinePlan(
	int FeatureSeed,
	Rectangle Area,
	Point Entrance,
	IReadOnlyList<MineSection> Sections,
	IReadOnlyList<MineRoute> Routes);

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
	int RoomCount,
	int FurnitureCount);

internal readonly record struct MountainRecord(
	int RegionId,
	Rectangle Area,
	int PeakY,
	int EntranceCount,
	int CloudTiles);

internal readonly record struct ValleyRecord(ValleyTheme Theme, Rectangle Area, int LiquidCells);

internal readonly record struct BridgeRecord(BridgeStyle Style, Rectangle Area, int DeckTiles);

internal readonly record struct SkyHighlandRecord(
	Rectangle Area,
	int WalkableSurfaceTiles,
	int InteriorRouteTiles,
	int CloudTiles,
	int LiquidCells);

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
	public const int CurrentVersion = 3;

	public int GenerationSeed { get; init; }
	public List<BuildTerrace> Terraces { get; } = [];
	public List<LandmarkRecord> Landmarks { get; } = [];
	public List<MountainRecord> Mountains { get; } = [];
	public List<ValleyRecord> Valleys { get; } = [];
	public List<BridgeRecord> Bridges { get; } = [];
	public List<SkyHighlandRecord> SkyHighlands { get; } = [];
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
	int SkyHighlandCount,
	int MineTrackTiles)
{
	public string Summary =>
		$"valid={Valid}; regions={RegionCount}; relief={Relief}; mountains={MountainCount}; " +
		$"caveRoutes={ConnectedCaveRoutes}; terraces={BuildTerraces}; " +
		$"landmarks={LandmarkCount}; accents={AccentCount}; bridges={BridgeCount}; " +
		$"skyHighlands={SkyHighlandCount}; mineTrackTiles={MineTrackTiles}";
}
