using System.Reflection;

if (args.Length == 4 && args[0] == "--scan")
{
    try
    {
        ScanTextures(args[1], args[2], args[3]);
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"xnbdecode: {exception.Message}");
        return 1;
    }
}

if (args.Length != 3)
{
    Console.Error.WriteLine(
        "usage: xnbdecode <FNA.dll> <input.xnb> <output.bin>\n" +
        "       xnbdecode --scan <FNA.dll> <images-dir> <output.tsv>"
    );
    return 2;
}

try
{
    LzxDecoderAdapter decoder = new(args[0]);
    File.WriteAllBytes(args[2], DecodeBody(args[1], decoder));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"xnbdecode: {exception.Message}");
    return 1;
}

static void ScanTextures(string fnaPath, string imagesPath, string outputPath)
{
    string imagesRoot = Path.GetFullPath(imagesPath);
    LzxDecoderAdapter decoder = new(fnaPath);
    string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (outputDirectory is not null)
    {
        Directory.CreateDirectory(outputDirectory);
    }

    using StreamWriter output = new(outputPath);
    foreach (string path in Directory.EnumerateFiles(imagesRoot, "*.xnb", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
    {
        try
        {
            byte[] body = DecodeBody(path, decoder);
            (int width, int height) = ReadTextureSize(body, path);
            string name = Path.GetRelativePath(imagesRoot, path);
            name = Path.ChangeExtension(name, null).Replace(Path.DirectorySeparatorChar, '/');
            output.WriteLine($"{name}\t{width}\t{height}");
        }
        catch (Exception exception)
        {
            throw new InvalidDataException($"{path}: {exception.Message}", exception);
        }
    }
}

static byte[] DecodeBody(string inputPath, LzxDecoderAdapter decoder)
{
    using FileStream input = File.OpenRead(inputPath);
    using BinaryReader reader = new(input);

    byte[] magic = reader.ReadBytes(3);
    if (!magic.SequenceEqual("XNB"u8.ToArray()))
    {
        throw new InvalidDataException($"{inputPath} is not an XNB file");
    }

    _ = reader.ReadByte();
    byte version = reader.ReadByte();
    byte flags = reader.ReadByte();
    int fileSize = reader.ReadInt32();

    if (version is not (4 or 5))
    {
        throw new InvalidDataException($"{inputPath} uses unsupported XNB version {version}");
    }

    if ((flags & 0x80) == 0)
    {
        return reader.ReadBytes(fileSize - 10);
    }

    int decompressedSize = reader.ReadInt32();
    byte[] compressed = reader.ReadBytes(fileSize - 14);
    return decoder.Decompress(compressed, decompressedSize);
}

static (int Width, int Height) ReadTextureSize(byte[] body, string inputPath)
{
    using MemoryStream stream = new(body, writable: false);
    using BinaryReader reader = new(stream);
    int readerCount = Read7BitInteger(reader);
    bool hasTextureReader = false;
    for (int index = 0; index < readerCount; index++)
    {
        int length = Read7BitInteger(reader);
        string readerName = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
        hasTextureReader |= readerName.Contains("Texture2DReader", StringComparison.Ordinal);
        _ = reader.ReadInt32();
    }
    if (!hasTextureReader)
    {
        throw new InvalidDataException($"{inputPath} does not contain a Texture2D");
    }

    _ = Read7BitInteger(reader);
    if (Read7BitInteger(reader) == 0)
    {
        throw new InvalidDataException($"{inputPath} has no root texture");
    }
    int surfaceFormat = reader.ReadInt32();
    int width = reader.ReadInt32();
    int height = reader.ReadInt32();
    int mipCount = reader.ReadInt32();
    int levelSize = reader.ReadInt32();
    if (width <= 0 || height <= 0 || mipCount <= 0)
    {
        throw new InvalidDataException($"{inputPath} has invalid Texture2D dimensions");
    }
    if (surfaceFormat is not (0 or 20) || levelSize != checked(width * height * 4))
    {
        throw new InvalidDataException(
            $"{inputPath} uses unsupported surface format {surfaceFormat} or compressed pixel data"
        );
    }
    if (stream.Position + levelSize > stream.Length)
    {
        throw new InvalidDataException($"{inputPath} has truncated pixel data");
    }
    return (width, height);
}

static int Read7BitInteger(BinaryReader reader)
{
    int result = 0;
    int shift = 0;
    while (shift <= 28)
    {
        byte value = reader.ReadByte();
        result |= (value & 0x7F) << shift;
        if ((value & 0x80) == 0)
        {
            return result;
        }
        shift += 7;
    }
    throw new InvalidDataException("invalid 7-bit integer");
}

sealed class LzxDecoderAdapter
{
    private readonly Type decoderType;
    private readonly MethodInfo decompress;

    public LzxDecoderAdapter(string fnaPath)
    {
        Assembly fna = Assembly.LoadFrom(Path.GetFullPath(fnaPath));
        decoderType = fna.GetType(
            "Microsoft.Xna.Framework.Content.LzxDecoder",
            throwOnError: true
        )!;
        decompress = decoderType.GetMethod(
            "Decompress",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        ) ?? throw new MissingMethodException(decoderType.FullName, "Decompress");
    }

    public byte[] Decompress(byte[] compressed, int decompressedSize)
    {
        object decoder = Activator.CreateInstance(
            decoderType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [16],
            culture: null
        )!;
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
}
