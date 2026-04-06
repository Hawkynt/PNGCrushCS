using System;
using System.IO;

namespace FileFormat.Atari8Bit;

/// <summary>Reads Atari 8-bit screen dumps from bytes, streams, or file paths.</summary>
public static class Atari8BitReader {

  public static Atari8BitFile FromFile(FileInfo file, Atari8BitMode? mode = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari 8-bit screen file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName), mode);
  }

  public static Atari8BitFile FromStream(Stream stream, Atari8BitMode? mode = null) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data, mode);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray(), mode);
  }

  public static Atari8BitFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Atari8BitFile FromBytes(byte[] data, Atari8BitMode? mode = null) {
    ArgumentNullException.ThrowIfNull(data);

    var resolvedMode = mode ?? _InferModeFromSize(data.Length);
    var expectedSize = Atari8BitFile.GetFileSize(resolvedMode);

    if (data.Length < expectedSize)
      throw new InvalidDataException($"Data too small for Atari 8-bit GR.{_ModeSuffix(resolvedMode)} screen dump. Expected {expectedSize} bytes, got {data.Length}.");

    if (data.Length != expectedSize)
      throw new InvalidDataException($"Invalid Atari 8-bit screen dump size. Expected exactly {expectedSize} bytes for GR.{_ModeSuffix(resolvedMode)}, got {data.Length}.");

    var width = Atari8BitFile.GetWidth(resolvedMode);
    var height = Atari8BitFile.GetHeight(resolvedMode);
    var bpp = Atari8BitFile.GetBitsPerPixel(resolvedMode);
    var bytesPerRow = Atari8BitFile.GetBytesPerRow(resolvedMode);
    var pixelScale = Atari8BitFile.GetPixelScale(resolvedMode);
    var pixelData = _UnpackPixels(data, width, height, bpp, bytesPerRow, pixelScale);
    var palette = Atari8BitFile.GetDefaultPalette(resolvedMode);

    return new() {
      Width = width,
      Height = height,
      Mode = resolvedMode,
      PixelData = pixelData,
      Palette = palette,
    };
  }

  private static Atari8BitMode _InferModeFromSize(int size) => size switch {
    Atari8BitFile.FileSize1920 => Atari8BitMode.Gr7,
    Atari8BitFile.FileSize7680 => Atari8BitMode.Gr8,
    _ => throw new InvalidDataException($"Unrecognized Atari 8-bit screen dump size: {size} bytes. Expected {Atari8BitFile.FileSize7680} or {Atari8BitFile.FileSize1920}.")
  };

  private static string _ModeSuffix(Atari8BitMode mode) => mode switch {
    Atari8BitMode.Gr7 => "7",
    Atari8BitMode.Gr8 => "8",
    Atari8BitMode.Gr9 => "9",
    Atari8BitMode.Gr15 => "15",
    _ => mode.ToString()
  };

  private static byte[] _UnpackPixels(byte[] data, int width, int height, int bpp, int bytesPerRow, int pixelScale) {
    var pixels = new byte[width * height];
    var pixelsPerByte = 8 / bpp;
    var mask = (1 << bpp) - 1;

    for (var y = 0; y < height; ++y)
      for (var byteCol = 0; byteCol < bytesPerRow; ++byteCol) {
        var b = data[y * bytesPerRow + byteCol];
        for (var p = 0; p < pixelsPerByte; ++p) {
          var shift = (pixelsPerByte - 1 - p) * bpp;
          var index = (b >> shift) & mask;
          var baseX = (byteCol * pixelsPerByte + p) * pixelScale;
          for (var s = 0; s < pixelScale; ++s) {
            var x = baseX + s;
            if (x < width)
              pixels[y * width + x] = (byte)index;
          }
        }
      }

    return pixels;
  }
}
