using System;
using Terraria;
using Terraria.ID;

namespace RicherBiomes.WorldGeneration;

internal static class RouteGenerator
{
	public static void Apply(WorldPlan plan)
	{
		CarveForestRootRoute(plan);
		BuildCanopyRoute(plan, plan.Forest.Start + 55, plan.Forest.Start + 160);
		BuildCanopyRoute(plan, plan.Forest.Start + 215, plan.Forest.Start + 330);
		CarveMountainCrossing(plan);
		CarveMountainChimney(plan, plan.Mountain.Start + 220);
		CarveMountainChimney(plan, plan.Mountain.Start + 425);
		BuildMountainLedges(plan);
	}

	private static void CarveForestRootRoute(WorldPlan plan)
	{
		for (int distance = plan.Forest.Start; distance <= plan.Forest.End; distance++) {
			int x = plan.XAt(distance);
			int surfaceY = Profiles.ForestSurface(plan, distance);
			int floorY = Profiles.ForestRootFloor(plan, distance);
			for (int y = floorY - 7; y < floorY; y++) {
				TileEditor.ClearTile(x, y);
				TileEditor.SetWall(x, y, WallID.LivingWoodUnsafe);
			}

			TileEditor.SetTile(x, floorY, distance % 19 < 4 ? TileID.LivingWood : TileID.Dirt);
			if (floorY > surfaceY && floorY - surfaceY < 8) {
				TileEditor.PlacePlatform(x, surfaceY);
			}
		}

		for (int distance = plan.Forest.Start + 42; distance < plan.Forest.End - 30; distance += 58) {
			int x = plan.XAt(distance);
			int floorY = Profiles.ForestRootFloor(plan, distance);
			TileEditor.SetTile(x, floorY - 7, TileID.LivingWood);
			TileEditor.SetTile(x + plan.Direction, floorY - 7, TileID.LivingWood);
			TileEditor.PlaceTorch(x + plan.Direction * 2, floorY - 2);
		}
	}

	private static void BuildCanopyRoute(WorldPlan plan, int startDistance, int endDistance)
	{
		int floorY = int.MaxValue;
		for (int distance = startDistance; distance <= endDistance; distance++) {
			floorY = Math.Min(floorY, Profiles.ForestSurface(plan, distance) - 18);
		}

		for (int distance = startDistance; distance <= endDistance; distance++) {
			int x = plan.XAt(distance);
			for (int y = floorY - 6; y < floorY; y++) {
				TileEditor.ClearTile(x, y);
			}

			if ((distance - startDistance) % 28 is >= 11 and <= 15) {
				TileEditor.PlacePlatform(x, floorY);
			}
			else {
				TileEditor.SetTile(x, floorY, TileID.LivingWood);
				TileEditor.SetTile(x, floorY + 1, TileID.LivingWood);
			}

			if ((distance - startDistance) % 31 is 0 or 1) {
				TileEditor.SetTile(x, floorY + 2, TileID.LeafBlock);
			}
		}

		PlaceRopeConnector(plan, startDistance + 4, floorY, Profiles.ForestSurface(plan, startDistance + 4));
		PlaceRopeConnector(plan, endDistance - 4, floorY, Profiles.ForestSurface(plan, endDistance - 4));
	}

	private static void CarveMountainCrossing(WorldPlan plan)
	{
		for (int distance = plan.Mountain.Start; distance <= plan.Mountain.End; distance++) {
			int x = plan.XAt(distance);
			int floorY = Profiles.MountainTunnelFloor(plan, distance);
			for (int y = floorY - 8; y < floorY; y++) {
				TileEditor.ClearTile(x, y);
				TileEditor.SetWall(x, y, WallID.Stone);
			}

			TileEditor.SetTile(x, floorY, TileID.Stone);
			if ((distance - plan.Mountain.Start) % 44 == 22) {
				TileEditor.PlaceTorch(x, floorY - 2);
			}
		}

		for (int distance = plan.Mountain.Start + 72; distance < plan.Mountain.End - 60; distance += 84) {
			int x = plan.XAt(distance);
			int floorY = Profiles.MountainTunnelFloor(plan, distance);
			for (int y = floorY - 7; y < floorY; y++) {
				TileEditor.SetWall(x, y, WallID.LivingWoodUnsafe);
			}
			TileEditor.SetTile(x, floorY - 8, TileID.WoodenBeam);
		}
	}

	private static void CarveMountainChimney(WorldPlan plan, int distance)
	{
		int centerX = plan.XAt(distance);
		int topY = Profiles.MountainSurface(plan, distance) - 1;
		int bottomY = Profiles.MountainTunnelFloor(plan, distance) - 1;

		for (int y = topY; y <= bottomY; y++) {
			for (int offset = -2; offset <= 2; offset++) {
				int x = centerX + offset;
				TileEditor.ClearTile(x, y);
				TileEditor.SetWall(x, y, WallID.Stone);
			}

			TileEditor.SetTile(centerX, y, TileID.Rope);
			if ((bottomY - y) % 13 == 0) {
				TileEditor.PlacePlatform(centerX - 2, y);
				TileEditor.PlacePlatform(centerX + 2, y);
			}
		}
	}

	private static void BuildMountainLedges(WorldPlan plan)
	{
		for (int distance = plan.Mountain.Start + 65; distance < plan.Mountain.End - 50; distance += 58) {
			int surfaceY = Profiles.MountainSurface(plan, distance);
			for (int offset = -5; offset <= 5; offset++) {
				int x = plan.XAt(distance) + offset;
				TileEditor.ClearTile(x, surfaceY - 2);
				TileEditor.PlacePlatform(x, surfaceY - 1);
			}
		}
	}

	private static void PlaceRopeConnector(WorldPlan plan, int distance, int topFloorY, int groundY)
	{
		int x = plan.XAt(distance);
		for (int y = topFloorY + 1; y < groundY; y++) {
			TileEditor.ClearTile(x, y);
			TileEditor.SetTile(x, y, TileID.Rope);
		}
	}
}
