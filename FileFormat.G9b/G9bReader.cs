using System;
using System.IO;

namespace FileFormat.G9b;

/// <summary>Reads V9990 GFX9000 (.g9b) files from bytes, streams, or file paths.</summary>
public static class G9bReader {

  /// <summary>Magic bytes "G9B".</summary>
  internal static readonly byte[] Magic = [0x47, 0x39, 0x42];

  /// <summary>Minimum header size: 3 (magic) + 2 (header size) + 1 (screen mode) + 1 (color mode) + 2 (width) + 2 (height) = 11.</summary>
  internal const int MinHeaderSize = 11;

  /// <summary>Default header size.</summary>
  internal const int DefaultHeaderSize = 11;

  public static G9bFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("G9B file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static G9bFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static G9bFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < MinHeaderSize)
      throw new InvalidDataException($"G9B file must be at least {MinHeaderSize} bytes, got {data.Length}.");

    if (data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2])
      throw new InvalidDataException("Invalid G9B magic bytes.");

    var headerSize = data[3] | (data[4] << 8);
    if (headerSize < MinHeaderSize)
      throw new InvalidDataException($"G9B header size must be at least {MinHeaderSize}, got {headerSize}.");
    if (data.Length < headerSize)
      throw new InvalidDataException($"G9B file too small for declared header size {headerSize}, got {data.Length} bytes.");

    var screenMode = data[5];
    var colorMode = data[6];
    var width = data[7] | (data[8] << 8);
    var height = data[9] | (data[10] << 8);

    if (width == 0)
      throw new InvalidDataException("Invalid G9B width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid G9B height: must be greater than zero.");

    var bytesPerPixel = screenMode switch {
      (byte)G9bScreenMode.Indexed8 => 1,
      (byte)G9bScreenMode.Rgb555 => 2,
      _ => throw new InvalidDataException($"Unsupported G9B screen mode: {screenMode}.")
    };

    var expectedPixelDataSize = width * height * bytesPerPixel;
    if (data.Length < headerSize + expectedPixelDataSize)
      throw new InvalidDataException($"G9B file too small: expected {headerSize + expectedPixelDataSize} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelDataSize];
    data.Slice(headerSize, expectedPixelDataSize).CopyTo(pixelData);

    return new G9bFile {
      Width = width,
      Height = height,
      ScreenMode = (G9bScreenMode)screenMode,
      ColorMode = colorMode,
      HeaderSize = headerSize,
      PixelData = pixelData,
    };
    }

  public static G9bFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MinHeaderSize)
      throw new InvalidDataException($"G9B file must be at least {MinHeaderSize} bytes, got {data.Length}.");

    if (data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2])
      throw new InvalidDataException("Invalid G9B magic bytes.");

    var headerSize = data[3] | (data[4] << 8);
    if (headerSize < MinHeaderSize)
      throw new InvalidDataException($"G9B header size must be at least {MinHeaderSize}, got {headerSize}.");
    if (data.Length < headerSize)
      throw new InvalidDataException($"G9B file too small for declared header size {headerSize}, got {data.Length} bytes.");

    var screenMode = data[5];
    var colorMode = data[6];
    var width = data[7] | (data[8] << 8);
    var height = data[9] | (data[10] << 8);

    if (width == 0)
      throw new InvalidDataException("Invalid G9B width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid G9B height: must be greater than zero.");

    var bytesPerPixel = screenMode switch {
      (byte)G9bScreenMode.Indexed8 => 1,
      (byte)G9bScreenMode.Rgb555 => 2,
      _ => throw new InvalidDataException($"Unsupported G9B screen mode: {screenMode}.")
    };

    var expectedPixelDataSize = width * height * bytesPerPixel;
    if (data.Length < headerSize + expectedPixelDataSize)
      throw new InvalidDataException($"G9B file too small: expected {headerSize + expectedPixelDataSize} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelDataSize];
    data.AsSpan(headerSize, expectedPixelDataSize).CopyTo(pixelData);

    return new G9bFile {
      Width = width,
      Height = height,
      ScreenMode = (G9bScreenMode)screenMode,
      ColorMode = colorMode,
      HeaderSize = headerSize,
      PixelData = pixelData,
    };
  }
}
