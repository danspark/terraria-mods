using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace WorldToVanilla;

internal enum WorldExportStatus
{
	Copied,
	AlreadyExists
}

internal readonly record struct WorldExportResult(
	WorldExportStatus Status,
	string DestinationPath);

internal static class WorldExportFile
{
	private const int CopyBufferSize = 128 * 1024;

	public static WorldExportResult Export(
		ReadOnlySpan<byte> worldData,
		string sourceFileName,
		string worldsDirectory)
	{
		if (worldData.IsEmpty) {
			throw new InvalidDataException("The selected world file is empty.");
		}
		if (!sourceFileName.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidDataException($"The selected file is not a Terraria world: {sourceFileName}");
		}

		Directory.CreateDirectory(worldsDirectory);

		foreach (string destinationPath in EnumerateDestinationPaths(worldsDirectory, sourceFileName)) {
			if (File.Exists(destinationPath)) {
				if (FileContentsEqual(destinationPath, worldData)) {
					return new WorldExportResult(WorldExportStatus.AlreadyExists, destinationPath);
				}

				continue;
			}

			WriteAtomically(destinationPath, worldData);
			return new WorldExportResult(WorldExportStatus.Copied, destinationPath);
		}

		throw new IOException("Could not find an unused file name in vanilla Terraria's Worlds folder.");
	}

	private static IEnumerable<string> EnumerateDestinationPaths(
		string worldsDirectory,
		string sourceFileName)
	{
		yield return Path.Combine(worldsDirectory, sourceFileName);

		string fileStem = Path.GetFileNameWithoutExtension(sourceFileName);
		string extension = Path.GetExtension(sourceFileName);
		yield return Path.Combine(worldsDirectory, $"{fileStem}_tModLoader{extension}");

		for (int copyNumber = 2; copyNumber <= 10_000; copyNumber++) {
			yield return Path.Combine(
				worldsDirectory,
				$"{fileStem}_tModLoader_{copyNumber}{extension}");
		}
	}

	private static bool FileContentsEqual(string path, ReadOnlySpan<byte> expected)
	{
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (stream.Length != expected.Length) {
			return false;
		}

		byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
		try {
			int offset = 0;
			while (offset < expected.Length) {
				int bytesRead = stream.Read(buffer, 0, Math.Min(buffer.Length, expected.Length - offset));
				if (bytesRead == 0 || !expected.Slice(offset, bytesRead).SequenceEqual(buffer.AsSpan(0, bytesRead))) {
					return false;
				}

				offset += bytesRead;
			}

			return true;
		}
		finally {
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private static void WriteAtomically(string destinationPath, ReadOnlySpan<byte> worldData)
	{
		string directory = Path.GetDirectoryName(destinationPath)
			?? throw new InvalidOperationException("The destination has no parent directory.");
		string temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.wld.tmp");

		try {
			using (FileStream stream = new(
				temporaryPath,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None)) {
				stream.Write(worldData);
				stream.Flush(flushToDisk: true);
			}

			File.Move(temporaryPath, destinationPath);
		}
		finally {
			if (File.Exists(temporaryPath)) {
				File.Delete(temporaryPath);
			}
		}
	}
}
