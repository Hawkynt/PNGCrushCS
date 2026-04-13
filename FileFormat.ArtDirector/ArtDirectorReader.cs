using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.ArtDirector;

/// <summary>Reads Atari ST Art Director images from bytes, streams, or file paths.</summary>
public static class ArtDirectorReader {

  public static ArtDirectorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Art Director file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ArtDirectorFile FromStream(Stream stream) {
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

  public static ArtDirectorFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != ArtDirectorFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Art Director data size: expected exactly {ArtDirectorFile.ExpectedFileSize} bytes, got {data.Length}.");

    var span = data;
    var resolution = BinaryPrimitives.ReadInt16BigEndian(span);

    if (resolution is < 0 or > 2)
      throw new InvalidDataException($"Invalid Art Director resolution value: {resolution}.");

    var (width, height) = resolution switch {
      0 => (320, 200),
      1 => (640, 200),
      2 => (640, 400),
      _ => (320, 200)
    };

    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(span[(ArtDirectorFile.PaletteOffset + i * 2)..]);

    var pixelData = new byte[ArtDirectorFile.PlanarDataSize];
    data.Slice(ArtDirectorFile.HeaderSize, ArtDirectorFile.PlanarDataSize).CopyTo(pixelData.AsSpan(0));

    return new ArtDirectorFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = palette,
      PixelData = pixelData
    };
    }

  public static ArtDirectorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != ArtDirectorFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Art Director data size: expected exactly {ArtDirectorFile.ExpectedFileSize} bytes, got {data.Length}.");

    var span = data.AsSpan();
    var resolution = BinaryPrimitives.ReadInt16BigEndian(span);

    if (resolution is < 0 or > 2)
      throw new InvalidDataException($"Invalid Art Director resolution value: {resolution}.");

    var (width, height) = resolution switch {
      0 => (320, 200),
      1 => (640, 200),
      2 => (640, 400),
      _ => (320, 200)
    };

    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(span[(ArtDirectorFile.PaletteOffset + i * 2)..]);

    var pixelData = new byte[ArtDirectorFile.PlanarDataSize];
    data.AsSpan(ArtDirectorFile.HeaderSize, ArtDirectorFile.PlanarDataSize).CopyTo(pixelData.AsSpan(0));

    return new ArtDirectorFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = palette,
      PixelData = pixelData
    };
  }
}
