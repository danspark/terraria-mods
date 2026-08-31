using System;

namespace RicherBiomes.WorldGeneration;

internal static class Profiles
{
	public static int ForestSurface(WorldPlan plan, int distance)
	{
		double t = (double)(distance - plan.Forest.Start) / (plan.Forest.Length - 1);
		double valleyAndRidges =
			-26d * Math.Sin(Math.PI * t) +
			22d * Math.Sin(3d * Math.PI * t) -
			9d * Math.Sin(5d * Math.PI * t);

		return plan.BaseSurfaceY + (int)Math.Round(valleyAndRidges);
	}

	public static int MountainSurface(WorldPlan plan, int distance)
	{
		int local = distance - plan.Mountain.Start;
		int edgeDistance = Math.Min(local, plan.Mountain.Length - 1 - local);
		int rise = plan.BaseSurfaceY - plan.PeakY;
		int ascent = Math.Min(rise, edgeDistance);
		int y = plan.BaseSurfaceY - ascent;

		if (ascent == rise) {
			int center = (plan.Mountain.Length - 1) / 2;
			int saddle = Math.Max(0, 10 - Math.Abs(local - center) / 5);
			y += saddle;
		}

		return y;
	}

	public static int MountainTunnelFloor(WorldPlan plan, int distance)
	{
		int local = distance - plan.Mountain.Start;
		int fromFarEdge = plan.Mountain.End - distance;

		if (local < 45) {
			return plan.BaseSurfaceY - local / 5;
		}

		if (fromFarEdge < 45) {
			return plan.BaseSurfaceY - fromFarEdge / 5;
		}

		return plan.BaseSurfaceY - 9 + (int)Math.Round(3d * Math.Sin(local / 54d));
	}

	public static int ForestRootFloor(WorldPlan plan, int distance)
	{
		int local = distance - plan.Forest.Start;
		int fromFarEdge = plan.Forest.End - distance;
		int depth = Math.Min(20, Math.Min(local, fromFarEdge) * 20 / 28);

		return ForestSurface(plan, distance) + depth +
			(int)Math.Round(2d * Math.Sin(local / 31d));
	}
}
