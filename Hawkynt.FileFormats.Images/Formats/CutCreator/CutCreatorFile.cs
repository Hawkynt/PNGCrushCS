using System;
using FileFormat.Core;

namespace FileFormat.CutCreator;

/// <summary>In-memory representation of a Cut Creator picture (.cut).</summary>
/// <remarks>
/// The whole file is the bitmap: 96 pixels across and 99 down, one bit each, most significant bit
/// leftmost, and a set bit is white. There is no header, no palette and no size in it — 1188 bytes
/// and nothing else, which is why the length is what identifies one.
/// <para/>
/// <c>.cut</c> was claimed only by Dr. Halo, which is a different format under the same name and
/// refused these for having no usable dimensions. An extension names several formats often enough
/// here that the registry tries every one that claims it, so both can have it.
/// </remarks>
public readonly record struct CutCreatorFile
  : IImageFormatReader<CutCreatorFile>, IImageToRawImage<CutCreatorFile>,
    IImageFromRawImage<CutCreatorFile>, IImageFormatWriter<CutCreatorFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 96;

  /// <summary>Pixels down.</summary>
  public const int Height = 99;

  /// <summary>Colours a picture holds: a set bit and a clear one.</summary>
  public const int ColorCount = 2;

  /// <summary>What a whole picture weighs, which is the only thing that identifies one.</summary>
  public const int FileSize = (Width + 7) / 8 * Height;

  static string IImageFormatMetadata<CutCreatorFile>.PrimaryExtension => ".cut";
  static string[] IImageFormatMetadata<CutCreatorFile>.FileExtensions => [".cut"];
  static CutCreatorFile IImageFormatReader<CutCreatorFile>.FromSpan(ReadOnlySpan<byte> data)
    => CutCreatorReader.FromSpan(data);
  static byte[] IImageFormatWriter<CutCreatorFile>.ToBytes(CutCreatorFile file)
    => CutCreatorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CutCreatorFile>.VideoModes => [
    new("Default", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The bitmap, one bit a pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// Draws it in the two colours the machine has: black, and luminance 14 rather than white.
  /// </summary>
  /// <remarks>
  /// The bright one is 0xEE. A colour byte carries its luminance in the low nibble and the chip
  /// ignores that nibble's bottom bit, so 15 is not a level the hardware can show.
  /// </remarks>
  public static RawImage ToRawImage(CutCreatorFile file)
    => MonochromePage.Decode(file.PixelData ?? [], Width, Height, inkIsWhite: true, Atari8BitGraphics.MonochromePalette);

  public static CutCreatorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() { PixelData = MonochromePage.Encode(image.SampleTo(Width, Height), Width, Height, inkIsWhite: true) };
  }
}
