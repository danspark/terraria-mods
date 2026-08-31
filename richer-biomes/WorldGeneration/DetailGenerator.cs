using Terraria;
using Terraria.ID;

namespace RicherBiomes.WorldGeneration;

internal static class DetailGenerator
{
	public static void Apply(WorldPlan plan)
	{
		TileEditor.Frame(plan);
		PlantForestTrees(plan);
		PlantMountainTrees(plan);
		AddForestLights(plan);
		TileEditor.Frame(plan);
	}

	private static void PlantForestTrees(WorldPlan plan)
	{
		for (int distance = plan.Forest.Start + 14; distance < plan.Forest.End - 12; distance += 23) {
			int surfaceY = Profiles.ForestSurface(plan, distance);
			int leftY = Profiles.ForestSurface(plan, distance - 1);
			int rightY = Profiles.ForestSurface(plan, distance + 1);
			if (surfaceY == leftY && surfaceY == rightY) {
				WorldGen.GrowTree(plan.XAt(distance), surfaceY);
			}
		}
	}

	private static void PlantMountainTrees(WorldPlan plan)
	{
		for (int distance = plan.Mountain.Start + 20; distance < plan.Mountain.End - 20; distance += 31) {
			int surfaceY = Profiles.MountainSurface(plan, distance);
			if (surfaceY > plan.PeakY + 78 && Main.tile[plan.XAt(distance), surfaceY].TileType == TileID.Grass) {
				WorldGen.GrowTree(plan.XAt(distance), surfaceY);
			}
		}
	}

	private static void AddForestLights(WorldPlan plan)
	{
		for (int distance = plan.Forest.Start + 20; distance < plan.Forest.End; distance += 46) {
			int x = plan.XAt(distance);
			int y = Profiles.ForestSurface(plan, distance) - 2;
			TileEditor.PlaceTorch(x, y);
		}
	}
}
