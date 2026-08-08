using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.CelGrey;

/// <summary>Reads four-bit greyscale .cel pictures from bytes, streams, or file paths.</summary>
public static class CelGreyReader {

  public static CelGreyFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CelGreyFile FromStream(Stream stream) {
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

  public static CelGreyFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= CelGreyFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a .cel picture: got {data.Length} bytes.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);

    if (width < 1 || height < 1)
      throw new InvalidDataException($"Invalid .cel size: {width}x{height}.");

    // There is no magic and three other formats claim this name, so the size stated has to account
    // for the file exactly or this is one of theirs.
    var needed = CelGreyFile.HeaderSize + CelGreyFile.BytesPerRow(width) * height;
    if (data.Length != needed)
      throw new InvalidDataException($"A {width}x{height} four-bit .cel is {needed} bytes, got {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      PixelData = data[CelGreyFile.HeaderSize..].ToArray(),
    };
  }

  public static CelGreyFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
