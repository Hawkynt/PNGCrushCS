using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GephardHires;

/// <summary>Reads Gephard Hires Graphics pictures from bytes, streams, or file paths.</summary>
public static class GephardHiresReader {

  public static GephardHiresFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Gephard Hires picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GephardHiresFile FromStream(Stream stream) {
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

  public static GephardHiresFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= GephardHiresFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a Gephard Hires picture: got {data.Length} bytes.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var height = data[2];

    if (width is < 1 or > GephardHiresFile.MaxWidth || height < 1)
      throw new InvalidDataException($"Invalid Gephard Hires size: {width}x{height}.");

    // The size stated has to account for the file: there is no magic, so this is what says the
    // header was read as the format means it rather than as some other format's first three bytes.
    var needed = GephardHiresFile.HeaderSize + MonochromePage.BytesPerRow(width) * height;
    if (data.Length != needed)
      throw new InvalidDataException($"A {width}x{height} Gephard Hires picture is {needed} bytes, got {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      PixelData = data[GephardHiresFile.HeaderSize..].ToArray(),
    };
  }

  public static GephardHiresFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
