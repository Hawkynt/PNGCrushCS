using System;
using FileFormat.Core;

namespace FileFormat.Oric;

/// <summary>In-memory representation of an Oric hi-res graphics screen dump.</summary>
public readonly record struct OricFile : IImageFormatReader<OricFile>, IImageToRawImage<OricFile>, IImageFromRawImage<OricFile>, IImageFormatWriter<OricFile> {

  static string IImageFormatMetadata<OricFile>.PrimaryExtension => ".oric";
  static string[] IImageFormatMetadata<OricFile>.FileExtensions => [".oric", ".tap"];
  static OricFile IImageFormatReader<OricFile>.FromSpan(ReadOnlySpan<byte> data) => OricReader.FromSpan(data);
  static byte[] IImageFormatWriter<OricFile>.ToBytes(OricFile file) => OricWriter.ToBytes(file);
  /// <summary>Always 240.</summary>
  public int Width => 240;

  /// <summary>Always 200.</summary>
  public int Height => 200;

  /// <summary>Raw screen data (40 bytes per row x 200 rows = 8000 bytes). Each byte is either pixel data (bit 6=0: bits 0-5 = 6 pixels MSB-first) or an attribute byte (bit 6=1).</summary>
  public byte[] ScreenData { get; init; }

  private static readonly byte[][] _OricPalette = [
    [0, 0, 0],       // 0 = Black
    [255, 0, 0],     // 1 = Red
    [0, 255, 0],     // 2 = Green
    [255, 255, 0],   // 3 = Yellow
    [0, 0, 255],     // 4 = Blue
    [255, 0, 255],   // 5 = Magenta
    [0, 255, 255],   // 6 = Cyan
    [255, 255, 255], // 7 = White
  ];

  public static RawImage ToRawImage(OricFile file) {
    const int rowBytes = 40;
    const int pixelsPerByte = 6;
    const int width = 240;
    const int height = 200;
    var pixels = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var ink = 7;
      var paper = 0;
      var pixelX = 0;

      for (var col = 0; col < rowBytes; ++col) {
        var b = y * rowBytes + col < file.ScreenData.Length ? file.ScreenData[y * rowBytes + col] : (byte)0;

        if ((b & 0x40) != 0) {
          // Attribute byte
          var colorIndex = b & 0x07;
          if ((b & 0x80) != 0)
            paper = colorIndex;
          else
            ink = colorIndex;

          // Attribute byte produces 6 paper-colored pixels
          for (var p = 0; p < pixelsPerByte && pixelX < width; ++p, ++pixelX) {
            var offset = (y * width + pixelX) * 3;
            var c = _OricPalette[paper];
            pixels[offset] = c[0];
            pixels[offset + 1] = c[1];
            pixels[offset + 2] = c[2];
          }
        } else {
          // Pixel byte: bits 5..0 are 6 pixels, MSB (bit 5) is leftmost
          for (var p = 5; p >= 0 && pixelX < width; --p, ++pixelX) {
            var set = ((b >> p) & 1) != 0;
            var offset = (y * width + pixelX) * 3;
            var c = _OricPalette[set ? ink : paper];
            pixels[offset] = c[0];
            pixels[offset + 1] = c[1];
            pixels[offset + 2] = c[2];
          }
        }
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  /// <summary>Builds a hi-res screen from any picture, sampling it to 240x200.</summary>
  /// <remarks>
  /// The Oric has no attribute memory: ink and paper are changed by bytes standing in the picture
  /// itself, and a byte spent on a colour change draws six pixels of paper where it stands. So a
  /// change is only free where those six pixels were going to be one colour anyway, and the encoder
  /// spends one exactly there — a group of six that is a single colour becomes a paper change to
  /// that colour, which draws it and leaves the new paper set for what follows.
  /// <para/>
  /// Where a group needs two colours the current pair cannot show, something has to give. The
  /// commonest of the group's colours is taken as the new paper, which draws that group in it and
  /// costs the pixels that wanted the other. Each row starts afresh with white ink on black paper,
  /// as the hardware does.
  /// </remarks>
  public static OricFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    const int width = 240;
    const int height = 200;
    const int rowBytes = 40;
    const int pixelsPerByte = 6;

    var rgb = image.SampleTo(width, height).EnsureFormat(PixelFormat.Rgb24).PixelData;
    var screenData = new byte[rowBytes * height];
    var line = new int[width];

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        line[x] = _NearestColor(rgb[at], rgb[at + 1], rgb[at + 2]);
      }

      var ink = 7;
      var paper = 0;

      for (var col = 0; col < rowBytes; ++col) {
        var start = col * pixelsPerByte;
        var at = y * rowBytes + col;

        if (_Fits(line, start, ink, paper)) {
          // A group that is all paper draws itself whatever the attribute byte does to the ink, so
          // that is where an ink change costs nothing — and it is the only place it does not.
          var wanted = _Uniform(line, start, paper) ? _WantedInk(line, start + pixelsPerByte, width, ink, paper) : -1;
          if (wanted >= 0) {
            ink = wanted;
            screenData[at] = (byte)(0x40 | ink);
            continue;
          }

          var value = 0;
          if (ink != paper)
            for (var p = 0; p < pixelsPerByte; ++p)
              if (line[start + p] == ink)
                value |= 1 << (5 - p);

          screenData[at] = (byte)value;
          continue;
        }

        // Nothing else can be drawn correctly here: an attribute byte paints its own six pixels in
        // the paper it sets, so the group's commonest colour becomes the paper and the rest is lost.
        paper = _Commonest(line, start);
        screenData[at] = (byte)(0xC0 | paper);
      }
    }

    return new() { ScreenData = screenData };
  }

  /// <summary>Whether six pixels can be drawn with the ink and paper currently set.</summary>
  private static bool _Fits(int[] line, int start, int ink, int paper) {
    for (var p = 0; p < 6; ++p)
      if (line[start + p] != ink && line[start + p] != paper)
        return false;

    return true;
  }

  private static bool _Uniform(int[] line, int start, int color) {
    for (var p = 0; p < 6; ++p)
      if (line[start + p] != color)
        return false;

    return true;
  }

  /// <summary>The ink the next six pixels want and cannot have, or -1 when they need no change.</summary>
  private static int _WantedInk(int[] line, int start, int width, int ink, int paper) {
    if (start + 6 > width)
      return -1;

    Span<int> counts = stackalloc int[8];
    for (var p = 0; p < 6; ++p)
      if (line[start + p] != paper)
        ++counts[line[start + p]];

    var best = -1;
    for (var color = 0; color < counts.Length; ++color)
      if (counts[color] > 0 && color != ink && (best < 0 || counts[color] > counts[best]))
        best = color;

    return best;
  }

  private static int _Commonest(int[] line, int start) {
    Span<int> counts = stackalloc int[8];
    for (var p = 0; p < 6; ++p)
      ++counts[line[start + p]];

    var best = 0;
    for (var color = 1; color < counts.Length; ++color)
      if (counts[color] > counts[best])
        best = color;

    return best;
  }

  /// <summary>Which of the eight colours a pixel is nearest.</summary>
  private static int _NearestColor(byte red, byte green, byte blue) {
    var best = 0;
    var bestCost = int.MaxValue;

    for (var i = 0; i < _OricPalette.Length; ++i) {
      int dr = red - _OricPalette[i][0], dg = green - _OricPalette[i][1], db = blue - _OricPalette[i][2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = i;
    }

    return best;
  }

}
