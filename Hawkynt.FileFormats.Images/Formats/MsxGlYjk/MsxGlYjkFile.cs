using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxGlYjk;

/// <summary>In-memory representation of an MSX2+ GL/SH picture in YJK colour
/// (.gla, .glb, .sha, .shb, .glc, .gls, .shc).</summary>
/// <remarks>
/// A four-byte header giving the dimensions, then one V9958 YJK byte per pixel — no BSAVE wrapper
/// and no fixed size, so these hold whatever the drawing program happened to be showing. The
/// extension says which of the two YJK readings applies: the <c>.gl[ab]</c> and <c>.sh[ab]</c>
/// pictures are Screen 10, where an odd luma escapes to a palette entry that lives in a companion
/// <c>.PLA</c> file, while <c>.glc</c>, <c>.gls</c> and <c>.shc</c> are Screen 12 and spend every
/// bit on colour.
/// </remarks>
public readonly record struct MsxGlYjkFile
  : IImageFormatReader<MsxGlYjkFile>, IImageToRawImage<MsxGlYjkFile>,
    IImageFromRawImage<MsxGlYjkFile>, IImageFormatWriter<MsxGlYjkFile> {

  /// <summary>Size of the header: width then height, each a little-endian 16-bit value.</summary>
  public const int HeaderSize = 4;

  /// <summary>Colours the Screen 10 palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Largest picture we accept, guarding against a corrupt header claiming gigabytes.</summary>
  public const int MaxDimension = 4096;

  static string IImageFormatMetadata<MsxGlYjkFile>.PrimaryExtension => ".glc";
  static string[] IImageFormatMetadata<MsxGlYjkFile>.FileExtensions => [".glc", ".gls", ".shc", ".gla", ".glb", ".sha", ".shb"];
  static MsxGlYjkFile IImageFormatReader<MsxGlYjkFile>.FromSpan(ReadOnlySpan<byte> data) => MsxGlYjkReader.FromSpan(data);

  /// <summary>
  /// Reads a named file, the extension being what its reader needs.
  /// </summary>
  /// <remarks>
  /// The reader takes the extension into account and only the by-bytes entry was wired up here,
  /// so the registry could never reach it: whatever the extension would have settled was decided
  /// by a default instead. Ten formats carried this, each one otherwise found only when a sample
  /// happened to expose it.
  /// </remarks>
  static MsxGlYjkFile IImageFormatReader<MsxGlYjkFile>.FromFile(FileInfo file) => MsxGlYjkReader.FromFile(file);
  static byte[] IImageFormatWriter<MsxGlYjkFile>.ToBytes(MsxGlYjkFile file) => MsxGlYjkWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MsxGlYjkFile>.VideoModes => [
    new("Screen 12", [(256, 212)], [19268]),
    new("Screen 10", [(256, 212)], [12515]),
  ];

  /// <summary>Which of the two YJK readings the extension calls for.</summary>
  public static MsxGlYjkMode ModeFromExtension(string extension) => extension.ToLowerInvariant() switch {
    ".gla" or ".glb" or ".sha" or ".shb" => MsxGlYjkMode.Screen10,
    _ => MsxGlYjkMode.Screen12,
  };

  /// <summary>Picture width.</summary>
  public int Width { get; init; }

  /// <summary>Picture height.</summary>
  public int Height { get; init; }

  /// <summary>Which YJK reading applies.</summary>
  public MsxGlYjkMode Mode { get; init; }

  /// <summary>The bitmap, one YJK byte per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// The sixteen Screen 10 palette colours, two bytes each, or empty. These are never in the file
  /// — they live in a companion <c>.PLA</c> — so a caller who has one supplies it here.
  /// </summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(MsxGlYjkFile file) {
    var width = file.Width;
    var height = file.Height;
    var data = file.PixelData ?? [];
    var usePalette = file.Mode == MsxGlYjkMode.Screen10;
    var palette = MsxGraphics.PaletteToRgb(file.Palette ?? [], ColorCount);
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var offset = y * width;
      if (offset + width > data.Length)
        break;

      MsxGraphics.DecodeYjkRow(data.AsSpan(offset, width), width, usePalette, palette, rgb.AsSpan(offset * 3, width * 3));
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  public static MsxGlYjkFile FromRawImage(RawImage image) => FromRawImage(image, MsxGlYjkMode.Screen12);

  /// <summary>Encodes for the screen the extension names rather than always for Screen 12.</summary>
  public static MsxGlYjkFile FromRawImage(RawImage image, string extension)
    => FromRawImage(image, ModeFromExtension(extension ?? string.Empty));

  /// <summary>Encodes a picture under a chosen one of the two YJK readings.</summary>
  public static MsxGlYjkFile FromRawImage(RawImage image, MsxGlYjkMode mode) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1 || image.Width > MaxDimension || image.Height > MaxDimension)
      throw new ArgumentException($"A GL/SH picture is at most {MaxDimension}x{MaxDimension}, got {image.Width}x{image.Height}.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var data = new byte[image.Width * image.Height];
    var usePalette = mode == MsxGlYjkMode.Screen10;

    for (var y = 0; y < image.Height; ++y)
      MsxGraphics.EncodeYjkRow(
        rgb.PixelData.AsSpan(y * image.Width * 3, image.Width * 3), image.Width, usePalette,
        data.AsSpan(y * image.Width, image.Width));

    return new() { Width = image.Width, Height = image.Height, Mode = mode, PixelData = data, Palette = [] };
  }
}
