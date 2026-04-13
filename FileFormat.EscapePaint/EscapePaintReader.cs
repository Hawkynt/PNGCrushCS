using System;
using System.IO;

namespace FileFormat.EscapePaint;

/// <summary>Reads Escape Paint files from bytes, streams, or file paths.</summary>
public static class EscapePaintReader {

  public static EscapePaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Escape Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EscapePaintFile FromStream(Stream stream) {
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

  public static EscapePaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < EscapePaintHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Escape Paint file.");

    if (data.Length < EscapePaintFile.FileSize)
      throw new InvalidDataException($"Data too small for the expected {EscapePaintFile.FileSize}-byte Escape Paint file.");

    var span = data;
    var header = EscapePaintHeader.ReadFrom(span);
    var palette = header.GetPaletteArray();

    var pixelData = new byte[32000];
    data.Slice(EscapePaintHeader.StructSize, 32000).CopyTo(pixelData.AsSpan(0));

    return new EscapePaintFile {
      Width = 320,
      Height = 200,
      Resolution = (ushort)header.Resolution,
      Palette = palette,
      PixelData = pixelData
    };
    }

  public static EscapePaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < EscapePaintHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Escape Paint file.");

    if (data.Length < EscapePaintFile.FileSize)
      throw new InvalidDataException($"Data too small for the expected {EscapePaintFile.FileSize}-byte Escape Paint file.");

    var span = data.AsSpan();
    var header = EscapePaintHeader.ReadFrom(span);
    var palette = header.GetPaletteArray();

    var pixelData = new byte[32000];
    data.AsSpan(EscapePaintHeader.StructSize, 32000).CopyTo(pixelData.AsSpan(0));

    return new EscapePaintFile {
      Width = 320,
      Height = 200,
      Resolution = (ushort)header.Resolution,
      Palette = palette,
      PixelData = pixelData
    };
  }
}
