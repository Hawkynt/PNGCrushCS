using System;
using FileFormat.Core;

namespace FileFormat.Cineon;

/// <summary>In-memory representation of a Cineon image.</summary>
[FormatMagicBytes([0x80, 0x2A, 0x5F, 0xD7])]
public sealed class CineonFile :
  IImageFormatReader<CineonFile>, IImageToRawImage<CineonFile>,
  IImageFromRawImage<CineonFile>, IImageFormatWriter<CineonFile> {

  static string IImageFormatMetadata<CineonFile>.PrimaryExtension => ".cin";
  static string[] IImageFormatMetadata<CineonFile>.FileExtensions => [".cin"];
  static CineonFile IImageFormatReader<CineonFile>.FromSpan(ReadOnlySpan<byte> data) => CineonReader.FromSpan(data);
  static byte[] IImageFormatWriter<CineonFile>.ToBytes(CineonFile file) => CineonWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerSample { get; init; }
  public byte Orientation { get; init; }
  public int ImageDataOffset { get; init; }
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts a Cineon image to a 16-bit <see cref="RawImage"/> by scaling 10-bit values to Rgb48.</summary>
  public static RawImage ToRawImage(CineonFile file) {
    // 10 bits a sample packed three to a 32-bit word is the classic Cineon and what the code below
    // reads, but the format allows 8, 12 and 16 as well, and 8 is what a modern writer produces for
    // an ordinary 8-bit source. Those were refused outright; they are simply unpacked samples.
    if (file.BitsPerSample is 8 or 16)
      return _FromWholeSamples(file);

    if (file.BitsPerSample != 10)
      throw new NotSupportedException(
        $"Cineon bit depth {file.BitsPerSample} is not supported; 8, 10 and 16 are.");

    var width = file.Width;
    var height = file.Height;
    var src = file.PixelData;
    var pixelCount = width * height;
    var result = new byte[pixelCount * 6];

    for (var i = 0; i < pixelCount; ++i) {
      var offset = i * 4;
      var word = (uint)(src[offset] << 24 | src[offset + 1] << 16 | src[offset + 2] << 8 | src[offset + 3]);
      var r = (ushort)(((word >> 22) & 0x3FF) * 65535 / 1023);
      var g = (ushort)(((word >> 12) & 0x3FF) * 65535 / 1023);
      var b = (ushort)(((word >> 2) & 0x3FF) * 65535 / 1023);
      var di = i * 6;
      result[di] = (byte)(r >> 8);
      result[di + 1] = (byte)r;
      result[di + 2] = (byte)(g >> 8);
      result[di + 3] = (byte)g;
      result[di + 4] = (byte)(b >> 8);
      result[di + 5] = (byte)b;
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb48,
      PixelData = result,
    };
  }

  /// <summary>Creates a 10-bit linear Cineon image from a <see cref="RawImage"/>. Accepts Rgb48 natively or any convertible format.</summary>
  public static CineonFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb48 = PixelConverter.Convert(image, PixelFormat.Rgb48);
    var width = rgb48.Width;
    var height = rgb48.Height;
    var src = rgb48.PixelData;
    var pixelCount = width * height;
    var packed = new byte[pixelCount * 4];

    for (var i = 0; i < pixelCount; ++i) {
      var si = i * 6;
      // Read BE uint16 channels, scale 16-bit to 10-bit
      var r = (uint)(((src[si] << 8) | src[si + 1]) * 1023 / 65535);
      var g = (uint)(((src[si + 2] << 8) | src[si + 3]) * 1023 / 65535);
      var b = (uint)(((src[si + 4] << 8) | src[si + 5]) * 1023 / 65535);
      // Pack into big-endian 32-bit word: R[31:22] G[21:12] B[11:2] padding[1:0]
      var word = (r << 22) | (g << 12) | (b << 2);
      var di = i * 4;
      packed[di] = (byte)(word >> 24);
      packed[di + 1] = (byte)(word >> 16);
      packed[di + 2] = (byte)(word >> 8);
      packed[di + 3] = (byte)word;
    }

    return new() {
      Width = width,
      Height = height,
      BitsPerSample = 10,
      Orientation = 0,
      ImageDataOffset = 0,
      PixelData = packed,
    };
  }

  /// <summary>
  /// The depths whose samples sit on byte boundaries — 8 and 16 bits — read straight out as RGB.
  /// </summary>
  /// <remarks>
  /// Cineon is big-endian, so a 16-bit sample is already in the order Rgb48 wants; an 8-bit one is
  /// scaled up to it so every depth leaves this type in the same format.
  /// </remarks>
  private static RawImage _FromWholeSamples(CineonFile file) {
    var pixelCount = file.Width * file.Height;
    var source = file.PixelData;
    var result = new byte[pixelCount * 6];
    var bytesPerSample = file.BitsPerSample / 8;

    for (var i = 0; i < pixelCount; ++i) {
      for (var channel = 0; channel < 3; ++channel) {
        var at = ((i * 3) + channel) * bytesPerSample;
        ushort value;
        if (bytesPerSample == 1)
          value = (ushort)(at < source.Length ? source[at] * 257 : 0); // 0..255 over the full range
        else
          value = at + 1 < source.Length ? (ushort)((source[at] << 8) | source[at + 1]) : (ushort)0;

        var target = (i * 6) + (channel * 2);
        result[target] = (byte)(value >> 8);
        result[target + 1] = (byte)value;
      }
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb48,
      PixelData = result,
    };
  }
}
