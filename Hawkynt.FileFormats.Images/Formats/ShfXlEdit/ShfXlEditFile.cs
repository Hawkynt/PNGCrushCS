using System;
using FileFormat.Core;

namespace FileFormat.ShfXlEdit;

/// <summary>In-memory representation of an SHF-XL Edit picture (.shx).</summary>
/// <remarks>
/// A C64 FLI screen 144 pixels across with sprites over it, in two forms that share nothing but
/// their dimensions. The unpacked one is a copy of video memory, so the sprite that covers a cell
/// has to be worked out from where the machine put it. The packed one is a rearrangement the editor
/// made for its own convenience — three plain planes of bitmap, mask and colour — which is much
/// simpler to read and could not have been displayed without being taken apart again.
/// </remarks>
public readonly record struct ShfXlEditFile
  : IImageFormatReader<ShfXlEditFile>, IImageToRawImage<ShfXlEditFile>,
    IImageFromRawImage<ShfXlEditFile>, IImageFormatWriter<ShfXlEditFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 144;

  /// <summary>Rows.</summary>
  public const int Height = 168;

  /// <summary>Bytes one row occupies in the packed form.</summary>
  public const int PackedStride = Width / 8;

  /// <summary>Size of the form that is a copy of video memory.</summary>
  public const int RawFileSize = 15362;

  /// <summary>Size the packed form unpacks to.</summary>
  public const int UnpackedSize = 9168;

  /// <summary>Offset of the sprite colour in the packed form.</summary>
  public const int PackedSpriteColorOffset = 3025;

  /// <summary>Offset of the sprite mask in the packed form.</summary>
  public const int PackedMaskOffset = 3072;

  /// <summary>Offset of the colour map in the packed form.</summary>
  public const int PackedColorOffset = 6144;

  static string IImageFormatMetadata<ShfXlEditFile>.PrimaryExtension => ".shx";
  static string[] IImageFormatMetadata<ShfXlEditFile>.FileExtensions => [".shx"];
  static ShfXlEditFile IImageFormatReader<ShfXlEditFile>.FromSpan(ReadOnlySpan<byte> data)
    => ShfXlEditReader.FromSpan(data);
  static byte[] IImageFormatWriter<ShfXlEditFile>.ToBytes(ShfXlEditFile file)
    => ShfXlEditWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ShfXlEditFile>.VideoModes => [
    new("SHF-XL", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The picture, unpacked if it was packed.</summary>
  public byte[] Data { get; init; }

  /// <summary>Whether the file is a copy of video memory rather than the editor's own layout.</summary>
  public bool IsRaw { get; init; }

  public static RawImage ToRawImage(ShfXlEditFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x)
      pixels[y * Width + x] = (byte)((file.IsRaw ? _RawPixel(data, x, y) : _PackedPixel(data, x, y)) & 15);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  /// <summary>Builds a picture from any image, sampling it to the 144x168 the editor showed.</summary>
  /// <remarks>
  /// Written in the editor's own layout rather than as a copy of video memory. The two are the same
  /// picture, but the video-memory form spreads a cell across a sprite table, five interleaved
  /// bitmap banks and a correction for the sprites the machine could not place contiguously — none
  /// of which a picture needs to say anything about. The editor's layout is three flat planes, and
  /// with the sprite plane clear every pixel comes from the colour map, which holds a pair of
  /// colours for each eight pixels of each single scanline.
  /// </remarks>
  public static ShfXlEditFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).EnsureFormat(PixelFormat.Rgb24).PixelData;
    var data = new byte[UnpackedSize];
    Span<int> line = stackalloc int[8];

    for (var y = 0; y < Height; ++y)
    for (var column = 0; column < PackedStride; ++column) {
      for (var x = 0; x < 8; ++x) {
        var at = (y * Width + (column << 3) + x) * 3;
        line[x] = Commodore64Graphics.FindNearestColorIndex(rgb[at], rgb[at + 1], rgb[at + 2]);
      }

      var (foreground, background) = _ChoosePair(line);
      var offset = y * PackedStride + column;
      var bits = 0;

      for (var x = 0; x < 8; ++x)
        if (_Distance(line[x], foreground) <= _Distance(line[x], background))
          bits |= 1 << (~x & 7);

      data[PackedMaskOffset + offset] = (byte)bits;
      data[PackedColorOffset + offset] = (byte)((foreground << 4) | background);
    }

    return new() { Data = data, IsRaw = false };
  }

  /// <summary>The two colours that between them describe eight pixels with the least total error.</summary>
  private static (int Foreground, int Background) _ChoosePair(ReadOnlySpan<int> indices) {
    int bestForeground = 0, bestBackground = 0;
    var bestError = long.MaxValue;

    for (var first = 0; first < Commodore64Graphics.ColorCount; ++first)
    for (var second = 0; second <= first; ++second) {
      long error = 0;
      foreach (var index in indices)
        error += Math.Min(_Distance(index, first), _Distance(index, second));

      if (error >= bestError)
        continue;

      bestError = error;
      bestForeground = first;
      bestBackground = second;
    }

    return (bestForeground, bestBackground);
  }

  /// <summary>Squared distance in RGB between two of the machine's colours.</summary>
  private static int _Distance(int left, int right) {
    if (left == right)
      return 0;

    int a = Commodore64Graphics.HexColors[left], b = Commodore64Graphics.HexColors[right];
    int dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
    int dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
    int db = (a & 0xFF) - (b & 0xFF);

    return dr * dr + dg * dg + db * db;
  }

  private static int _RawPixel(ReadOnlySpan<byte> data, int x, int y) {
    var bit = ~x & 7;
    var column = x >> 3;
    var inLine = column / 3;

    // Fifteen sprites cover a line, reused down the screen; the correction accounts for the ones
    // the machine could not place contiguously.
    var sprite = ((y - 1) & 7) * 15 + inLine;
    if (sprite < 105)
      sprite += ((inLine + 1) >> 1) * 3;

    if (((_At(data, 8194 + (sprite << 6) + y % 21 * 3 + column % 3) >> bit) & 1) != 0)
      return data[1003];

    var offset = (y & ~7) * 5 + column;

    return _At(data, 13 + ((y & 7) << 10) + offset)
           >> (((_At(data, 8282 + (offset << 3) + (y & 7)) >> bit) & 1) << 2);
  }

  private static int _PackedPixel(ReadOnlySpan<byte> data, int x, int y) {
    var bit = ~x & 7;
    var offset = y * PackedStride + (x >> 3);

    if (((_At(data, offset) >> bit) & 1) != 0)
      return _At(data, PackedSpriteColorOffset);

    return _At(data, PackedColorOffset + offset)
           >> (((_At(data, PackedMaskOffset + offset) >> bit) & 1) << 2);
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
