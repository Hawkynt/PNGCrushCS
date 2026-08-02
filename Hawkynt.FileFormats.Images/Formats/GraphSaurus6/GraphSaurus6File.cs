using System;
using FileFormat.Core;

namespace FileFormat.GraphSaurus6;

/// <summary>In-memory representation of a Graph Saurus Screen 6 picture (.sr6).</summary>
/// <remarks>
/// A BSAVE header and then a Screen 6 bitmap, two bits a pixel across 512. Unlike the fixed-size
/// <c>.sc6</c> files, the height comes from the header's end address, so a picture can be shorter
/// than a full screen — Graph Saurus saved whatever the drawing occupied rather than padding it out.
/// <para/>
/// Screen 6 puts 512 pixels on a line by halving the vertical resolution, so every stored row is
/// drawn on two scanlines. The palette belongs to a companion <c>.PL6</c>; without one the picture
/// means what the machine starts up showing, which is black and three greens.
/// </remarks>
// The byte 0xFE opens every BSAVE file the MSX writes, whichever screen mode it holds, so it says
// what the container is and nothing about which of these formats this is. Nine of them declared it
// as their magic, and the registry consults magic before extension — so whichever it happened to
// reach first took every MSX picture. A Screen 5 file, 256 by 212, was being opened as a Screen 6
// one and drawn 512 by 424. The extension is what tells these apart, and it is what decides now.
public readonly record struct GraphSaurus6File
  : IImageFormatReader<GraphSaurus6File>, IImageToRawImage<GraphSaurus6File>,
    IImageFromRawImage<GraphSaurus6File>, IImageFormatWriter<GraphSaurus6File> {

  /// <summary>Stored pixels per row.</summary>
  public const int Width = 512;

  /// <summary>Bytes one stored row occupies, at four pixels per byte.</summary>
  public const int BytesPerRow = Width / 4;

  /// <summary>Offset of the bitmap, after the BSAVE header.</summary>
  public const int BitmapOffset = MsxGraphics.BsaveHeaderSize;

  /// <summary>Colours a Screen 6 picture can show at once.</summary>
  public const int ColorCount = 4;

  /// <summary>Tallest picture the mode displays.</summary>
  public const int MaxHeight = 212;

  static string IImageFormatMetadata<GraphSaurus6File>.PrimaryExtension => ".sr6";
  static string[] IImageFormatMetadata<GraphSaurus6File>.FileExtensions => [".sr6"];
  static GraphSaurus6File IImageFormatReader<GraphSaurus6File>.FromSpan(ReadOnlySpan<byte> data)
    => GraphSaurus6Reader.FromSpan(data);
  static byte[] IImageFormatWriter<GraphSaurus6File>.ToBytes(GraphSaurus6File file)
    => GraphSaurus6Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<GraphSaurus6File>.VideoModes => [
    new("Screen 6", [(Width, IntegerRange.Any)], [ColorCount])
  ];

  /// <summary>Stored rows; the picture is drawn twice as tall.</summary>
  public int StoredHeight { get; init; }

  /// <summary>The bitmap, four pixels per byte, most significant pair leftmost.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(GraphSaurus6File file) {
    var data = file.PixelData ?? [];
    var height = file.StoredHeight * 2;
    var pixels = new byte[Width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < Width; ++x) {
      // A linear pixel index, four to a byte, so a row is 128 bytes.
      var index = (y >> 1) * Width + x;
      var b = index >> 2 < data.Length ? data[index >> 2] : 0;
      pixels[y * Width + x] = (byte)((b >> ((~index & 3) << 1)) & 3);
    }

    return new() {
      Width = Width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = MsxGraphics.Screen6DefaultPaletteRgb.ToArray(),
      PaletteCount = ColorCount,
    };
  }

  /// <summary>Builds a picture in the four colours the machine starts up showing.</summary>
  /// <remarks>
  /// The palette lives in a companion file rather than this one, so a picture written alone means
  /// whatever the machine powers on with — black and three greens. Those are the four this encodes
  /// against, since writing against colours the file cannot carry would only look right until it
  /// was opened somewhere else.
  /// <para/>
  /// Screen 6 buys its 512 pixels a line by halving the vertical resolution, so a stored row is
  /// drawn on two scanlines and is read from the upper of the pair rather than averaged.
  /// </remarks>
  public static GraphSaurus6File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var stored = MaxHeight / 2;
    var rgb = image.SampleTo(Width, stored * 2);
    var palette = MsxGraphics.Screen6DefaultPaletteRgb;
    var pixels = new byte[stored * BytesPerRow];

    for (var row = 0; row < stored; ++row)
    for (var x = 0; x < Width; ++x) {
      var at = (row * 2 * Width + x) * 3;
      var index = row * Width + x;

      pixels[index >> 2] |= (byte)(_Nearest(rgb.PixelData, at, palette) << ((~index & 3) << 1));
    }

    return new() { StoredHeight = stored, PixelData = pixels };
  }

  /// <summary>Which of the four colours a pixel is closest to.</summary>
  private static int _Nearest(ReadOnlySpan<byte> rgb, int pixel, ReadOnlySpan<byte> palette) {
    var best = 0;
    var bestCost = long.MaxValue;

    for (var entry = 0; entry < ColorCount; ++entry) {
      long dr = rgb[pixel] - palette[entry * 3];
      long dg = rgb[pixel + 1] - palette[entry * 3 + 1];
      long db = rgb[pixel + 2] - palette[entry * 3 + 2];
      var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = entry;
    }

    return best;
  }
}
