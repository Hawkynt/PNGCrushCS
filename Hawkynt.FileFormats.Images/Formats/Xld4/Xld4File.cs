using System;
using FileFormat.Core;

namespace FileFormat.Xld4;

/// <summary>In-memory representation of an XLD4 picture (.q4).</summary>
/// <remarks>
/// A PC-98 picture compressed twice over. The outer layer is a dictionary coder whose alphabet is
/// seventeen symbols rather than 256, because what it is compressing is not bytes but the
/// sixteen-colour indices of the picture plus one marker; the inner layer is a run-length coder
/// over those symbols, which is why its lengths are written as two base-seventeen digits.
/// <para/>
/// The picture is divided into chunks, each dictionary-coded on its own and each saying how many
/// pixels it covers — so a dictionary never has to grow beyond what one chunk needs. The complete
/// file length is only sixteen bits, therefore sufficiently high-entropy pictures cannot be
/// represented even though their dimensions and colour count are otherwise valid.
/// </remarks>
public readonly record struct Xld4File
  : IImageFormatReader<Xld4File>, IImageToRawImage<Xld4File>, IImageFromRawImage<Xld4File>, IImageFormatWriter<Xld4File> {

  /// <summary>Pixels across.</summary>
  public const int Width = 640;

  /// <summary>Rows.</summary>
  public const int Height = 400;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 16;

  static string IImageFormatMetadata<Xld4File>.PrimaryExtension => ".q4";
  static string[] IImageFormatMetadata<Xld4File>.FileExtensions => [".q4"];
  static Xld4File IImageFormatReader<Xld4File>.FromSpan(ReadOnlySpan<byte> data)
    => Xld4Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Xld4File>.ToBytes(Xld4File file) => Xld4Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Xld4File>.VideoModes => [
    new("NEC PC-98", [(Width, Height)], [ColorCount])
  ];

  /// <summary>One palette index per pixel.</summary>
  public byte[] Pixels { get; init; }

  /// <summary>Sixteen RGB triplets.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(Xld4File file) => new() {
    Width = Width,
    Height = Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.Pixels ?? new byte[Width * Height],
    Palette = file.Palette ?? new byte[ColorCount * 3],
    PaletteCount = ColorCount,
  };

  public static Xld4File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height != Height)
      throw new ArgumentException($"Expected {Width}x{Height} but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = image.EnsureIndexedAtMost(ColorCount);
    var palette = new byte[ColorCount * 3];
    if (indexed.Palette is { Length: > 0 } sourcePalette)
      sourcePalette.AsSpan(0, Math.Min(sourcePalette.Length, palette.Length)).CopyTo(palette);

    return new() {
      Pixels = indexed.PixelData[..],
      Palette = palette,
    };
  }
}
