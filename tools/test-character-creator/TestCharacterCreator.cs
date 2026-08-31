using System;
using System.IO;
using System.Reflection;
using Terraria;
using Terraria.GameContent.Creative;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace TestCharacterCreator;

public sealed class TestCharacterCreator : Mod
{
}

public sealed class CharacterCreatorSystem : ModSystem
{
	private const string CharacterNameVariable = "TEST_CHARACTER_NAME";
	private const string CharacterModeVariable = "TEST_CHARACTER_MODE";
	private const string ResultPathVariable = "TEST_CHARACTER_RESULT";

	private readonly record struct ItemSpec(int Type, int Stack = 1, int Prefix = 0);

	public override void PostSetupContent()
	{
		string? characterName = Environment.GetEnvironmentVariable(CharacterNameVariable);
		if (string.IsNullOrWhiteSpace(characterName)) {
			return;
		}

		characterName = characterName.Trim();
		if (characterName.Length > 20) {
			throw new InvalidOperationException("The test character name cannot exceed 20 characters.");
		}
		string characterMode = Environment.GetEnvironmentVariable(CharacterModeVariable) ?? "journey";
		bool journey = characterMode switch {
			"journey" => true,
			"classic" => false,
			_ => throw new InvalidOperationException($"Unknown character mode: {characterMode}")
		};

		string playerPath = Main.GetPlayerPathFromName(characterName, cloudSave: false);
		if (File.Exists(playerPath) || File.Exists(Path.ChangeExtension(playerPath, ".tplr"))) {
			throw new InvalidOperationException($"Refusing to overwrite {playerPath}");
		}

		Player player = CreatePlayer(characterName, journey);
		int previousLocalPlayer = Main.myPlayer;
		try {
			Main.myPlayer = player.whoAmI;
			CreativePowerManager.Instance
				.GetPower<CreativePowers.GodmodePower>()
				.SetEnabledState(player.whoAmI, state: journey);

			SavePlayer(player, playerPath);
			VerifySavedPlayer(playerPath, journey);
			string resultPath = Environment.GetEnvironmentVariable(ResultPathVariable)
				?? throw new InvalidOperationException($"{ResultPathVariable} was not provided.");
			File.WriteAllText(resultPath, playerPath);
		}
		finally {
			Main.myPlayer = previousLocalPlayer;
		}
	}

	private static void SavePlayer(Player player, string playerPath)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(playerPath)
			?? throw new InvalidOperationException("The player path has no parent directory."));

		PlayerFileData playerFile = new(playerPath, cloudSave: false) {
			Metadata = FileMetadata.FromCurrentSettings(FileType.Player),
			Player = player
		};

		byte[] vanillaData = Player.SavePlayerFile_Vanilla(playerFile);
		Type playerIoType = typeof(Mod).Assembly.GetType(
			"Terraria.ModLoader.IO.PlayerIO",
			throwOnError: true)
			?? throw new TypeLoadException("Terraria.ModLoader.IO.PlayerIO");
		MethodInfo saveModData = playerIoType.GetMethod(
			"SaveData",
			BindingFlags.Static | BindingFlags.NonPublic)
			?? throw new MissingMethodException(playerIoType.FullName, "SaveData");
		TagCompound modData = saveModData.Invoke(null, [player]) as TagCompound
			?? throw new InvalidOperationException("tModLoader did not return player sidecar data.");

		File.WriteAllBytes(playerPath, vanillaData);
		TagIO.ToFile(modData, Path.ChangeExtension(playerPath, ".tplr"));
	}

	private static Player CreatePlayer(string characterName, bool journey)
	{
		Player player = new() {
			name = characterName,
			difficulty = journey ? PlayerDifficultyID.Creative : PlayerDifficultyID.SoftCore,
			whoAmI = 0,
			statLifeMax = 500,
			statLife = 500,
			statManaMax = 200,
			statMana = 200,
			ConsumedLifeCrystals = 15,
			ConsumedLifeFruit = 20,
			ConsumedManaCrystals = 9,
			extraAccessory = true,
			ateArtisanBread = true,
			usedAegisCrystal = true,
			usedAegisFruit = true,
			usedArcaneCrystal = true,
			usedGalaxyPearl = true,
			usedGummyWorm = true,
			usedAmbrosia = true,
			unlockedSuperCart = true,
			enabledSuperCart = true,
			savedPerPlayerFieldsThatArentInThePlayerClass = new Player.SavedPlayerDataWithAnnoyingRules()
		};

		CreativePowerManager.Instance.ResetDataForNewPlayer(player);
		player.savedPerPlayerFieldsThatArentInThePlayerClass.godmodePowerEnabled = journey;

		Equip(player.armor[0], new ItemSpec(ItemID.SolarFlareHelmet));
		Equip(player.armor[1], new ItemSpec(ItemID.SolarFlareBreastplate));
		Equip(player.armor[2], new ItemSpec(ItemID.SolarFlareLeggings));

		Equip(player.armor[3], new ItemSpec(ItemID.TerrasparkBoots, Prefix: PrefixID.Warding));
		Equip(player.armor[4], new ItemSpec(ItemID.LongRainbowTrailWings, Prefix: PrefixID.Warding));
		Equip(player.armor[5], new ItemSpec(ItemID.EmpressFlightBooster, Prefix: PrefixID.Warding));
		Equip(player.armor[6], new ItemSpec(ItemID.MasterNinjaGear, Prefix: PrefixID.Warding));
		Equip(player.armor[7], new ItemSpec(ItemID.Magiluminescence, Prefix: PrefixID.Warding));
		Equip(player.armor[8], new ItemSpec(ItemID.CelestialShell, Prefix: PrefixID.Warding));

		Equip(player.miscEquips[1], new ItemSpec(ItemID.SuspiciousLookingTentacle));
		Equip(player.miscEquips[3], new ItemSpec(ItemID.DrillContainmentUnit));
		Equip(player.miscEquips[4], new ItemSpec(ItemID.LunarHook));

		ItemSpec[] inventory = [
			new(ItemID.Zenith, Prefix: PrefixID.Legendary),
			new(ItemID.DrillContainmentUnit),
			new(ItemID.RodOfHarmony),
			new(ItemID.Shellphone),
			new(ItemID.LastPrism, Prefix: PrefixID.Mythical),
			new(ItemID.SDMG, Prefix: PrefixID.Unreal),
			new(ItemID.EmpressBlade, Prefix: PrefixID.Ruthless),
			new(ItemID.RainbowWhip),
			new(ItemID.PortalGun),
			new(ItemID.HandOfCreation, Prefix: PrefixID.Warding),
			new(ItemID.SuperHealingPotion, 9999),
			new(ItemID.SuperManaPotion, 9999),
			new(ItemID.HorseshoeBundle, Prefix: PrefixID.Warding),
			new(ItemID.AmphibianBoots, Prefix: PrefixID.Warding),
			new(ItemID.FrogGear, Prefix: PrefixID.Warding),
			new(ItemID.ArcticDivingGear, Prefix: PrefixID.Warding),
			new(ItemID.GravityGlobe, Prefix: PrefixID.Warding),
			new(ItemID.AnkhShield, Prefix: PrefixID.Warding),
			new(ItemID.FrozenShield, Prefix: PrefixID.Warding),
			new(ItemID.BottomlessBucket),
			new(ItemID.BottomlessLavaBucket),
			new(ItemID.BottomlessHoneyBucket),
			new(ItemID.BottomlessShimmerBucket),
			new(ItemID.Clentaminator2),
			new(ItemID.MechanicalLens),
			new(ItemID.Binoculars)
		];

		for (int slot = 0; slot < inventory.Length; slot++) {
			Equip(player.inventory[slot], inventory[slot]);
		}

		Equip(player.inventory[50], new ItemSpec(ItemID.PlatinumCoin, 9999));
		Equip(player.inventory[54], new ItemSpec(ItemID.EndlessMusketPouch));
		Equip(player.inventory[55], new ItemSpec(ItemID.EndlessQuiver));
		Equip(player.inventory[56], new ItemSpec(ItemID.RocketIV, 9999));
		player.selectedItem = 0;
		return player;
	}

	private static void Equip(Item destination, ItemSpec spec)
	{
		destination.SetDefaults(spec.Type);
		destination.stack = Math.Clamp(spec.Stack, 1, destination.maxStack);
		if (spec.Prefix != 0 && !destination.Prefix(spec.Prefix)) {
			throw new InvalidOperationException(
				$"Prefix {spec.Prefix} is not valid for item {spec.Type}.");
		}
	}

	private static void VerifySavedPlayer(string playerPath, bool journey)
	{
		PlayerFileData loadedFile = Player.LoadPlayer(playerPath, cloudSave: false);
		Player loaded = loadedFile.Player ?? throw new InvalidOperationException("tModLoader could not reload the generated player.");

		Require(loaded.loadStatus is 0 or 1, $"load status was {loaded.loadStatus}");
		byte expectedDifficulty = journey ? PlayerDifficultyID.Creative : PlayerDifficultyID.SoftCore;
		Require(loaded.difficulty == expectedDifficulty, $"difficulty was {loaded.difficulty}");
		Require(loaded.statLifeMax == 500, $"maximum life was {loaded.statLifeMax}");
		Require(loaded.statManaMax == 200, $"maximum mana was {loaded.statManaMax}");
		Require(
			loaded.savedPerPlayerFieldsThatArentInThePlayerClass.godmodePowerEnabled == journey,
			"godmode state did not match the requested character mode");
		Require(loaded.miscEquips[1].type == ItemID.SuspiciousLookingTentacle, "Suspicious Looking Tentacle is not in the light-pet slot");
		Require(loaded.miscEquips[3].type == ItemID.DrillContainmentUnit, "Drill Containment Unit is not in the mount slot");
		Require(loaded.inventory[0].type == ItemID.Zenith, "Zenith is not in hotbar slot 1");
		Require(loaded.armor[0].type == ItemID.SolarFlareHelmet, "Solar armor did not persist");
		Require(loaded.armor[3].type == ItemID.TerrasparkBoots, "movement accessories did not persist");

		ModContent.GetInstance<TestCharacterCreator>().Logger.Info(
			$"Test character verified after save/load: name={loaded.name}, " +
			$"journey={journey}, godmode={journey}, life={loaded.statLifeMax}, mana={loaded.statManaMax}, " +
			$"lightPet={loaded.miscEquips[1].Name}, mount={loaded.miscEquips[3].Name}, " +
			$"weapon={loaded.inventory[0].Name}");
	}

	private static void Require(bool condition, string failure)
	{
		if (!condition) {
			throw new InvalidOperationException("Generated test character validation failed: " + failure);
		}
	}
}
