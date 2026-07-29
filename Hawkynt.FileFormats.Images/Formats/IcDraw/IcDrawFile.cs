using System;
using FileFormat.Core;

namespace FileFormat.IcDraw;

/// <summary>In-memory representation of an Atari Falcon ICDRAW icon (.ibi, .ib3).</summary>
/// <remarks>
/// A 64-byte header carrying the tag and the 32x32 size, then a word-interleaved four-bitplane
/// image. Colours are not stored: ICDRAW draws its icons in the fixed sixteen-colour palette GEM
/// boots with, so an encoder has to map into that palette rather than choose one.
/// <para>
/// No byte-level specification of the header is published; the field offsets here are the ones
/// readers actually check, and the two file sizes divide exactly as one image plus a 1-bit mask
/// (.ibi) and as three images (.ib3), which is what the "single icon" and "icon group" names
/// suggest. Everything else in the header is carried through untouched.
/// </para>
/// </remarks>
public readonly record struct IcDrawFile
  : IImageFormatReader<IcDrawFile>, IImageToRawImage<IcDrawFile>,
    IImageFromRawImage<IcDrawFile>, IImageFormatWriter<IcDrawFile> {

  /// <summary>The tag a single-icon file starts with.</summary>
  public static ReadOnlySpan<byte> SingleIconSignature => "ICBI"u8;

  /// <summary>The tag an icon-group file starts with.</summary>
  public static ReadOnlySpan<byte> IconGroupSignature => "ICB3"u8;

  /// <summary>Size of the header.</summary>
  public const int HeaderSize = 64;

  /// <summary>Offset of the 16-bit big-endian width and height.</summary>
  public const int SizeOffset = 8;

  /// <summary>Icons are always this wide and tall.</summary>
  public const int IconSize = 32;

  /// <summary>Bitplanes an icon uses.</summary>
  public const int Bitplanes = 4;

  /// <summary>Colours an icon can draw with.</summary>
  public const int ColorCount = 1 << Bitplanes;

  /// <summary>Size of one image.</summary>
  public const int ImageDataSize = IconSize * IconSize * Bitplanes / 8;

  /// <summary>Size of the 1-bit mask a single-icon file appends.</summary>
  public const int MaskDataSize = IconSize * IconSize / 8;

  /// <summary>Images an icon-group file holds.</summary>
  public const int GroupImageCount = 3;

  /// <summary>Size of a single-icon file.</summary>
  public const int SingleIconFileSize = HeaderSize + ImageDataSize + MaskDataSize;

  /// <summary>Size of an icon-group file.</summary>
  public const int IconGroupFileSize = HeaderSize + ImageDataSize * GroupImageCount;

  static string IImageFormatMetadata<IcDrawFile>.PrimaryExtension => ".ibi";
  static string[] IImageFormatMetadata<IcDrawFile>.FileExtensions => [".ibi", ".ib3"];
  static IcDrawFile IImageFormatReader<IcDrawFile>.FromSpan(ReadOnlySpan<byte> data) => IcDrawReader.FromSpan(data);
  static byte[] IImageFormatWriter<IcDrawFile>.ToBytes(IcDrawFile file) => IcDrawWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<IcDrawFile>.VideoModes => [
    new("Icon", [(IconSize, IconSize)], [ColorCount])
  ];

  /// <summary>
  /// The fixed sixteen colours ICDRAW draws in, as RGB triplets. This is the GEM boot palette:
  /// eight bright colours, then the same hues at two thirds intensity, ending in black.
  /// </summary>
  public static ReadOnlySpan<byte> Palette => [
    255, 255, 255,
    255, 0, 0,
    0, 255, 0,
    255, 255, 0,
    0, 0, 255,
    255, 0, 255,
    0, 255, 255,
    170, 170, 170,
    85, 85, 85,
    170, 0, 0,
    0, 170, 0,
    170, 170, 0,
    0, 0, 170,
    170, 0, 170,
    0, 170, 170,
    0, 0, 0,
  ];

  /// <summary>Which of the two file kinds this is.</summary>
  public IcDrawVariant Variant { get; init; }

  /// <summary>The header, carried through as found.</summary>
  public byte[] Header { get; init; }

  /// <summary>The word-interleaved four-bitplane image.</summary>
  public byte[] ImageData { get; init; }

  /// <summary>
  /// The single-icon mask, one bit per pixel; empty for an icon group. A set bit is opaque.
  /// </summary>
  public byte[] Mask { get; init; }

  /// <summary>The second and third images of a group; empty for a single icon.</summary>
  public byte[] AdditionalImages { get; init; }

  public static RawImage ToRawImage(IcDrawFile file) {
    var chunky = PlanarConverter.AtariStToChunky(file.ImageData, IconSize, IconSize, Bitplanes);

    return new() {
      Width = IconSize,
      Height = IconSize,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = Palette.ToArray(),
      PaletteCount = ColorCount,
    };
  }

  public static IcDrawFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != IconSize || image.Height != IconSize)
      throw new ArgumentException($"An ICDRAW icon is {IconSize}x{IconSize}, got {image.Width}x{image.Height}.", nameof(image));

    // The palette is not ours to choose, so map straight into the one ICDRAW draws with.
    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var mapped = ColorQuantizer.MapToPalette(bgra.PixelData, IconSize * IconSize, Palette.ToArray());

    var chunky = new byte[IconSize * IconSize];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)mapped.Indices[i];

    // A pixel counts as part of the icon unless the source says it is see-through.
    var mask = new byte[MaskDataSize];
    for (var i = 0; i < chunky.Length; ++i)
      if (bgra.PixelData[i * 4 + 3] >= 128)
        mask[i >> 3] |= (byte)(0x80 >> (i & 7));

    var header = new byte[HeaderSize];
    SingleIconSignature.CopyTo(header);
    header[SizeOffset + 1] = IconSize;
    header[SizeOffset + 3] = IconSize;

    return new() {
      Variant = IcDrawVariant.SingleIcon,
      Header = header,
      ImageData = PlanarConverter.ChunkyToAtariSt(chunky, IconSize, IconSize, Bitplanes),
      Mask = mask,
      AdditionalImages = [],
    };
  }
}
