using System;
using System.IO;

namespace FileFormat.FontasyGrafik;

/// <summary>Reads Atari ST Fontasy Grafik images from bytes, streams, or file paths.</summary>
public static class FontasyGrafikReader {

  public static FontasyGrafikFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Fontasy Grafik file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FontasyGrafikFile FromStream(Stream stream) {
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

  public static FontasyGrafikFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < FontasyGrafikFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Fontasy Grafik file: expected at least {FontasyGrafikFile.ExpectedFileSize} bytes, got {data.Length}.");

    var header = FontasyGrafikHeader.ReadFrom(data);
    var palette = header.Palette;

    var pixelData = new byte[FontasyGrafikFile.PlanarDataSize];
    data.Slice(FontasyGrafikFile.PaletteSize + FontasyGrafikFile.PaddingSize, FontasyGrafikFile.PlanarDataSize).CopyTo(pixelData.AsSpan(0));

    return new FontasyGrafikFile {
      Palette = palette,
      PixelData = pixelData
    };
    }

  public static FontasyGrafikFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FontasyGrafikFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Fontasy Grafik file: expected at least {FontasyGrafikFile.ExpectedFileSize} bytes, got {data.Length}.");

    var header = FontasyGrafikHeader.ReadFrom(data);
    var palette = header.Palette;

    var pixelData = new byte[FontasyGrafikFile.PlanarDataSize];
    data.AsSpan(FontasyGrafikFile.PaletteSize + FontasyGrafikFile.PaddingSize, FontasyGrafikFile.PlanarDataSize).CopyTo(pixelData.AsSpan(0));

    return new FontasyGrafikFile {
      Palette = palette,
      PixelData = pixelData
    };
  }
}
