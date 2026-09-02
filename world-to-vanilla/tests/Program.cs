using WorldToVanilla;

string testDirectory = Path.Combine(
	Path.GetTempPath(),
	$"world-to-vanilla-tests-{Guid.NewGuid():N}");

try {
	Directory.CreateDirectory(testDirectory);
	byte[] worldData = [1, 2, 3, 4, 5];

	WorldExportResult first = WorldExportFile.Export(worldData, "Test.wld", testDirectory);
	Require(first.Status == WorldExportStatus.Copied, "first export should copy the world");
	Require(Path.GetFileName(first.DestinationPath) == "Test.wld", "first export should keep the file name");
	Require(File.ReadAllBytes(first.DestinationPath).SequenceEqual(worldData), "copied bytes should match");

	WorldExportResult repeated = WorldExportFile.Export(worldData, "Test.wld", testDirectory);
	Require(repeated.Status == WorldExportStatus.AlreadyExists, "repeat export should reuse identical file");
	Require(repeated.DestinationPath == first.DestinationPath, "repeat export should report the original file");

	byte[] changedWorldData = [9, 8, 7];
	WorldExportResult conflict = WorldExportFile.Export(changedWorldData, "Test.wld", testDirectory);
	Require(conflict.Status == WorldExportStatus.Copied, "different data should create another file");
	Require(Path.GetFileName(conflict.DestinationPath) == "Test_tModLoader.wld", "conflict should use the tModLoader suffix");
	Require(File.ReadAllBytes(first.DestinationPath).SequenceEqual(worldData), "conflict must not overwrite the first file");

	byte[] thirdWorldData = [6, 6, 6];
	WorldExportResult numbered = WorldExportFile.Export(thirdWorldData, "Test.wld", testDirectory);
	Require(Path.GetFileName(numbered.DestinationPath) == "Test_tModLoader_2.wld", "another conflict should use a number");

	WorldExportResult repeatedNumbered = WorldExportFile.Export(thirdWorldData, "Test.wld", testDirectory);
	Require(repeatedNumbered.Status == WorldExportStatus.AlreadyExists, "numbered export should also be idempotent");
	Require(repeatedNumbered.DestinationPath == numbered.DestinationPath, "numbered repeat should report its existing file");

	RequireThrows<InvalidDataException>(
		() => WorldExportFile.Export([], "Empty.wld", testDirectory),
		"empty worlds should be rejected");
	RequireThrows<InvalidDataException>(
		() => WorldExportFile.Export(worldData, "NotAWorld.txt", testDirectory),
		"non-world files should be rejected");

	Require(
		WorldExportFreshness.Decide(7, []) == WorldExportState.NotExported,
		"a world with no matching copy should be marked not exported");
	Require(
		WorldExportFreshness.Decide(7, [7]) == WorldExportState.UpToDate,
		"an equal revision should be marked up to date");
	Require(
		WorldExportFreshness.Decide(7, [5, 6]) == WorldExportState.Outdated,
		"older copies should be marked outdated");
	Require(
		WorldExportFreshness.Decide(7, [6, 8]) == WorldExportState.VanillaNewer,
		"a newer vanilla copy should be preserved and identified");
	Require(
		WorldExportFreshness.Decide(7, [6, 8, 7]) == WorldExportState.UpToDate,
		"any exact copy should make the current tModLoader revision up to date");

	Guid worldId = Guid.NewGuid();
	string modernWorldPath = Path.Combine(testDirectory, "Modern.wld");
	WriteWorldHeader(modernWorldPath, version: 300, revision: 42, worldId);
	Require(
		WorldExportIdentityReader.TryRead(modernWorldPath, out WorldExportIdentity identity),
		"world identity should be readable from newer world format versions");
	Require(identity.WorldId == worldId, "world identity should preserve the UUID");
	Require(identity.Revision == 42, "world identity should preserve the save revision");

	string invalidWorldPath = Path.Combine(testDirectory, "Invalid.wld");
	File.WriteAllBytes(invalidWorldPath, [1, 2, 3]);
	Require(
		!WorldExportIdentityReader.TryRead(invalidWorldPath, out _),
		"an invalid world header should not produce an export identity");

	Console.WriteLine("World export and freshness tests passed.");
}
finally {
	if (Directory.Exists(testDirectory)) {
		Directory.Delete(testDirectory, recursive: true);
	}
}

static void WriteWorldHeader(string path, int version, uint revision, Guid worldId)
{
	const ulong metadataMagicNumber = 27981915666277746UL;
	const byte worldFileType = 2;

	using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
	using BinaryWriter writer = new(stream);
	writer.Write(version);
	writer.Write(metadataMagicNumber | ((ulong)worldFileType << 56));
	writer.Write(revision);
	writer.Write(0UL);
	writer.Write((short)1);
	long headerOffsetPosition = stream.Position;
	writer.Write(0);
	int headerOffset = checked((int)stream.Position);
	stream.Position = headerOffsetPosition;
	writer.Write(headerOffset);
	stream.Position = headerOffset;
	writer.Write("Test world");
	writer.Write("seed");
	writer.Write(0UL);
	writer.Write(worldId.ToByteArray());
}

static void Require(bool condition, string failure)
{
	if (!condition) {
		throw new InvalidOperationException(failure);
	}
}

static void RequireThrows<TException>(Action action, string failure)
	where TException : Exception
{
	try {
		action();
	}
	catch (TException) {
		return;
	}

	throw new InvalidOperationException(failure);
}
