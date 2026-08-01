using System;
using FileFormat.Core;
using System.IO;

namespace FileFormat.AtariGr7;

/// <summary>Reads Atari 8-bit Graphics Mode 7 screen dumps from bytes, streams, or file paths.</summary>
public static class AtariGr7Reader {

  public static AtariGr7File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari GR.7 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGr7File FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static AtariGr7File FromSpan(ReadOnlySpan<byte> data) {
    // A whole number of rows and then the four colour registers, which is why the length leaves a
    // remainder of four rather than none.
    if (data.Length != AtariGr7File.FileSize)
      throw new InvalidDataException(
        $"An Atari GR.7 screen is {AtariGr7File.FileSize} bytes — its rows and then four registers — got {data.Length}.");

    var pixelData = _UnpackPixels(data);

    return new AtariGr7File {
      PixelData = pixelData,
      // The registers the file states rather than a set assumed for it.
      Palette = _ReadRegisters(data),
    };
  }

  public static AtariGr7File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static byte[] _UnpackPixels(ReadOnlySpan<byte> data) {
    var pixels = new byte[AtariGr7File.PixelWidth * AtariGr7File.PixelHeight];

    for (var y = 0; y < AtariGr7File.PixelHeight; ++y)
      for (var byteCol = 0; byteCol < AtariGr7File.BytesPerRow; ++byteCol) {
        var b = data[y * AtariGr7File.BytesPerRow + byteCol];
        var baseX = byteCol * 4;
        pixels[y * AtariGr7File.PixelWidth + baseX] = (byte)((b >> 6) & 0x03);
        pixels[y * AtariGr7File.PixelWidth + baseX + 1] = (byte)((b >> 4) & 0x03);
        pixels[y * AtariGr7File.PixelWidth + baseX + 2] = (byte)((b >> 2) & 0x03);
        pixels[y * AtariGr7File.PixelWidth + baseX + 3] = (byte)(b & 0x03);
      }

    return pixels;
  }

  /// <summary>Turns the four trailing registers into the colours they name.</summary>
  private static byte[] _ReadRegisters(ReadOnlySpan<byte> data) {
    var gtia = Atari8BitGraphics.Palette;
    var palette = new byte[AtariGr7File.RegisterCount * 3];
    var at = AtariGr7File.BytesPerRow * AtariGr7File.PixelHeight;

    for (var i = 0; i < AtariGr7File.RegisterCount; ++i) {
      var entry = (data[at + i] & 254) * 3;
      gtia.Slice(entry, 3).CopyTo(palette.AsSpan(i * 3));
    }

    return palette;
  }
}
