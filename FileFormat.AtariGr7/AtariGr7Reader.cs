using System;
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
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static AtariGr7File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AtariGr7File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariGr7File.FileSize)
      throw new InvalidDataException($"Data too small for Atari GR.7 screen dump. Expected {AtariGr7File.FileSize} bytes, got {data.Length}.");
    if (data.Length != AtariGr7File.FileSize)
      throw new InvalidDataException($"Invalid Atari GR.7 screen dump size. Expected exactly {AtariGr7File.FileSize} bytes, got {data.Length}.");

    var pixelData = _UnpackPixels(data);

    return new AtariGr7File {
      PixelData = pixelData,
      Palette = AtariGr7File.DefaultPalette[..],
    };
  }

  private static byte[] _UnpackPixels(byte[] data) {
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
}
