using System;
using FileFormat.Core;

namespace FileFormat.MapletownNl3;

/// <summary>In-memory representation of a Mapletown Network NL3 picture (.nl3).</summary>
/// <remarks>
/// A picture written to survive a bulletin board: every byte of it is a printable character, so the
/// file can be pasted into a message and read back out. That constraint shapes everything — the
/// values run 0 to 160 rather than 0 to 255 because that is how many usable characters there were,
/// a palette entry needs two of them, and a few characters are stored as the multi-byte sequences a
/// Japanese board would pass through unaltered.
/// <para/>
/// The picture is stored column by column, which is how the terminal drew it.
/// </remarks>
public readonly record struct MapletownNl3File
  : IImageFormatReader<MapletownNl3File>, IImageToRawImage<MapletownNl3File>,
    IImageFromRawImage<MapletownNl3File>, IImageFormatWriter<MapletownNl3File> {

  /// <summary>Pixels across.</summary>
  public const int Width = 160;

  /// <summary>Rows.</summary>
  public const int Height = 100;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 64;

  static string IImageFormatMetadata<MapletownNl3File>.PrimaryExtension => ".nl3";
  static string[] IImageFormatMetadata<MapletownNl3File>.FileExtensions => [".nl3"];
  static MapletownNl3File IImageFormatReader<MapletownNl3File>.FromSpan(ReadOnlySpan<byte> data)
    => MapletownNl3Reader.FromSpan(data);
  static byte[] IImageFormatWriter<MapletownNl3File>.ToBytes(MapletownNl3File file)
    => MapletownNl3Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MapletownNl3File>.VideoModes => [
    new("NEC PC-98", [(Width, Height)], [ColorCount])
  ];

  /// <summary>One palette index per pixel.</summary>
  public byte[] Pixels { get; init; }

  /// <summary>Sixty-four RGB triplets.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(MapletownNl3File file) => new() {
    Width = Width,
    Height = Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.Pixels ?? new byte[Width * Height],
    Palette = file.Palette ?? new byte[ColorCount * 3],
    PaletteCount = ColorCount,
  };

  /// <summary>Levels each channel of a palette entry can take.</summary>
  public const int Levels = 9;

  /// <summary>Builds a picture from an image, reduced to sixty-four colours it chooses itself.</summary>
  /// <remarks>
  /// The palette is free but the colours in it are not: each channel has nine levels and no more,
  /// so the picture is brought to that grid before its colours are counted. Sixty-four is generous
  /// for a drawing and thin for a photograph, which is what the format was for.
  /// </remarks>
  public static MapletownNl3File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var scaled = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var sourceX = image.Width == Width ? x : x * image.Width / Width;
      var sourceY = image.Height == Height ? y : y * image.Height / Height;
      var source = (sourceY * image.Width + sourceX) * 3;
      var target = (y * Width + x) * 3;

      // Rounded, not truncated: the levels are 255/8 apart, so truncating puts every value that
      // is not exactly on a level onto the one below it and loses the top level altogether.
      for (var channel = 0; channel < 3; ++channel) {
        var level = (rgb.PixelData[source + channel] * (Levels - 1) + 127) / 255;
        scaled[target + channel] = (byte)(level * 255 / (Levels - 1));
      }
    }

    var palette = _ChoosePalette(scaled);

    return new() { Pixels = PaletteQuantizer.Quantize(scaled, Width, Height, palette, ColorCount), Palette = palette };
  }

  /// <summary>Picks the sixty-four commonest colours, which is exact for a picture with no more.</summary>
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
