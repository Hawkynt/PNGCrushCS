using System;
using System.IO;

namespace FileFormat.ArtStudio8;

/// <summary>Reads Art Studio (Atari 8-bit) images from bytes, streams, or file paths.</summary>
public static class ArtStudio8Reader {

  public static ArtStudio8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Art Studio file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ArtStudio8File FromStream(Stream stream) {
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

  public static ArtStudio8File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != ArtStudio8File.FileSize)
      throw new InvalidDataException($"Invalid Art Studio data size: expected exactly {ArtStudio8File.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[ArtStudio8File.FileSize];
    data.Slice(0, ArtStudio8File.FileSize).CopyTo(pixelData);

    return new ArtStudio8File { PixelData = pixelData };
    }

  public static ArtStudio8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != ArtStudio8File.FileSize)
      throw new InvalidDataException($"Invalid Art Studio data size: expected exactly {ArtStudio8File.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[ArtStudio8File.FileSize];
    data.AsSpan(0, ArtStudio8File.FileSize).CopyTo(pixelData);

    return new ArtStudio8File { PixelData = pixelData };
  }
}
