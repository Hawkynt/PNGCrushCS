using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxGl16;

/// <summary>In-memory representation of a sixteen-colour MSX2 GL picture (.gl5, .sh5, .gl7, .sh7).</summary>
/// <remarks>
/// A four-byte header giving the dimensions, then four bits per pixel, high half of each byte
/// leftmost. Which screen it belongs to is not in the file: the <c>.gl5</c> and <c>.sh5</c>
/// pictures are Screen 5 and drawn as stored, while <c>.gl7</c> and <c>.sh7</c> are Screen 7, whose
/// 512-pixel lines cost half the vertical resolution, so every stored row covers two scanlines.
/// <para/>
/// The palette is not in the file either — it lives in a companion <c>.PL5</c> or <c>.PL7</c> — so a
/// picture read on its own shows the sixteen colours an MSX2 starts up with.
/// </remarks>
public readonly record struct MsxGl16File
  : IImageFormatReader<MsxGl16File>, IImageToRawImage<MsxGl16File>,
    IImageFromRawImage<MsxGl16File>, IImageFormatWriter<MsxGl16File> {

  /// <summary>Size of the header: width then height, each a little-endian 16-bit value.</summary>
  public const int HeaderSize = 4;

  /// <summary>Colours a picture can show at once.</summary>
  public const int ColorCount = 16;

  /// <summary>Largest picture we accept, guarding against a corrupt header claiming gigabytes.</summary>
  public const int MaxDimension = 4096;

  static string IImageFormatMetadata<MsxGl16File>.PrimaryExtension => ".gl5";
  static string[] IImageFormatMetadata<MsxGl16File>.FileExtensions => [".gl5", ".sh5", ".gl7", ".sh7"];
  static MsxGl16File IImageFormatReader<MsxGl16File>.FromSpan(ReadOnlySpan<byte> data) => MsxGl16Reader.FromSpan(data);

  /// <summary>
  /// Reads a named file, the extension being what its reader needs.
  /// </summary>
  /// <remarks>
  /// The reader takes the extension into account and only the by-bytes entry was wired up here,
  /// so the registry could never reach it: whatever the extension would have settled was decided
  /// by a default instead. Ten formats carried this, each one otherwise found only when a sample
  /// happened to expose it.
  /// </remarks>
  static MsxGl16File IImageFormatReader<MsxGl16File>.FromFile(FileInfo file) => MsxGl16Reader.FromFile(file);
  static byte[] IImageFormatWriter<MsxGl16File>.ToBytes(MsxGl16File file) => MsxGl16Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MsxGl16File>.VideoModes => [
    new("Screen 5", [(256, 212)], [ColorCount]),
    new("Screen 7", [(512, 424)], [ColorCount]),
  ];

  /// <summary>Bytes a bitmap of the given size occupies: two pixels per byte.</summary>
  public static int PixelDataSizeFor(int width, int height) => (width * height + 1) / 2;

  /// <summary>Which screen the extension names.</summary>
  public static MsxGl16Mode ModeFromExtension(string extension)
    => extension.ToLowerInvariant() is ".gl7" or ".sh7" ? MsxGl16Mode.Screen7 : MsxGl16Mode.Screen5;

  /// <summary>Scanlines one stored row is drawn on.</summary>
  public static int RowScaleFor(MsxGl16Mode mode) => mode == MsxGl16Mode.Screen7 ? 2 : 1;

  /// <summary>Stored width.</summary>
  public int Width { get; init; }

  /// <summary>Stored height.</summary>
  public int Height { get; init; }

  /// <summary>Which screen this belongs to.</summary>
  public MsxGl16Mode Mode { get; init; }

  /// <summary>The bitmap, two pixels per byte.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The sixteen colours, two bytes each, or empty for the machine's startup palette.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(MsxGl16File file) {
    var data = file.PixelData ?? [];
    var stored = file.Palette ?? [];
    var scale = RowScaleFor(file.Mode);
    var width = file.Width;
    var height = file.Height * scale;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)MsxGraphics.GetNibble(data, 0, (y / scale) * width + x);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = MsxGraphics.PaletteToRgb(stored.Length > 0 ? stored : MsxGraphics.DefaultPalette, ColorCount),
      PaletteCount = ColorCount,
    };
  }

  public static MsxGl16File FromRawImage(RawImage image) => FromRawImage(image, MsxGl16Mode.Screen5);

  /// <summary>Encodes for the screen the extension names rather than always for Screen 5.</summary>
  public static MsxGl16File FromRawImage(RawImage image, string extension)
    => FromRawImage(image, ModeFromExtension(extension ?? string.Empty));

  /// <summary>Encodes a picture for a chosen one of the two screens.</summary>
  public static MsxGl16File FromRawImage(RawImage image, MsxGl16Mode mode) {
    ArgumentNullException.ThrowIfNull(image);
    var scale = RowScaleFor(mode);
    if (image.Width < 1 || image.Height < scale || image.Width > MaxDimension || image.Height > MaxDimension)
      throw new ArgumentException($"A GL16 picture is at most {MaxDimension}x{MaxDimension}, got {image.Width}x{image.Height}.", nameof(image));
    if (image.Height % scale != 0)
      throw new ArgumentException($"A Screen 7 picture has an even height; got {image.Height}.", nameof(image));

    // The file is a four-byte header and pixels, with nowhere to state a palette — so a reader has
    // only the machine's own sixteen colours to go by. Choosing colours freely and writing indices
    // into them, as this did, left every index naming a different colour than it was picked for.
    var machine = MsxGraphics.PaletteToRgb(MsxGraphics.DefaultPalette, ColorCount);
    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, machine);
    var stored = image.Height / scale;
    var data = new byte[PixelDataSizeFor(image.Width, stored)];

    // On Screen 7 only the first of each pair of scanlines is kept; the machine shows it twice.
    for (var y = 0; y < stored; ++y)
    for (var x = 0; x < image.Width; ++x) {
      var index = y * image.Width + x;
      var color = indexed.PixelData[y * scale * image.Width + x] & 15;
      data[index >> 1] |= (byte)((index & 1) == 0 ? color << 4 : color);
    }

    return new() {
      Width = image.Width,
      Height = stored,
      Mode = mode,
      PixelData = data,
      Palette = [],
    };
  }
}
