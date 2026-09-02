using System;

namespace VanillaWorldsOverhauled.WorldGeneration;

internal static class OrganicBoundary
{
	public static int Profile(
		int coordinate,
		int seed,
		int macroScale,
		int detailScale,
		int macroAmplitude,
		int detailAmplitude)
	{
		double macro = (Value1D(coordinate, seed ^ 0x4D41_4352, macroScale) - 0.5d) * 2d * macroAmplitude;
		double detail = (Value1D(coordinate, seed ^ 0x4445_5441, detailScale) - 0.5d) * 2d * detailAmplitude;
		int grain = Hash(coordinate, seed ^ 0x4752_4149) % 3 - 1;
		return (int)Math.Round(macro + detail) + grain;
	}

	public static double Field(int x, int y, int seed, int macroScale, int detailScale)
	{
		int warpScale = Math.Max(9, macroScale * 2);
		int warpX = (int)Math.Round((Value2D(x, y, seed ^ 0x5741_5258, warpScale) - 0.5d) * macroScale * 1.4d);
		int warpY = (int)Math.Round((Value2D(x, y, seed ^ 0x5741_5259, warpScale) - 0.5d) * macroScale * 1.1d);
		double macro = Value2D(x + warpX, y + warpY, seed ^ 0x4649_454C, macroScale);
		double detail = Value2D(x, y, seed ^ 0x4445_5446, detailScale);
		double grain = Hash(x * 73_856_093 ^ y * 19_349_663, seed ^ 0x4752_4E46) / (double)int.MaxValue;
		return Math.Clamp(macro * 0.58d + detail * 0.29d + grain * 0.13d, 0d, 1d);
	}

	private static double Value1D(int coordinate, int seed, int cellSize)
	{
		cellSize = Math.Max(2, cellSize);
		int cell = FloorDivide(coordinate, cellSize);
		double local = (coordinate - cell * cellSize) / (double)cellSize;
		double blend = Smooth(local);
		return Lerp(UnitHash(cell, 0, seed), UnitHash(cell + 1, 0, seed), blend);
	}

	private static double Value2D(int x, int y, int seed, int cellSize)
	{
		cellSize = Math.Max(2, cellSize);
		int cellX = FloorDivide(x, cellSize);
		int cellY = FloorDivide(y, cellSize);
		double localX = Smooth((x - cellX * cellSize) / (double)cellSize);
		double localY = Smooth((y - cellY * cellSize) / (double)cellSize);
		double top = Lerp(UnitHash(cellX, cellY, seed), UnitHash(cellX + 1, cellY, seed), localX);
		double bottom = Lerp(UnitHash(cellX, cellY + 1, seed), UnitHash(cellX + 1, cellY + 1, seed), localX);
		return Lerp(top, bottom, localY);
	}

	private static int FloorDivide(int value, int divisor) =>
		value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

	private static double UnitHash(int x, int y, int seed) =>
		Hash(x * 73_856_093 ^ y * 19_349_663, seed) / (double)int.MaxValue;

	private static int Hash(int value, int seed)
	{
		unchecked {
			uint hash = (uint)value ^ (uint)seed;
			hash ^= hash >> 16;
			hash *= 0x7FEB_352Du;
			hash ^= hash >> 15;
			hash *= 0x846C_A68Bu;
			hash ^= hash >> 16;
			return (int)(hash & 0x7FFF_FFFFu);
		}
	}

	private static double Smooth(double value) => value * value * (3d - 2d * value);

	private static double Lerp(double left, double right, double amount) => left + (right - left) * amount;
}
