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
  : IImageFormatReader<ZsStaffKid98File>, IImageToRawImage<ZsStaffKid98File> {

  /// <summary>What every file begins with.</summary>
  public const string Signature = "FORMAT-A";

  static string IImageFormatMetadata<ZsStaffKid98File>.PrimaryExtension => ".zim";
  static string[] IImageFormatMetadata<ZsStaffKid98File>.FileExtensions => [".zim"];
  static ZsStaffKid98File IImageFormatReader<ZsStaffKid98File>.FromSpan(ReadOnlySpan<byte> data)
    => ZsStaffKid98Reader.FromSpan(data);
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
}
