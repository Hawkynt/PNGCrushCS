using System;

namespace FileFormat.Pdf.Rendering.Icc;

/// <summary>
/// A transform from an input profile to sRGB, 8 bits in and 8 bits out, built the way Little CMS links
/// two profiles with black-point compensation and without optimisation.
/// </summary>
/// <remarks>
/// <para>
/// That is the transform poppler asks Little CMS for (cmsFLAGS_NOOPTIMIZE |
/// cmsFLAGS_BLACKPOINTCOMPENSATION, the graphics state's rendering intent, the built-in sRGB profile as
/// the display), so matching it is matching the oracle: on patches painted through FOGRA39 and
/// Ghostscript's CMYK, Adobe RGB, Rec. 709 and a grey profile, pdftoppm agrees with Little CMS's
/// transform to the unit under every intent.
/// </para>
/// <para>
/// Converted from Little CMS 2 (MIT): DefaultICCintents, ComputeConversion, AddConversion and
/// ComputeBlackPointCompensation of cmscnvrt.c, cmsDetectBlackPoint and its helpers of cmssamp.c,
/// cmsCreate_sRGBProfile of cmsvirt.c with _cmsBuildRGB2XYZtransferMatrix of cmswtpnt.c, and the 8/16-bit
/// formatters' rounding. Device-to-PCS float tags (D2Bx) are not read; a profile carrying only those is
/// left to the colour space's alternate.
/// </para>
/// </remarks>
internal sealed class IccTransform {
  private static readonly double[] _SrgbGamma = [2.4, 1.0 / 1.055, 0.055 / 1.055, 1.0 / 12.92, 0.04045];
  private static readonly Lazy<double[]> _SrgbToXyz = new(_BuildSrgbMatrix);

  private readonly IccPipeline _pipeline;

  private IccTransform(IccPipeline pipeline) => this._pipeline = pipeline;

  public int Inputs => this._pipeline.Inputs;

  /// <summary>Links a profile, used as input, to sRGB under an intent; null when the profile cannot be used.</summary>
  public static IccTransform? Create(IccProfile profile, IccIntent intent) {
    if (profile.IsLinkOrAbstractOrNamed || profile.Channels == 0)
      return null;

    var input = profile.InputPipeline(intent);
    if (input == null || input.Inputs != profile.Channels || input.Outputs != 3)
      return null;

    var pipeline = new IccPipeline(profile.Channels, 3);
    pipeline.Stages.AddRange(input.Stages);

    // ComputeConversion: absolute colorimetric scales by the white points; the others get black-point compensation.
    double[] m = [1, 0, 0, 0, 1, 0, 0, 0, 1];
    double[] offset = [0, 0, 0];
    if (intent == IccIntent.AbsoluteColorimetric) {
      var white = profile.MediaWhitePoint;
      m = [white.X / IccMath.D50X, 0, 0, 0, white.Y / IccMath.D50Y, 0, 0, 0, white.Z / IccMath.D50Z];
    } else {
      var black = DetectBlackPoint(profile, intent);
      if (black.X != 0 || black.Y != 0 || black.Z != 0)
        (m, offset) = _BlackPointCompensation(black);
    }

    for (var k = 0; k < 3; ++k)
      offset[k] /= IccMath.MaxEncodeableXyz;

    // AddConversion: into XYZ, then the matrix unless it is (nearly) the identity.
    if (profile.Pcs == IccProfile.Lab)
      pipeline.Stages.Add(IccStage.LabToXyz());
    if (!_IsEmptyLayer(m, offset))
      pipeline.Stages.Add(IccStage.Matrix(3, 3, m, offset));

    // The built-in sRGB profile used for output (BuildRGBOutputMatrixShaper).
    var inverse = IccMath.Inverse(_SrgbToXyz.Value)!;
    for (var i = 0; i < 9; ++i)
      inverse[i] *= IccMath.OutpAdj;
    var curve = IccToneCurve.Parametric(-4, _SrgbGamma);
    pipeline.Stages.Add(IccStage.Matrix(3, 3, inverse, null));
    pipeline.Stages.Add(IccStage.Curves([curve, curve, curve]));
    return new IccTransform(pipeline);
  }

  /// <summary>Converts one colour, 8 bits per component, to 8-bit sRGB (8 → 16 → pipeline → 16 → 8).</summary>
  public void Convert(ReadOnlySpan<byte> input, Span<byte> rgb) {
    Span<float> a = stackalloc float[this._pipeline.Inputs];
    Span<float> o = stackalloc float[3];
    for (var i = 0; i < a.Length; ++i)
      a[i] = (float)(input[i] * 257 / 65535.0);

    this._pipeline.Evaluate(a, o);
    for (var i = 0; i < 3; ++i) {
      uint w = IccMath.SaturateWord(o[i] * 65535.0);
      rgb[i] = (byte)(((w * 65281u + 8388608u) >> 24) & 0xFF);
    }
  }

  /// <summary>cmsDetectBlackPoint, for a profile used as input, in D50 XYZ.</summary>
  public static (double X, double Y, double Z) DetectBlackPoint(IccProfile profile, IccIntent intent) {
    if (profile.IsLinkOrAbstractOrNamed || intent == IccIntent.AbsoluteColorimetric)
      return (0, 0, 0);

    if (profile.IsV4 && intent is IccIntent.Perceptual or IccIntent.Saturation) {
      if (profile.IsMatrixShaper)
        return _DarkerColorant(profile, IccIntent.RelativeColorimetric);
      return (0.00336, 0.0034731, 0.00287);
    }

    if (intent == IccIntent.RelativeColorimetric && profile.IsOutputClass && profile.IsInkSpace)
      return _PerceptualBlack(profile);

    return _DarkerColorant(profile, intent);
  }

  /// <summary>BlackPointAsDarkerColorant: the darkest colorant to Lab, forced neutral and clipped.</summary>
  private static (double X, double Y, double Z) _DarkerColorant(IccProfile profile, IccIntent intent) {
    if (!profile.SupportsIntent(intent, true))
      return (0, 0, 0);

    ushort[]? black = profile.ColorSpace switch {
      IccProfile.Gray => [0],
      IccProfile.Rgb => [0, 0, 0],
      IccProfile.Cmyk => [0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF],
      IccProfile.Cmy => [0xFFFF, 0xFFFF, 0xFFFF],
      IccProfile.Lab => [0, 0x8080, 0x8080],
      _ => null,
    };
    var pipeline = profile.InputPipeline(intent);
    if (black == null || pipeline == null || pipeline.Inputs != black.Length)
      return (0, 0, 0);

    Span<float> input = stackalloc float[black.Length];
    for (var i = 0; i < black.Length; ++i)
      input[i] = (float)(black[i] / 65535.0);
    var l = _ToLab(profile, pipeline, input).L;
    if (l > 95 || l < 0)
      l = 0;
    else if (l > 50)
      l = 50;
    return IccMath.LabToXyz(l, 0, 0);
  }

  /// <summary>BlackPointUsingPerceptualBlack: Lab 0 through the perceptual output table and back through the colorimetric input one.</summary>
  private static (double X, double Y, double Z) _PerceptualBlack(IccProfile profile) {
    if (!profile.SupportsIntent(IccIntent.Perceptual, true))
      return (0, 0, 0);

    var toDevice = profile.OutputPipeline(IccIntent.Perceptual);
    var toPcs = profile.InputPipeline(IccIntent.RelativeColorimetric);
    if (toDevice == null || toPcs == null)
      return (0, 0, 0);

    // Lab 0 is XYZ 0. Linking a v4 profile under the perceptual intent forces black-point compensation
    // (_cmsLinkProfiles), from the Lab profile's black (zero) to this profile's perceptual black, which
    // for a v4 LUT profile is the fixed one: XYZ 0 lands on that black itself.
    (double X, double Y, double Z) xyz = (0, 0, 0);
    if (profile.IsV4)
      xyz = profile.IsMatrixShaper ? _DarkerColorant(profile, IccIntent.RelativeColorimetric) : (0.00336, 0.0034731, 0.00287);

    Span<float> pcs = stackalloc float[3];
    pcs[0] = (float)(xyz.X / IccMath.MaxEncodeableXyz);
    pcs[1] = (float)(xyz.Y / IccMath.MaxEncodeableXyz);
    pcs[2] = (float)(xyz.Z / IccMath.MaxEncodeableXyz);
    if (profile.Pcs == IccProfile.Lab)
      IccStage.XyzToLab().Evaluate(pcs.ToArray(), pcs);

    Span<float> device = stackalloc float[IccMath.MaxChannels + 1];
    toDevice.Evaluate(pcs, device);
    var l = _ToLab(profile, toPcs, device[..toPcs.Inputs]).L;
    if (l > 50)
      l = 50;
    return IccMath.LabToXyz(l, 0, 0);
  }

  private static (double L, double A, double B) _ToLab(IccProfile profile, IccPipeline pipeline, ReadOnlySpan<float> input) {
    Span<float> pcs = stackalloc float[3];
    pipeline.Evaluate(input, pcs);
    if (profile.Pcs == IccProfile.Xyz)
      IccStage.XyzToLab().Evaluate(pcs.ToArray(), pcs);
    return (pcs[0] * 100.0, pcs[1] * 255.0 - 128.0, pcs[2] * 255.0 - 128.0);
  }

  /// <summary>ComputeBlackPointCompensation towards a zero output black: a scale and offset in XYZ.</summary>
  private static (double[] M, double[] Offset) _BlackPointCompensation((double X, double Y, double Z) black) {
    double[] d50 = [IccMath.D50X, IccMath.D50Y, IccMath.D50Z];
    double[] bp = [black.X, black.Y, black.Z];
    var m = new double[9];
    var offset = new double[3];
    for (var i = 0; i < 3; ++i) {
      var t = bp[i] - d50[i];
      m[i * 4] = (0 - d50[i]) / t;
      offset[i] = -d50[i] * (0 - bp[i]) / t;
    }

    return (m, offset);
  }

  private static bool _IsEmptyLayer(double[] m, double[] offset) {
    double diff = 0;
    for (var i = 0; i < 9; ++i)
      diff += Math.Abs(m[i] - (i % 4 == 0 ? 1 : 0));
    foreach (var o in offset)
      diff += Math.Abs(o);
    return diff < 0.002;
  }

  /// <summary>The colorants of Little CMS's built-in sRGB: Rec. 709 primaries and D65 white, Bradford-adapted to D50.</summary>
  private static double[] _BuildSrgbMatrix() {
    double xn = 0.3127, yn = 0.3290;
    double xr = 0.64, yr = 0.33, xg = 0.30, yg = 0.60, xb = 0.15, yb = 0.06;
    double[] primaries = [xr, xg, xb, yr, yg, yb, 1 - xr - yr, 1 - xg - yg, 1 - xb - yb];
    var inverse = IccMath.Inverse(primaries)!;
    var (cx, cy, cz) = IccMath.Apply(inverse, xn / yn, 1.0, (1.0 - xn - yn) / yn);
    double[] m = [
      cx * xr, cy * xg, cz * xb,
      cx * yr, cy * yg, cz * yb,
      cx * (1 - xr - yr), cy * (1 - xg - yg), cz * (1 - xb - yb),
    ];

    // _cmsAdaptMatrixToD50: the white's xyY to XYZ, then Bradford to D50.
    var white = (xn / yn, 1.0, (1 - xn - yn) / yn);
    var adapt = IccMath.Bradford(white, (IccMath.D50X, IccMath.D50Y, IccMath.D50Z));
    return IccMath.Multiply(adapt, m);
  }
}
