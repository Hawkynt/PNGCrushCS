using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.OcpArtStudioWindow;

/// <summary>In-memory representation of an Advanced OCP Art Studio window (.win).</summary>
/// <remarks>
/// A rectangle cut out of an Amstrad mode 0 screen, with its size stored in the last five bytes
/// rather than the first — the program appended it after writing the picture, which is what a
/// clipping routine that does not know its own extent until it finishes naturally does.
/// <para/>
/// The colours are not in the file at all. They live in a .pal beside it, which also says which
/// screen mode the palette was made for; a window is only a window of a mode 0 screen, so a
/// palette naming any other mode belongs to a different picture.
/// </remarks>
public readonly record struct OcpArtStudioWindowFile
  : IImageFormatReader<OcpArtStudioWindowFile>, IImageToRawImage<OcpArtStudioWindowFile>,
    IImageFromRawImage<OcpArtStudioWindowFile>, IImageFormatWriter<OcpArtStudioWindowFile> {

  /// <summary>Colours a mode 0 screen shows.</summary>
  public const int ColorCount = 16;

  /// <summary>Bytes the size occupies at the end of the file.</summary>
  public const int TrailerLength = 5;

  /// <summary>The mode a window's palette must name.</summary>
  public const int RequiredMode = 0;

  /// <summary>Widest picture the stored width can describe, it counting two positions a pixel.</summary>
  public const int MaximumWidth = 320;

  /// <summary>Tallest picture the single height byte can describe.</summary>
  public const int MaximumHeight = 200;

  /// <summary>The extension of the file a window keeps its colours in.</summary>
  public const string CompanionExtension = ".pal";

  /// <summary>Bytes a companion palette holds past any header.</summary>
  private const int _PALETTE_LENGTH = 239;

  /// <summary>Bytes between one colour of a companion palette and the next.</summary>
  private const int _PALETTE_STRIDE = 12;

  static string IImageFormatMetadata<OcpArtStudioWindowFile>.PrimaryExtension => ".win";
  static string[] IImageFormatMetadata<OcpArtStudioWindowFile>.FileExtensions => [".win"];
  static OcpArtStudioWindowFile IImageFormatReader<OcpArtStudioWindowFile>.FromSpan(ReadOnlySpan<byte> data)
    => OcpArtStudioWindowReader.FromSpan(data);

  /// <summary>Reads the file together with the companion it cannot be shown without.</summary>
  static OcpArtStudioWindowFile IImageFormatReader<OcpArtStudioWindowFile>.FromFile(FileInfo file)
    => OcpArtStudioWindowReader.FromFile(file);
  static byte[] IImageFormatWriter<OcpArtStudioWindowFile>.ToBytes(OcpArtStudioWindowFile file)
    => OcpArtStudioWindowWriter.ToBytes(file);

  /// <summary>Writes the palette beside the window, which stores none of its own.</summary>
  static void IImageFormatWriter<OcpArtStudioWindowFile>.WriteCompanions(
    OcpArtStudioWindowFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(Path.ChangeExtension(target.FullName, CompanionExtension), PaletteFile(file));
  }
  static VideoMode[] IImageFormatMetadata<OcpArtStudioWindowFile>.VideoModes => [
    new("Window", [(new IntegerRange(1, 640), new IntegerRange(1, 200))], [ColorCount])
  ];

  /// <summary>The bitmap, unpacked if it was packed.</summary>
  public byte[] Bitmap { get; init; }

  /// <summary>The palette from the companion file, as RGB triplets.</summary>
  public byte[] Palette { get; init; }

  /// <summary>
  /// Pixels across, which is half what the file stores — a mode 0 pixel occupies two of the
  /// machine's screen positions, and the stored width counts those.
  /// </summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>
  /// Bytes one row occupies, which follows from the stored width rather than the pixel one.
  /// </summary>
  /// <remarks>
  /// Mode 0 fits two pixels in a byte, so a row of N pixels is N/2 bytes — but the header counts
  /// screen positions, of which there are 2N, and the row length is computed from those. The two
  /// halvings cancel, which is easy to do only once by mistake.
  /// </remarks>
  public int Stride { get; init; }

  public static RawImage ToRawImage(OcpArtStudioWindowFile file) {
    var bitmap = file.Bitmap ?? [];
    var stride = file.Stride;
    var pixels = new byte[file.Width * file.Height];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < file.Width; ++x) {
      var at = y * stride + (x >> 2);
      var b = at < bitmap.Length ? bitmap[at] : 0;

      pixels[y * file.Width + x] = (byte)AmstradGraphics.Mode0Index(b, (x & 2) != 0);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = file.Palette,
      PaletteCount = ColorCount,
    };
  }

  /// <summary>
  /// Cuts a picture down to a mode 0 window: sixteen of the Gate Array's colours, two screen
  /// positions to a pixel.
  /// </summary>
  /// <remarks>
  /// A mode 0 pixel occupies two of the positions the picture is shown at, so a picture is stored at
  /// half the width it comes back out at. Each pair of columns is therefore given one colour — the
  /// left one's — and the other is lost, which is the mode's own resolution rather than a shortcut.
  /// A picture larger than a screen is sampled down to one first; anything smaller is a window of
  /// exactly its own size, which is what a window is for.
  /// </remarks>
  public static OcpArtStudioWindowFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Min(Math.Max(image.Width, 1), MaximumWidth);
    var height = Math.Min(Math.Max(image.Height, 1), MaximumHeight);
    var source = image.Width == width && image.Height == height ? image : image.SampleTo(width, height);

    var palette = _ChoosePalette(source, out var indexed);
    var stride = ((width << 1) + 7) >> 3;
    var bitmap = new byte[stride * height];

    for (var y = 0; y < height; ++y)
    for (var at = 0; at < stride; ++at) {
      var x = at << 2;
      bitmap[y * stride + at] = AmstradGraphics.Mode0Byte(_IndexAt(indexed, width, height, x, y), _IndexAt(indexed, width, height, x + 2, y));
    }

    return new() { Bitmap = bitmap, Palette = palette, Width = width, Height = height, Stride = stride };
  }

  private static int _IndexAt(RawImage indexed, int width, int height, int x, int y)
    => x < width && y < height ? indexed.PixelData[y * width + x] : 0;

  /// <summary>
  /// Reduces the picture to sixteen colours and pulls each of them onto the nearest the hardware
  /// has, the companion being able to name nothing else.
  /// </summary>
  private static byte[] _ChoosePalette(RawImage image, out RawImage indexed) {
    indexed = image.EnsureIndexedAtMost(ColorCount);

    var palette = new byte[ColorCount * 3];
    var chosen = indexed.Palette ?? [];

    for (var i = 0; i < ColorCount; ++i) {
      var entry = i * 3;
      var nearest = entry + 2 < chosen.Length
        ? _NearestHardwareColor(chosen[entry], chosen[entry + 1], chosen[entry + 2])
        : 0;

      AmstradGraphics.Palette.Slice(nearest * 3, 3).CopyTo(palette.AsSpan(entry));
    }

    return palette;
  }

  private static int _NearestHardwareColor(byte red, byte green, byte blue) {
    var best = 0;
    var bestCost = int.MaxValue;

    for (var candidate = 0; candidate < AmstradGraphics.ColorCount; ++candidate) {
      var entry = candidate * 3;
      int dr = red - AmstradGraphics.Palette[entry],
        dg = green - AmstradGraphics.Palette[entry + 1],
        db = blue - AmstradGraphics.Palette[entry + 2];

      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return best;
  }

  /// <summary>The companion palette's own bytes: a mode, then a colour every twelfth byte.</summary>
  public static byte[] PaletteFile(OcpArtStudioWindowFile file) {
    var data = new byte[_PALETTE_LENGTH];
    data[0] = RequiredMode;

    var palette = file.Palette ?? [];
    for (var i = 0; i < ColorCount; ++i) {
      var entry = i * 3;
      var nearest = entry + 2 < palette.Length
        ? _NearestHardwareColor(palette[entry], palette[entry + 1], palette[entry + 2])
        : 0;

      data[3 + i * _PALETTE_STRIDE] = (byte)(AmstradGraphics.ColorBias + nearest);
    }

    return data;
  }
}
