using System;
using FileFormat.Core;

namespace FileFormat.Pc98Ebd;

/// <summary>In-memory representation of a PC-98 EBD picture (.ebd).</summary>
/// <remarks>
/// Sixteen colours and four bitplanes at 640 pixels across, with the height following from the file
/// length rather than being stored — the format has no header at all beyond its palette.
/// <para/>
/// That palette is written two ways. Some files store each channel as a nibble in its own byte and
/// some store it already widened to eight bits, and nothing says which. The two are told apart by
/// looking: a byte whose halves are equal is a widened four-bit value, and a file where that does
/// not hold everywhere must have its high nibbles clear or it is not a palette at all.
/// </remarks>
public readonly record struct Pc98EbdFile
  : IImageFormatReader<Pc98EbdFile>, IImageToRawImage<Pc98EbdFile>,
    IImageFromRawImage<Pc98EbdFile>, IImageFormatWriter<Pc98EbdFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 640;

  /// <summary>Bitplanes a pixel is spread over.</summary>
  public const int Planes = 4;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 1 << Planes;

  /// <summary>Rows a picture written from an image takes, which is the machine's own screen.</summary>
  public const int DefaultHeight = 400;

  /// <summary>Offset of the bitmap, after the palette.</summary>
  public const int BitmapOffset = ColorCount * 3;

  /// <summary>Bytes one row of the picture occupies across all four planes.</summary>
  public const int Stride = Width / 8 * Planes;

  static string IImageFormatMetadata<Pc98EbdFile>.PrimaryExtension => ".ebd";
  static string[] IImageFormatMetadata<Pc98EbdFile>.FileExtensions => [".ebd"];
  static Pc98EbdFile IImageFormatReader<Pc98EbdFile>.FromSpan(ReadOnlySpan<byte> data)
    => Pc98EbdReader.FromSpan(data);
  static byte[] IImageFormatWriter<Pc98EbdFile>.ToBytes(Pc98EbdFile file) => Pc98EbdWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Pc98EbdFile>.VideoModes => [
    new("EBD", [(Width, IntegerRange.Any)], [ColorCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Rows, derived from the file length.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(Pc98EbdFile file) {
    var data = file.Data ?? [];

    return new() {
      Width = Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = PlanarConverter.NonInterleavedPlanarToChunky(
        data.AsSpan(BitmapOffset), Width, file.Height, Planes),
      Palette = ReadPalette(data),
      PaletteCount = ColorCount,
    };
  }

  /// <summary>Reads the palette, widening its channels only when they are not already widened.</summary>
  public static byte[] ReadPalette(ReadOnlySpan<byte> data) {
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < palette.Length && i < data.Length; ++i) {
      var c = data[i];
      palette[i] = (c >> 4) == (c & 15) ? c : (byte)(c * 17);
    }

    return palette;
  }

  /// <summary>Builds a picture, choosing sixteen colours from the chip's four-bit grid.</summary>
  /// <remarks>
  /// Each palette channel is one nibble widened by repeating it, and a reader tells a widened byte
  /// from a raw nibble by whether its two halves match. Writing the widened form keeps that test
  /// unambiguous — every byte written has equal halves, so nothing can be mistaken for the other
  /// convention.
  /// </remarks>
  public static Pc98EbdFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, DefaultHeight);
    var snapped = new byte[Width * DefaultHeight * 3];

    // Onto the four-bit grid first: two shades the hardware cannot tell apart must not each claim
    // one of the sixteen entries.
    for (var i = 0; i < snapped.Length; ++i)
      snapped[i] = (byte)((rgb.PixelData[i] + 8) / 17 * 17);

    var palette = _ChoosePalette(snapped);
    var indices = PaletteQuantizer.Quantize(snapped, Width, DefaultHeight, palette, ColorCount);

    var data = new byte[BitmapOffset + DefaultHeight * Stride];
    for (var i = 0; i < palette.Length; ++i)
      data[i] = palette[i];

    PlanarConverter
      .ChunkyToNonInterleavedPlanar(indices, Width, DefaultHeight, Planes)
      .CopyTo(data.AsSpan(BitmapOffset));

    return new() { Data = data, Height = DefaultHeight };
  }

  /// <summary>Picks the commonest colours, which is exact for a picture with no more than sixteen.</summary>
  private static byte[] _ChoosePalette(ReadOnlySpan<byte> rgb) {
    var counts = new System.Collections.Generic.Dictionary<int, int>();
    for (var i = 0; i + 2 < rgb.Length; i += 3) {
      var key = (rgb[i] << 16) | (rgb[i + 1] << 8) | rgb[i + 2];
      counts[key] = counts.TryGetValue(key, out var seen) ? seen + 1 : 1;
    }

    var chosen = new System.Collections.Generic.List<int>(counts.Keys);
    chosen.Sort((a, b) => {
      var byCount = counts[b].CompareTo(counts[a]);

      // Ties break on the colour itself, so the result does not depend on dictionary order.
      return byCount != 0 ? byCount : a.CompareTo(b);
    });

    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount && i < chosen.Count; ++i) {
      palette[i * 3] = (byte)(chosen[i] >> 16);
      palette[i * 3 + 1] = (byte)(chosen[i] >> 8);
      palette[i * 3 + 2] = (byte)chosen[i];
    }

    return palette;
  }
}
