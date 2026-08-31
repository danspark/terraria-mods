using System;

namespace RicherBiomes.WorldGeneration;

internal readonly record struct FeatureSpan(string Name, int Start, int Length)
{
	public int End => Start + Length - 1;

	public bool Contains(int distance) => distance >= Start && distance <= End;
}

internal sealed record WorldPlan(
	int SpawnX,
	int Direction,
	int OriginX,
	int BaseSurfaceY,
	int PeakY,
	int MineBottomY,
	int[] OriginalSurfaceY,
	FeatureSpan Forest,
	FeatureSpan Mountain,
	FeatureSpan Mine)
{
	public const int SpawnBuffer = 180;

	public int TotalLength => Math.Max(Mountain.End, Mine.End) + 1;

	public int XAt(int distance) => OriginX + Direction * distance;

	public int DistanceAt(int x) => (x - OriginX) * Direction;

	public int OriginalSurfaceAt(int distance) => OriginalSurfaceY[distance];

	public int MinX => Math.Min(XAt(0), XAt(TotalLength - 1));

	public int MaxX => Math.Max(XAt(0), XAt(TotalLength - 1));

	public string DirectionName => Direction > 0 ? "east" : "west";
}

internal sealed record GenerationReport(
	bool Valid,
	int ForestRelief,
	int MountainPeakY,
	int MountainRouteSamples,
	int MineDepth,
	int RopeTiles,
	int PlatformTiles)
{
	public string Summary =>
		$"valid={Valid}; forestRelief={ForestRelief}; peakY={MountainPeakY}; " +
		$"mountainRouteSamples={MountainRouteSamples}; mineDepth={MineDepth}; " +
		$"ropeTiles={RopeTiles}; platformTiles={PlatformTiles}";
}
