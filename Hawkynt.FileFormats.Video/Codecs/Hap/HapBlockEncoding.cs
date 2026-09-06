using System;

namespace FileFormat.Codecs.Hap;

/// <summary>
/// DXT1/BC1, DXT5/BC3 and Scaled YCoCg DXT5 block compression: sixteen RGBA pixels priced down to
/// two endpoints and a two- or three-bit index apiece.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/texturedspenc.c</c>, copyright (c) 2015 Vittorio Giovara and
/// based there on public domain code by Fabian Giesen, Sean Barrett and Yann Collet. That file is
/// under the MIT licence rather than FFmpeg's usual LGPL — see
/// <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> beside this one — so it is taken whole rather than only
/// consulted, which is what makes the output here comparable to ffmpeg's own <c>hap</c> encoder byte
/// for byte instead of merely equivalent.
/// <para/>
/// <b>Why the endpoint arithmetic here is not the decoder's.</b> The encoder's own
/// <see cref="_Expand5"/> and <see cref="_Expand6"/> are bit replication — <c>(v &lt;&lt; 3) | (v
/// &gt;&gt; 2)</c> written out — where <see cref="HapBlockDecoding"/>'s tables round instead, and the
/// two genuinely disagree at nine of the thirty-two five-bit values. That is not a mistake in either:
/// the encoder's tables are only ever used to decide which of a block's four colours each pixel is
/// nearest, a decision a value or two of slack cannot spoil, and copying them exactly is what keeps
/// this encoder's choice of endpoints identical to the reference's. The decode tables state what the
/// bits mean; these state how the reference picks them.
/// <para/>
/// <b>The colour endpoints</b> come from a principal-axis fit: the block's per-channel mean and
/// covariance, four rounds of power iteration to find the axis of greatest spread, the two pixels
/// furthest apart along it as a first pair of endpoints, then one least-squares refinement against the
/// indices that pair produced. A block whose sixteen pixels are one RGBA word skips all of it and
/// reads its endpoint pair straight out of <see cref="_Match5"/> and <see cref="_Match6"/>, tables
/// that state, for every eight-bit target, the two quantised endpoints whose one-third interpolation
/// lands nearest it.
/// <para/>
/// <b>The alpha ramp</b> is DXT5's eight-value one: the block's own minimum and maximum, then six
/// interpolated steps, each pixel taking the nearest by a linear scale rather than by search.
/// <para/>
/// <b>Scaled YCoCg</b> reorders each pixel into (Co, Cg, 0, Y) and then compresses it as an ordinary
/// DXT5 block: chroma into the colour part, luma into the alpha part at its full eight-sample
/// precision, and a per-block scale factor of zero — which the reconstruction reads as a scale of one,
/// so no widening is applied. The reference declines to search for a scale that would widen a
/// low-chroma block back out, and this follows it, because a scale it chose differently from the
/// reference would make every byte of the texture differ from ffmpeg's.
/// <para/>
/// <b>One place this deliberately parts from the reference.</b> A block holding one or two colours
/// that the block decode's own 5-6-5 widening states outright can be carried untouched — name the two
/// colours as the endpoint pair and index 0 and 1 are them, unmixed, in either of DXT1's branches.
/// The reference does not: its match tables aim at the one-third interpolation and are built against a
/// widening by bit replication, not the rounding widening the decode uses, so such a block comes back
/// a level or two off. <see cref="_CompressColour"/> therefore checks whether the block it just
/// wrote decodes back to the block it came from, and only where it does not, and only where the block
/// is one the format could have held exactly, writes it exactly instead. Measured over 96 textures
/// against ffmpeg's own Hap encoder that is 20 bytes in 3 of them, and every one of those blocks is
/// exact here and was not there. Nothing else in this type departs from the reference by a bit.
/// </remarks>
internal static class HapBlockEncoding {

  /// <summary>Compresses one 4x4 block into eight bytes of DXT1/BC1. Alpha is dropped.</summary>
  public static void CompressDxt1(ReadOnlySpan<byte> block, int stride, Span<byte> destination)
    => _CompressColour(block, stride, destination, keepWhatIsExact: true);

  /// <summary>Compresses one 4x4 block into sixteen bytes of DXT5/BC3. Alpha is kept.</summary>
  public static void CompressDxt5(ReadOnlySpan<byte> block, int stride, Span<byte> destination) {
    _CompressAlpha(block, stride, destination);
    _CompressColour(block, stride, destination[8..], keepWhatIsExact: true);
  }

  /// <summary>
  /// Compresses one 4x4 block into sixteen bytes of Scaled YCoCg DXT5. Alpha is dropped — the channel
  /// it would have occupied carries luma.
  /// </summary>
  public static void CompressScaledYCoCg(ReadOnlySpan<byte> block, int stride, Span<byte> destination) {
    Span<byte> reordered = stackalloc byte[64];
    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x)
        _ToYCoCg(block[(x * 4 + y * stride)..], reordered[(x * 4 + y * 16)..]);

    _CompressAlpha(reordered, 16, destination);

    // No exactness to keep here: the transform above has already left the block, so writing the
    // chroma pair as endpoints would gain nothing and cost the reference's own bytes.
    _CompressColour(reordered, 16, destination[8..], keepWhatIsExact: false);
  }

  // ==============================================================================================
  // The colour part
  // ==============================================================================================

  private static void _CompressColour(ReadOnlySpan<byte> block, int stride, Span<byte> destination, bool keepWhatIsExact) {
    uint mask;
    ushort maximum, minimum;

    if (_IsOneColour(block, stride)) {

      // A singular block: the least-squares system below would have no solution, and the match tables
      // answer the question it would have been asked outright.
      mask = 0xAAAAAAAA;
      maximum = _NearestEndpoints(block[0], block[1], block[2], first: true);
      minimum = _NearestEndpoints(block[0], block[1], block[2], first: false);
    } else {
      _OptimiseColours(block, stride, out maximum, out minimum);
      mask = maximum != minimum ? _MatchColours(block, stride, maximum, minimum) : 0;

      if (_RefineColours(block, stride, ref maximum, ref minimum, mask))
        mask = maximum != minimum ? _MatchColours(block, stride, maximum, minimum) : 0;
    }

    _Write(destination, maximum, minimum, mask);

    // Everything above is the reference's, byte for byte. What follows is not, and it only ever
    // runs where the reference's own answer is wrong in a way the format did not require: a block of
    // one or two colours that the 5-6-5 grid states outright, which the endpoint pair could have
    // carried untouched and which the match tables instead land a level or two away from — they aim
    // at the one-third interpolation, and are built against a widening of five and six bits that is
    // not the one the decode uses. Such a block is rewritten as its own colours; every other block,
    // and every block the reference already got exactly right, keeps the reference's bytes.
    if (!keepWhatIsExact || _Reproduces(destination, block, stride))
      return;

    if (_TryStateExactly(block, stride, out var exactMaximum, out var exactMinimum, out var exactMask))
      _Write(destination, exactMaximum, exactMinimum, exactMask);
  }

  private static void _Write(Span<byte> destination, ushort maximum, ushort minimum, uint mask) {

    // The four-colour branch of the block decode is the one where the first endpoint is the larger,
    // and it is the only one this encoder writes; exchanging the pair inverts every index.
    if (maximum < minimum) {
      (minimum, maximum) = (maximum, minimum);
      mask ^= 0x55555555;
    }

    destination[0] = (byte)maximum;
    destination[1] = (byte)(maximum >> 8);
    destination[2] = (byte)minimum;
    destination[3] = (byte)(minimum >> 8);
    destination[4] = (byte)mask;
    destination[5] = (byte)(mask >> 8);
    destination[6] = (byte)(mask >> 16);
    destination[7] = (byte)(mask >> 24);
  }

  /// <summary>Whether a written colour block decodes back to the block it was made from, exactly.</summary>
  private static bool _Reproduces(ReadOnlySpan<byte> coded, ReadOnlySpan<byte> block, int stride) {
    Span<byte> palR = stackalloc byte[4];
    Span<byte> palG = stackalloc byte[4];
    Span<byte> palB = stackalloc byte[4];
    HapBlockDecoding.BuildPalette(coded, palR, palG, palB);

    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        var i = y * 4 + x;
        var index = (coded[4 + i / 4] >> (i % 4 * 2)) & 3;
        var at = x * 4 + y * stride;
        if (block[at] != palR[index] || block[at + 1] != palG[index] || block[at + 2] != palB[index])
          return false;
      }

    return true;
  }

  /// <summary>
  /// Names the block's own colours as the endpoint pair, where it holds at most two and the 5-6-5
  /// grid states both of them outright.
  /// </summary>
  /// <remarks>
  /// Index 0 and index 1 are the two endpoints unmixed in either of DXT1's branches, so a block
  /// written this way decodes back to itself whichever way round the pair ends up.
  /// </remarks>
  private static bool _TryStateExactly(ReadOnlySpan<byte> block, int stride, out ushort first, out ushort second, out uint mask) {
    first = 0;
    second = 0;
    mask = 0;

    var found = 0;
    Span<int> colours = stackalloc int[2];

    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        var at = x * 4 + y * stride;
        var colour = (block[at] << 16) | (block[at + 1] << 8) | block[at + 2];
        var which = -1;

        for (var i = 0; i < found; ++i)
          if (colours[i] == colour) {
            which = i;
            break;
          }

        if (which < 0) {
          if (found == 2)
            return false;

          colours[found] = colour;
          which = found++;
        }

        if (which == 1)
          mask |= 1u << ((y * 4 + x) * 2);
      }

    if (!HapBlockDecoding.TryPack565((byte)(colours[0] >> 16), (byte)(colours[0] >> 8), (byte)colours[0], out first))
      return false;

    if (found == 1) {
      second = first;
      return true;
    }

    return HapBlockDecoding.TryPack565((byte)(colours[1] >> 16), (byte)(colours[1] >> 8), (byte)colours[1], out second);
  }

  /// <summary>Whether all sixteen pixels are the same RGBA word — alpha included, as the reference
  /// compares it.</summary>
  private static bool _IsOneColour(ReadOnlySpan<byte> block, int stride) {
    var first = _Word(block, 0);
    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x)
        if (_Word(block, x * 4 + y * stride) != first)
          return false;

    return true;
  }

  private static uint _Word(ReadOnlySpan<byte> block, int at)
    => (uint)(block[at] | (block[at + 1] << 8) | (block[at + 2] << 16) | (block[at + 3] << 24));

  /// <summary>One of the two endpoints the match tables name for a colour a whole block holds.</summary>
  private static ushort _NearestEndpoints(int r, int g, int b, bool first) {
    var which = first ? 0 : 1;
    return (ushort)((_Match5[r * 2 + which] << 11) | (_Match6[g * 2 + which] << 5) | _Match5[b * 2 + which]);
  }

  /// <summary>
  /// Finds a first pair of endpoints: the two pixels furthest apart along the block's axis of
  /// greatest colour spread, quantised to 5-6-5.
  /// </summary>
  private static void _OptimiseColours(ReadOnlySpan<byte> block, int stride, out ushort maximum, out ushort minimum) {
    Span<int> mean = stackalloc int[3];
    Span<int> lowest = stackalloc int[3];
    Span<int> highest = stackalloc int[3];

    for (var channel = 0; channel < 3; ++channel) {

      // The reference seeds all three from the block's first pixel and then walks every pixel
      // including that one, so the first is counted twice in the sum; the shift that follows divides
      // by sixteen regardless. Kept as it is — the mean only centres the covariance.
      var sum = (int)block[channel];
      var low = sum;
      var high = sum;

      for (var y = 0; y < 4; ++y)
        for (var x = 0; x < 4; ++x) {
          int value = block[channel + x * 4 + y * stride];
          sum += value;
          if (value < low)
            low = value;
          else if (value > high)
            high = value;
        }

      mean[channel] = (sum + 8) >> 4;
      lowest[channel] = low;
      highest[channel] = high;
    }

    Span<int> covariance = stackalloc int[6];
    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        var at = x * 4 + y * stride;
        var r = block[at] - mean[0];
        var g = block[at + 1] - mean[1];
        var b = block[at + 2] - mean[2];

        covariance[0] += r * r;
        covariance[1] += r * g;
        covariance[2] += r * b;
        covariance[3] += g * g;
        covariance[4] += g * b;
        covariance[5] += b * b;
      }

    Span<float> scaled = stackalloc float[6];
    for (var i = 0; i < 6; ++i)
      scaled[i] = covariance[i] / 255.0f;

    var axisR = (float)(highest[0] - lowest[0]);
    var axisG = (float)(highest[1] - lowest[1]);
    var axisB = (float)(highest[2] - lowest[2]);

    for (var iteration = 0; iteration < _POWER_ITERATIONS; ++iteration) {
      var r = axisR * scaled[0] + axisG * scaled[1] + axisB * scaled[2];
      var g = axisR * scaled[1] + axisG * scaled[3] + axisB * scaled[4];
      var b = axisR * scaled[2] + axisG * scaled[4] + axisB * scaled[5];

      axisR = r;
      axisG = g;
      axisB = b;
    }

    var magnitude = Math.Abs((double)axisR);
    if (Math.Abs((double)axisG) > magnitude)
      magnitude = Math.Abs((double)axisG);
    if (Math.Abs((double)axisB) > magnitude)
      magnitude = Math.Abs((double)axisB);

    int weightR, weightG, weightB;
    if (magnitude < 4.0f) {

      // Too little spread for the axis to mean anything; project onto luminance instead.
      weightR = 299;
      weightG = 587;
      weightB = 114;
    } else {
      magnitude = 512.0 / magnitude;
      weightR = (int)(axisR * magnitude);
      weightG = (int)(axisG * magnitude);
      weightB = (int)(axisB * magnitude);
    }

    var least = block[0] * weightR + block[1] * weightG + block[2] * weightB;
    var most = least;
    var leastAt = 0;
    var mostAt = 0;

    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        var at = x * 4 + y * stride;
        var dot = block[at] * weightR + block[at + 1] * weightG + block[at + 2] * weightB;

        if (dot < least) {
          least = dot;
          leastAt = at;
        } else if (dot > most) {
          most = dot;
          mostAt = at;
        }
      }

    maximum = _ToRgb565(block[mostAt], block[mostAt + 1], block[mostAt + 2]);
    minimum = _ToRgb565(block[leastAt], block[leastAt + 1], block[leastAt + 2]);
  }

  /// <summary>
  /// Chooses each pixel's index by projecting it onto the line through the block's four colours and
  /// taking the nearest of the three crossover points.
  /// </summary>
  /// <remarks>
  /// A one-dimensional approximation of the Euclidean nearest, and not always the same answer, but
  /// close enough that the reference has never needed the search — see cbloom's "DXTC summary".
  /// </remarks>
  private static uint _MatchColours(ReadOnlySpan<byte> block, int stride, ushort first, ushort second) {
    Span<byte> colours = stackalloc byte[16];
    _FromRgb565(colours, first);
    _FromRgb565(colours[4..], second);
    _Interpolate(colours[8..], colours, colours[4..]);
    _Interpolate(colours[12..], colours[4..], colours);

    var directionR = colours[0] - colours[4];
    var directionG = colours[1] - colours[5];
    var directionB = colours[2] - colours[6];

    Span<int> dots = stackalloc int[16];
    Span<int> stops = stackalloc int[4];
    var k = 0;

    for (var y = 0; y < 4; ++y) {
      for (var x = 0; x < 4; ++x) {
        var at = x * 4 + y * stride;
        dots[k++] = block[at] * directionR + block[at + 1] * directionG + block[at + 2] * directionB;
      }

      stops[y] = colours[y * 4] * directionR + colours[y * 4 + 1] * directionG + colours[y * 4 + 2] * directionB;
    }

    var firstPoint = (stops[1] + stops[3]) >> 1;
    var halfPoint = (stops[3] + stops[2]) >> 1;
    var fourthPoint = (stops[2] + stops[0]) >> 1;

    var mask = 0u;
    for (var i = 0; i < 16; ++i) {
      var dot = dots[i];
      var bits = (dot < halfPoint ? 4 : 0) | (dot < firstPoint ? 2 : 0) | (dot < fourthPoint ? 1 : 0);

      mask >>= 2;
      mask |= _IndexMap[bits];
    }

    return mask;
  }

  /// <summary>
  /// Solves for the endpoint pair that best fits the indices already chosen, by normal equations and
  /// Cramer's rule, and says whether it moved.
  /// </summary>
  private static bool _RefineColours(ReadOnlySpan<byte> block, int stride, ref ushort maximum, ref ushort minimum, uint mask) {
    var oldMaximum = maximum;
    var oldMinimum = minimum;
    ushort refinedMaximum, refinedMinimum;

    if ((mask ^ (mask << 2)) < 4) {

      // Every pixel took the same index, so the system is singular; the block's mean colour and the
      // match tables answer it instead.
      var r = 8;
      var g = 8;
      var b = 8;
      for (var y = 0; y < 4; ++y)
        for (var x = 0; x < 4; ++x) {
          var at = x * 4 + y * stride;
          r += block[at];
          g += block[at + 1];
          b += block[at + 2];
        }

      r >>= 4;
      g >>= 4;
      b >>= 4;

      refinedMaximum = _NearestEndpoints(r, g, b, first: true);
      refinedMinimum = _NearestEndpoints(r, g, b, first: false);
    } else {
      var walking = mask;
      var weightedR = 0;
      var weightedG = 0;
      var weightedB = 0;
      var totalR = 0;
      var totalG = 0;
      var totalB = 0;
      var products = 0;

      for (var y = 0; y < 4; ++y)
        for (var x = 0; x < 4; ++x) {
          var step = (int)(walking & 3);
          var weight = _IndexWeights[step];
          var at = x * 4 + y * stride;
          int r = block[at];
          int g = block[at + 1];
          int b = block[at + 2];

          products += _IndexProducts[step];
          weightedR += weight * r;
          weightedG += weight * g;
          weightedB += weight * b;
          totalR += r;
          totalG += g;
          totalB += b;

          walking >>= 2;
        }

      totalR = 3 * totalR - weightedR;
      totalG = 3 * totalG - weightedG;
      totalB = 3 * totalB - weightedB;

      // The three weight products were accumulated into one register, a byte-field apiece.
      var xx = products >> 16;
      var yy = (products >> 8) & 0xFF;
      var xy = products & 0xFF;

      var scaleR = 3.0f * 31.0f / 255.0f / (xx * yy - xy * xy);
      var scaleG = scaleR * 63.0f / 31.0f;
      var scaleB = scaleR;

      refinedMaximum = (ushort)(
        (_ClipTo((int)((weightedR * yy - totalR * xy) * scaleR + 0.5f), 5) << 11)
        | (_ClipTo((int)((weightedG * yy - totalG * xy) * scaleG + 0.5f), 6) << 5)
        | _ClipTo((int)((weightedB * yy - totalB * xy) * scaleB + 0.5f), 5));

      refinedMinimum = (ushort)(
        (_ClipTo((int)((totalR * xx - weightedR * xy) * scaleR + 0.5f), 5) << 11)
        | (_ClipTo((int)((totalG * xx - weightedG * xy) * scaleG + 0.5f), 6) << 5)
        | _ClipTo((int)((totalB * xx - weightedB * xy) * scaleB + 0.5f), 5));
    }

    maximum = refinedMaximum;
    minimum = refinedMinimum;
    return oldMinimum != refinedMinimum || oldMaximum != refinedMaximum;
  }

  // ==============================================================================================
  // The alpha part
  // ==============================================================================================

  /// <summary>
  /// Writes the eight-byte alpha block: the two extreme values, then a three-bit index a pixel into
  /// the ramp between them.
  /// </summary>
  private static void _CompressAlpha(ReadOnlySpan<byte> block, int stride, Span<byte> destination) {
    destination[..8].Clear();

    int low = block[3];
    var high = low;
    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        int value = block[3 + x * 4 + y * stride];
        if (value < low)
          low = value;
        else if (value > high)
          high = value;
      }

    destination[0] = (byte)high;
    destination[1] = (byte)low;

    // One value throughout: every index is zero, which already names it.
    if (low == high)
      return;

    // Given this pair of extremes these indices are the optimal ones, by Fabian Giesen's derivation
    // ("DXT5 alpha block index determination"): a linear scale, no search.
    var span = high - low;
    var quarters = span * 4;
    var halves = span * 2;
    var bias = span < 8 ? span - 1 - low * 7 : span / 2 + 2 - low * 7;

    var at = 2;
    var pending = 0;
    var bits = 0;

    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        var scaled = block[3 + x * 4 + y * stride] * 7 + bias;
        var index = 0;

        if (scaled >= quarters) {
          index += 4;
          scaled -= quarters;
        }

        if (scaled >= halves) {
          index += 2;
          scaled -= halves;
        }

        if (scaled >= span)
          ++index;

        // The linear scale runs from the lowest value to the highest; the coded index has the two
        // extremes at 0 and 1 and the ramp between them at 2 to 7.
        index = -index & 7;
        if (index < 2)
          index ^= 1;

        pending |= index << bits;
        bits += 3;
        if (bits < 8)
          continue;

        destination[at++] = (byte)pending;
        pending >>= 8;
        bits -= 8;
      }
  }

  // ==============================================================================================
  // Colour space and quantisation
  // ==============================================================================================

  /// <summary>
  /// Turns one RGBA pixel into the (Co, Cg, scale, Y) quadruple a Scaled YCoCg block holds.
  /// </summary>
  /// <remarks>
  /// The scale factor is written as zero, which the reconstruction reads as a scale of one. Alpha is
  /// dropped: the channel it would occupy is where luma goes.
  /// </remarks>
  private static void _ToYCoCg(ReadOnlySpan<byte> pixel, Span<byte> destination) {
    int r = pixel[0];
    var g = (pixel[1] + 1) >> 1;
    int b = pixel[2];
    var t = (2 + r + b) >> 2;

    destination[0] = _ClipToByte(128 + ((r - b + 1) >> 1));
    destination[1] = _ClipToByte(128 + g - t);
    destination[2] = 0;
    destination[3] = _ClipToByte(g + t);
  }

  /// <summary>Eight bits down to the width of a 5-6-5 field, by the reference's own scaling.</summary>
  private static ushort _ToRgb565(int r, int g, int b)
    => (ushort)((_Scale(r, 31) << 11) | (_Scale(g, 63) << 5) | _Scale(b, 31));

  /// <summary>Multiplication in eight-bit space: <c>a * b / 255</c>, rounded, without a divide.</summary>
  private static int _Scale(int a, int b) => (a * b + 128 + ((a * b + 128) >> 8)) >> 8;

  /// <summary>A 5-6-5 word back out to three eight-bit samples, plus a zero the caller ignores.</summary>
  private static void _FromRgb565(Span<byte> destination, ushort value) {
    destination[0] = _Expand5[(value & 0xF800) >> 11];
    destination[1] = _Expand6[(value & 0x07E0) >> 5];
    destination[2] = _Expand5[value & 0x001F];
    destination[3] = 0;
  }

  /// <summary>The colour one third of the way from the first to the second.</summary>
  private static void _Interpolate(Span<byte> destination, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) {
    destination[0] = (byte)((2 * first[0] + second[0]) / 3);
    destination[1] = (byte)((2 * first[1] + second[1]) / 3);
    destination[2] = (byte)((2 * first[2] + second[2]) / 3);
  }

  private static int _ClipTo(int value, int bits) {
    var highest = (1 << bits) - 1;
    return value < 0 ? 0 : value > highest ? highest : value;
  }

  private static byte _ClipToByte(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  // ==============================================================================================
  // Tables
  // ==============================================================================================

  /// <summary>How many rounds of power iteration find the block's principal axis.</summary>
  private const int _POWER_ITERATIONS = 4;

  /// <summary>Where each of the eight crossover outcomes puts its two index bits, already shifted to
  /// the top of the mask so the walk can shift the mask down instead of up.</summary>
  private static readonly uint[] _IndexMap = [
    0u << 30, 2u << 30, 0u << 30, 2u << 30, 3u << 30, 3u << 30, 1u << 30, 1u << 30,
  ];

  /// <summary>Three times each index's weight on the first endpoint, by coded index.</summary>
  private static readonly int[] _IndexWeights = [3, 0, 2, 1];

  /// <summary>The three products of those weights an index contributes, one byte field apiece.</summary>
  private static readonly int[] _IndexProducts = [0x090000, 0x000900, 0x040102, 0x010402];

  /// <summary>
  /// Five bits out to eight, by bit replication — the reference encoder's own table, and not the one
  /// <see cref="HapBlockDecoding"/> reads the same bits back with. See this type's remarks.
  /// </summary>
  private static readonly byte[] _Expand5 = [
    0, 8, 16, 24, 33, 41, 49, 57, 66, 74, 82, 90, 99, 107, 115, 123,
    132, 140, 148, 156, 165, 173, 181, 189, 198, 206, 214, 222, 231, 239, 247, 255,
  ];

  /// <summary>Six bits out to eight, on the same convention as <see cref="_Expand5"/>.</summary>
  private static readonly byte[] _Expand6 = [
    0, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60,
    65, 69, 73, 77, 81, 85, 89, 93, 97, 101, 105, 109, 113, 117, 121, 125,
    130, 134, 138, 142, 146, 150, 154, 158, 162, 166, 170, 174, 178, 182, 186, 190,
    195, 199, 203, 207, 211, 215, 219, 223, 227, 231, 235, 239, 243, 247, 251, 255,
  ];

  /// <summary>
  /// For every eight-bit target, the two five-bit endpoints — the first, then the second — whose
  /// one-third interpolation lands nearest it.
  /// </summary>
  private static readonly byte[] _Match5 = [
    0, 0, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0, 1, 0, 1, 1,
    1, 1, 2, 0, 2, 0, 0, 4, 2, 1, 2, 1, 2, 1, 3, 0,
    3, 0, 3, 0, 3, 1, 1, 5, 3, 2, 3, 2, 4, 0, 4, 0,
    4, 1, 4, 1, 4, 2, 4, 2, 4, 2, 3, 5, 5, 1, 5, 1,
    5, 2, 4, 4, 5, 3, 5, 3, 5, 3, 6, 2, 6, 2, 6, 2,
    6, 3, 5, 5, 6, 4, 6, 4, 4, 8, 7, 3, 7, 3, 7, 3,
    7, 4, 7, 4, 7, 4, 7, 5, 5, 9, 7, 6, 7, 6, 8, 4,
    8, 4, 8, 5, 8, 5, 8, 6, 8, 6, 8, 6, 7, 9, 9, 5,
    9, 5, 9, 6, 8, 8, 9, 7, 9, 7, 9, 7, 10, 6, 10, 6,
    10, 6, 10, 7, 9, 9, 10, 8, 10, 8, 8, 12, 11, 7, 11, 7,
    11, 7, 11, 8, 11, 8, 11, 8, 11, 9, 9, 13, 11, 10, 11, 10,
    12, 8, 12, 8, 12, 9, 12, 9, 12, 10, 12, 10, 12, 10, 11, 13,
    13, 9, 13, 9, 13, 10, 12, 12, 13, 11, 13, 11, 13, 11, 14, 10,
    14, 10, 14, 10, 14, 11, 13, 13, 14, 12, 14, 12, 12, 16, 15, 11,
    15, 11, 15, 11, 15, 12, 15, 12, 15, 12, 15, 13, 13, 17, 15, 14,
    15, 14, 16, 12, 16, 12, 16, 13, 16, 13, 16, 14, 16, 14, 16, 14,
    15, 17, 17, 13, 17, 13, 17, 14, 16, 16, 17, 15, 17, 15, 17, 15,
    18, 14, 18, 14, 18, 14, 18, 15, 17, 17, 18, 16, 18, 16, 16, 20,
    19, 15, 19, 15, 19, 15, 19, 16, 19, 16, 19, 16, 19, 17, 17, 21,
    19, 18, 19, 18, 20, 16, 20, 16, 20, 17, 20, 17, 20, 18, 20, 18,
    20, 18, 19, 21, 21, 17, 21, 17, 21, 18, 20, 20, 21, 19, 21, 19,
    21, 19, 22, 18, 22, 18, 22, 18, 22, 19, 21, 21, 22, 20, 22, 20,
    20, 24, 23, 19, 23, 19, 23, 19, 23, 20, 23, 20, 23, 20, 23, 21,
    21, 25, 23, 22, 23, 22, 24, 20, 24, 20, 24, 21, 24, 21, 24, 22,
    24, 22, 24, 22, 23, 25, 25, 21, 25, 21, 25, 22, 24, 24, 25, 23,
    25, 23, 25, 23, 26, 22, 26, 22, 26, 22, 26, 23, 25, 25, 26, 24,
    26, 24, 24, 28, 27, 23, 27, 23, 27, 23, 27, 24, 27, 24, 27, 24,
    27, 25, 25, 29, 27, 26, 27, 26, 28, 24, 28, 24, 28, 25, 28, 25,
    28, 26, 28, 26, 28, 26, 27, 29, 29, 25, 29, 25, 29, 26, 28, 28,
    29, 27, 29, 27, 29, 27, 30, 26, 30, 26, 30, 26, 30, 27, 29, 29,
    30, 28, 30, 28, 30, 28, 31, 27, 31, 27, 31, 27, 31, 28, 31, 28,
    31, 28, 31, 29, 31, 29, 31, 30, 31, 30, 31, 30, 31, 31, 31, 31,
  ];

  /// <summary>The same for the six-bit green field.</summary>
  private static readonly byte[] _Match6 = [
    0, 0, 0, 1, 1, 0, 1, 0, 1, 1, 2, 0, 2, 1, 3, 0,
    3, 0, 3, 1, 4, 0, 4, 0, 4, 1, 5, 0, 5, 1, 6, 0,
    6, 0, 6, 1, 7, 0, 7, 0, 7, 1, 8, 0, 8, 1, 8, 1,
    8, 2, 9, 1, 9, 2, 9, 2, 9, 3, 10, 2, 10, 3, 10, 3,
    10, 4, 11, 3, 11, 4, 11, 4, 11, 5, 12, 4, 12, 5, 12, 5,
    12, 6, 13, 5, 13, 6, 8, 16, 13, 7, 14, 6, 14, 7, 9, 17,
    14, 8, 15, 7, 15, 8, 11, 16, 15, 9, 15, 10, 16, 8, 16, 9,
    16, 10, 15, 13, 17, 9, 17, 10, 17, 11, 15, 16, 18, 10, 18, 11,
    18, 12, 16, 16, 19, 11, 19, 12, 19, 13, 17, 17, 20, 12, 20, 13,
    20, 14, 19, 16, 21, 13, 21, 14, 21, 15, 20, 17, 22, 14, 22, 15,
    25, 10, 22, 16, 23, 15, 23, 16, 26, 11, 23, 17, 24, 16, 24, 17,
    27, 12, 24, 18, 25, 17, 25, 18, 28, 13, 25, 19, 26, 18, 26, 19,
    29, 14, 26, 20, 27, 19, 27, 20, 30, 15, 27, 21, 28, 20, 28, 21,
    28, 21, 28, 22, 29, 21, 29, 22, 24, 32, 29, 23, 30, 22, 30, 23,
    25, 33, 30, 24, 31, 23, 31, 24, 27, 32, 31, 25, 31, 26, 32, 24,
    32, 25, 32, 26, 31, 29, 33, 25, 33, 26, 33, 27, 31, 32, 34, 26,
    34, 27, 34, 28, 32, 32, 35, 27, 35, 28, 35, 29, 33, 33, 36, 28,
    36, 29, 36, 30, 35, 32, 37, 29, 37, 30, 37, 31, 36, 33, 38, 30,
    38, 31, 41, 26, 38, 32, 39, 31, 39, 32, 42, 27, 39, 33, 40, 32,
    40, 33, 43, 28, 40, 34, 41, 33, 41, 34, 44, 29, 41, 35, 42, 34,
    42, 35, 45, 30, 42, 36, 43, 35, 43, 36, 46, 31, 43, 37, 44, 36,
    44, 37, 44, 37, 44, 38, 45, 37, 45, 38, 40, 48, 45, 39, 46, 38,
    46, 39, 41, 49, 46, 40, 47, 39, 47, 40, 43, 48, 47, 41, 47, 42,
    48, 40, 48, 41, 48, 42, 47, 45, 49, 41, 49, 42, 49, 43, 47, 48,
    50, 42, 50, 43, 50, 44, 48, 48, 51, 43, 51, 44, 51, 45, 49, 49,
    52, 44, 52, 45, 52, 46, 51, 48, 53, 45, 53, 46, 53, 47, 52, 49,
    54, 46, 54, 47, 57, 42, 54, 48, 55, 47, 55, 48, 58, 43, 55, 49,
    56, 48, 56, 49, 59, 44, 56, 50, 57, 49, 57, 50, 60, 45, 57, 51,
    58, 50, 58, 51, 61, 46, 58, 52, 59, 51, 59, 52, 62, 47, 59, 53,
    60, 52, 60, 53, 60, 53, 60, 54, 61, 53, 61, 54, 61, 54, 61, 55,
    62, 54, 62, 55, 62, 55, 62, 56, 63, 55, 63, 56, 63, 56, 63, 57,
    63, 58, 63, 59, 63, 59, 63, 60, 63, 61, 63, 62, 63, 62, 63, 63,
  ];
}
