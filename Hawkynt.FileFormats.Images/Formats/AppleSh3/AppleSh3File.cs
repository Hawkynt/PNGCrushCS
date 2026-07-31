using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.AppleSh3;

/// <summary>In-memory representation of an unpacked 3200-colour picture (.sh3, .3200).</summary>
/// <remarks>
/// The same picture a 3201 file holds — an Apple IIGS super hi-res screen with a fresh sixteen
/// colour palette for every scanline — but written out as it sits in memory rather than packed: the
/// bitmap first at two pixels a byte, then the two hundred palettes. There is no header at all, so
/// the length is the whole of the identification.
/// <para/>
/// Each palette is stored in reverse order, which is how the hardware's registers are addressed.
/// </remarks>
public readonly record struct AppleSh3File
  : IImageFormatReader<AppleSh3File>, IImageToRawImage<AppleSh3File>,
    IImageFromRawImage<AppleSh3File>, IImageFormatWriter<AppleSh3File> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Colours a scanline's palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Bytes one row occupies: two pixels a byte.</summary>
  public const int Stride = Width / 2;

  /// <summary>Where the palettes start, after the bitmap.</summary>
  public const int PalettesOffset = Stride * Height;

  /// <summary>Total file size.</summary>
  public const int FileSize = PalettesOffset + Height * ColorCount * 2;

  static string IImageFormatMetadata<AppleSh3File>.PrimaryExtension => ".sh3";
  static string[] IImageFormatMetadata<AppleSh3File>.FileExtensions => [".sh3", ".3200"];
  static AppleSh3File IImageFormatReader<AppleSh3File>.FromSpan(ReadOnlySpan<byte> data)
    => AppleSh3Reader.FromSpan(data);
  static byte[] IImageFormatWriter<AppleSh3File>.ToBytes(AppleSh3File file) => AppleSh3Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AppleSh3File>.VideoModes => [
    new("3200 colours", [(Width, Height)], [3200])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(AppleSh3File file) {
    var data = file.Data ?? [];
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y) {
      var palette = AppleIIGSGraphics.ReadPalette(data, PalettesOffset + y * ColorCount * 2, reversed: true);

      for (var x = 0; x < Width; ++x) {
        var at = y * Stride + (x >> 1);
        var index = at < data.Length ? (x & 1) == 0 ? data[at] >> 4 : data[at] & 15 : 0;

        var entry = index * 3;
        var target = (y * Width + x) * 3;
        rgb[target] = palette[entry];
        rgb[target + 1] = palette[entry + 1];
        rgb[target + 2] = palette[entry + 2];
      }
    }

    return new() { Width = Width, Height = Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Builds a picture, choosing a fresh sixteen colours for every scanline.</summary>
  /// <remarks>
  /// This is what the format is for. The chip holds sixteen colours, but the palette can be
  /// reloaded between one line and the next, so a picture may use up to 3200 — and choosing them a
  /// line at a time is both what the hardware wants and what makes the choice easy: a scanline of
  /// a photograph rarely holds more than sixteen distinct colours once they are on the four-bit
  /// grid the palette stores, so most lines come out exact rather than approximated.
  /// <para/>
  /// Error is diffused within a line but never into the next, since the next line has a palette of
  /// its own and would only have to undo it.
  /// </remarks>
  public static AppleSh3File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var data = new byte[FileSize];
    var line = new byte[Width * 3];

    for (var y = 0; y < Height; ++y) {
      var sourceY = image.Height == Height ? y : y * image.Height / Height;

      for (var x = 0; x < Width; ++x) {
        var sourceX = image.Width == Width ? x : x * image.Width / Width;
        var source = (sourceY * image.Width + sourceX) * 3;

        // The palette carries four bits a channel, so the line is brought to that grid before its
        // colours are counted — otherwise near-identical shades would each claim an entry.
        for (var channel = 0; channel < 3; ++channel)
          line[x * 3 + channel] = (byte)((rgb.PixelData[source + channel] + 8) / 17 * 17);
      }

      var palette = _ChooseLinePalette(line);
      var indices = PaletteQuantizer.Quantize(line, Width, 1, palette, ColorCount);

      for (var x = 0; x < Width; x += 2)
        data[y * Stride + (x >> 1)] = (byte)((indices[x] << 4) | indices[x + 1]);

      // The entries are stored last first, the hardware's registers being addressed downwards.
      var at = PalettesOffset + y * ColorCount * 2;
      for (var i = 0; i < ColorCount; ++i) {
        var entry = at + ((i ^ (ColorCount - 1)) << 1);
        data[entry] = (byte)(((palette[i * 3 + 1] / 17) << 4) | (palette[i * 3 + 2] / 17));
        data[entry + 1] = (byte)(palette[i * 3] / 17);
      }
    }

    return new() { Data = data };
  }

  /// <summary>Picks the sixteen commonest colours of one line, which is exact for a line with no more.</summary>
  private static byte[] _ChooseLinePalette(ReadOnlySpan<byte> line) {
    var counts = new System.Collections.Generic.Dictionary<int, int>();
    for (var i = 0; i + 2 < line.Length; i += 3) {
      var key = (line[i] << 16) | (line[i + 1] << 8) | line[i + 2];
      counts[key] = counts.GetValueOrDefault(key) + 1;
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
