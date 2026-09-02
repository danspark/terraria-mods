using System;
using System.Collections.Generic;
using System.IO;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.UI;

namespace WorldToVanilla;

internal static class WorldExportUi
{
	private const float ButtonSpacing = 24f;
	private static readonly HashSet<WorldExportButton> Buttons = [];
	private static Mod? _mod;
	private static WorldExportCatalog? _catalog;

	public static void Load(Mod mod)
	{
		_mod = mod;
		On_UIWorldSelect.OnActivate += RefreshCatalog;
		On_UIWorldListItem.AddTmlElements += AddExportButton;
	}

	public static void Unload()
	{
		On_UIWorldSelect.OnActivate -= RefreshCatalog;
		On_UIWorldListItem.AddTmlElements -= AddExportButton;
		DetachButtons();
		_catalog = null;
		_mod = null;
	}

	private static void RefreshCatalog(
		On_UIWorldSelect.orig_OnActivate original,
		UIWorldSelect worldSelect)
	{
		DetachButtons();
		_catalog = WorldExportCatalog.Load(WorldExporter.GetVanillaWorldsDirectory());
		original(worldSelect);
	}

	private static void AddExportButton(
		On_UIWorldListItem.orig_AddTmlElements original,
		UIWorldListItem item,
		WorldFileData world,
		ref float offset)
	{
		original(item, world, ref offset);
		WorldExportCatalog catalog = _catalog
			??= WorldExportCatalog.Load(WorldExporter.GetVanillaWorldsDirectory());
		WorldExportState state = catalog.GetState(world);

		WorldExportButton button = new(
			UICommon.ButtonOpenFolder,
			state,
			GetStatusText(state, world.Name)) {
			VAlign = 1f
		};
		button.Left.Set(offset, 0f);
		button.OnLeftClick += (_, _) => Export(world, button);
		item.Append(button);
		Buttons.Add(button);
		offset += ButtonSpacing;
	}

	private static void Export(WorldFileData world, WorldExportButton button)
	{
		try {
			WorldExportResult result = WorldExporter.Export(world);
			string destinationFileName = Path.GetFileName(result.DestinationPath);
			_catalog?.Record(world);
			string tooltipText = result.Status switch {
				WorldExportStatus.Copied => Language.GetTextValue(
					"Mods.WorldToVanilla.UI.Exported",
					destinationFileName),
				WorldExportStatus.AlreadyExists => Language.GetTextValue(
					"Mods.WorldToVanilla.UI.AlreadyExported",
					destinationFileName),
				_ => throw new ArgumentOutOfRangeException(nameof(result.Status))
			};
			button.SetState(WorldExportState.UpToDate, tooltipText);

			SoundEngine.PlaySound(SoundID.Unlock);
			_mod?.Logger.Info(
				$"Exported world '{world.Name}' to vanilla Terraria: {result.DestinationPath} " +
				$"({result.Status})");
		}
		catch (Exception exception) {
			button.SetState(
				WorldExportState.Unavailable,
				Language.GetTextValue(
					"Mods.WorldToVanilla.UI.ExportFailed",
					exception.Message));
			SoundEngine.PlaySound(SoundID.MenuClose);
			_mod?.Logger.Error($"Could not export world '{world.Name}' to vanilla Terraria.", exception);
		}
	}

	private static string GetStatusText(WorldExportState state, string worldName)
	{
		return state switch {
			WorldExportState.NotExported => Language.GetTextValue(
				"Mods.WorldToVanilla.UI.Export",
				worldName),
			WorldExportState.UpToDate => Language.GetTextValue(
				"Mods.WorldToVanilla.UI.UpToDate"),
			WorldExportState.Outdated => Language.GetTextValue(
				"Mods.WorldToVanilla.UI.Outdated"),
			WorldExportState.VanillaNewer => Language.GetTextValue(
				"Mods.WorldToVanilla.UI.VanillaNewer"),
			WorldExportState.Unavailable => Language.GetTextValue(
				"Mods.WorldToVanilla.UI.StatusUnavailable"),
			_ => throw new ArgumentOutOfRangeException(nameof(state))
		};
	}

	private static void DetachButtons()
	{
		foreach (WorldExportButton button in Buttons) {
			button.Remove();
		}

		Buttons.Clear();
	}
}
