using System;
using System.IO;

namespace FileFormat.AladdinPaint;

/// <summary>Reads Aladdin Paint files from bytes, streams, or file paths.</summary>
public static class AladdinPaintReader {

  public static AladdinPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Aladdin Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AladdinPaintFile FromStream(Stream stream) {
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

  public static AladdinPaintFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AladdinPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AladdinPaintHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Aladdin Paint file.");

    if (data.Length < AladdinPaintFile.FileSize)
      throw new InvalidDataException($"Data too small for the expected {AladdinPaintFile.FileSize}-byte Aladdin Paint file.");

    var span = data.AsSpan();
    var header = AladdinPaintHeader.ReadFrom(span);
    var palette = header.GetPaletteArray();

    var pixelData = new byte[32000];
    data.AsSpan(AladdinPaintHeader.StructSize, 32000).CopyTo(pixelData.AsSpan(0));

    return new AladdinPaintFile {
      Width = 320,
      Height = 200,
      Resolution = (ushort)header.Resolution,
      Palette = palette,
      PixelData = pixelData
    };
  }
}
