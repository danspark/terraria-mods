using System;
using System.Collections.Generic;
using System.IO;
using Terraria.IO;

namespace WorldToVanilla;

internal sealed class WorldExportCatalog
{
	private readonly Dictionary<Guid, HashSet<uint>> _revisionsByWorldId = [];

	private WorldExportCatalog(bool isAvailable)
	{
		IsAvailable = isAvailable;
	}

	private bool IsAvailable { get; }

	public static WorldExportCatalog Load(string worldsDirectory)
	{
		WorldExportCatalog catalog = new(isAvailable: true);
		if (!Directory.Exists(worldsDirectory)) {
			return catalog;
		}

		try {
			foreach (string path in Directory.EnumerateFiles(
				worldsDirectory,
				"*.wld",
				SearchOption.TopDirectoryOnly)) {
				if (WorldExportIdentityReader.TryRead(path, out WorldExportIdentity identity)) {
					catalog.Record(identity);
				}
			}

			return catalog;
		}
		catch (Exception exception) when (
			exception is IOException
				or UnauthorizedAccessException
				or ArgumentException) {
			return new WorldExportCatalog(isAvailable: false);
		}
	}

	public WorldExportState GetState(WorldFileData world)
	{
		if (!IsAvailable || world.Metadata is null || world.UniqueId == Guid.Empty) {
			return WorldExportState.Unavailable;
		}

		return _revisionsByWorldId.TryGetValue(world.UniqueId, out HashSet<uint>? revisions)
			? WorldExportFreshness.Decide(world.Metadata.Revision, revisions)
			: WorldExportState.NotExported;
	}

	public void Record(WorldFileData world)
	{
		if (world.Metadata is not null && world.UniqueId != Guid.Empty) {
			Record(new WorldExportIdentity(world.UniqueId, world.Metadata.Revision));
		}
	}

	private void Record(WorldExportIdentity identity)
	{
		if (!_revisionsByWorldId.TryGetValue(identity.WorldId, out HashSet<uint>? revisions)) {
			revisions = [];
			_revisionsByWorldId.Add(identity.WorldId, revisions);
		}

		revisions.Add(identity.Revision);
	}
}
