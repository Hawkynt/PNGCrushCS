using System;
using System.IO;

namespace FileFormat.Canvas;

/// <summary>Reads Canvas ST files from bytes, streams, or file paths.</summary>
public static class CanvasReader {

  public static CanvasFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Canvas ST file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CanvasFile FromStream(Stream stream) {
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

  public static CanvasFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < CanvasHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Canvas ST file.");

    if (data.Length < CanvasFile.FileSize)
      throw new InvalidDataException($"Data too small for the expected {CanvasFile.FileSize}-byte Canvas ST file.");

    var span = data;
    var header = CanvasHeader.ReadFrom(span);
    var palette = header.GetPaletteArray();

    var pixelData = new byte[32000];
    data.Slice(CanvasHeader.StructSize, 32000).CopyTo(pixelData.AsSpan(0));

    return new CanvasFile {
      Width = 320,
      Height = 200,
      Resolution = (ushort)header.Resolution,
      Palette = palette,
      PixelData = pixelData
    };
    }

  public static CanvasFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CanvasHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Canvas ST file.");

    if (data.Length < CanvasFile.FileSize)
      throw new InvalidDataException($"Data too small for the expected {CanvasFile.FileSize}-byte Canvas ST file.");

    var span = data.AsSpan();
    var header = CanvasHeader.ReadFrom(span);
    var palette = header.GetPaletteArray();

    var pixelData = new byte[32000];
    data.AsSpan(CanvasHeader.StructSize, 32000).CopyTo(pixelData.AsSpan(0));

    return new CanvasFile {
      Width = 320,
      Height = 200,
      Resolution = (ushort)header.Resolution,
      Palette = palette,
      PixelData = pixelData
    };
  }
}
