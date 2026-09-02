using System.Collections.Generic;

namespace WorldToVanilla;

internal enum WorldExportState
{
	NotExported,
	UpToDate,
	Outdated,
	VanillaNewer,
	Unavailable
}

internal static class WorldExportFreshness
{
	public static WorldExportState Decide(
		uint sourceRevision,
		IEnumerable<uint> exportedRevisions)
	{
		bool foundExport = false;
		bool foundNewerExport = false;

		foreach (uint exportedRevision in exportedRevisions) {
			foundExport = true;
			if (exportedRevision == sourceRevision) {
				return WorldExportState.UpToDate;
			}

			foundNewerExport |= exportedRevision > sourceRevision;
		}

		if (!foundExport) {
			return WorldExportState.NotExported;
		}

		return foundNewerExport
			? WorldExportState.VanillaNewer
			: WorldExportState.Outdated;
	}
}
