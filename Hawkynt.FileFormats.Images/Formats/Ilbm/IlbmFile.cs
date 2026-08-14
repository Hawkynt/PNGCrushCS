using System;
using FileFormat.Core;

namespace FileFormat.Ilbm;

/// <summary>In-memory representation of an IFF ILBM image.</summary>
[FormatMagicBytes([0x46, 0x4F, 0x52, 0x4D])]
public readonly record struct IlbmFile : IImageFormatReader<IlbmFile>, IImageToRawImage<IlbmFile>, IImageFromRawImage<IlbmFile>, IImageFormatWriter<IlbmFile> {

  static string IImageFormatMetadata<IlbmFile>.PrimaryExtension => ".lbm";
  /// <summary>Every name an IFF bitmap arrives under, <c>.blk</c> among them.</summary>
  /// <remarks>
  /// <c>.blk</c> is an Amiga IFF block saved out of a paint program under a name of its own; the
  /// bytes are an ordinary <c>FORM ILBM</c>. Nothing is guessed from the name — the reader still
  /// requires the group identifier and the form type, so a file that only happens to be called
  /// <c>.blk</c> is refused rather than drawn.
  /// </remarks>
  static string[] IImageFormatMetadata<IlbmFile>.FileExtensions => [".lbm", ".ilbm", ".iff", ".blk", ".ham", ".ham6", ".ham8", ".256", ".ap2", ".beam", ".dct", ".dr", ".mp", ".bl1", ".bl2", ".bl3"];
  static IlbmFile IImageFormatReader<IlbmFile>.FromSpan(ReadOnlySpan<byte> data) => IlbmReader.FromSpan(data);

  static bool? IImageFormatMetadata<IlbmFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x4D
      && header[8] == 0x49 && header[9] == 0x4C && header[10] == 0x42 && header[11] == 0x4D;

  static byte[] IImageFormatWriter<IlbmFile>.ToBytes(IlbmFile file) => IlbmWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int NumPlanes { get; init; }
  public IlbmCompression Compression { get; init; }
  public IlbmMasking Masking { get; init; }
  public int TransparentColor { get; init; }
  public byte XAspect { get; init; }
  public byte YAspect { get; init; }
  public int PageWidth { get; init; }
  public int PageHeight { get; init; }
  /// <summary>
  /// The picture unpacked from its planes: one byte a pixel where the planes index a palette, and
  /// one byte for each group of eight where they carry the colour itself.
  /// </summary>
  public byte[] PixelData { get; init; }

  public byte[]? Palette { get; init; }

  /// <summary>
  /// One palette a scanline, sixteen colours each as RGB triplets, when the file carries a SHAM
  /// chunk — otherwise null.
  /// </summary>
  /// <remarks>
  /// Sliced HAM changes the palette as the beam travels down the screen, which is how a machine with
  /// sixteen registers shows a picture with hundreds of colours in it. Decoding it against the single
  /// CMAP instead, as this did, gets the first line about right and drifts further out with every
  /// line after.
  /// </remarks>
  public byte[]? ScanlinePalettes { get; init; }

  /// <summary>CAMG viewport mode bits (from the Amiga display hardware).</summary>
  public uint ViewportMode { get; init; }

  /// <summary>Colours a sliced palette holds for each scanline.</summary>
  internal const int SlicedPaletteEntries = 16;

  /// <summary>Whether the image uses Hold-And-Modify mode (HAM6 or HAM8).</summary>
  public bool IsHam => (ViewportMode & 0x800) != 0;

  /// <summary>Whether the image uses Extra Half-Brite mode.</summary>
  public bool IsEhb => (ViewportMode & 0x80) != 0;

  /// <summary>Plane counts that carry whole colour bytes rather than an index into a palette.</summary>
  /// <remarks>
  /// Twenty-four planes are the three colour bytes and thirty-two add an alpha, one byte to each
  /// group of eight planes. Such a file carries no CMAP — there is nothing for it to describe — so
  /// reading it as indexed produces a picture with no palette to draw it by, which is what happened
  /// to everything XnView's converter wrote under this name.
  /// </remarks>
  internal static bool IsDeepPlaneCount(int numPlanes) => numPlanes is 24 or 32;

  /// <summary>Whether this picture's planes are colour bytes rather than palette indices.</summary>
  public bool IsDeep => IsDeepPlaneCount(this.NumPlanes);

  /// <summary>Converts this ILBM file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IlbmFile file) {

    // Deep first, and before the mode bits are consulted: a truecolour picture has no palette for
    // HAM or half-brite to work from, and the pixel data is already the colours rather than indices
    // into anything. CAMG is written on these files all the same — XnView's converter puts 0x1000
    // on every one — so asking about the modes first would send some of them down a branch that
    // reads its own bytes as indices.
    if (file.IsDeep)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = file.NumPlanes == 32 ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
        PixelData = file.PixelData[..],
      };

    // HAM mode: decode indexed data to RGB via HamDecoder
    if (file.IsHam && file.Palette is { } hamPalette) {
      var rgb = file.ScanlinePalettes is { } sliced
        ? HamDecoder.Decode(file.PixelData, sliced, file.Width, file.Height, file.NumPlanes, SlicedPaletteEntries)
        : HamDecoder.Decode(file.PixelData, hamPalette, file.Width, file.Height, file.NumPlanes);
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = rgb,
      };
    }

    // A palette that changes down the screen is not only a HAM thing: Dynamic HiRes states one per
    // scanline for an ordinary sixteen-colour picture, and rendering those against the single CMAP
    // gets the colours of all but the first few lines wrong.
    if (!file.IsHam && file.ScanlinePalettes is { } perLine) {
      var slices = perLine.Length / (SlicedPaletteEntries * 3);
      var rgb = new byte[file.Width * file.Height * 3];
      for (var y = 0; y < file.Height; ++y) {
        var line = slices == 0 ? 0 : (slices >= file.Height ? y : y * slices / file.Height);
        var paletteAt = line * SlicedPaletteEntries * 3;
        for (var x = 0; x < file.Width; ++x) {
          var index = file.PixelData[y * file.Width + x] % SlicedPaletteEntries;
          var from = paletteAt + index * 3;
          var to = (y * file.Width + x) * 3;
          if (from + 2 >= perLine.Length)
            continue;

          rgb[to] = perLine[from];
          rgb[to + 1] = perLine[from + 1];
          rgb[to + 2] = perLine[from + 2];
        }
      }

      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = rgb,
      };
    }

    // EHB mode: expand 32-entry palette to 64 entries (entries 32..63 = half brightness)
    if (file.IsEhb && file.Palette is { } ehbPalette) {
      var basePaletteCount = Math.Min(ehbPalette.Length / 3, 32);
      var expandedPalette = new byte[64 * 3];
      ehbPalette.AsSpan(0, basePaletteCount * 3).CopyTo(expandedPalette.AsSpan(0));
      for (var i = 0; i < basePaletteCount; ++i) {
        expandedPalette[(i + 32) * 3] = (byte)(ehbPalette[i * 3] / 2);
        expandedPalette[(i + 32) * 3 + 1] = (byte)(ehbPalette[i * 3 + 1] / 2);
        expandedPalette[(i + 32) * 3 + 2] = (byte)(ehbPalette[i * 3 + 2] / 2);
      }

      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = file.PixelData[..],
        Palette = expandedPalette,
        PaletteCount = 64,
      };
    }

    // Normal indexed mode
    var palette = file.Palette is { } p ? p[..] : null;
    var paletteCount = palette != null ? palette.Length / 3 : 1 << file.NumPlanes;

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Creates an <see cref="IlbmFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static IlbmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    // ILBM is a planar indexed format, so anything else has to be reduced to a palette. The
    // conversion handles both cases: an image already within 256 colours keeps them exactly,
    // and a fuller one is quantized rather than refused.
    image = image.EnsureFormat(PixelFormat.Indexed8);

    var pixelData = image.PixelData[..];
    var palette = image.Palette is { } p ? p[..] : null;
    var numPlanes = Math.Max(1, (int)Math.Ceiling(Math.Log2(Math.Max(image.PaletteCount, 2))));

    return new() {
      Width = image.Width,
      Height = image.Height,
      NumPlanes = numPlanes,
      Compression = IlbmCompression.None,
      Masking = IlbmMasking.None,
      TransparentColor = 0,
      XAspect = 1,
      YAspect = 1,
      PageWidth = image.Width,
      PageHeight = image.Height,
      PixelData = pixelData,
      Palette = palette,
      ViewportMode = 0,
    };
  }
}
