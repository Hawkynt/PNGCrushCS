using System;
using System.IO;

namespace FileFormat.AtariDrg;

/// <summary>Reads Atari 8-bit DRG graphics screen dumps from bytes, streams, or file paths.</summary>
public static class AtariDrgReader {

  public static AtariDrgFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari DRG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariDrgFile FromStream(Stream stream) {
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

  public static AtariDrgFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariDrgFile.FileSize)
      throw new InvalidDataException($"Data too small for Atari DRG screen dump. Expected {AtariDrgFile.FileSize} bytes, got {data.Length}.");
    if (data.Length != AtariDrgFile.FileSize)
      throw new InvalidDataException($"Invalid Atari DRG screen dump size. Expected exactly {AtariDrgFile.FileSize} bytes, got {data.Length}.");

    var pixelData = _UnpackPixels(data);

    return new AtariDrgFile {
      PixelData = pixelData,
      Palette = AtariDrgFile.DefaultPalette[..],
    };
  }

  private static byte[] _UnpackPixels(byte[] data) {
    var pixels = new byte[AtariDrgFile.PixelWidth * AtariDrgFile.PixelHeight];

    for (var y = 0; y < AtariDrgFile.PixelHeight; ++y)
      for (var byteCol = 0; byteCol < AtariDrgFile.BytesPerRow; ++byteCol) {
        var b = data[y * AtariDrgFile.BytesPerRow + byteCol];
        var baseX = byteCol * 4;
        pixels[y * AtariDrgFile.PixelWidth + baseX] = (byte)((b >> 6) & 0x03);
        pixels[y * AtariDrgFile.PixelWidth + baseX + 1] = (byte)((b >> 4) & 0x03);
        pixels[y * AtariDrgFile.PixelWidth + baseX + 2] = (byte)((b >> 2) & 0x03);
        pixels[y * AtariDrgFile.PixelWidth + baseX + 3] = (byte)(b & 0x03);
      }

    return pixels;
  }
}
