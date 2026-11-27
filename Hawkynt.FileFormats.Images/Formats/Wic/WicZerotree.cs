using System;
using System.Numerics;

namespace FileFormat.Wic;

/// <summary>Bit-plane zerotree coding of one wavelet pyramid, as JWIC lays it out.</summary>
/// <remarks>
/// Coefficients are held sign-magnitude: bit 31 is the sign, the rest the magnitude. The pyramid
/// sits in the matrix the usual way (low-pass corner top-left, each level's horizontal, diagonal and
/// vertical bands beside it), and a "node" at <c>(x, y)</c> of level <c>l</c> ≥ 1 owns three
/// coefficients: <c>(x, y)</c> in the band below the low-pass corner, <c>(x + lw, y)</c> diagonal to
/// it and <c>(x + lw, y − lh)</c> above that, where <c>lw × lh</c> is the size of one band of that
/// level. Its four children are <c>(2x, 2y)</c> … <c>(2x + 1, 2y + 1)</c> one level finer.
/// <para/>
/// A per-node significance table shares the coefficient matrix's geometry: the node at <c>(x, y)</c>
/// keeps its entry at <c>y · width + x</c>. On the encoding side the entry is the bit length of the
/// largest magnitude anywhere in the node's tree; on the decoding side it is the bit plane at which
/// the node turned significant, zero until then. Either way "parent entry equals the current plane"
/// means the parent became significant in this very pass, which is what selects the second half of
/// the node contexts.
/// </remarks>
internal static class WicZerotree {

  private const uint _SIGN = 0x80000000u;
  private const uint _MAGNITUDE = 0x7FFFFFFFu;

  /// <summary>Decodes one plane of <paramref name="width"/> × <paramref name="height"/> coefficients.</summary>
  public static uint[] Decode(WicQmCoder.Decoder coder, int width, int height, int levels, int topPlane) {
    var scan = new DecodingScan(coder, width, height);
    var topWidth = width >> levels;
    var topHeight = height >> levels;
    for (var plane = topPlane; plane > 0 && !scan.Stopped; --plane)
      for (var y = 0; y < topHeight && !scan.Stopped; ++y)
        for (var x = 0; x < topWidth && !scan.Stopped; ++x)
          scan.Visit(x, y, plane, 0, -1, -1, topWidth, topHeight);

    return scan.Coefficients;
  }

  /// <summary>The highest bit plane a pyramid needs, i.e. the bit length of its largest magnitude.</summary>
  public static int TopPlane(byte[] treeBits, int width, int height, int levels) {
    var top = 0;
    for (var y = 0; y < height >> levels; ++y)
      for (var x = 0; x < width >> levels; ++x)
        top = Math.Max(top, treeBits[y * width + x]);

    return top;
  }

  /// <summary>Bit length of the largest magnitude in every node's tree.</summary>
  public static byte[] TreeBitLengths(uint[] coefficients, int width, int height, int levels) {
    var bits = new byte[width * height];

    // Finest level first, so every coarser node can fold in its four children.
    for (var level = 1; level <= levels; ++level) {
      var bandWidth = width >> level;
      var bandHeight = height >> level;
      for (var j = 0; j < bandHeight; ++j)
        for (var i = 0; i < bandWidth; ++i) {
          var own = Math.Max(
            _Magnitude(coefficients[j * width + bandWidth + i]),
            Math.Max(_Magnitude(coefficients[(bandHeight + j) * width + i]), _Magnitude(coefficients[(bandHeight + j) * width + bandWidth + i])));
          int value = _BitLength(own);
          if (level > 1) {
            var childRow = (bandHeight << 1) + (j << 1);
            var childColumn = i << 1;
            value = Math.Max(value, Math.Max(
              Math.Max(bits[childRow * width + childColumn], bits[childRow * width + childColumn + 1]),
              Math.Max(bits[(childRow + 1) * width + childColumn], bits[(childRow + 1) * width + childColumn + 1])));
          }

          bits[(bandHeight + j) * width + i] = (byte)value;
        }
    }

    var topWidth = width >> levels;
    var topHeight = height >> levels;
    for (var j = 0; j < topHeight; ++j)
      for (var i = 0; i < topWidth; ++i)
        bits[j * width + i] = (byte)Math.Max(_BitLength(_Magnitude(coefficients[j * width + i])), bits[(topHeight + j) * width + i]);

    return bits;
  }

  /// <summary>Encodes one plane until it is complete or the stream has grown to <paramref name="byteBudget"/> bytes.</summary>
  public static void Encode(WicQmCoder.Encoder coder, uint[] coefficients, byte[] treeBits, int width, int height, int levels, long byteBudget) {
    var scan = new EncodingScan(coder, coefficients, treeBits, width, height, byteBudget);
    var topWidth = width >> levels;
    var topHeight = height >> levels;
    for (var plane = TopPlane(treeBits, width, height, levels); plane > 0 && !scan.Stopped; --plane)
      for (var y = 0; y < topHeight && !scan.Stopped; ++y)
        for (var x = 0; x < topWidth && !scan.Stopped; ++x)
          scan.Visit(x, y, plane, 0, -1, -1, topWidth, topHeight);
  }

  private static uint _Magnitude(uint value) => value & _MAGNITUDE;

  private static int _BitLength(uint magnitude) => 32 - BitOperations.LeadingZeroCount(magnitude);

  private sealed class DecodingScan(WicQmCoder.Decoder coder, int width, int height) {
    public readonly uint[] Coefficients = new uint[width * height];
    private readonly byte[] _significantAt = new byte[width * height];

    public bool Stopped { get; private set; }

    public void Visit(int x, int y, int plane, int level, int parentX, int parentY, int bandWidth, int bandHeight) {
      if (this.Stopped)
        return;

      if (coder.Decode(WicQmCoder.ContinueContext) == 0) {
        this.Stopped = true;
        return;
      }

      var node = y * width + x;
      if (this._significantAt[node] == 0) {
        var context = WicQmCoder.NodeContext + level;
        if (level > 0 && this._significantAt[parentY * width + parentX] == plane)
          context += 16;

        if (coder.Decode(context) == 0)
          return;

        this._significantAt[node] = (byte)plane;
      }

      var bit = 1u << (plane - 1);
      if (level == 0) {
        this._Coefficient(node, bit, WicQmCoder.LowPassContext);
        this.Visit(x, y + bandHeight, plane, 1, x, y, bandWidth, bandHeight);
        return;
      }

      var horizontal = node;
      var diagonal = node + bandWidth;
      var vertical = node + bandWidth - bandHeight * width;
      var c = this.Coefficients;

      this._Coefficient(horizontal, bit, WicQmCoder.HorizontalContext + level
        + (_Magnitude(c[diagonal]) >= bit << 1 || _Magnitude(c[vertical]) >= bit << 1 ? 16 : 0));
      this._Coefficient(diagonal, bit, WicQmCoder.DiagonalContext + level
        + (_Magnitude(c[horizontal]) >= bit || _Magnitude(c[vertical]) >= bit << 1 ? 16 : 0));
      this._Coefficient(vertical, bit, WicQmCoder.VerticalContext + level
        + (_Magnitude(c[horizontal]) >= bit || _Magnitude(c[diagonal]) >= bit ? 16 : 0));

      if (y << 1 >= height)
        return;

      for (var child = 0; child < 4; ++child)
        this.Visit((x << 1) + (child & 1), (y << 1) + (child >> 1), plane, level + 1, x, y, bandWidth << 1, bandHeight << 1);
    }

    /// <summary>Refines a significant coefficient, or asks whether an insignificant one has become so.</summary>
    private void _Coefficient(int index, uint bit, int significanceContext) {
      ref var value = ref this.Coefficients[index];
      if (value != 0) {
        value = (value & ~bit) | (coder.Decode(WicQmCoder.RefinementContext) != 0 ? bit : 0) | (bit >> 1);
        return;
      }

      if (coder.Decode(significanceContext) != 0)
        value = (coder.Decode(WicQmCoder.SignContext) != 0 ? _SIGN : 0) | bit | (bit >> 1);
    }
  }

  private sealed class EncodingScan(WicQmCoder.Encoder coder, uint[] coefficients, byte[] treeBits, int width, int height, long byteBudget) {

    public bool Stopped { get; private set; }

    public void Visit(int x, int y, int plane, int level, int parentX, int parentY, int bandWidth, int bandHeight) {
      if (this.Stopped)
        return;

      if (byteBudget <= coder.BytesWritten) {
        coder.Encode(0, WicQmCoder.ContinueContext);
        this.Stopped = true;
        return;
      }

      coder.Encode(1, WicQmCoder.ContinueContext);

      var node = y * width + x;
      var context = WicQmCoder.NodeContext + level;
      if (level > 0 && treeBits[parentY * width + parentX] == plane)
        context += 16;

      var treeBitsHere = treeBits[node];
      if (treeBitsHere < plane) {
        coder.Encode(0, context);
        return;
      }

      if (treeBitsHere == plane)
        coder.Encode(1, context);

      var bit = 1u << (plane - 1);
      if (level == 0) {
        this._Coefficient(node, bit, WicQmCoder.LowPassContext);
        this.Visit(x, y + bandHeight, plane, 1, x, y, bandWidth, bandHeight);
        return;
      }

      var horizontal = node;
      var diagonal = node + bandWidth;
      var vertical = node + bandWidth - bandHeight * width;
      var h = _Magnitude(coefficients[horizontal]);
      var d = _Magnitude(coefficients[diagonal]);
      var v = _Magnitude(coefficients[vertical]);

      this._Coefficient(horizontal, bit, WicQmCoder.HorizontalContext + level + (d >= bit << 1 || v >= bit << 1 ? 16 : 0));
      this._Coefficient(diagonal, bit, WicQmCoder.DiagonalContext + level + (h >= bit || v >= bit << 1 ? 16 : 0));
      this._Coefficient(vertical, bit, WicQmCoder.VerticalContext + level + (h >= bit || d >= bit ? 16 : 0));

      if (y << 1 >= height)
        return;

      for (var child = 0; child < 4; ++child)
        this.Visit((x << 1) + (child & 1), (y << 1) + (child >> 1), plane, level + 1, x, y, bandWidth << 1, bandHeight << 1);
    }

    private void _Coefficient(int index, uint bit, int significanceContext) {
      var value = coefficients[index];
      var magnitude = _Magnitude(value);
      if (magnitude >= bit << 1) {
        coder.Encode((magnitude & bit) != 0 ? 1 : 0, WicQmCoder.RefinementContext);
        return;
      }

      var significant = magnitude >= bit;
      coder.Encode(significant ? 1 : 0, significanceContext);
      if (significant)
        coder.Encode((value & _SIGN) != 0 ? 1 : 0, WicQmCoder.SignContext);
    }
  }
}
