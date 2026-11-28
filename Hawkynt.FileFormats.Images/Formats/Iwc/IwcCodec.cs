using System;
using System.IO;

namespace FileFormat.Iwc;

/// <summary>The IWC subband pyramid: geometry, the integer lifting wavelet, quantisation and colour.</summary>
/// <remarks>
/// <para>
/// A picture is decomposed into <c>levels</c> nodes. Node 0 covers the whole picture; each further
/// node covers the previous node's approximation, which is the previous size halved and rounded
/// down. A node of <c>w × h</c> samples splits into an approximation band of
/// <c>⌊w/2⌋ × ⌊h/2⌋</c> (the odd rows and columns), H of <c>⌈w/2⌉ × ⌊h/2⌋</c> (odd rows, even
/// columns), V of <c>⌊w/2⌋ × ⌈h/2⌉</c> and D of <c>⌈w/2⌉ × ⌈h/2⌉</c>.
/// </para>
/// <para>
/// The wavelet is a reversible integer lifting pair with mirrored ends, in Java's truncating integer
/// arithmetic: the even samples are predicted from their odd neighbours, <c>d = x − (l + r) / 2</c>,
/// and the odd samples updated from the even details, <c>s = x + (l + r + 2) / 4</c>. Synthesis undoes
/// the update and then the prediction, along every row and then along every column; analysis does
/// the reverse, columns first.
/// </para>
/// <para>
/// Every coefficient travels as one byte value 0..252: an approximation coefficient is
/// <c>minimum + value × step</c>, a detail coefficient <c>(value − 128) × step</c>, each evaluated in
/// single precision and truncated. The three channels are Y, Cb and Cr of JPEG's YCbCr without the
/// 128 offset.
/// </para>
/// </remarks>
internal static class IwcCodec {

  private const int _DETAIL_ZERO = 128;

  /// <summary>Size of every node of the pyramid.</summary>
  public static (int Width, int Height)[] Nodes(int width, int height, int levels) {
    var nodes = new (int, int)[levels];
    for (var level = 0; level < levels; ++level, width /= 2, height /= 2)
      nodes[level] = (width, height);
    return nodes;
  }

  /// <summary>How many byte values the payload expands to.</summary>
  public static long SymbolCount(int width, int height, int levels) {
    long count = 0;
    var nodes = Nodes(width, height, levels);
    foreach (var (w, h) in nodes)
      count += (long)(w - w / 2) * (h / 2) + (long)(w / 2) * (h - h / 2) + (long)(w - w / 2) * (h - h / 2);

    var (lastWidth, lastHeight) = nodes[^1];
    count += (long)(lastWidth / 2) * (lastHeight / 2);
    return 3 * count;
  }

  /// <summary>Rebuilds the RGB picture from the header and the expanded byte values.</summary>
  public static byte[] Decode(IwcHeader header, byte[] symbols) {
    var nodes = Nodes(header.Width, header.Height, header.Levels);
    var position = 0;

    var (lastWidth, lastHeight) = nodes[^1];
    var approximationWidth = lastWidth / 2;
    var approximationHeight = lastHeight / 2;
    var approximation = new short[3][];
    for (var channel = 0; channel < 3; ++channel)
      approximation[channel] = new short[approximationWidth * approximationHeight];

    for (var x = 0; x < approximationWidth; ++x)
      for (var y = 0; y < approximationHeight; ++y)
        for (var channel = 0; channel < 3; ++channel)
          approximation[channel][y * approximationWidth + x] = (short)JavaInt(header.ApproximationMinima[channel] + symbols[position++] * header.ApproximationScales[channel]);

    var horizontal = new short[header.Levels][][];
    var vertical = new short[header.Levels][][];
    var diagonal = new short[header.Levels][][];
    for (var level = header.Levels - 1; level >= 0; --level) {
      var (w, h) = nodes[level];
      int ax = w / 2, ay = h / 2, hx = w - ax, vy = h - ay;
      var scales = header.LevelScales[level];
      horizontal[level] = new short[3][];
      vertical[level] = new short[3][];
      diagonal[level] = new short[3][];

      for (var channel = 0; channel < 3; ++channel)
        horizontal[level][channel] = _ReadDetail(symbols, ref position, hx, ay, columnMajor: true, scales.Horizontal[channel]);
      for (var channel = 0; channel < 3; ++channel)
        vertical[level][channel] = _ReadDetail(symbols, ref position, ax, vy, columnMajor: false, scales.Vertical[channel]);
      for (var channel = 0; channel < 3; ++channel)
        diagonal[level][channel] = _ReadDetail(symbols, ref position, hx, vy, columnMajor: true, scales.Diagonal[channel]);
    }

    var planes = new short[3][];
    for (var channel = 0; channel < 3; ++channel) {
      var current = approximation[channel];
      for (var level = header.Levels - 1; level >= 0; --level) {
        var (w, h) = nodes[level];
        current = Synthesise(current, horizontal[level][channel], vertical[level][channel], diagonal[level][channel], w, h);
      }

      planes[channel] = current;
    }

    var rgb = new byte[header.Width * header.Height * 3];
    for (var i = 0; i < header.Width * header.Height; ++i) {
      var (red, green, blue) = ToRgb(planes[0][i], planes[1][i], planes[2][i]);
      rgb[i * 3] = red;
      rgb[i * 3 + 1] = green;
      rgb[i * 3 + 2] = blue;
    }

    return rgb;
  }

  /// <summary>The applet's colour conversion: double precision from the 16-bit samples, truncated, wrapped to 16 bits, clamped.</summary>
  public static (byte Red, byte Green, byte Blue) ToRgb(short luma, short blueDifference, short redDifference) {
    double y = luma, cb = blueDifference, cr = redDifference;
    return (
      _Clamp((short)JavaInt(y + 1.402 * cr)),
      _Clamp((short)JavaInt(y - 0.34413 * cb - 0.71414 * cr)),
      _Clamp((short)JavaInt(y + 1.772 * cb)));
  }

  /// <summary>Java's float-to-int conversion: truncation, saturating, NaN to zero.</summary>
  public static int JavaInt(double value)
    => double.IsNaN(value) ? 0 : value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;

  private static byte _Clamp(short value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

  private static short[] _ReadDetail(byte[] symbols, ref int position, int width, int height, bool columnMajor, float scale) {
    var band = new short[width * height];
    var outer = columnMajor ? width : height;
    var inner = columnMajor ? height : width;
    for (var o = 0; o < outer; ++o)
      for (var i = 0; i < inner; ++i) {
        var (x, y) = columnMajor ? (o, i) : (i, o);
        band[y * width + x] = (short)JavaInt((symbols[position++] - _DETAIL_ZERO) * scale);
      }

    return band;
  }

  /// <summary>Interleaves the four bands of a <paramref name="width"/> × <paramref name="height"/> node and undoes the lifting.</summary>
  public static short[] Synthesise(short[] approximation, short[] horizontal, short[] vertical, short[] diagonal, int width, int height) {
    int ax = width / 2, ay = height / 2, hx = width - ax, vy = height - ay;
    var picture = new short[width * height];
    for (var r = 0; r < ay; ++r) {
      for (var c = 0; c < ax; ++c)
        picture[(2 * r + 1) * width + 2 * c + 1] = approximation[r * ax + c];
      for (var c = 0; c < hx; ++c)
        picture[(2 * r + 1) * width + 2 * c] = horizontal[r * hx + c];
    }

    for (var r = 0; r < vy; ++r) {
      for (var c = 0; c < ax; ++c)
        picture[2 * r * width + 2 * c + 1] = vertical[r * ax + c];
      for (var c = 0; c < hx; ++c)
        picture[2 * r * width + 2 * c] = diagonal[r * hx + c];
    }

    var line = new int[Math.Max(width, height)];
    for (var row = 0; row < height; ++row) {
      for (var x = 0; x < width; ++x)
        line[x] = picture[row * width + x];
      _Unlift(line, width);
      for (var x = 0; x < width; ++x)
        picture[row * width + x] = (short)line[x];
    }

    for (var column = 0; column < width; ++column) {
      for (var y = 0; y < height; ++y)
        line[y] = picture[y * width + column];
      _Unlift(line, height);
      for (var y = 0; y < height; ++y)
        picture[y * width + column] = (short)line[y];
    }

    return picture;
  }

  /// <summary>Splits a <paramref name="width"/> × <paramref name="height"/> plane into its four bands: the exact inverse of <see cref="Synthesise"/>.</summary>
  public static (int[] Approximation, int[] Horizontal, int[] Vertical, int[] Diagonal) Analyse(int[] picture, int width, int height) {
    var work = (int[])picture.Clone();
    var line = new int[Math.Max(width, height)];
    for (var column = 0; column < width; ++column) {
      for (var y = 0; y < height; ++y)
        line[y] = work[y * width + column];
      _Lift(line, height);
      for (var y = 0; y < height; ++y)
        work[y * width + column] = line[y];
    }

    for (var row = 0; row < height; ++row) {
      Array.Copy(work, row * width, line, 0, width);
      _Lift(line, width);
      Array.Copy(line, 0, work, row * width, width);
    }

    int ax = width / 2, ay = height / 2, hx = width - ax, vy = height - ay;
    var approximation = new int[ax * ay];
    var horizontal = new int[hx * ay];
    var vertical = new int[ax * vy];
    var diagonal = new int[hx * vy];
    for (var r = 0; r < ay; ++r) {
      for (var c = 0; c < ax; ++c)
        approximation[r * ax + c] = work[(2 * r + 1) * width + 2 * c + 1];
      for (var c = 0; c < hx; ++c)
        horizontal[r * hx + c] = work[(2 * r + 1) * width + 2 * c];
    }

    for (var r = 0; r < vy; ++r) {
      for (var c = 0; c < ax; ++c)
        vertical[r * ax + c] = work[2 * r * width + 2 * c + 1];
      for (var c = 0; c < hx; ++c)
        diagonal[r * hx + c] = work[2 * r * width + 2 * c];
    }

    return (approximation, horizontal, vertical, diagonal);
  }

  /// <summary>Neighbour of <paramref name="index"/> on one side, mirrored back inside a line of <paramref name="length"/>.</summary>
  private static int _Left(int index) => index > 0 ? index - 1 : index + 1;

  private static int _Right(int index, int length) => index + 1 < length ? index + 1 : index - 1;

  /// <summary>Synthesis along one line of at least two samples: undo the update on the odd samples, then the prediction on the even ones.</summary>
  private static void _Unlift(int[] line, int length) {
    if (length < 2)
      throw new InvalidDataException("IWC lifting needs at least two samples per line.");

    for (var odd = 1; odd < length; odd += 2)
      line[odd] = (short)(line[odd] - (line[odd - 1] + line[_Right(odd, length)] + 2) / 4);
    for (var even = 0; even < length; even += 2)
      line[even] = (short)(line[even] + (line[_Left(even)] + line[_Right(even, length)]) / 2);
  }

  /// <summary>Analysis along one line: predict the even samples, then update the odd ones.</summary>
  private static void _Lift(int[] line, int length) {
    for (var even = 0; even < length; even += 2)
      line[even] -= (line[_Left(even)] + line[_Right(even, length)]) / 2;
    for (var odd = 1; odd < length; odd += 2)
      line[odd] += (line[odd - 1] + line[_Right(odd, length)] + 2) / 4;
  }
}
