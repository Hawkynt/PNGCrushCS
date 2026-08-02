using System;
using System.IO;
using System.Text;

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

  public static SpookySpritesFalconFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < SpookySpritesFalconHeader.StructSize
        || Encoding.ASCII.GetString(data[..4]) != SpookySpritesFalconHeader.Signature)
      throw new InvalidDataException("Not a Spooky Sprites picture.");

    var header = SpookySpritesFalconHeader.ReadFrom(data);
    int width = header.Width, height = header.Height;
    if (width == 0 || height == 0)
      throw new InvalidDataException($"A Spooky Sprites picture states no size: {width}x{height}.");

    return new() {
      Width = width,
      Height = height,
      PixelData = SpookySpritesFalconRleCompressor.Decompress(
        data[SpookySpritesFalconHeader.StructSize..], width * height),
    };
  }

  public static SpookySpritesFalconFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
