using System;
using System.IO;

namespace FileFormat.AtariAgp;

/// <summary>Reads Atari 8-bit AGP images from bytes, streams, or file paths.</summary>
public static class AtariAgpReader {

  public static AtariAgpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari AGP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariAgpFile FromStream(Stream stream) {
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

  public static AtariAgpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariAgpFile.FileSizeGr7)
      throw new InvalidDataException($"Data too small for Atari AGP image. Minimum {AtariAgpFile.FileSizeGr7} bytes, got {data.Length}.");

    var mode = _InferMode(data.Length);
    var width = AtariAgpFile.GetWidth(mode);
    var height = AtariAgpFile.GetHeight(mode);

    byte foreground = 0;
    byte background = 0;
    if (mode == AtariAgpMode.Graphics8WithColors) {
      background = data[AtariAgpFile.FileSizeGr8];
      foreground = data[AtariAgpFile.FileSizeGr8 + 1];
    }

    var rawSize = mode == AtariAgpMode.Graphics7 ? AtariAgpFile.FileSizeGr7 : AtariAgpFile.FileSizeGr8;
    var pixelData = mode == AtariAgpMode.Graphics7
      ? _UnpackGr7(data, width, height)
      : _UnpackGr8(data, width, height);

    return new AtariAgpFile {
      Width = width,
      Height = height,
      Mode = mode,
      PixelData = pixelData,
      Palette = AtariAgpFile.GetDefaultPalette(mode),
      ForegroundColor = foreground,
      BackgroundColor = background,
    };
  }

  private static AtariAgpMode _InferMode(int size) => size switch {
    AtariAgpFile.FileSizeGr7 => AtariAgpMode.Graphics7,
    AtariAgpFile.FileSizeGr8 => AtariAgpMode.Graphics8,
    AtariAgpFile.FileSizeGr8WithColors => AtariAgpMode.Graphics8WithColors,
    _ => throw new InvalidDataException($"Unrecognized Atari AGP file size: {size} bytes. Expected {AtariAgpFile.FileSizeGr7}, {AtariAgpFile.FileSizeGr8}, or {AtariAgpFile.FileSizeGr8WithColors}.")
  };

  private static byte[] _UnpackGr8(byte[] data, int width, int height) {
    var bytesPerRow = width / 8;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
      for (var byteCol = 0; byteCol < bytesPerRow; ++byteCol) {
        var b = data[y * bytesPerRow + byteCol];
        var baseX = byteCol * 8;
        for (var bit = 0; bit < 8; ++bit) {
          var x = baseX + bit;
          if (x < width)
            pixels[y * width + x] = (byte)((b >> (7 - bit)) & 1);
        }
      }

    return pixels;
  }

  private static byte[] _UnpackGr7(byte[] data, int width, int height) {
    var bytesPerRow = 40;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
      for (var byteCol = 0; byteCol < bytesPerRow; ++byteCol) {
        var b = data[y * bytesPerRow + byteCol];
        var baseX = byteCol * 4;
        pixels[y * width + baseX] = (byte)((b >> 6) & 0x03);
        pixels[y * width + baseX + 1] = (byte)((b >> 4) & 0x03);
        pixels[y * width + baseX + 2] = (byte)((b >> 2) & 0x03);
        pixels[y * width + baseX + 3] = (byte)(b & 0x03);
      }

    return pixels;
  }
}
