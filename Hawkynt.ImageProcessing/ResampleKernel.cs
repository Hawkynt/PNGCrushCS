using System;

namespace Hawkynt.ImageProcessing;

/// <summary>Which reconstruction filter a resample reads the source through.</summary>
/// <remarks>
/// These are the six that earn their place, not a catalogue. Each one is a different answer to the
/// same question — what is between two samples — and the answers differ in ways a caller can see:
/// how much ringing, how much softness, whether an edge survives.
/// </remarks>
public enum ResampleKernel {

  /// <summary>
  /// Takes the nearest sample and nothing else. The only filter that invents no colour the source
  /// did not have, which is why pixel art and palette pictures want it and photographs do not.
  /// </summary>
  Nearest,

  /// <summary>
  /// Averages the samples a destination pixel covers. Correct for shrinking by a whole factor and
  /// blocky for anything else; on enlargement it degenerates to <see cref="Nearest"/>.
  /// </summary>
  Box,

  /// <summary>
  /// A straight line between two samples. Cheap, always soft, and never rings.
  /// </summary>
  Bilinear,

  /// <summary>
  /// The interpolating cubic — Catmull-Rom, the spline that passes through its samples. Sharper
  /// than bilinear at the price of a little ringing, and the usual meaning of "bicubic".
  /// </summary>
  Bicubic,

  /// <summary>
  /// Mitchell-Netravali's cubic, tuned to trade the ringing of an interpolating cubic against the
  /// blur of a B-spline. The safe default for enlargement.
  /// </summary>
  Mitchell,

  /// <summary>
  /// A three-lobed windowed sinc. The sharpest of the six on reduction and the one most willing to
  /// ring on a hard edge.
  /// </summary>
  Lanczos,
}

/// <summary>
/// One reconstruction filter: how wide it reaches and what it weights a sample at that distance by.
/// </summary>
/// <remarks>
/// Converted from ImageMagick's <c>resize.c</c>, whose licence (an Apache-2.0 derivative) this
/// project can absorb — see <c>THIRD-PARTY-NOTICE.ImageMagick.txt</c>. The filters themselves come
/// from Mitchell and Netravali's 1988 paper and from the sinc window they are usually set against;
/// what was taken from ImageMagick is the part that is arbitrary-but-agreed, and therefore the part
/// where being different from everybody else is simply being wrong: the support each filter is
/// truncated at, the B and C that name each cubic, and the windowing that turns a sinc into a
/// Lanczos.
/// <para/>
/// The one deliberate difference is that <see cref="Lanczos"/> evaluates a real
/// <c>sin(pi x)/(pi x)</c> where ImageMagick evaluates a minimax polynomial approximation of it,
/// accurate to about one part in 4x10^7. That is far below a 16-bit component, let alone an 8-bit
/// one, so the approximation buys speed at a precision nobody can observe — and the exact form is
/// the one a reader can check against the definition.
/// </remarks>
internal readonly struct ResampleWeighting {

  private readonly ResampleKernel _kernel;
  private readonly double _p0, _p2, _p3, _q0, _q1, _q2, _q3;

  private ResampleWeighting(ResampleKernel kernel, double support, double b, double c) {
    this._kernel = kernel;
    this.Support = support;

    // The cubic's coefficients as Mitchell and Netravali derive them from B and C. Written out
    // rather than evaluated per sample because they do not change within a resample.
    this._p0 = (6.0 - 2.0 * b) / 6.0;
    this._p2 = (-18.0 + 12.0 * b + 6.0 * c) / 6.0;
    this._p3 = (12.0 - 9.0 * b - 6.0 * c) / 6.0;
    this._q0 = (8.0 * b + 24.0 * c) / 6.0;
    this._q1 = (-12.0 * b - 48.0 * c) / 6.0;
    this._q2 = (6.0 * b + 30.0 * c) / 6.0;
    this._q3 = (-b - 6.0 * c) / 6.0;
  }

  /// <summary>How far from a destination centre the filter still contributes, in source pixels.</summary>
  public double Support { get; }

  /// <summary>The filter for a kernel.</summary>
  public static ResampleWeighting For(ResampleKernel kernel) => kernel switch {
    // A support of zero is what marks point sampling: the resample widens it to half a pixel and
    // stops scaling it, which lands on exactly one source sample however far the size moves.
    ResampleKernel.Nearest => new(kernel, 0.0, 0.0, 0.0),
    ResampleKernel.Box => new(kernel, 0.5, 0.0, 0.0),
    ResampleKernel.Bilinear => new(kernel, 1.0, 0.0, 0.0),
    ResampleKernel.Bicubic => new(kernel, 2.0, 0.0, 0.5),
    ResampleKernel.Mitchell => new(kernel, 2.0, 1.0 / 3.0, 1.0 / 3.0),
    ResampleKernel.Lanczos => new(kernel, 3.0, 0.0, 0.0),
    _ => throw new ArgumentOutOfRangeException(nameof(kernel), kernel, null),
  };

  /// <summary>The weight a sample this far from the destination centre contributes.</summary>
  public double Weight(double distance) {
    var x = Math.Abs(distance);

    switch (this._kernel) {
      case ResampleKernel.Nearest:
      case ResampleKernel.Box:
        // Equal weight throughout the window, and deliberately not truncated at the support: point
        // sampling asks for weights outside it and has to get one.
        return 1.0;
      case ResampleKernel.Bilinear:
        return x < 1.0 ? 1.0 - x : 0.0;
      case ResampleKernel.Bicubic:
      case ResampleKernel.Mitchell:
        if (x < 1.0)
          return this._p0 + x * x * (this._p2 + x * this._p3);
        return x < 2.0 ? this._q0 + x * (this._q1 + x * (this._q2 + x * this._q3)) : 0.0;
      case ResampleKernel.Lanczos:
        // sinc(x) windowed by sinc(x/support): the window's first zero sits on the support, so the
        // three lobes are cut where they already meet zero rather than chopped mid-lobe.
        return _Sinc(x / this.Support) * _Sinc(x);
      default:
        throw new InvalidOperationException("Unknown resample kernel.");
    }
  }

  private static double _Sinc(double x) {
    if (x == 0.0)
      return 1.0;

    var alpha = Math.PI * x;
    return Math.Sin(alpha) / alpha;
  }
}
