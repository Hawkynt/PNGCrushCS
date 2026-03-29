using System;
using System.IO;

namespace FileFormat.ImagicPaint;

/// <summary>Reads Imagic Paint files from bytes, streams, or file paths.</summary>
public static class ImagicPaintReader {

  public static ImagicPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Imagic Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ImagicPaintFile FromStream(Stream stream) {
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

  public static ImagicPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ImagicPaintHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Imagic Paint file.");

    if (data.Length < ImagicPaintFile.FileSize)
      throw new InvalidDataException($"Data too small for the expected {ImagicPaintFile.FileSize}-byte Imagic Paint file.");

    var span = data.AsSpan();
    var header = ImagicPaintHeader.ReadFrom(span);
    var palette = header.GetPaletteArray();

    var pixelData = new byte[32000];
    data.AsSpan(ImagicPaintHeader.StructSize, 32000).CopyTo(pixelData.AsSpan(0));

    return new ImagicPaintFile {
      Width = 320,
      Height = 200,
      Resolution = (ushort)header.Resolution,
      Palette = palette,
      PixelData = pixelData
    };
  }
}
