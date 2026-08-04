using System;
using FileFormat.Core;

namespace FileFormat.EggPaint;

/// <summary>In-memory representation of an EggPaint / TruePaint picture (.trp) for the Atari.</summary>
/// <remarks>
/// This used to be a Commodore 64 multicolour reader: a load address, a bitmap, a video matrix, a
/// colour RAM and a background, insisting on 10003 bytes. No .trp is anything of the kind, and none
/// of the three in the corpus was read while RECOIL draws all three.
/// <para/>
/// A real one is four bytes of "TRUP", a width, a height, and then one sixteen-bit colour per pixel —
/// five bits of red, six of green and five of blue. Every sample is exactly its own stated size:
/// 128 by 128, 320 by 120 and 256 by 256, each width times height times two plus eight.
/// </remarks>
public readonly record struct EggPaintFile
  : IImageFormatReader<EggPaintFile>, IImageToRawImage<EggPaintFile>, IImageFromRawImage<EggPaintFile>, IImageFormatWriter<EggPaintFile> {

  static string IImageFormatMetadata<EggPaintFile>.PrimaryExtension => ".trp";
  static string[] IImageFormatMetadata<EggPaintFile>.FileExtensions => [".trp"];
  static EggPaintFile IImageFormatReader<EggPaintFile>.FromSpan(ReadOnlySpan<byte> data) => EggPaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<EggPaintFile>.ToBytes(EggPaintFile file) => EggPaintWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<EggPaintFile>.VideoModes => [
    new("TruePaint", [(IntegerRange.Any, IntegerRange.Any)], [65536])
  ];

  /// <summary>The four bytes a picture opens with.</summary>
  internal static ReadOnlySpan<byte> Magic => "TRUP"u8;

  /// <summary>Bytes ahead of the pixels: the magic and the two sizes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>One sixteen-bit colour per pixel, big-endian, as the file holds them.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// Widens each pixel to eight bits a channel.
  /// </summary>
  /// <remarks>
  /// The five- and six-bit fields are spread over the whole byte by repeating their own top bits,
  /// which is what RECOIL does: read that way the smallest sample matches it on every pixel, where
  /// scaling the fields instead agrees on barely a quarter of them.
  /// </remarks>
  /// <summary>
  /// Builds a truecolour picture, five bits of red and blue and six of green.
  /// </summary>
  /// <remarks>
  /// The word is big-endian, which is the machine's order and not the one the rest of this format's
  /// neighbours use. Green gets the extra bit because the eye has most of its resolution there,
  /// which is the same reason every other sixteen-bit direct-colour format of the period does it.
  /// </remarks>
  public static EggPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.ToRgb24();
    var pixels = new byte[image.Width * image.Height * 2];

    for (var i = 0; i < image.Width * image.Height; ++i) {
      var value = ((rgb[i * 3] >> 3) << 11) | ((rgb[i * 3 + 1] >> 2) << 5) | (rgb[i * 3 + 2] >> 3);
      pixels[i * 2] = (byte)(value >> 8);
      pixels[i * 2 + 1] = (byte)value;
    }

    return new() { Width = image.Width, Height = image.Height, PixelData = pixels };
  }

  public static RawImage ToRawImage(EggPaintFile file) {
    var width = file.Width;
    var height = file.Height;
    var source = file.PixelData ?? [];
    var rgb = new byte[width * height * 3];

    for (var i = 0; i < width * height; ++i) {
      var at = i * 2;
      var value = at + 1 < source.Length ? (source[at] << 8) | source[at + 1] : 0;

      var red = (value >> 11) & 0x1F;
      var green = (value >> 5) & 0x3F;
      var blue = value & 0x1F;

      var offset = i * 3;
      rgb[offset] = (byte)((red << 3) | (red >> 2));
      rgb[offset + 1] = (byte)((green << 2) | (green >> 4));
      rgb[offset + 2] = (byte)((blue << 3) | (blue >> 2));
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }
}
