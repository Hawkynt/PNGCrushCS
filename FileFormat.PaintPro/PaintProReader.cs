using System;
using System.IO;

namespace FileFormat.PaintPro;

/// <summary>Reads Paint Pro files from bytes, streams, or file paths.</summary>
public static class PaintProReader {

  public static PaintProFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Paint Pro file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PaintProFile FromStream(Stream stream) {
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

  public static PaintProFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PaintProHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Paint Pro file.");

    if (data.Length < PaintProFile.FileSize)
      throw new InvalidDataException($"Data too small for the expected {PaintProFile.FileSize}-byte Paint Pro file.");

    var span = data;
    var header = PaintProHeader.ReadFrom(span);
    var palette = header.GetPaletteArray();

    var pixelData = new byte[32000];
    data.Slice(PaintProHeader.StructSize, 32000).CopyTo(pixelData.AsSpan(0));

    return new PaintProFile {
      Width = 320,
      Height = 200,
      Resolution = (ushort)header.Resolution,
      Palette = palette,
      PixelData = pixelData
    };
    }

  public static PaintProFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PaintProHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Paint Pro file.");

    if (data.Length < PaintProFile.FileSize)
      throw new InvalidDataException($"Data too small for the expected {PaintProFile.FileSize}-byte Paint Pro file.");

    var span = data.AsSpan();
    var header = PaintProHeader.ReadFrom(span);
    var palette = header.GetPaletteArray();

    var pixelData = new byte[32000];
    data.AsSpan(PaintProHeader.StructSize, 32000).CopyTo(pixelData.AsSpan(0));

    return new PaintProFile {
      Width = 320,
      Height = 200,
      Resolution = (ushort)header.Resolution,
      Palette = palette,
      PixelData = pixelData
    };
  }
}
