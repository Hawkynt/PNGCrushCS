using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.SpeccyExtended;

/// <summary>In-memory representation of a Speccy eXtended Graphics (SXG) picture.</summary>
/// <remarks>
/// This was read as a ZX Spectrum screen with an extra attribute plane — 7684 bytes, 256 by 192, the
/// machine's fifteen colours. It is none of those. An SXG is a ZX Evolution picture: it states its
/// own size, carries its own sixteen colours and holds four bits a pixel, and the samples are 38926
/// and 25102 bytes at 320 by 240 and 256 by 192. All were refused, and the reason given was that the
/// magic was "SX" — the signature is at offset one, after a leading 0x7F, and the check read from
/// nought.
/// <para/>
/// The palette was the last thing to give: the sixteen colours appear nowhere in the file as bytes
/// or as nibbles in any channel order, which is what a search for them assumed. They are five bits a
/// channel in a sixteen-bit word, and five bits do not fall on a nibble.
/// </remarks>
[FormatDetectionPriority(100)]
[FormatMagicBytes([0x7F, 0x53, 0x58, 0x47])]
public sealed class SpeccyExtendedFile : IImageFormatReader<SpeccyExtendedFile>, IImageToRawImage<SpeccyExtendedFile>, IImageFromRawImage<SpeccyExtendedFile>, IImageFormatWriter<SpeccyExtendedFile> {

  static string IImageFormatMetadata<SpeccyExtendedFile>.PrimaryExtension => ".sxg";
  static string[] IImageFormatMetadata<SpeccyExtendedFile>.FileExtensions => [".sxg"];
  static SpeccyExtendedFile IImageFormatReader<SpeccyExtendedFile>.FromSpan(ReadOnlySpan<byte> data) => SpeccyExtendedReader.FromSpan(data);
  static byte[] IImageFormatWriter<SpeccyExtendedFile>.ToBytes(SpeccyExtendedFile file) => SpeccyExtendedWriter.ToBytes(file);

  /// <summary>The signature, which begins with a byte before "SXG" rather than with it.</summary>
  static bool? IImageFormatMetadata<SpeccyExtendedFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[0] == 0x7F && header[1] == 0x53 && header[2] == 0x58 && header[3] == 0x47;

  /// <summary>Where the width sits, as a sixteen-bit little-endian value; the height follows it.</summary>
  internal const int WidthOffset = 8;

  /// <summary>Where the sixteen colours start, two bytes apiece.</summary>
  internal const int PaletteOffset = 16;

  /// <summary>How many colours a picture has.</summary>
  internal const int PaletteCount = 16;

  /// <summary>Where the picture starts. What lies between the palette and here is not established.</summary>
  internal const int PixelOffset = 526;

  /// <summary>
  /// What a five-bit channel is worth out of 255.
  /// </summary>
  /// <remarks>
  /// Not 31. The reference tool draws a channel of 8 as 85 and one of 16 as 170, which is a scale of
  /// 24 rather than of the 31 five bits could hold, and anything above 24 comes out white. Found by
  /// setting one entry to a single bit at a time and reading back what was drawn.
  /// </remarks>
  internal const int ChannelFullScale = 24;

  /// <summary>Pixels across, as the file states.</summary>
  public int Width { get; init; }

  /// <summary>Rows, as the file states.</summary>
  public int Height { get; init; }

  /// <summary>Sixteen colours, three bytes apiece.</summary>
  public byte[] Palette { get; init; } = [];

  /// <summary>One index per pixel.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Turns one of the file's sixteen-bit colours into red, green and blue.</summary>
  /// <remarks>Five bits a channel, red highest, and the word is little-endian.</remarks>
  internal static (byte Red, byte Green, byte Blue) DecodeColor(ushort value) => (
    _Scale((value >> 10) & 0x1F),
    _Scale((value >> 5) & 0x1F),
    _Scale(value & 0x1F));

  private static byte _Scale(int channel)
    => (byte)Math.Min(255, channel * 255 / ChannelFullScale);

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(SpeccyExtendedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData,
      Palette = file.Palette,
      PaletteCount = PaletteCount,
    };
  }

  /// <summary>Builds a picture from any image, keeping its size and reducing it to sixteen colours.</summary>
  /// <remarks>
  /// The size is the file's to state, so nothing has to be sampled away — only the colours are
  /// fixed at sixteen, and the picture carries its own, so they are quantised rather than matched
  /// against a table. Five bits a channel are kept, which is what the writer narrows them to.
  /// </remarks>
  public static SpeccyExtendedFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Min(image.Width, ushort.MaxValue);
    var height = Math.Min(image.Height, ushort.MaxValue);
    var source = image.SampleTo(width, height).EnsureFormat(PixelFormat.Bgra32);
    var quantized = ColorQuantizer.Quantize(source.PixelData, width * height, PaletteCount);

    var palette = new byte[PaletteCount * 3];
    for (var i = 0; i < quantized.Count * 3; ++i)
      palette[i] = quantized.Palette[i];

    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(quantized.Indices[i] & 0x0F);

    return new() { Width = width, Height = height, Palette = palette, PixelData = pixels };
  }
}
