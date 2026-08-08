using System;
using FileFormat.Core;

namespace FileFormat.ZsStaffKid98;

/// <summary>In-memory representation of a Z's Staff Kid98 picture (.zim).</summary>
/// <remarks>
/// A PC-98 format that stores a picture as a list of horizontal runs rather than a screen: each run
/// says where it starts and how long it is, and anything no run covers keeps the first palette
/// entry. A drawing with a lot of background costs almost nothing, which is what the program was
/// for.
/// <para/>
/// A run's four bitplanes are packed by a scheme of nested flags — a byte of bits saying which of
/// eight bytes follow, each of those saying which of eight more follow, and so on for three levels
/// — so a plane that is mostly empty costs a bit per eight bytes instead of a byte. The bytes that
/// do arrive are then differenced twice, once against the byte before and once against the byte
/// two back, which is what makes a dithered plane compress at all.
/// </remarks>
public readonly record struct ZsStaffKid98File
  : IImageFormatReader<ZsStaffKid98File>, IImageToRawImage<ZsStaffKid98File>,
    IImageFromRawImage<ZsStaffKid98File>, IImageFormatWriter<ZsStaffKid98File> {

  /// <summary>What every file begins with.</summary>
  public const string Signature = "FORMAT-A";

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>The largest picture the header can state, its dimensions being stored one less.</summary>
  public const int MaxExtent = 65536;

  static string IImageFormatMetadata<ZsStaffKid98File>.PrimaryExtension => ".zim";
  static string[] IImageFormatMetadata<ZsStaffKid98File>.FileExtensions => [".zim"];
  static ZsStaffKid98File IImageFormatReader<ZsStaffKid98File>.FromSpan(ReadOnlySpan<byte> data)
    => ZsStaffKid98Reader.FromSpan(data);
  static byte[] IImageFormatWriter<ZsStaffKid98File>.ToBytes(ZsStaffKid98File file)
    => ZsStaffKid98Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZsStaffKid98File>.VideoModes => [
    new("NEC PC-98", [(IntegerRange.Any, IntegerRange.Any)], [16])
  ];

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>One palette index per pixel.</summary>
  public byte[] Pixels { get; init; }

  /// <summary>Sixteen RGB triplets.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(ZsStaffKid98File file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.Pixels ?? [],
    Palette = file.Palette ?? new byte[48],
    PaletteCount = 16,
  };

  /// <summary>Reduces a picture to the sixteen colours the file stores, at whatever size it is.</summary>
  /// <remarks>
  /// The picture keeps its own size: this format states its dimensions rather than assuming a screen,
  /// so there is nothing to sample to. The palette is the picture's own — the file carries one, and
  /// carrying it is what lets sixteen colours be sixteen good ones.
  /// </remarks>
  public static ZsStaffKid98File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Width > MaxExtent || image.Height > MaxExtent)
      throw new ArgumentException(
        $"A Z's Staff header states {MaxExtent}x{MaxExtent} at most, not {image.Width}x{image.Height}.",
        nameof(image));

    var indexed = image.EnsureIndexedAtMost(ColorCount);
    var palette = new byte[ColorCount * 3];
    (indexed.Palette ?? []).AsSpan(0, Math.Min(palette.Length, (indexed.Palette ?? []).Length)).CopyTo(palette);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Pixels = indexed.PixelData,
      Palette = palette,
    };
  }
}
