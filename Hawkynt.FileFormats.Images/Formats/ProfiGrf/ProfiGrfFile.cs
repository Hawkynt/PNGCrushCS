using System;
using FileFormat.Core;

namespace FileFormat.ProfiGrf;

/// <summary>In-memory representation of a Profi picture (.grf).</summary>
/// <remarks>
/// The Profi was a Spectrum clone with a wider screen and a real palette, and this format shows
/// both: 512 pixels across, and sixteen colours chosen from a byte each rather than the Spectrum's
/// fixed eight. The attribute is still one per eight pixels, so the cell constraint the Spectrum is
/// known for is not gone — but ink and paper get a brightness bit each here rather than sharing
/// one, which is what makes all sixteen palette entries reachable in a single cell.
/// <para/>
/// Attributes are stored interleaved with the bitmap, a byte of pixels then its attribute, rather
/// than in a plane of their own.
/// </remarks>
public readonly record struct ProfiGrfFile
  : IImageFormatReader<ProfiGrfFile>, IImageToRawImage<ProfiGrfFile>,
    IImageFromRawImage<ProfiGrfFile>, IImageFormatWriter<ProfiGrfFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 512;

  /// <summary>Rows the display shows: each stored row is drawn twice.</summary>
  public const int Height = 480;

  /// <summary>Rows the file stores.</summary>
  public const int StoredRows = Height / 2;

  /// <summary>Bytes one stored row occupies: a byte of pixels and an attribute for every eight.</summary>
  public const int Stride = Width / 8 * 2;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Offset of the palette.</summary>
  public const int PaletteOffset = 10;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 128;

  /// <summary>Total file size.</summary>
  public const int FileSize = BitmapOffset + Stride * StoredRows;

  /// <summary>The ten bytes every file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => [0, 2, 240, 0, 4, 0, 128, 0, 1, 19];

  static string IImageFormatMetadata<ProfiGrfFile>.PrimaryExtension => ".grf";
  static string[] IImageFormatMetadata<ProfiGrfFile>.FileExtensions => [".grf"];
  static ProfiGrfFile IImageFormatReader<ProfiGrfFile>.FromSpan(ReadOnlySpan<byte> data)
    => ProfiGrfReader.FromSpan(data);
  static byte[] IImageFormatWriter<ProfiGrfFile>.ToBytes(ProfiGrfFile file)
    => ProfiGrfWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ProfiGrfFile>.VideoModes => [
    new("Profi", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(ProfiGrfFile file) {
    var data = file.Data ?? [];

    // Green in the top three bits, red in the next three, blue in the bottom two.
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var c = PaletteOffset + i < data.Length ? data[PaletteOffset + i] : 0;
      palette[i * 3] = ChannelScaling.Expand3((c >> 2) & 7);
      palette[i * 3 + 1] = ChannelScaling.Expand3(c >> 5);
      palette[i * 3 + 2] = ChannelScaling.Expand2(c & 3);
    }

    var pixels = new byte[Width * Height];
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = BitmapOffset + (y >> 1) * Stride + (x >> 3) * 2;
      var ink = at < data.Length && ((data[at] >> (~x & 7)) & 1) != 0;
      var attribute = at + 1 < data.Length ? data[at + 1] : 0;

      // Ink takes the low nibble with bit 6 as its brightness, paper the high one with bit 7.
      pixels[y * Width + x] = (byte)(ink
        ? ((attribute >> 3) & 8) | (attribute & 7)
        : ((attribute >> 4) & 8) | ((attribute >> 3) & 7));
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  /// <summary>Builds a picture, choosing sixteen colours and then two of them per group of eight.</summary>
  /// <remarks>
  /// The attribute is shaped like a Spectrum's — an ink and a paper, each with its own brightness
  /// bit — but the sixteen colours it indexes are the file's own rather than the hardware's, so the
  /// palette is chosen first and the pairs afterwards.
  /// <para/>
  /// Only half the rows are stored; each is shown twice. The picture is sampled at the row that is
  /// actually kept rather than averaged with the one that is not, since averaging would blur a
  /// pair of rows that the hardware was never going to show separately.
  /// </remarks>
  public static ProfiGrfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var snapped = new byte[Width * StoredRows * 3];

    // Green and red carry three bits each and blue only two, so the grid is not the same in every
    // channel and a colour has to be snapped to its own.
    for (var row = 0; row < StoredRows; ++row)
    for (var x = 0; x < Width; ++x) {
      var from = (row * 2 * Width + x) * 3;
      var to = (row * Width + x) * 3;
      snapped[to] = ChannelScaling.Expand3((rgb.PixelData[from] * 7 + 127) / 255);
      snapped[to + 1] = ChannelScaling.Expand3((rgb.PixelData[from + 1] * 7 + 127) / 255);
      snapped[to + 2] = ChannelScaling.Expand2((rgb.PixelData[from + 2] * 3 + 127) / 255);
    }

    var palette = _ChoosePalette(snapped);
    var data = new byte[FileSize];

    for (var i = 0; i < ColorCount; ++i)
      data[PaletteOffset + i] = (byte)(
        ((palette[i * 3 + 1] * 7 + 127) / 255 << 5)
        | ((palette[i * 3] * 7 + 127) / 255 << 2)
        | (palette[i * 3 + 2] * 3 + 127) / 255);

    for (var row = 0; row < StoredRows; ++row)
    for (var group = 0; group < Width / 8; ++group) {
      var (ink, paper, bits) = _ChooseGroup(snapped, palette, group * 8, row);
      var at = BitmapOffset + row * Stride + group * 2;

      data[at] = bits;
      data[at + 1] = (byte)(((paper >> 3) << 7) | ((ink >> 3) << 6) | ((paper & 7) << 3) | (ink & 7));
    }

    return new() { Data = data };
  }

  /// <summary>The two palette entries that describe one group of eight pixels with the least error.</summary>
  private static (int Ink, int Paper, byte Bits) _ChooseGroup(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> palette, int left, int row) {
    int bestInk = 0, bestPaper = 0, bestBits = 0;
    var bestCost = long.MaxValue;

    for (var ink = 0; ink < ColorCount; ++ink)
    for (var paper = 0; paper <= ink; ++paper) {
      var cost = 0L;
      var bits = 0;

      for (var x = 0; x < 8; ++x) {
        var at = (row * Width + left + x) * 3;
        var toInk = _Distance(rgb, at, palette, ink);
        var toPaper = _Distance(rgb, at, palette, paper);

        if (toInk <= toPaper) {
          bits |= 1 << (7 - x);
          cost += toInk;
        } else
          cost += toPaper;
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      bestInk = ink;
      bestPaper = paper;
      bestBits = bits;
    }

    return (bestInk, bestPaper, (byte)bestBits);
  }

  private static long _Distance(ReadOnlySpan<byte> rgb, int pixel, ReadOnlySpan<byte> palette, int entry) {
    long dr = rgb[pixel] - palette[entry * 3];
    long dg = rgb[pixel + 1] - palette[entry * 3 + 1];
    long db = rgb[pixel + 2] - palette[entry * 3 + 2];

    return dr * dr * 77 + dg * dg * 150 + db * db * 29;
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
