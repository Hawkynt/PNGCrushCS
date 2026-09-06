using System;

namespace FileFormat.IffDctv;

/// <summary>
/// The DCTV composite-video encoding itself: the signature line, the colour map, and the conversion
/// between an RGB picture and the stream of 8-bit composite samples a DCTV unit reconstructs.
/// </summary>
/// <remarks>
/// DCTV is not a raster format. A DCTV picture is an ordinary Amiga ILBM whose pixel values are not
/// colours at all but a digitised composite television waveform: pairs of adjacent 4-bit pixels form
/// one 8-bit sample, the average of two neighbouring samples is luminance, and their second
/// difference is a chrominance subcarrier whose sign alternates. The external decoder box turned that
/// back into colour. Nothing about this is documented — Digital Creations never published it — so the
/// constants here are copied from genuine DCTV files rather than derived: the colour map and the
/// signature line are byte-identical to <c>XZGIRL.DCTV</c>, <c>sauvage.dctv</c>, <c>flowers.DCTV</c>
/// and <c>landscapes.DCTV</c>, and the reconstruction arithmetic follows RECOIL's
/// <c>RECOIL_DecodeDctv</c>, which this implementation reproduces pixel-for-pixel on all four.
/// <para/>
/// Because luminance is the average of two samples and chrominance is carried at half the horizontal
/// and half the vertical rate, the format cannot hold a picture exactly. That is the encoding, not a
/// shortcut taken here.
/// </remarks>
internal static class IffDctvCodec {

  /// <summary>Pixels of the signature line that carry the synchronisation sequence.</summary>
  internal const int SignatureLength = 256;

  /// <summary>Distance from the right edge at which the signature sequence is repeated.</summary>
  private const int _TRAILING_SIGNATURE_OFFSET = 264;

  /// <summary>Narrowest picture that can carry both copies of the signature sequence.</summary>
  internal const int MinimumWidth = _TRAILING_SIGNATURE_OFFSET;

  /// <summary>Widest picture the reconstruction's chrominance delay line can follow.</summary>
  internal const int MaximumWidth = 2048;

  /// <summary>Seed of the signature sequence generator.</summary>
  private const int _SIGNATURE_SEED = 125;

  /// <summary>Feedback mask of the signature sequence generator.</summary>
  private const int _SIGNATURE_TAPS = 390;

  /// <summary>
  /// The DCTV colour map, copied verbatim from the CMAP chunk of genuine DCTV files. These are not
  /// picture colours; they are the sixteen analogue levels the Amiga's video DAC had to emit for the
  /// DCTV box to read a 4-bit sample nibble off the composite output. Entry <c>c</c> encodes the
  /// nibble <c>c</c>, spread across the bits RECOIL recovers as
  /// <c>blue bit 4, red bit 7, blue bit 7, green bit 7</c>.
  /// </summary>
  internal static readonly byte[] ColourMap = [
    0x00, 0x00, 0x00, 0x77, 0x88, 0x66, 0x77, 0x77, 0x88, 0x77, 0x88, 0x88,
    0x88, 0x77, 0x66, 0x88, 0x88, 0x66, 0x88, 0x77, 0x88, 0x88, 0x88, 0x88,
    0x00, 0x00, 0x11, 0x77, 0x88, 0x77, 0x77, 0x77, 0x99, 0x77, 0x88, 0x99,
    0x88, 0x77, 0x77, 0x88, 0x88, 0x77, 0x88, 0x77, 0x99, 0x88, 0x88, 0x99,
  ];

  /// <summary>Number of bitplanes a written DCTV picture uses. Three-plane files exist and read back
  /// fine, but they drop the bottom bit of every sample, so nothing is gained by writing one.</summary>
  internal const int WrittenPlaneCount = 4;

  /// <summary>CAMG viewport mode of a written picture: HIRES and LACE, the 1:1 pixel DCTV screen.</summary>
  internal const uint WrittenViewportMode = 0x8004;

  /// <summary>
  /// The synchronisation sequence a DCTV unit looks for in the top-left corner. Pixel 0 carries zero;
  /// every later pixel carries the complement of a shift register bit.
  /// </summary>
  internal static void FillSignature(Span<byte> bits) {
    bits[0] = 0;
    var register = _SIGNATURE_SEED;
    for (var i = 1; i < bits.Length; ++i) {
      bits[i] = (byte)(1 - (register & 1));
      if ((register & 1) != 0)
        register ^= _SIGNATURE_TAPS;

      register >>= 1;
    }
  }

  /// <summary>
  /// Writes one complete signature line as 4-bit sample nibbles. The sequence appears twice — once at
  /// the left edge and once ending eight pixels short of the right edge — and every other bit of the
  /// line is zero. All four genuine files agree on this exactly.
  /// </summary>
  internal static void WriteSignatureLine(Span<byte> line) {
    line.Clear();
    Span<byte> bits = stackalloc byte[SignatureLength];
    FillSignature(bits);

    var width = line.Length;
    for (var i = 0; i < SignatureLength; ++i) {
      var nibble = (byte)(bits[i] != 0 ? 8 : 0);
      line[i] = nibble;
      line[width - _TRAILING_SIGNATURE_OFFSET + i] = nibble;
    }
  }

  /// <summary>Whether <paramref name="line"/> opens with the DCTV synchronisation sequence.</summary>
  internal static bool IsSignatureLine(ReadOnlySpan<byte> line) {
    if (line.Length < SignatureLength)
      return false;

    Span<byte> bits = stackalloc byte[SignatureLength];
    FillSignature(bits);
    for (var x = 0; x < SignatureLength; ++x)
      if ((line[x] >> 3) != bits[x])
        return false;

    return true;
  }

  /// <summary>
  /// Recovers the 4-bit sample nibble a palette entry stands for. RECOIL reads it out of four
  /// scattered bits of the colour rather than trusting the index, because the DCTV box read analogue
  /// levels and any palette producing those levels is a valid DCTV palette.
  /// </summary>
  internal static byte NibbleFromColour(int rgb)
    => (byte)((rgb >> 1 & 8) | (rgb >> 21 & 4) | (rgb >> 6 & 2) | (rgb >> 15 & 1));

  private static int _Clamp(int value) => value <= 0 ? 0 : value >= 255 ? 255 : value;

  /// <summary>
  /// Reconstructs the picture from a grid of 4-bit sample nibbles, signature lines included. This is
  /// the arithmetic RECOIL performs, and it agrees with <c>recoil2png</c> pixel-for-pixel on every
  /// genuine DCTV file tested.
  /// </summary>
  internal static byte[] Decode(ReadOnlySpan<byte> samples, int width, int contentHeight, bool interlaced) {
    var interlace = interlaced ? 1 : 0;
    var height = contentHeight - (interlaced ? 2 : 1);
    var rgb = new byte[width * height * 3];
    var chroma = new int[MaximumWidth];

    for (var y = 0; y < height; ++y) {
      var odd = (y >> interlace) & 1;
      var source = (y + (interlaced ? 2 : 1)) * width;
      var colour = 0;
      var previous = 0;
      var older = 0;

      for (var x = 0; x < width; ++x) {
        if ((x & 1) == odd) {
          var n = x + 1 < width ? Spread(samples[source + x]) << 1 | Spread(samples[source + x + 1]) : 0;
          var luma = (previous + n) >> 1;
          luma = luma <= 64 ? 0 : luma >= 224 ? 255 : (luma - 64) * 8 / 5;

          var u = n + older - (previous << 1);
          if (u < 0)
            u += 3;

          u >>= 2;
          if (((x + 1) & 2) == 0)
            u = -u;

          // Each line carries only one of the two chrominance components and borrows the other from
          // the line two above it in the same field, which is where the vertical colour rate goes.
          var slot = (x & ~1) | (y & interlace);
          var v = y > interlace ? chroma[slot] : 0;
          chroma[slot] = u;
          if (odd == 0)
            (u, v) = (v, u);

          older = previous;
          previous = n;

          colour = _Clamp(luma + v * 4655 / 2560) << 16
            | _Clamp(luma - (v * 2372 + u * 1616) / 2560) << 8
            | _Clamp(luma + u * 8286 / 2560);
        }

        var target = (y * width + x) * 3;
        rgb[target] = (byte)(colour >> 16);
        rgb[target + 1] = (byte)(colour >> 8);
        rgb[target + 2] = (byte)colour;
      }
    }

    return rgb;
  }

  /// <summary>Spreads a 4-bit sample nibble into the bit positions the composite sample uses.</summary>
  internal static int Spread(byte nibble)
    => (nibble & 8) << 3 | (nibble & 4) << 2 | (nibble & 2) << 1 | (nibble & 1);

  /// <summary>
  /// Turns an RGB picture into the grid of 4-bit sample nibbles a DCTV unit expects, signature lines
  /// included. Luminance goes into the sample level, chrominance into an alternating excursion around
  /// it; the excursion is limited to whatever headroom the level leaves, which is what makes bright
  /// saturated colour desaturate rather than clip.
  /// </summary>
  internal static byte[] Encode(ReadOnlySpan<byte> rgb, int width, int height) {
    var contentHeight = height + 2;
    var samples = new byte[width * contentHeight];
    WriteSignatureLine(samples.AsSpan(0, width));
    samples.AsSpan(0, width).CopyTo(samples.AsSpan(width, width));

    var luma = new double[width * height];
    var chromaU = new double[width * height];
    var chromaV = new double[width * height];
    for (var i = 0; i < width * height; ++i) {
      double r = rgb[i * 3];
      double g = rgb[i * 3 + 1];
      double b = rgb[i * 3 + 2];
      var y = (299.0 * r + 587.0 * g + 114.0 * b) / 1000.0;
      luma[i] = y;
      chromaV[i] = (r - y) * 2560.0 / 4655.0;
      chromaU[i] = (b - y) * 2560.0 / 8286.0;
    }

    for (var y = 0; y < height; ++y) {
      var odd = y >> 1 & 1;
      var chroma = odd != 0 ? chromaU : chromaV;
      // The chrominance written on this line is read back by this line and by the one two below it,
      // which is where the format's vertical colour resolution goes.
      var partner = y + 2 < height ? y + 2 : y;
      var target = (y + 2) * width;

      for (int x = odd, k = 0; x < width; x += 2, ++k) {
        var right = x + 1 < width ? x + 1 : x;
        var level = 64.0 + (luma[y * width + x] + luma[y * width + right]) / 2.0 * 5.0 / 8.0;
        var headroom = Math.Min(level, 255.0 - level);
        var excursion = (chroma[y * width + x] + chroma[y * width + right]
          + chroma[partner * width + x] + chroma[partner * width + right]) / 4.0;
        excursion = Math.Clamp(excursion, -headroom, headroom);

        // The subcarrier alternates sample by sample, and the decoder flips its sign on a two-pixel
        // cycle; this is the phase that makes the two cancel so the recovered chroma keeps its sign.
        var evenSample = (k & 1) == 0;
        var sign = evenSample == (odd == 0) ? -1.0 : 1.0;
        var n = (int)Math.Round(level + sign * excursion);
        n = n < 0 ? 0 : n > 255 ? 255 : n;

        // The sample straddles two pixels: the left one carries its odd bits, the right one its even.
        samples[target + x] = (byte)((n >> 7 & 1) << 3 | (n >> 5 & 1) << 2 | (n >> 3 & 1) << 1 | (n >> 1 & 1));
        if (x + 1 < width)
          samples[target + x + 1] = (byte)((n >> 6 & 1) << 3 | (n >> 4 & 1) << 2 | (n >> 2 & 1) << 1 | (n & 1));
      }
    }

    return samples;
  }
}
