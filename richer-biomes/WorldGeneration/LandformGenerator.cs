using System;
using Terraria;
using Terraria.ID;

namespace RicherBiomes.WorldGeneration;

internal static class LandformGenerator
{
	public static void Apply(WorldPlan plan)
	{
		for (int distance = plan.Forest.Start; distance <= plan.Forest.End; distance++) {
			ShapeColumn(plan, distance, Profiles.ForestSurface(plan, distance), forest: true);
		}

		for (int distance = plan.Forest.End + 1; distance < plan.Mountain.Start; distance++) {
			ShapeColumn(plan, distance, plan.BaseSurfaceY, forest: true);
		}

		for (int distance = plan.Mountain.Start; distance <= plan.Mountain.End; distance++) {
			ShapeColumn(plan, distance, Profiles.MountainSurface(plan, distance), forest: false);
		}
	}

	private static void ShapeColumn(WorldPlan plan, int distance, int desiredSurfaceY, bool forest)
	{
		int x = plan.XAt(distance);
		int originalSurfaceY = plan.OriginalSurfaceAt(distance);
		int clearTop = Math.Max(25, Math.Min(desiredSurfaceY, originalSurfaceY) - 55);

		for (int y = clearTop; y < desiredSurfaceY; y++) {
			TileEditor.ClearTile(x, y);
		}

		int fillBottom = Math.Max(desiredSurfaceY + 16, originalSurfaceY + 10);
		bool snowy = !forest && desiredSurfaceY <= plan.PeakY + 62;
		for (int y = desiredSurfaceY; y <= fillBottom; y++) {
			ushort tileType = SelectTile(desiredSurfaceY, y, forest, snowy);
			TileEditor.SetTile(x, y, tileType);

			if (y > desiredSurfaceY) {
				ushort wallType = snowy ? WallID.SnowWallUnsafe : y - desiredSurfaceY < 10 ? WallID.DirtUnsafe : WallID.Stone;
				TileEditor.SetWall(x, y, wallType);
			}
		}
	}

	private static ushort SelectTile(int surfaceY, int y, bool forest, bool snowy)
	{
		int depth = y - surfaceY;
		if (snowy) {
			return depth < 8 ? TileID.SnowBlock : depth < 15 ? TileID.IceBlock : TileID.Stone;
		}

		if (depth == 0) {
			return TileID.Grass;
		}

		return depth < (forest ? 12 : 8) ? TileID.Dirt : TileID.Stone;
	}
}
