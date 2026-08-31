using System.Reflection;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: xnbdecode <FNA.dll> <input.xnb> <output.bin>");
    return 2;
}

try
{
    Decode(args[0], args[1], args[2]);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"xnbdecode: {exception.Message}");
    return 1;
}

static void Decode(string fnaPath, string inputPath, string outputPath)
{
    using FileStream input = File.OpenRead(inputPath);
    using BinaryReader reader = new(input);

    byte[] magic = reader.ReadBytes(3);
    if (!magic.SequenceEqual("XNB"u8.ToArray()))
    {
        throw new InvalidDataException($"{inputPath} is not an XNB file");
    }

    _ = reader.ReadByte(); // Target platform.
    byte version = reader.ReadByte();
    byte flags = reader.ReadByte();
    int fileSize = reader.ReadInt32();

    if (version is not (4 or 5))
    {
        throw new InvalidDataException($"unsupported XNB version {version}");
    }

    byte[] body;
    if ((flags & 0x80) == 0)
    {
        body = reader.ReadBytes(fileSize - 10);
    }
    else
    {
        int decompressedSize = reader.ReadInt32();
        byte[] compressed = reader.ReadBytes(fileSize - 14);
        body = DecompressLzx(fnaPath, compressed, decompressedSize);
    }

    File.WriteAllBytes(outputPath, body);
}

static byte[] DecompressLzx(string fnaPath, byte[] compressed, int decompressedSize)
{
    Assembly fna = Assembly.LoadFrom(Path.GetFullPath(fnaPath));
    Type decoderType = fna.GetType("Microsoft.Xna.Framework.Content.LzxDecoder", throwOnError: true)!;
    object decoder = Activator.CreateInstance(
        decoderType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args: [16],
        culture: null
    )!;
    MethodInfo decompress = decoderType.GetMethod(
        "Decompress",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    ) ?? throw new MissingMethodException(decoderType.FullName, "Decompress");

    using MemoryStream source = new(compressed, writable: false);
    using MemoryStream destination = new(new byte[decompressedSize], writable: true);
    long compressedPosition = 0;

    while (compressedPosition < compressed.Length)
    {
        int first = source.ReadByte();
        int second = source.ReadByte();
        if (first < 0 || second < 0)
        {
            throw new InvalidDataException("truncated XNB compression block header");
        }

        int blockSize;
        int frameSize = 32_768;
        if (first == 0xFF)
        {
            int frameHigh = second;
            int frameLow = source.ReadByte();
            int blockHigh = source.ReadByte();
            int blockLow = source.ReadByte();
            if (frameLow < 0 || blockHigh < 0 || blockLow < 0)
            {
                throw new InvalidDataException("truncated XNB extended compression block header");
            }

            frameSize = (frameHigh << 8) | frameLow;
            blockSize = (blockHigh << 8) | blockLow;
            compressedPosition += 5;
        }
        else
        {
            blockSize = (first << 8) | second;
            compressedPosition += 2;
        }

        if (blockSize == 0 || frameSize == 0)
        {
            break;
        }

        _ = decompress.Invoke(decoder, [source, blockSize, destination, frameSize]);
        compressedPosition += blockSize;
        source.Seek(compressedPosition, SeekOrigin.Begin);
    }

    if (destination.Position != decompressedSize)
    {
        throw new InvalidDataException(
            $"XNB decompressed to {destination.Position} bytes, expected {decompressedSize}"
        );
    }

    return destination.ToArray();
}
