using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.WorldBuilding;

namespace RicherBiomes.WorldGeneration;

internal static class TerraceGenerator
{
	private const int SearchStep = 24;
	private const int CandidateBudget = 9;

	public static int MinimumRequiredCount => Math.Clamp(Main.maxTilesX / 2400 + 1, 2, 4);

	public static void Reserve(WorldPlan plan, GenerationManifest manifest, GenerationProgress progress)
	{
		for (int index = 0; index < plan.TerraceRequests.Count; index++) {
			TerraceRequest request = plan.TerraceRequests[index];
			if (!TryReserve(request, request.Required, respectStructureMap: !request.Required, out BuildTerrace terrace, out string failure)) {
				if (request.Required) {
					throw new InvalidOperationException(
						"Richer Biomes could not preserve the required spawn building terrace. " + failure);
				}
			}
			else {
				manifest.Terraces.Add(terrace);
			}

			progress.Set((double)(index + 1) / plan.TerraceRequests.Count);
		}

		int redundancyTarget = Math.Min(plan.Regions.Count, MinimumRequiredCount + 2);
		Backfill(plan, manifest, redundancyTarget, respectStructureMap: true);
	}

	public static void RepairReserved(GenerationManifest manifest)
	{
		for (int index = manifest.Terraces.Count - 1; index >= 0; index--) {
			BuildTerrace terrace = manifest.Terraces[index];
			int left = terrace.Area.Left;
			int right = terrace.Area.Right - 1;
			if (!TryMeasureSurface(left, right, out int surfaceY, out int relief)) {
				if (terrace.SpawnTerrace) {
					throw new InvalidOperationException("Richer Biomes lost the required spawn building terrace before stabilization.");
				}
				manifest.Terraces.RemoveAt(index);
				continue;
			}
			if (relief <= 2) {
				continue;
			}

			Rectangle preflight = new(left, surfaceY - 12, terrace.Area.Width, 28);
			if (!TileEditor.IsSafeForTerrainFeature(preflight)) {
				if (terrace.SpawnTerrace) {
					throw new InvalidOperationException($"Richer Biomes could not repair the spawn building terrace at x={left} without touching an object.");
				}
				manifest.Terraces.RemoveAt(index);
				continue;
			}

			Flatten(left, right, surfaceY);
			Rectangle repairedArea = new(left, surfaceY - 12, terrace.Area.Width, 28);
			manifest.Terraces[index] = new BuildTerrace(repairedArea, surfaceY, terrace.SpawnTerrace);
		}
	}

	public static void EnsureMinimumCount(WorldPlan plan, GenerationManifest manifest)
	{
		RepairReserved(manifest);
		Backfill(plan, manifest, MinimumRequiredCount, respectStructureMap: false);
	}

	private static void Backfill(
		WorldPlan plan,
		GenerationManifest manifest,
		int targetCount,
		bool respectStructureMap)
	{
		if (manifest.Terraces.Count >= targetCount) {
			return;
		}

		foreach (TerraceRequest candidate in FinalCandidates(plan, manifest)) {
			if (TryReserve(candidate, spawnTerrace: false, respectStructureMap, out BuildTerrace terrace, out string _)) {
				manifest.Terraces.Add(terrace);
			}

			if (manifest.Terraces.Count >= targetCount) {
				return;
			}
		}
	}

	private static bool TryReserve(
		TerraceRequest request,
		bool spawnTerrace,
		bool respectStructureMap,
		out BuildTerrace terrace,
		out string failure)
	{
		List<string> rejections = [];
		foreach (int centerX in CandidateCenters(request)) {
			int left = centerX - request.Width / 2;
			int right = left + request.Width - 1;
			if (!WorldGen.InWorld(left, 50, 45) || !WorldGen.InWorld(right, 50, 45)) {
			rejections.Add($"x={centerX}: world padding");
				continue;
			}

			int maximumRepairableRelief = spawnTerrace ? 96 : 12;
			if (!TryMeasureSurface(left, right, out int surfaceY, out int relief) || relief > maximumRepairableRelief) {
				rejections.Add($"x={centerX}: surface relief {relief}");
				continue;
			}

			Rectangle area = new(left, surfaceY - 12, request.Width, 28);
			if (!TileEditor.IsSafeForTerrainFeature(area)) {
				rejections.Add($"x={centerX}: protected tile, liquid, wire, or chest");
				continue;
			}

			if (!spawnTerrace && respectStructureMap && !GenVars.structures.CanPlace(area, padding: 8)) {
				rejections.Add($"x={centerX}: StructureMap collision");
				continue;
			}

			if (relief > 2) {
				Flatten(left, right, surfaceY);
				if (!TryMeasureSurface(left, right, out surfaceY, out relief) || relief > 2) {
					rejections.Add($"x={centerX}: bounded flattening left {relief} relief");
					continue;
				}
				area = new Rectangle(left, surfaceY - 12, request.Width, 28);
			}

			GenVars.structures.AddProtectedStructure(area, padding: 8);
			terrace = new BuildTerrace(area, surfaceY, spawnTerrace);
			failure = string.Empty;
			return true;
		}

		terrace = default;
		failure = string.Join("; ", rejections);
		return false;
	}

	private static void Flatten(int left, int right, int targetY)
	{
		for (int x = left; x <= right; x++) {
			int currentY = BiomeClassifier.TryFindGroundSupport(x, out int naturalY)
				? naturalY
				: WorldPlanner.FindSurfaceY(x);
			ushort material = Main.tile[x, currentY].TileType;
			if (currentY < targetY) {
				for (int y = currentY; y < targetY; y++) {
					TileEditor.ClearTerrain(x, y);
				}
			}
			else {
				for (int y = targetY; y <= currentY; y++) {
					TileEditor.SetTerrain(x, y, material);
				}
			}

			TileEditor.SetTerrain(x, targetY, material);
		}
	}

	private static IEnumerable<int> CandidateCenters(TerraceRequest request)
	{
		yield return request.PreferredX;
		if (request.Required) {
			yield break;
		}

		for (int attempt = 1; attempt < CandidateBudget; attempt++) {
			int magnitude = (attempt + 1) / 2;
			int direction = attempt % 2 == 1 ? -1 : 1;
			yield return request.PreferredX + direction * magnitude * SearchStep;
		}
	}

	private static IEnumerable<TerraceRequest> FinalCandidates(WorldPlan plan, GenerationManifest manifest)
	{
		foreach (WorldRegion region in plan.Regions
			.OrderBy(region => region.Landform is LandformKind.QuietLowland or LandformKind.Plateau ? 0 : 1)
			.ThenByDescending(region => Math.Abs(region.CenterX - plan.SpawnX))) {
			int[] numerators = [1, 2, 3];
			foreach (int numerator in numerators) {
				int centerX = region.Left + region.Width * numerator / 4;
				if (Math.Abs(centerX - plan.SpawnX) < 180
					|| manifest.Terraces.Any(terrace => Math.Abs(terrace.Area.Center.X - centerX) < 220)) {
					continue;
				}

				yield return new TerraceRequest(centerX, 64, Required: false);
			}
		}
	}

	private static bool TryMeasureSurface(int left, int right, out int medianY, out int relief)
	{
		List<int> samples = [];
		int minimum = int.MaxValue;
		int maximum = int.MinValue;
		for (int x = left; x <= right; x += 2) {
			int y = BiomeClassifier.TryFindGroundSupport(x, out int naturalY)
				? naturalY
				: WorldPlanner.FindSurfaceY(x);
			if (!TileEditor.IsSolid(x, y)) {
				medianY = 0;
				relief = int.MaxValue;
				return false;
			}

			samples.Add(y);
			minimum = Math.Min(minimum, y);
			maximum = Math.Max(maximum, y);
		}

		samples.Sort();
		medianY = samples[samples.Count / 2];
		relief = maximum - minimum;
		return true;
	}
}
