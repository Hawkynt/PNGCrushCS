using System;
using FileFormat.Core;

namespace FileFormat.PictureGearPocket;

/// <summary>In-memory representation of a Sony PictureGear Pocket picture (.prc).</summary>
/// <remarks>
/// The picture format of Sony's CLIÉ handhelds: a Palm resource database, type and creator both
/// <c>IMVS</c>, holding a 44x44 thumbnail in the Palm system palette and the picture itself, cut
/// into square tiles that are each one resource and usually run-length coded. The picture is either
/// eight bits against its own palette — what PictureGear Lite writes, up to 224 colours — or sixteen
/// bits of RGB 5:6:5, which the 2003 release of Sony's library added. Formats/PictureGearPocket/SPEC.md
/// in the repository describes the layout.
/// </remarks>
public readonly record struct PictureGearPocketFile
  : IImageFormatReader<PictureGearPocketFile>, IImageToRawImage<PictureGearPocketFile>,
    IImageFromRawImage<PictureGearPocketFile>, IImageFormatWriter<PictureGearPocketFile> {

  /// <summary>The widest and tallest picture written; Sony's own code does its row arithmetic in sixteen bits.</summary>
  public const int MaximumExtent = 4096;

  /// <summary>The most colours Sony's writer ever puts in a palette, which is what <see cref="FromRawImage(RawImage)"/> reduces to.</summary>
  public const int MaximumColors = 224;

  /// <summary>The thumbnail's width and height.</summary>
  public const int ThumbnailSize = 44;

  static string IImageFormatMetadata<PictureGearPocketFile>.PrimaryExtension => ".prc";
  static string[] IImageFormatMetadata<PictureGearPocketFile>.FileExtensions => [".prc"];
  static PictureGearPocketFile IImageFormatReader<PictureGearPocketFile>.FromSpan(ReadOnlySpan<byte> data) => PictureGearPocketReader.FromSpan(data);
  static byte[] IImageFormatWriter<PictureGearPocketFile>.ToBytes(PictureGearPocketFile file) => PictureGearPocketWriter.ToBytes(file);
  static bool? IImageFormatMetadata<PictureGearPocketFile>.MatchesSignature(ReadOnlySpan<byte> header) => PictureGearPocketReader.HasSignature(header);
  static VideoMode[] IImageFormatMetadata<PictureGearPocketFile>.VideoModes => [
    new("Indexed", [(new IntegerRange(1, MaximumExtent), new IntegerRange(1, MaximumExtent))], [new IntegerRange(1, MaximumColors)]),
    new("High colour", [(new IntegerRange(1, MaximumExtent), new IntegerRange(1, MaximumExtent))]),
  ];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Eight (palette indices) or sixteen (big-endian RGB 5:6:5).</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>The side of the square tiles the picture is stored in: 32, or 64 once both sides reach 320.</summary>
  public int TileSize { get; init; }

  /// <summary>Whether the tiles are run-length coded, which is how Sony's software writes them unless told not to.</summary>
  public bool IsCompressed { get; init; }

  /// <summary>The database name the handheld lists the picture under, at most 31 characters.</summary>
  public string Name { get; init; }

  /// <summary>When the database was created, in the handheld's local time; <c>default</c> when none is recorded.</summary>
  public DateTime Created { get; init; }

  /// <summary>When the database was last changed, in the handheld's local time; <c>default</c> when none is recorded.</summary>
  public DateTime Modified { get; init; }

  /// <summary>When the handheld last backed the database up, in its local time; <c>default</c> when it never has.</summary>
  public DateTime LastBackup { get; init; }

  /// <summary>The handheld's count of changes to the database; Sony's software writes nought and HotSync raises it.</summary>
  public uint ModificationNumber { get; init; }

  /// <summary>The database header's unique-id seed, which a resource database has no use for; kept so a file is written back as it was read.</summary>
  public uint UniqueIdSeed { get; init; }

  /// <summary>Red, green and blue of each palette entry of an eight-bit picture; <c>null</c> means the Palm system palette, and a sixteen-bit picture has none.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>One palette index per pixel at eight bits, two bytes of big-endian RGB 5:6:5 at sixteen; top row first.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The 44x44 thumbnail as indices into the Palm system palette, or <c>null</c> to have the writer make one from the picture.</summary>
  public byte[]? Thumbnail { get; init; }

  public static RawImage ToRawImage(PictureGearPocketFile file) {
    if (file.BitsPerPixel == 16)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = ExpandHighColor(file.PixelData, file.Width * file.Height),
      };

    var palette = file.Palette ?? PictureGearPocketCodec.SystemPaletteRgb();
    var count = palette.Length / 3;
    var pixels = file.PixelData[..(file.Width * file.Height)];
    var highest = 0;
    foreach (var index in pixels)
      highest = Math.Max(highest, index);

    // An index past the end of the palette is not something Sony's software writes; it reads one as
    // whatever lies past its palette, and this reads it as black.
    if (highest >= count) {
      var padded = new byte[(highest + 1) * 3];
      palette.CopyTo(padded, 0);
      palette = padded;
      count = highest + 1;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette[..(count * 3)],
      PaletteCount = count,
    };
  }

  /// <summary>The thumbnail as a 44x44 picture in the Palm system palette, or <c>null</c> when the file has none.</summary>
  public static RawImage? ThumbnailToRawImage(PictureGearPocketFile file) => file.Thumbnail is not { Length: ThumbnailSize * ThumbnailSize } thumbnail
    ? null
    : new() {
      Width = ThumbnailSize,
      Height = ThumbnailSize,
      Format = PixelFormat.Indexed8,
      PixelData = thumbnail[..],
      Palette = PictureGearPocketCodec.SystemPaletteRgb(),
      PaletteCount = 256,
    };

  /// <summary>Reduces the picture to <see cref="MaximumColors"/> colours, as PictureGear Lite does, and stores it run-length coded.</summary>
  public static PictureGearPocketFile FromRawImage(RawImage image) => FromRawImage(image, 8);

  /// <summary>Stores the picture at eight bits against a palette of at most <see cref="MaximumColors"/>, or at sixteen bits of RGB 5:6:5.</summary>
  public static PictureGearPocketFile FromRawImage(RawImage image, int bitsPerPixel) {
    ArgumentNullException.ThrowIfNull(image);
    if (bitsPerPixel is not (8 or 16))
      throw new ArgumentOutOfRangeException(nameof(bitsPerPixel), bitsPerPixel, "A PictureGear Pocket picture is eight or sixteen bits deep.");
    if (image.Width < 1 || image.Height < 1 || image.Width > MaximumExtent || image.Height > MaximumExtent)
      throw new ArgumentException($"A PictureGear Pocket picture is from 1x1 to {MaximumExtent}x{MaximumExtent}, not {image.Width}x{image.Height}.", nameof(image));

    var rgb = image.EnsureFormat(PixelFormat.Rgb24).PixelData;
    var thumbnail = PictureGearPocketWriter.MakeThumbnail(rgb, image.Width, image.Height);
    var common = new PictureGearPocketFile {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = bitsPerPixel,
      TileSize = PictureGearPocketWriter.TileSizeFor(image.Width, image.Height),
      IsCompressed = true,
      Name = "Picture",
      Thumbnail = thumbnail,
    };

    if (bitsPerPixel == 16) {
      var words = new byte[image.Width * image.Height * 2];
      for (var i = 0; i < image.Width * image.Height; ++i) {
        var value = (rgb[i * 3] & 0xF8) << 8 | (rgb[i * 3 + 1] & 0xFC) << 3 | rgb[i * 3 + 2] >> 3;
        words[i * 2] = (byte)(value >> 8);
        words[i * 2 + 1] = (byte)value;
      }

      return common with { PixelData = words };
    }

    var indexed = image.EnsureIndexedAtMost(MaximumColors);
    var count = Math.Min(indexed.PaletteCount, MaximumColors);
    var pixels = new byte[image.Width * image.Height];
    indexed.PixelData.AsSpan(0, pixels.Length).CopyTo(pixels);
    return common with {
      Palette = (indexed.Palette ?? []).AsSpan(0, count * 3).ToArray(),
      PixelData = pixels,
    };
  }

  /// <summary>Widens big-endian RGB 5:6:5 to eight bits a channel the way Sony's decoder does, value times 255 over the field's maximum.</summary>
  internal static byte[] ExpandHighColor(ReadOnlySpan<byte> words, int pixelCount) {
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var value = words[i * 2] << 8 | words[i * 2 + 1];
      rgb[i * 3] = (byte)((value >> 11) * 255 / 31);
      rgb[i * 3 + 1] = (byte)(((value >> 5) & 0x3F) * 255 / 63);
      rgb[i * 3 + 2] = (byte)((value & 0x1F) * 255 / 31);
    }

    return rgb;
  }
}
