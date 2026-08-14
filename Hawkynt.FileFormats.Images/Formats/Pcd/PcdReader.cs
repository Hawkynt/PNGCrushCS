using System;
using System.IO;

namespace FileFormat.Pcd;

/// <summary>Reads Kodak Photo CD images from bytes, streams, or file paths.</summary>
public static class PcdReader {

  public static PcdFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PCD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PcdFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static PcdFile FromSpan(ReadOnlySpan<byte> data) {
    var (width, height, rgb) = ReadPlanes(data, photoYcc: true);

    return new() {
      Width = width,
      Height = height,
      PixelData = rgb,
    };
  }

  /// <summary>Reads the largest size the file holds whole, as RGB.</summary>
  /// <param name="photoYcc">
  /// Whether the stored triple is Photo YCC, which is what a <c>.pcd</c> means by it. A
  /// <c>.pcds</c> is the same container read without that transform, so it says no.
  /// </param>
  /// <remarks>
  /// The container is the same either way — the same preamble, the same magic, the same pyramid at
  /// the same offsets, the same interleave and the same half-resolution chrominance — so both
  /// spellings read it here and only the last step differs.
  /// </remarks>
  internal static (int Width, int Height, byte[] Rgb) ReadPlanes(ReadOnlySpan<byte> data, bool photoYcc) {
    if (data.Length < PcdFile.PreambleSize + PcdFile.Magic.Length)
      throw new InvalidDataException("Data too small for a valid PCD file.");

    if (!data.Slice(PcdFile.PreambleSize, PcdFile.Magic.Length).SequenceEqual(PcdFile.Magic))
      throw new InvalidDataException("Invalid PCD magic at offset 2048: expected \"PCD_IPI\".");

    // The largest resolution the file actually holds is the picture; the smaller ones are previews
    // of it, and returning one of those would be answering a different question than was asked.
    var chosen = -1;
    for (var i = 0; i < PcdFile.Resolutions.Length; ++i) {
      var (width, height, offset) = PcdFile.Resolutions[i];
      if (offset + PcdFile.PlaneBytes(width, height) <= data.Length)
        chosen = i;
    }

    if (chosen < 0)
      throw new InvalidDataException($"A PCD file of {data.Length} bytes holds none of the sizes whole.");

    var (chosenWidth, chosenHeight, chosenOffset) = PcdFile.Resolutions[chosen];

    return (chosenWidth, chosenHeight, _Decode(data[chosenOffset..], chosenWidth, chosenHeight, photoYcc));
  }

  /// <summary>Turns one resolution's planes into RGB.</summary>
  /// <remarks>
  /// The planes are interleaved two rows at a time: two rows of luminance, then one row of each
  /// chrominance at half the width, because the chrominance is stored at half resolution both ways.
  /// Each chrominance row therefore serves the pair of luminance rows above it, and each of its
  /// samples serves two pixels across.
  /// </remarks>
  private static byte[] _Decode(ReadOnlySpan<byte> data, int width, int height, bool photoYcc) {
    var half = width / 2;
    var groupBytes = width * 2 + half * 2;
    var rgb = new byte[width * height * 3];

    var chromaRows = (height + 1) / 2;

    for (var y = 0; y < height; ++y) {
      var luminanceRow = y / 2 * groupBytes + (y & 1) * width;

      for (var x = 0; x < width; ++x) {
        var luminance = _At(data, luminanceRow + x);
        var blue = _Chroma(data, groupBytes, width, half, chromaRows, 0, x, y);
        var red = _Chroma(data, groupBytes, width, half, chromaRows, half, x, y);

        var pixel = rgb.AsSpan((y * width + x) * 3, 3);
        if (photoYcc)
          _ToRgb(luminance, blue, red, pixel);
        else {
          // Taken as they stand: the three planes are the three channels in the order they are
          // stored, so the luminance plane is red and the two chrominance planes are green and
          // blue. Nothing is scaled either, because there is no extended range to fit back into a
          // byte when the file is not claiming one.
          pixel[0] = (byte)luminance;
          pixel[1] = (byte)blue;
          pixel[2] = (byte)red;
        }
      }
    }

    return rgb;
  }

  /// <summary>The extent of the transform's output before it is fitted to a byte.</summary>
  /// <remarks>
  /// Photo YCC carries luminance past white on purpose — the format was built to keep highlights a
  /// negative captured and a screen cannot show — so the matrix runs to about 350 rather than 255.
  /// Clipping that at 255 turns every bright area flat white; the range is fitted instead, which is
  /// what makes a neutral sample come back as the value it went in as.
  /// </remarks>
  internal const double ExtendedRange = 350.0;

  /// <summary>The Photo CD colour transform, which is not the one video uses.</summary>
  /// <remarks>
  /// The two chrominance channels are centred on 156 and 137 rather than on the middle of the range,
  /// because neither is symmetric about grey in this space.
  /// </remarks>
  private static void _ToRgb(int luminance, int blue, int red, Span<byte> rgb) {
    var l = luminance * 1.3584;
    var c1 = (blue - 156) * 2.2179;
    var c2 = (red - 137) * 1.8215;

    rgb[0] = _Fit(l + c2);
    rgb[1] = _Fit(l - 0.4303 * (blue - 156) - 0.9271 * (red - 137));
    rgb[2] = _Fit(l + c1);
  }

  /// <summary>Fits one channel of the extended range into a byte.</summary>
  /// <summary>One chrominance sample, doubled up from the half-resolution plane it comes from.</summary>
  /// <remarks>
  /// The doubling is not a plain repeat and not a centred interpolation either: every second output
  /// takes its stored sample unchanged and the one between takes the mean of that sample and the
  /// next. Repeating instead leaves a blocky edge wherever the colour changes sharply, and centring
  /// the interpolation shifts every sample half a pixel, which is worse than either.
  /// <para/>
  /// The pixel that falls between four stored samples is rounded once, from the four, rather than
  /// twice from a pair of already-rounded means. Both are the same bilinear interpolation and the
  /// nested form is off by one wherever the two roundings go the same way — which is a quarter of
  /// the picture, and is exactly where this used to disagree with ImageMagick.
  /// </remarks>
  private static int _Chroma(
    ReadOnlySpan<byte> data, int groupBytes, int width, int half, int chromaRows, int plane, int x, int y) {
    var topLeft = _ChromaAt(data, groupBytes, width, half, chromaRows, plane, x >> 1, y >> 1);
    var oddX = (x & 1) != 0;
    var oddY = (y & 1) != 0;
    if (!oddX && !oddY)
      return topLeft;

    if (!oddY) {
      var topRight = _ChromaAt(data, groupBytes, width, half, chromaRows, plane, (x >> 1) + 1, y >> 1);

      return (topLeft + topRight + 1) >> 1;
    }

    var bottomLeft = _ChromaAt(data, groupBytes, width, half, chromaRows, plane, x >> 1, (y >> 1) + 1);
    if (!oddX)
      return (topLeft + bottomLeft + 1) >> 1;

    var topRightCorner = _ChromaAt(data, groupBytes, width, half, chromaRows, plane, (x >> 1) + 1, y >> 1);
    var bottomRight = _ChromaAt(data, groupBytes, width, half, chromaRows, plane, (x >> 1) + 1, (y >> 1) + 1);

    return (topLeft + topRightCorner + bottomLeft + bottomRight + 2) >> 2;
  }

  /// <summary>One stored chrominance sample, with the edges of the plane held rather than wrapped.</summary>
  private static int _ChromaAt(
    ReadOnlySpan<byte> data, int groupBytes, int width, int half, int chromaRows, int plane, int column, int row) {
    row = row >= chromaRows ? chromaRows - 1 : row;
    column = column >= half ? half - 1 : column;

    return _At(data, row * groupBytes + width * 2 + plane + column);
  }

  private static byte _Fit(double value) {
    var scaled = value * 255.0 / ExtendedRange;

    return scaled <= 0 ? (byte)0 : scaled >= 255 ? (byte)255 : (byte)(scaled + 0.5);
  }

  private static int _At(ReadOnlySpan<byte> data, int offset) => offset < data.Length ? data[offset] : 0;

  public static PcdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
