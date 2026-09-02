using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace RicherBiomes.WorldGeneration;

internal static class ForestWaterBridgeGenerator
{
	private const int FeatureSeedSalt = 0x464C_414B;
	private const int CandidateBudget = 360;

	public static void Apply(WorldPlan plan, GenerationManifest manifest, GenerationProgress progress)
	{
		manifest.ForestLakeBridges.Clear();
		UnifiedRandom random = new(MixSeed(plan.GenerationSeed, FeatureSeedSalt));
		int occurrenceChance = Main.maxTilesX <= 4200 ? 58 : Main.maxTilesX <= 6400 ? 66 : 72;
		if (random.Next(100) >= occurrenceChance) {
			progress.Set(1d);
			return;
		}

		int desired = Main.maxTilesX >= 8400 && random.NextBool(3) ? 2 : 1;
		for (int index = 0; index < desired; index++) {
			if (!TryPlan(plan, manifest, random, index, out ForestLakeBridgePlan bridge)) {
				break;
			}

			ForestLakeBridgeRecord record = Build(bridge);
			manifest.ForestLakeBridges.Add(record);
			GenVars.structures.AddProtectedStructure(record.Area, padding: 8);
			progress.Set((double)(index + 1) / desired);
		}

		progress.Set(1d);
	}

	public static void RepairAndRefill(GenerationManifest manifest)
	{
		for (int index = 0; index < manifest.ForestLakeBridges.Count; index++) {
			ForestLakeBridgeRecord record = manifest.ForestLakeBridges[index];
			ForestLakeBridgePlan plan = new(
				record.Style,
				record.Area,
				record.DeckY,
				record.WaterlineY,
				record.Depth,
				record.FeatureSeed);
			manifest.ForestLakeBridges[index] = Build(plan);
		}
	}

	private static bool TryPlan(
		WorldPlan plan,
		GenerationManifest manifest,
		UnifiedRandom random,
		int featureIndex,
		out ForestLakeBridgePlan accepted)
	{
		ForestLakeBridgePlan? best = null;
		int bestScore = int.MinValue;
		for (int attempt = 0; attempt < CandidateBudget; attempt++) {
			int width = random.Next(82, Main.maxTilesX >= 6400 ? 129 : 115);
			int centerX = random.Next(plan.CoastMargin + width, Main.maxTilesX - plan.CoastMargin - width);
			WorldRegion region = plan.RegionAt(centerX);
			if (region.Landform == LandformKind.Mountain || Math.Abs(centerX - plan.SpawnX) < 220) {
				continue;
			}

			int left = centerX - width / 2;
			int right = left + width;
			int forestColumns = 0;
			int minimumGround = int.MaxValue;
			int maximumGround = int.MinValue;
			int leftSum = 0;
			int rightSum = 0;
			const int edgeSamples = 8;
			bool complete = true;
			for (int x = left; x < right; x++) {
				if (!BiomeClassifier.TryFindGroundSupport(x, out int groundY)) {
					complete = false;
					break;
				}
				minimumGround = Math.Min(minimumGround, groundY);
				maximumGround = Math.Max(maximumGround, groundY);
				forestColumns += BiomeClassifier.ClassifySupport(Main.tile[x, groundY].TileType, x, groundY) == BiomeKind.Forest ? 1 : 0;
				if (x < left + edgeSamples) {
					leftSum += groundY;
				}
				if (x >= right - edgeSamples) {
					rightSum += groundY;
				}
			}

			if (!complete || forestColumns < width * 4 / 5 || maximumGround - minimumGround > 16) {
				continue;
			}

			int leftGround = leftSum / edgeSamples;
			int rightGround = rightSum / edgeSamples;
			if (Math.Abs(leftGround - rightGround) > 7) {
				continue;
			}

			int deckY = (leftGround + rightGround) / 2 - 1;
			int waterlineY = deckY + 5;
			int depth = random.Next(15, 26);
			Rectangle area = new(left - 9, minimumGround - 15, width + 18, waterlineY + depth + 9 - (minimumGround - 15));
			if (!WorldGen.InWorld(area.Left, area.Top, 18)
				|| !WorldGen.InWorld(area.Right - 1, area.Bottom - 1, 18)
				|| !TileEditor.IsSafeForTerrainFeature(area)
				|| !GenVars.structures.CanPlace(area, padding: 4)
				|| IntersectsOwnedFeature(manifest, area, 16)) {
				continue;
			}

			int seed = MixSeed(plan.GenerationSeed, FeatureSeedSalt ^ featureIndex * 7919 ^ centerX);
			ForestBridgeStyle style = (ForestBridgeStyle)Math.Abs(seed % 3);
			ForestLakeBridgePlan candidate = new(style, area, deckY, waterlineY, depth, seed);
			int score = forestColumns * 6 - (maximumGround - minimumGround) * 22 + Math.Abs(centerX - plan.SpawnX) / 18;
			if (score > bestScore) {
				best = candidate;
				bestScore = score;
			}
		}

		accepted = best ?? default;
		return best is not null;
	}

	private static ForestLakeBridgeRecord Build(ForestLakeBridgePlan plan)
	{
		int innerLeft = plan.Area.Left + 9;
		int innerRight = plan.Area.Right - 10;
		int span = Math.Max(1, innerRight - innerLeft);
		ushort deckMaterial = plan.Style switch {
			ForestBridgeStyle.LivingWoodCauseway => TileID.LivingWood,
			ForestBridgeStyle.StoneAndTimber => TileID.GrayBrick,
			_ => TileID.WoodBlock
		};
		ushort basinWall = plan.Style == ForestBridgeStyle.StoneAndTimber ? WallID.Stone : WallID.DirtUnsafe;

		for (int x = innerLeft; x <= innerRight; x++) {
			double amount = (double)(x - innerLeft) / span;
			double bowl = Math.Pow(Math.Sin(Math.PI * amount), 0.72d);
			int floorJitter = OrganicBoundary.Profile(x, plan.Seed ^ 0x4245_4421, 27, 7, 3, 2);
			int floorY = plan.WaterlineY + 2 + (int)Math.Round(plan.Depth * bowl) + floorJitter;
			floorY = Math.Clamp(floorY, plan.WaterlineY + 2, plan.Area.Bottom - 5);
			int rimLift = (int)Math.Round(bowl * 4d)
				+ OrganicBoundary.Profile(x, plan.Seed ^ 0x5249_4D21, 19, 5, 2, 1);
			int clearTop = Math.Min(plan.DeckY - 8, plan.WaterlineY - 2 - Math.Max(0, rimLift));
			for (int y = Math.Max(plan.Area.Top + 2, clearTop); y < floorY; y++) {
				TileEditor.ClearTerrain(x, y, clearWall: y < plan.WaterlineY - 1);
				if (y >= plan.WaterlineY - 1) {
					TileEditor.SetWall(x, y, basinWall);
				}
			}

			int shellDepth = 4 + Math.Abs(OrganicBoundary.Profile(x, plan.Seed ^ 0x5348_454C, 23, 9, 2, 1));
			for (int depth = 0; depth < shellDepth; depth++) {
				ushort material = depth == 0 && (Math.Abs(x - innerLeft) < 7 || Math.Abs(innerRight - x) < 7)
					? TileID.Grass
					: depth < 2 ? TileID.Dirt : TileID.Stone;
				TileEditor.SetTerrain(x, floorY + depth, material);
			}
			for (int y = plan.WaterlineY; y < floorY; y++) {
				TileEditor.SetLiquid(x, y, (byte)LiquidID.Water, byte.MaxValue);
			}
		}

		BuildOrganicBanks(plan, innerLeft, innerRight);
		(int deckTiles, int supportTiles) = BuildBridge(plan, innerLeft, innerRight, deckMaterial);
		TileEditor.Frame(plan.Area, border: 3);
		int waterCells = CountWater(plan.Area);
		return new ForestLakeBridgeRecord(
			plan.Style,
			plan.Area,
			plan.DeckY,
			plan.WaterlineY,
			plan.Depth,
			plan.Seed,
			waterCells,
			deckTiles,
			supportTiles);
	}

	private static void BuildOrganicBanks(ForestLakeBridgePlan plan, int innerLeft, int innerRight)
	{
		foreach ((int edgeX, int direction) in new[] { (innerLeft, -1), (innerRight, 1) }) {
			for (int step = 0; step <= 11; step++) {
				int x = edgeX + direction * step;
				int surfaceY = plan.WaterlineY - 1
					- step / 2
					+ OrganicBoundary.Profile(x, plan.Seed ^ edgeX ^ 0x4241_4E4B, 13, 5, 2, 1);
				for (int y = plan.Area.Top + 1; y < surfaceY; y++) {
					TileEditor.ClearTerrain(x, y, clearWall: true);
				}
				int depth = 5 + Math.Max(0, OrganicBoundary.Profile(x, plan.Seed ^ 0x4241_5345, 17, 7, 3, 1));
				for (int y = surfaceY; y < surfaceY + depth; y++) {
					TileEditor.SetTerrain(x, y, y == surfaceY ? TileID.Grass : TileID.Dirt);
				}
				if (step < 7) {
					SlopeType slope = direction < 0 ? SlopeType.SlopeDownLeft : SlopeType.SlopeDownRight;
					Tile bank = Main.tile[x, surfaceY];
					bank.Slope = slope;
				}
			}
		}
	}

	private static (int DeckTiles, int SupportTiles) BuildBridge(
		ForestLakeBridgePlan plan,
		int innerLeft,
		int innerRight,
		ushort deckMaterial)
	{
		int deckTiles = 0;
		int supportTiles = 0;
		int bridgeLeft = innerLeft - 7;
		int bridgeRight = innerRight + 7;
		int dropBayA = bridgeLeft + (bridgeRight - bridgeLeft) / 3;
		int dropBayB = bridgeLeft + (bridgeRight - bridgeLeft) * 2 / 3;
		for (int x = bridgeLeft; x <= bridgeRight; x++) {
			for (int y = plan.DeckY - 7; y < plan.DeckY; y++) {
				TileEditor.ClearTerrain(x, y, clearWall: y < plan.DeckY - 4);
			}
			bool dropBay = Math.Abs(x - dropBayA) <= 1 || Math.Abs(x - dropBayB) <= 1;
			if (dropBay) {
				TileEditor.ClearTerrain(x, plan.DeckY);
				TileEditor.ClearTerrain(x, plan.DeckY + 1);
				TileEditor.TryPlacePlatformForced(x, plan.DeckY, style: 0);
			}
			else {
				TileEditor.SetTerrain(x, plan.DeckY, deckMaterial);
				TileEditor.SetTerrain(x, plan.DeckY + 1, deckMaterial);
			}
			deckTiles++;

			double wallField = OrganicBoundary.Field(x, plan.DeckY, plan.Seed ^ 0x5452_5553, 23, 7);
			if (wallField > 0.43d) {
				for (int y = plan.DeckY - 4; y < plan.DeckY; y++) {
					TileEditor.SetWall(x, y, WallID.LivingWoodUnsafe);
				}
			}
		}

		UnifiedRandom random = new(MixSeed(plan.Seed, 0x5355_5050));
		for (int supportX = bridgeLeft + random.Next(7, 12); supportX < bridgeRight - 4;) {
			int bottomY = FindSupportFloor(plan, supportX);
			for (int y = plan.DeckY + 2; y < bottomY; y++) {
				if (Main.tile[supportX, y].LiquidAmount > 0 && y < plan.WaterlineY + 2) {
					continue;
				}
				TileEditor.SetTerrain(supportX, y, TileID.WoodenBeam);
				if (supportX + 1 < bridgeRight) {
					TileEditor.SetTerrain(supportX + 1, y, TileID.WoodenBeam);
				}
				supportTiles += 2;
			}
			TileEditor.TryPlaceTorch(supportX - 1, plan.DeckY - 3);
			supportX += random.Next(9, 15);
		}

		TileEditor.SetSlopedTerrain(bridgeLeft, plan.DeckY, deckMaterial, SlopeType.SlopeDownRight);
		TileEditor.SetSlopedTerrain(bridgeRight, plan.DeckY, deckMaterial, SlopeType.SlopeDownLeft);
		return (deckTiles, supportTiles);
	}

	private static int FindSupportFloor(ForestLakeBridgePlan plan, int x)
	{
		for (int y = plan.WaterlineY + 1; y < plan.Area.Bottom - 2; y++) {
			if (TileEditor.IsSolid(x, y)) {
				return y;
			}
		}
		return plan.Area.Bottom - 3;
	}

	private static int CountWater(Rectangle area)
	{
		int count = 0;
		for (int x = area.Left; x < area.Right; x++) {
			for (int y = area.Top; y < area.Bottom; y++) {
				Tile tile = Main.tile[x, y];
				count += tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Water ? 1 : 0;
			}
		}
		return count;
	}

	private static bool IntersectsOwnedFeature(GenerationManifest manifest, Rectangle area, int padding)
	{
		Rectangle padded = area;
		padded.Inflate(padding, padding);
		return manifest.Terraces.Any(record => record.Area.Intersects(padded))
			|| manifest.Valleys.Any(record => record.Area.Intersects(padded))
			|| manifest.Bridges.Any(record => record.Area.Intersects(padded))
			|| manifest.SkyHighlands.Any(record => record.Area.Intersects(padded))
			|| manifest.BiomeTransitions.Any(record => record.Area.Intersects(padded))
			|| manifest.ForestLakeBridges.Any(record => record.Area.Intersects(padded));
	}

	private static int MixSeed(int seed, int salt)
	{
		unchecked {
			uint value = (uint)seed ^ (uint)salt;
			value ^= value >> 16;
			value *= 0x7FEB_352Du;
			value ^= value >> 15;
			value *= 0x846C_A68Bu;
			value ^= value >> 16;
			return (int)value;
		}
	}

	private readonly record struct ForestLakeBridgePlan(
		ForestBridgeStyle Style,
		Rectangle Area,
		int DeckY,
		int WaterlineY,
		int Depth,
		int Seed);
}
