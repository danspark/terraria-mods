using System;
using System.IO;
using ReLogic.OS;
using Terraria;
using Terraria.IO;
using Terraria.Utilities;

namespace WorldToVanilla;

internal static class WorldExporter
{
	public static WorldExportResult Export(WorldFileData world)
	{
		ArgumentNullException.ThrowIfNull(world);

		byte[] worldData = FileUtilities.ReadAllBytes(world.Path, world.IsCloudSave);
		string worldsDirectory = GetVanillaWorldsDirectory();
		return WorldExportFile.Export(worldData, world.GetFileName(), worldsDirectory);
	}

	public static string GetVanillaWorldsDirectory()
	{
		string terrariaStorage = Platform
			.Get<IPathService>()
			.GetStoragePath(Program.TerrariaSaveFolderPath);

		return Path.GetFullPath(Path.Combine(terrariaStorage, "Worlds"));
	}
}
