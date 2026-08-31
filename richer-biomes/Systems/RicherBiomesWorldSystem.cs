using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using RicherBiomes.WorldGeneration;
using Terraria;
using Terraria.GameContent.Generation;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.WorldBuilding;

namespace RicherBiomes.Systems;

public sealed class RicherBiomesWorldSystem : ModSystem
{
	private WorldPlan? _plan;
	private GenerationReport? _report;

	public static ActiveFeatureInfo? ActiveFeature { get; private set; }

	public override void ClearWorld()
	{
		_plan = null;
		_report = null;
		ActiveFeature = null;
	}

	public override void ModifyWorldGenTasks(List<GenPass> tasks, ref double totalWeight)
	{
		int finalCleanupIndex = tasks.FindIndex(pass => pass.Name == "Final Cleanup");
		if (finalCleanupIndex < 0) {
			throw new InvalidOperationException("Richer Biomes could not find Terraria's Final Cleanup world-generation pass.");
		}

		int index = finalCleanupIndex + 1;
		tasks.Insert(index++, new PassLegacy("Richer Biomes: plan featured corridor", Plan));
		tasks.Insert(index++, new PassLegacy("Richer Biomes: shape forest and mountain", ShapeLandforms));
		tasks.Insert(index++, new PassLegacy("Richer Biomes: carve routes and mine", CarveRoutes));
		tasks.Insert(index++, new PassLegacy("Richer Biomes: add route details", AddDetails));
		tasks.Insert(index, new PassLegacy("Richer Biomes: validate playable routes", Validate));
		totalWeight += 5d;
	}

	public override void PostWorldGen()
	{
		WorldPlan plan = RequirePlan();
		GenerationReport report = _report ?? throw new InvalidOperationException("Richer Biomes validation did not run.");
		ActiveFeature = new ActiveFeatureInfo(plan.Direction, WorldPlan.SpawnBuffer, plan.OriginX, report.Summary);
		Mod.Logger.Info($"Richer Biomes generated a {plan.TotalLength}-tile corridor {plan.DirectionName} of spawn. {report.Summary}");
	}

	public override void SaveWorldData(TagCompound tag)
	{
		if (ActiveFeature is null) {
			return;
		}

		tag["generated"] = true;
		tag["direction"] = ActiveFeature.Direction;
		tag["startDistance"] = ActiveFeature.StartDistance;
		tag["originX"] = ActiveFeature.OriginX;
		tag["validation"] = ActiveFeature.ValidationSummary;
	}

	public override void LoadWorldData(TagCompound tag)
	{
		if (!tag.ContainsKey("generated") || !tag.GetBool("generated")) {
			ActiveFeature = null;
			return;
		}

		ActiveFeature = new ActiveFeatureInfo(
			tag.GetInt("direction"),
			tag.GetInt("startDistance"),
			tag.GetInt("originX"),
			tag.GetString("validation"));

		Mod.Logger.Info($"Loaded Richer Biomes world metadata. {ActiveFeature.ValidationSummary}");
	}

	public override void SaveWorldHeader(TagCompound tag)
	{
		if (ActiveFeature is not null) {
			tag["richerBiomes"] = true;
		}
	}

	private void Plan(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Planning a connected forest, mountain, and mine";
		_plan = WorldPlanner.Create();
		Mod.Logger.Info($"Richer Biomes plan: direction={_plan.DirectionName}, x={_plan.MinX}..{_plan.MaxX}, baseY={_plan.BaseSurfaceY}, peakY={_plan.PeakY}, mineBottomY={_plan.MineBottomY}");
	}

	private void ShapeLandforms(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Raising ridges and a sky-piercing mountain";
		LandformGenerator.Apply(RequirePlan());
	}

	private void CarveRoutes(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Carving layered routes and abandoned mine workings";
		WorldPlan plan = RequirePlan();
		RouteGenerator.Apply(plan);
		MineGenerator.Apply(plan);
	}

	private void AddDetails(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Growing trees and finishing route markers";
		WorldPlan plan = RequirePlan();
		DetailGenerator.Apply(plan);
		GenVars.structures.AddProtectedStructure(
			new Rectangle(plan.MinX - 8, Math.Max(20, plan.PeakY - 8), plan.MaxX - plan.MinX + 17, plan.MineBottomY - plan.PeakY + 24),
			padding: 8);
	}

	private void Validate(GenerationProgress progress, GameConfiguration _)
	{
		progress.Message = "Checking every Richer Biomes route";
		_report = WorldValidator.Validate(RequirePlan());
		Mod.Logger.Info("Richer Biomes validation passed. " + _report.Summary);
	}

	private WorldPlan RequirePlan() =>
		_plan ?? throw new InvalidOperationException("Richer Biomes world plan was not created before generation began.");
}

public sealed record ActiveFeatureInfo(int Direction, int StartDistance, int OriginX, string ValidationSummary)
{
	public string DirectionName => Direction > 0 ? "east" : "west";
}
