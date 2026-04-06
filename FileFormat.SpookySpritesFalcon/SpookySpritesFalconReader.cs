using System;
using System.IO;

namespace FileFormat.SpookySpritesFalcon;

/// <summary>Reads Spooky Sprites Atari Falcon compressed 16-bit true color files from bytes, streams, or file paths.</summary>
public static class SpookySpritesFalconReader {

  public static SpookySpritesFalconFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Spooky Sprites Falcon file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SpookySpritesFalconFile FromStream(Stream stream) {
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

  public static SpookySpritesFalconFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SpookySpritesFalconFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SpookySpritesFalconHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Spooky Sprites Falcon file.");

    var header = SpookySpritesFalconHeader.ReadFrom(data.AsSpan());
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width == 0 || height == 0)
      throw new InvalidDataException("Spooky Sprites Falcon image dimensions must be non-zero.");

    var compressedData = data.AsSpan(SpookySpritesFalconHeader.StructSize);
    var pixelCount = width * height;
    var pixelData = SpookySpritesFalconRleCompressor.Decompress(compressedData, pixelCount);

    return new SpookySpritesFalconFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
