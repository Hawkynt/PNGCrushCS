using System;
using FileFormat.Core;

namespace FileFormat.Apple3201;

/// <summary>In-memory representation of a 3201 picture (.3201).</summary>
/// <remarks>
/// An Apple IIGS super hi-res screen with a palette per scanline rather than the sixteen the
/// hardware holds at once — the machine can be made to reload its palette between lines, so a
/// picture can use 3200 colours where the chip offers sixteen. The name is that number.
/// <para/>
/// The palettes come first, all two hundred of them, and the bitmap is packed afterwards. Each
/// palette is stored in reverse order, which is how the hardware's registers are addressed.
/// </remarks>
public readonly record struct Apple3201File
  : IImageFormatReader<Apple3201File>, IImageToRawImage<Apple3201File>,
    IImageFromRawImage<Apple3201File>, IImageFormatWriter<Apple3201File> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Colours a scanline's palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Offset of the palettes.</summary>
  public const int PalettesOffset = 4;

  /// <summary>Bytes one scanline's palette occupies.</summary>
  public const int PaletteSize = ColorCount * 2;

  /// <summary>Offset of the packed bitmap.</summary>
  public const int BitmapOffset = PalettesOffset + Height * PaletteSize;

  /// <summary>Bytes one row unpacks to: two pixels a byte.</summary>
  public const int Stride = Width / 2;

  /// <summary>The four bytes every file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => [193, 208, 208, 0];

  static string IImageFormatMetadata<Apple3201File>.PrimaryExtension => ".3201";
  static string[] IImageFormatMetadata<Apple3201File>.FileExtensions => [".3201"];
  static Apple3201File IImageFormatReader<Apple3201File>.FromSpan(ReadOnlySpan<byte> data)
    => Apple3201Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Apple3201File>.ToBytes(Apple3201File file) => Apple3201Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Apple3201File>.VideoModes => [
    new("3200 colours", [(Width, Height)], [3200])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>The unpacked bitmap, two pixels a byte.</summary>
  public byte[] Bitmap { get; init; }

  public static RawImage ToRawImage(Apple3201File file) {
    var data = file.Data ?? [];
    var bitmap = file.Bitmap ?? [];
    var rgb = new byte[Width * Height * 3];

    Span<byte> palette = stackalloc byte[ColorCount * 3];

    for (var y = 0; y < Height; ++y) {
      _ReadPalette(data, PalettesOffset + y * PaletteSize, palette);

      for (var x = 0; x < Width; ++x) {
        var at = y * Stride + (x >> 1);
        var b = at < bitmap.Length ? bitmap[at] : 0;
        var entry = ((x & 1) == 0 ? b >> 4 : b & 15) * 3;

        var target = (y * Width + x) * 3;
        rgb[target] = palette[entry];
        rgb[target + 1] = palette[entry + 1];
        rgb[target + 2] = palette[entry + 2];
      }
    }

    return new() { Width = Width, Height = Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// Reads one scanline's palette, whose entries are stored in the reverse of the order they are
  /// used — which is the order the hardware's registers are addressed in.
  /// </summary>
  private static void _ReadPalette(ReadOnlySpan<byte> data, int offset, Span<byte> palette) {
    for (var c = 0; c < ColorCount; ++c) {
      var at = offset + ((c ^ (ColorCount - 1)) << 1);
      if (at + 1 >= data.Length)
        break;

      var gb = data[at];
      palette[c * 3] = ChannelScaling.Expand4(data[at + 1] & 15);
      palette[c * 3 + 1] = ChannelScaling.Expand4(gb >> 4);
      palette[c * 3 + 2] = ChannelScaling.Expand4(gb & 15);
    }
  }

  /// <summary>Builds a picture, choosing a fresh sixteen colours for every scanline.</summary>
  /// <remarks>
  /// The same picture the unpacked form holds, so the same choice is made: sixteen colours a line,
  /// picked from that line, which is exact for a line with no more once its colours are on the
  /// four-bit grid the palette stores.
  /// </remarks>
  public static Apple3201File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var data = new byte[PalettesOffset + Height * PaletteSize];
    var bitmap = new byte[Stride * Height];
    var line = new byte[Width * 3];

    Signature.CopyTo(data);

    for (var y = 0; y < Height; ++y) {
      var sourceY = image.Height == Height ? y : y * image.Height / Height;

      for (var x = 0; x < Width; ++x) {
        var sourceX = image.Width == Width ? x : x * image.Width / Width;
        var source = (sourceY * image.Width + sourceX) * 3;

        for (var channel = 0; channel < 3; ++channel)
          line[x * 3 + channel] = (byte)((rgb.PixelData[source + channel] + 8) / 17 * 17);
      }

      var palette = _ChooseLinePalette(line);
      var indices = PaletteQuantizer.Quantize(line, Width, 1, palette, ColorCount);

      for (var x = 0; x < Width; x += 2)
        bitmap[y * Stride + (x >> 1)] = (byte)((indices[x] << 4) | indices[x + 1]);

      // The entries go in last first, the hardware's registers being addressed downwards.
      var at = PalettesOffset + y * PaletteSize;
      for (var i = 0; i < ColorCount; ++i) {
        var entry = at + ((i ^ (ColorCount - 1)) << 1);
        data[entry] = (byte)(((palette[i * 3 + 1] / 17) << 4) | (palette[i * 3 + 2] / 17));
        data[entry + 1] = (byte)(palette[i * 3] / 17);
      }
    }

    return new() { Data = data, Bitmap = bitmap };
  }

  /// <summary>Picks the sixteen commonest colours of one line.</summary>
  private static byte[] _ChooseLinePalette(ReadOnlySpan<byte> line) {
    var counts = new System.Collections.Generic.Dictionary<int, int>();
    for (var i = 0; i + 2 < line.Length; i += 3) {
      var key = (line[i] << 16) | (line[i + 1] << 8) | line[i + 2];
      counts[key] = counts.TryGetValue(key, out var seen) ? seen + 1 : 1;
    }

    var chosen = new System.Collections.Generic.List<int>(counts.Keys);
    chosen.Sort((a, b) => {
      var byCount = counts[b].CompareTo(counts[a]);

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
