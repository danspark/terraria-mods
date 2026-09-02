using System;
using System.IO;

namespace WorldToVanilla;

internal readonly record struct WorldExportIdentity(Guid WorldId, uint Revision);

internal static class WorldExportIdentityReader
{
	private const int FirstVersionWithMetadata = 135;
	private const int FirstVersionWithWorldId = 181;
	private const ulong MetadataMagicNumber = 27981915666277746UL;
	private const byte WorldFileType = 2;

	public static bool TryRead(string path, out WorldExportIdentity identity)
	{
		identity = default;

		try {
			using FileStream stream = new(
				path,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite | FileShare.Delete);
			using BinaryReader reader = new(stream);

			int version = reader.ReadInt32();
			if (version < FirstVersionWithWorldId) {
				return false;
			}

			uint revision = ReadRevision(reader, version);
			short sectionCount = reader.ReadInt16();
			if (sectionCount <= 0) {
				return false;
			}

			int headerOffset = reader.ReadInt32();
			if (headerOffset < stream.Position || headerOffset >= stream.Length) {
				return false;
			}

			stream.Position = headerOffset;
			_ = reader.ReadString();
			_ = reader.ReadString();
			_ = reader.ReadUInt64();

			byte[] worldIdBytes = reader.ReadBytes(16);
			if (worldIdBytes.Length != 16) {
				return false;
			}

			Guid worldId = new(worldIdBytes);
			if (worldId == Guid.Empty) {
				return false;
			}

			identity = new WorldExportIdentity(worldId, revision);
			return true;
		}
		catch (Exception exception) when (
			exception is IOException
				or UnauthorizedAccessException
				or FormatException
				or ArgumentException) {
			return false;
		}
	}

	private static uint ReadRevision(BinaryReader reader, int version)
	{
		if (version < FirstVersionWithMetadata) {
			return 0;
		}

		ulong packedMetadata = reader.ReadUInt64();
		ulong magicNumber = packedMetadata & 0x00FFFFFFFFFFFFFFUL;
		byte fileType = (byte)(packedMetadata >> 56);
		if (magicNumber != MetadataMagicNumber || fileType != WorldFileType) {
			throw new FormatException("The file does not contain Terraria world metadata.");
		}

		uint revision = reader.ReadUInt32();
		_ = reader.ReadUInt64();
		return revision;
	}
}
