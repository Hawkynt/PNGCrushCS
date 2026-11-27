using System;
using System.IO;

namespace FileFormat.WhyPic;

/// <summary>Reads WhyPic (.ypc) pictures from bytes, streams, or file paths.</summary>
public static class WhyPicReader {

  /// <summary>The size, version and colour count, which is all the header holds.</summary>
  internal readonly record struct Header(int Width, int Height, int Colors, WhyPicLayout Layout);

  /// <summary>The scale a single size field is spread evenly over: twelve bits.</summary>
  internal const int SizeScale = 4096;

  /// <summary>The scale an interpolation weight is stored on.</summary>
  internal const int WeightScale = 65536;

  /// <summary>The scale the palette's own interpolation weight is stored on.</summary>
  internal const int PaletteWeightScale = 256;

  public static WhyPicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WhyPic file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WhyPicFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static WhyPicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Decodes the header alone, or returns <c>null</c> when the bytes are not a WhyPic picture.</summary>
  internal static Header? ReadHeader(ReadOnlySpan<byte> data) {
    if (data.Length < 6 || data[0] != 'Y' || data[1] != 'p')
      return null;

    try {
      var decoder = new WhyPicRangeDecoder(data, 2);
      return _ReadHeader(ref decoder);
    } catch (InvalidDataException) {
      return null;
    } catch (NotSupportedException) {
      return null;
    }
  }

  private static Header _ReadHeader(ref WhyPicRangeDecoder decoder) {
    var width = decoder.Uniform(SizeScale);
    var height = decoder.Uniform(SizeScale);
    if (width == 0 || height == 0)
      throw new InvalidDataException($"WhyPic picture states a size of {width}x{height}.");

    // The version is a prefix code: 0 for 1.0, 100 for 1.1, and 101, 110 and 111 held back.
    var layout = WhyPicLayout.Version10;
    if (decoder.Uniform(2) != 0) {
      var second = decoder.Uniform(2);
      var third = decoder.Uniform(2);
      if (second != 0 || third != 0)
        throw new NotSupportedException($"WhyPic version code 1{second}{third} is reserved for a format that was never published.");
      layout = WhyPicLayout.Version11;
    }

    var colors = decoder.Uniform(2) == 0 ? 16 : 256;
    return new(width, height, colors, layout);
  }

  public static WhyPicFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 2 || data[0] != 'Y' || data[1] != 'p')
      throw new InvalidDataException("Not a WhyPic picture: it does not start with \"Yp\".");

    var mirror = new WhyPicRangeEncoder();
    var decoder = new WhyPicRangeDecoder(data, 2, mirror);
    var header = _ReadHeader(ref decoder);
    var colors = header.Colors;

    var compressed = decoder.Uniform(2) == 0;
    var bgr = compressed ? _ReadModelledPalette(ref decoder, colors) : _ReadPlainPalette(ref decoder, colors);

    var weights = new double[WhyPicContextModel.MaxOrder + 1][];
    for (var order = 0; order <= WhyPicContextModel.MaxOrder; ++order) {
      var row = new double[order + 1];
      var rest = 1.0;
      for (var i = 0; i < order; ++i) {
        row[i] = (double)decoder.Uniform(WeightScale) / WeightScale;
        rest -= row[i];
      }

      if (rest < 0)
        throw new InvalidDataException($"WhyPic interpolation weights for order {order} add up to more than one.");
      row[order] = rest;
      weights[order] = row;
    }

    // Version 1.1 renumbers a 16-colour picture's indices so the likeliest colours sit where the
    // decoder looks first; the table is a permutation, sent as successive choices from what is left.
    byte[]? renumber = null;
    if (header.Layout == WhyPicLayout.Version11 && colors == 16) {
      renumber = new byte[16];
      Span<byte> left = stackalloc byte[16];
      for (var i = 0; i < 16; ++i)
        left[i] = (byte)i;
      for (var i = 0; i < 15; ++i) {
        var pick = decoder.Uniform(16 - i);
        renumber[i] = left[pick];
        left[(pick + 1)..(16 - i)].CopyTo(left[pick..]);
      }
      renumber[15] = left[0];
    }

    var pixels = _DecodePixels(ref decoder, header, weights);

    // An arithmetic code has no length and no check: a file that lost its end, or had bits changed,
    // still decodes — to a different picture. But each picture has exactly one code, the one its
    // intervals produce, so the decoded symbols are coded again as they are read and the result
    // must be what the file holds. Anything after that code is ignored, as WhyPic ignores it.
    var canonical = mirror.Finish();
    if (canonical.Length > data.Length - 2)
      throw new InvalidDataException("WhyPic stream is truncated: the picture's code runs past the end of the file.");
    if (!data.Slice(2, canonical.Length).SequenceEqual(canonical))
      throw new InvalidDataException("WhyPic stream is corrupt: the decoded picture does not code back to the bytes it was read from.");

    if (renumber != null)
      for (var i = 0; i < pixels.Length; ++i)
        pixels[i] = renumber[pixels[i]];

    var palette = new byte[colors * 3];
    for (var i = 0; i < colors; ++i) {
      palette[i * 3] = bgr[i * 3 + 2];
      palette[i * 3 + 1] = bgr[i * 3 + 1];
      palette[i * 3 + 2] = bgr[i * 3];
    }

    return new() {
      Width = header.Width,
      Height = header.Height,
      Colors = colors,
      Layout = header.Layout,
      PaletteCompressed = compressed,
      Palette = palette,
      PixelData = pixels,
    };
  }

  private static byte[] _ReadPlainPalette(ref WhyPicRangeDecoder decoder, int colors) {
    var bgr = new byte[colors * 3];
    for (var i = 0; i < bgr.Length; ++i)
      bgr[i] = (byte)decoder.Uniform(256);
    return bgr;
  }

  private static byte[] _ReadModelledPalette(ref WhyPicRangeDecoder decoder, int colors) {
    var lambda = (double)decoder.Uniform(PaletteWeightScale) / PaletteWeightScale;
    var bgr = new byte[colors * 3];
    Span<int> counts = stackalloc int[256];
    counts.Clear();

    for (var i = 0; i < bgr.Length; ++i) {
      var target = decoder.Target(WhyPicContextModel.ProbabilityScale);
      var below = 0;
      for (var value = 0; value < 256; ++value) {
        var (low, high) = WhyPicPaletteModel.Interval(lambda, i, value, below, counts[value]);

        // The last value takes whatever is left, the end of the line included.
        if (value == 255 || (low <= target && target < high)) {
          bgr[i] = (byte)value;
          decoder.Narrow(low, high, WhyPicContextModel.ProbabilityScale);
          break;
        }

        below += counts[value];
      }

      ++counts[bgr[i]];
    }

    return bgr;
  }

  private static byte[] _DecodePixels(ref WhyPicRangeDecoder decoder, Header header, double[][] weights) {
    var (width, height, colors) = (header.Width, header.Height, header.Colors);
    var count = width * height;
    var coded = new byte[count];
    var model = new WhyPicContextModel(colors);
    var uniform = 1.0 / colors;
    var frontLoaded = header.Layout == WhyPicLayout.Version11;
    Span<byte> neighbours = stackalloc byte[8];

    for (var at = 0; at < count; ++at) {
      WhyPicNeighbourhood.Gather(coded, at, width, neighbours);
      model.Lookup(neighbours);
      var lambda = weights[model.Order];
      var target = decoder.Target(WhyPicContextModel.ProbabilityScale);
      var max = model.MostLikely();

      int symbol, low, high;
      var settled = false;
      symbol = low = high = 0;

      if (frontLoaded) {
        // The predicted colour occupies the front of the line; its upper end is computed before the
        // counts are reordered, which comes to the same number.
        var p = lambda[0] / colors;
        for (var g = 0; g < model.Order; ++g)
          p += lambda[g + 1] * model.Frequencies[g][max] / model.Totals[g];
        high = (int)(p * WhyPicContextModel.ProbabilityScale);
        if (target < high) {
          settled = true;
          symbol = 0;
          low = 0;
        } else
          model.SwapToFront(max);
      }

      if (!settled) {
        model.Accumulate();
        if (target == WhyPicContextModel.ProbabilityScale) {
          // The very top of the line belongs to the last colour, whose interval is closed there.
          symbol = colors - 1;
          low = model.Cumulative(lambda, symbol, uniform);
          high = WhyPicContextModel.ProbabilityScale;
        } else {
          var lo = 0;
          var hi = colors - 1;
          while (lo < hi) {
            var mid = (lo + hi + 1) >> 1;
            if (model.Cumulative(lambda, mid, uniform) <= target)
              lo = mid;
            else
              hi = mid - 1;
          }

          symbol = lo;
          low = model.Cumulative(lambda, symbol, uniform);
          high = model.Cumulative(lambda, symbol + 1, uniform);
          if (target >= high)
            throw new InvalidDataException("WhyPic stream is corrupt: the code value lies beyond every colour.");
        }
      }

      if (frontLoaded)
        symbol = symbol == max ? 0 : symbol == 0 ? max : symbol;

      decoder.Narrow(low, high, WhyPicContextModel.ProbabilityScale);
      coded[at] = (byte)symbol;

      // Once the first dot is known, everything off the top and left edge is that colour, so the
      // context it is counted under is that colour eight times.
      if (at == 0)
        neighbours.Fill((byte)symbol);
      model.Update(neighbours, symbol);
    }

    return coded;
  }
}
