using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// The noise layer (libjxl <c>lib/jxl/dec_noise.cc</c> and
/// <c>render_pipeline/stage_noise.cc</c>).
/// </summary>
/// <remarks>
/// A frame may ask for noise to be put back into it. The encoder throws away
/// the grain of a photograph because it costs a great many bits and carries
/// almost no information, and states instead how much of it there was at eight
/// brightness levels. The decoder generates a field of its own and shapes it to
/// that curve.
///
/// <para>None of it is a decoder's choice. The field comes from a stated
/// generator seeded with the frame and group position, so every decoder
/// produces the same grain in the same places; a plausible-looking field of
/// someone else's random numbers would be a different picture. That is why the
/// generator below is libjxl's own, down to the order the three planes are
/// drawn from it.</para>
/// </remarks>
internal static class JxlNoise {

  /// <summary>libjxl <c>kNumNoisePoints</c>: the curve has eight points.</summary>
  public const int NumNoisePoints = 8;

  /// <summary>libjxl <c>kNoisePrecision</c>: the curve is stated in tenths of
  /// a thousandth, ten bits each.</summary>
  private const float _NoisePrecision = 1024.0f;

  /// <summary>How many floats one turn of the generator yields.</summary>
  private const int _FloatsPerBatch = 16;

  /// <summary>The index a file's first shown frame is rendered under.</summary>
  private const uint _FirstVisibleFrame = 1;

  /// <summary>
  /// Read the noise curve.
  /// </summary>
  public static float[] Decode(JxlBitReader reader) {
    ArgumentNullException.ThrowIfNull(reader);

    var lut = new float[NumNoisePoints];
    for (var i = 0; i < NumNoisePoints; ++i)
      lut[i] = reader.ReadBits(10) / _NoisePrecision;
    return lut;
  }

  /// <summary>
  /// The first sixteen numbers the generator yields for a given seed, so a test
  /// can check it against the algorithm rather than against this decoder.
  /// </summary>
  internal static float[] FirstRandomNumbersForTest(uint seed1, uint seed2, uint seed3, uint seed4) {
    var rng = new _Xorshift128Plus(seed1, seed2, seed3, seed4);
    var bits = new ulong[_Xorshift128Plus.N];
    var floats = new float[_FloatsPerBatch];
    rng.Fill(bits);
    _BitsToFloats(bits, floats);
    return floats;
  }

  /// <summary>Whether the curve asks for anything at all.</summary>
  public static bool HasAny(float[] lut) {
    ArgumentNullException.ThrowIfNull(lut);
    foreach (var value in lut)
      if (Math.Abs(value) > 1e-3f)
        return true;
    return false;
  }

  /// <summary>
  /// Add the noise to a frame's three planes, which are in XYB and have already
  /// been through the smoothing and edge-preserving filters.
  /// </summary>
  /// <param name="planes">X, Y and B.</param>
  /// <param name="width">Picture width.</param>
  /// <param name="height">Picture height.</param>
  /// <param name="lut">The stated curve.</param>
  /// <param name="yToX">The frame's base X correlation.</param>
  /// <param name="yToB">Its base B correlation.</param>
  public static void Apply(float[][] planes, int width, int height, float[] lut, float yToX, float yToB) {
    ArgumentNullException.ThrowIfNull(planes);
    ArgumentNullException.ThrowIfNull(lut);
    if (planes.Length < 3)
      throw new ArgumentException("Noise is added to three planes.", nameof(planes));
    if (!HasAny(lut))
      return;

    var noise = _RandomPlanes(width, height);
    for (var c = 0; c < 3; ++c)
      noise[c] = _Convolve(noise[c], width, height);

    // libjxl's normaliser: the kernel roughly doubles the range the older
    // approximation had, so this is half what it used to be.
    const float norm = 0.22f;
    const float rgCorrelated = 0.9921875f;   // 127/128
    const float rgIndependent = 0.0078125f;  // 1/128

    for (var i = 0; i < width * height; ++i) {
      var x = planes[0][i];
      var y = planes[1][i];

      // The curve is looked up on the red and green a pixel would have, which
      // is where the noise is actually seen.
      var green = y - x;
      var red = y + x;
      var strengthGreen = Math.Clamp(_Evaluate(lut, green * 0.5f), 0.0f, 1.0f);
      var strengthRed = Math.Clamp(_Evaluate(lut, red * 0.5f), 0.0f, 1.0f);

      var randomRed = noise[0][i] * norm;
      var randomGreen = noise[1][i] * norm;
      var randomCorrelated = noise[2][i] * norm;

      // Almost all of the grain is common to both channels, which is what makes
      // it read as luminance noise rather than as colour speckle.
      var redNoise = strengthRed * (rgIndependent * randomRed + rgCorrelated * randomCorrelated);
      var greenNoise = strengthGreen * (rgIndependent * randomGreen + rgCorrelated * randomCorrelated);
      var together = redNoise + greenNoise;

      planes[0][i] = x + (yToX * together + (redNoise - greenNoise));
      planes[1][i] = y + together;
      planes[2][i] += yToB * together;
    }
  }

  /// <summary>
  /// The curve, read at a point between its eight stated ones (libjxl
  /// <c>StrengthEvalLut</c>).
  /// </summary>
  private static float _Evaluate(float[] lut, float x) {
    const int scale = NumNoisePoints - 2;
    var scaled = Math.Max(0.0f, x * scale);
    var floor = MathF.Floor(scaled);
    var frac = scaled - floor;
    if (scaled >= scale + 1) {
      floor = scale;
      frac = 1.0f;
    }

    var index = (int)floor;
    if (index < 0)
      index = 0;
    if (index > NumNoisePoints - 2)
      index = NumNoisePoints - 2;
    return lut[index] + frac * (lut[index + 1] - lut[index]);
  }

  /// <summary>
  /// Three planes of random numbers, drawn one after another from a single
  /// generator (libjxl <c>Random3Planes</c>).
  /// </summary>
  /// <remarks>
  /// The seed is the frame's index and the group's position. Only frames of one
  /// group are handled here, so both indices and both positions are zero; a
  /// frame of several groups seeds each group separately and its edges need the
  /// neighbouring group's numbers, which is not worked out here.
  /// </remarks>
  private static float[][] _RandomPlanes(int width, int height) {
    // The count of frames shown so far, the count of frames not shown since the
    // last one that was, and the group's corner. libjxl raises the first of
    // those before it decodes a frame rather than after, so the first frame
    // shown is the first and not the zeroth — which is not a detail: seeding
    // with a zero there gives a field that has nothing to do with libjxl's, and
    // the picture is wrong everywhere by a couple of levels.
    var rng = new _Xorshift128Plus(_FirstVisibleFrame, 0, 0, 0);
    var planes = new float[3][];
    for (var c = 0; c < 3; ++c)
      planes[c] = _RandomImage(rng, width, height);
    return planes;
  }

  private static float[] _RandomImage(_Xorshift128Plus rng, int width, int height) {
    var plane = new float[width * height];
    var bits = new ulong[_Xorshift128Plus.N];
    var floats = new float[_FloatsPerBatch];

    for (var y = 0; y < height; ++y) {
      var row = y * width;
      var x = 0;
      // Whole turns only, then one more for whatever is left of the row. The
      // count of turns is what advances the generator, so it has to be the same
      // count libjxl takes even where the last one spills past the row.
      for (; x + _FloatsPerBatch < width; x += _FloatsPerBatch) {
        rng.Fill(bits);
        _BitsToFloats(bits, floats);
        Array.Copy(floats, 0, plane, row + x, _FloatsPerBatch);
      }

      rng.Fill(bits);
      _BitsToFloats(bits, floats);
      Array.Copy(floats, 0, plane, row + x, Math.Min(_FloatsPerBatch, width - x));
    }

    return plane;
  }

  /// <summary>One turn's bits as floats in [1, 2). The kernel below sums to
  /// nothing, so the offset of one does not reach the picture.</summary>
  private static void _BitsToFloats(ulong[] bits, float[] floats) {
    for (var i = 0; i < bits.Length; ++i) {
      var value = bits[i];
      floats[i * 2] = _ToFloat((uint)value);
      floats[i * 2 + 1] = _ToFloat((uint)(value >> 32));
    }
  }

  private static float _ToFloat(uint bits) => BitConverter.UInt32BitsToSingle((bits >> 9) | 0x3F800000u);

  /// <summary>
  /// libjxl's <c>ConvolveNoiseStage</c>: four times one minus a five by five
  /// box, which turns white noise into something with the grain of film.
  /// </summary>
  private static float[] _Convolve(float[] plane, int width, int height) {
    var result = new float[plane.Length];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var centre = plane[y * width + x];
      var others = 0.0f;
      for (var dy = -2; dy <= 2; ++dy)
      for (var dx = -2; dx <= 2; ++dx) {
        if (dx == 0 && dy == 0)
          continue;
        others += plane[_Mirror(y + dy, height) * width + _Mirror(x + dx, width)];
      }

      result[y * width + x] = others * 0.16f + centre * -3.84f;
    }

    return result;
  }

  /// <summary>The picture reflected where the kernel reaches past its edge.</summary>
  private static int _Mirror(int at, int size) {
    if (at < 0)
      at = -1 - at;
    if (at >= size)
      at = 2 * size - 1 - at;
    return Math.Clamp(at, 0, size - 1);
  }

  /// <summary>
  /// libjxl's <c>Xorshift128Plus</c>: eight generators run together, seeded
  /// through SplitMix64.
  /// </summary>
  private sealed class _Xorshift128Plus {

    public const int N = 8;

    private readonly ulong[] _s0 = new ulong[N];
    private readonly ulong[] _s1 = new ulong[N];

    public _Xorshift128Plus(uint seed1, uint seed2, uint seed3, uint seed4) {
      const ulong golden = 0x9E3779B97F4A7C15ul;
      _s0[0] = _SplitMix64(((ulong)seed1 << 32) + seed2 + golden);
      _s1[0] = _SplitMix64(((ulong)seed3 << 32) + seed4 + golden);
      for (var i = 1; i < N; ++i) {
        _s0[i] = _SplitMix64(_s0[i - 1]);
        _s1[i] = _SplitMix64(_s1[i - 1]);
      }
    }

    public void Fill(ulong[] randomBits) {
      for (var i = 0; i < N; ++i) {
        var s1 = _s0[i];
        var s0 = _s1[i];
        randomBits[i] = s1 + s0;
        _s0[i] = s0;
        s1 ^= s1 << 23;
        _s1[i] = s1 ^ s0 ^ (s1 >> 18) ^ (s0 >> 5);
      }
    }

    private static ulong _SplitMix64(ulong z) {
      z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
      z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
      return z ^ (z >> 31);
    }
  }
}
