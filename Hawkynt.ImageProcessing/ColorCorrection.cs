using System;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing;

/// <summary>A map from one component value to another, both normalised so that full scale is one.</summary>
/// <remarks>
/// The map is allowed to leave the range: a levels curve below its black point is negative and a
/// contrast curve above its white point is greater than one. Clamping happens once, where the
/// components are written back to storage.
/// </remarks>
/// <param name="value">The component to map.</param>
public delegate double ToneCurve(double value);

/// <summary>Whether a correction judges the colour components together or one at a time.</summary>
public enum ChannelScope {

  /// <summary>
  /// One range across every colour component, so a correction moves the colours without moving the
  /// balance between them.
  /// </summary>
  Joint,

  /// <summary>
  /// A range per colour component, so each is stretched to fill the scale on its own. This removes a
  /// colour cast, and by the same mechanism it invents one where the cast was the subject.
  /// </summary>
  PerChannel,
}

/// <summary>
/// The corrections that change what a colour is without changing where it is: brightness, contrast,
/// gamma, saturation, hue, and the levels that combine a black point, a white point and a midpoint.
/// </summary>
/// <remarks>
/// <para><b>Which numbers these are.</b> Every correction here works on the component values as the
/// picture stores them, not on linear light. That is a choice and it is worth stating, because it is
/// the reason a brightness of +20 lifts the shadows more than the highlights: the stored values are
/// gamma-encoded, and the arithmetic is being done on the encoding. The alternative — decode to
/// linear, correct, re-encode — is defensible for some of these and wrong for others (a gamma
/// adjustment is by definition an operation on the encoding), and mixing the two conventions inside
/// one panel of sliders is worse than either. ImageMagick's defaults make the same choice, which is
/// what makes the two comparable at all.</para>
/// <para><b>Alpha is not a colour.</b> No correction here touches the alpha channel. Brightening a
/// picture should not make its transparent parts opaque, and ImageMagick's default channel mask
/// leaves alpha alone for the same reason.</para>
/// <para><b>Indexed pictures stay indexed.</b> A correction on a palette picture is applied to the
/// palette, which is both faster and lossless: the indices are untouched, the palette keeps its
/// size, and nothing has to be re-quantised. Corrections are the one family where that works —
/// anything that mixes neighbouring pixels cannot stay indexed, and those operations say so.</para>
/// </remarks>
public static class ColorCorrection {

  /// <summary>Applies an arbitrary curve to every colour component.</summary>
  public static RawImage ApplyCurve(RawImage image, ToneCurve curve) {
    ArgumentNullException.ThrowIfNull(image);
    ArgumentNullException.ThrowIfNull(curve);

    return _PerColor(image, _ => curve);
  }

  /// <summary>
  /// Shifts the tone scale and stretches it about its middle in one pass.
  /// </summary>
  /// <param name="image">The picture to correct.</param>
  /// <param name="brightness">Percent to shift by; 0 leaves the picture alone, 100 drives it white.</param>
  /// <param name="contrast">
  /// Percent to stretch by; 0 leaves the picture alone, 100 would be an infinite slope and is
  /// refused, and negative values flatten towards mid grey.
  /// </param>
  /// <remarks>
  /// One straight line rather than two passes, because two passes clip twice: a brightness pass that
  /// drives highlights to white loses the detail a following contrast pass would have pulled back
  /// down. Combining them into <c>slope * value + intercept</c> means the only clip is the final one.
  /// <para/>
  /// The slope is <c>100/(100-contrast)</c> above zero and <c>1+contrast/100</c> below it, so that
  /// the two halves of the control meet at a slope of one and neither half can invert the picture.
  /// The intercept places the pivot at mid grey, so raising contrast does not also darken or lighten.
  /// </remarks>
  public static RawImage BrightnessContrast(RawImage image, double brightness, double contrast) {
    ArgumentNullException.ThrowIfNull(image);
    if (contrast >= 100.0)
      throw new ArgumentOutOfRangeException(
        nameof(contrast), contrast, "A contrast of 100 percent is an infinitely steep curve.");

    var slope = contrast < 0.0 ? 0.01 * contrast + 1.0 : 100.0 / (100.0 - contrast);
    var intercept = (0.01 * brightness - 0.5) * slope + 0.5;
    return _PerColor(image, _ => value => slope * value + intercept);
  }

  /// <summary>Shifts the tone scale without stretching it.</summary>
  /// <param name="image">The picture to correct.</param>
  /// <param name="brightness">Percent to shift by; 0 leaves the picture alone.</param>
  public static RawImage Brightness(RawImage image, double brightness)
    => BrightnessContrast(image, brightness, 0.0);

  /// <summary>Stretches the tone scale about mid grey without shifting it.</summary>
  /// <param name="image">The picture to correct.</param>
  /// <param name="contrast">Percent to stretch by; 0 leaves the picture alone.</param>
  public static RawImage Contrast(RawImage image, double contrast)
    => BrightnessContrast(image, 0.0, contrast);

  /// <summary>
  /// Bends the tone scale without moving either end of it.
  /// </summary>
  /// <param name="image">The picture to correct.</param>
  /// <param name="gamma">
  /// The exponent's reciprocal, following the convention every image tool uses: above one lightens
  /// the midtones, below one darkens them, and one leaves the picture alone.
  /// </param>
  /// <remarks>
  /// The exponent applied is <c>1/gamma</c>, not <c>gamma</c>. That inversion is the convention —
  /// a gamma of 2.2 undoes an encoding of 2.2 — and it is old enough that reversing it to be
  /// "clearer" would only be a trap for the next caller.
  /// </remarks>
  public static RawImage Gamma(RawImage image, double gamma) {
    ArgumentNullException.ThrowIfNull(image);
    if (gamma <= 0.0)
      throw new ArgumentOutOfRangeException(nameof(gamma), gamma, "A gamma has to be greater than zero.");

    var exponent = 1.0 / gamma;
    return _PerColor(image, _ => value => value <= 0.0 ? value : Math.Pow(value, exponent));
  }

  /// <summary>
  /// Maps a chosen black point to black and a chosen white point to white, bending what is between.
  /// </summary>
  /// <param name="image">The picture to correct.</param>
  /// <param name="blackPoint">The value that becomes black, normalised so that full scale is one.</param>
  /// <param name="whitePoint">The value that becomes white, normalised so that full scale is one.</param>
  /// <param name="midpointGamma">
  /// Where the midtone sits between the two points: above one lifts it, below one drops it, one
  /// leaves the mapping straight.
  /// </param>
  /// <remarks>
  /// Values below the black point come out negative and are clamped to black on the way to storage;
  /// values above the white point come out greater than one and are clamped to white. Clamping
  /// before the exponent — which is the obvious way to write this — would flatten every one of them
  /// to exactly black or exactly white *and* make the curve's slope at the ends wrong.
  /// </remarks>
  public static RawImage Levels(
    RawImage image, double blackPoint, double whitePoint, double midpointGamma = 1.0) {
    ArgumentNullException.ThrowIfNull(image);
    if (midpointGamma <= 0.0)
      throw new ArgumentOutOfRangeException(
        nameof(midpointGamma), midpointGamma, "A midpoint gamma has to be greater than zero.");

    var curve = _LevelCurve(blackPoint, whitePoint, midpointGamma);
    return _PerColor(image, _ => curve);
  }

  /// <summary>
  /// Finds the darkest and lightest values the picture actually contains and stretches them to fill
  /// the scale.
  /// </summary>
  /// <param name="image">The picture to correct.</param>
  /// <param name="scope">Whether to stretch the colour components together or one at a time.</param>
  /// <remarks>
  /// <see cref="ChannelScope.Joint"/> is the default because it is the safe one: it fixes a picture
  /// that is merely flat without deciding that its colour cast was a mistake.
  /// <para/>
  /// A picture with no range at all — one flat colour — is returned untouched rather than stretched
  /// by a division by zero.
  /// </remarks>
  public static RawImage AutoLevels(RawImage image, ChannelScope scope = ChannelScope.Joint) {
    ArgumentNullException.ThrowIfNull(image);

    // The range has to come from the pixels even when the correction will be applied to a palette:
    // a palette entry no pixel uses is not part of the picture's range.
    var probe = PixelSurfaces.Read(image);
    var channels = probe.ColorChannels;

    if (scope == ChannelScope.Joint) {
      var (min, max) = _Range(probe, 0, channels);
      if (max - min < _FLAT)
        return image;

      var curve = _LevelCurve(min, max, 1.0);
      return _PerColor(image, _ => curve);
    }

    var curves = new ToneCurve[channels];
    var moved = false;
    for (var channel = 0; channel < channels; ++channel) {
      var (min, max) = _Range(probe, channel, channel + 1);
      if (max - min < _FLAT) {
        curves[channel] = static value => value;
        continue;
      }

      curves[channel] = _LevelCurve(min, max, 1.0);
      moved = true;
    }

    return moved ? _PerColor(image, channel => curves[Math.Min(channel, channels - 1)]) : image;
  }

  /// <summary>
  /// Spreads each colour component's values so that every level is used about equally often.
  /// </summary>
  /// <param name="image">The picture to correct.</param>
  /// <param name="scope">
  /// <see cref="ChannelScope.Joint"/> builds one histogram from luma and applies its map to every
  /// colour component, as <c>-equalize</c> does, which moves tone without much moving hue.
  /// <see cref="ChannelScope.PerChannel"/> equalises each component on its own, as
  /// <c>-channel RGB -equalize</c> does, which also pulls the colour balance towards grey.
  /// </param>
  /// <remarks>
  /// A tool for recovering detail from a flat picture, not a neutral correction: the map depends on
  /// the whole picture, so the same pixel value can land differently in two pictures.
  /// </remarks>
  public static RawImage Equalize(RawImage image, ChannelScope scope = ChannelScope.Joint) {
    ArgumentNullException.ThrowIfNull(image);

    var maps = Equalization.Maps(PixelSurfaces.Read(image), scope == ChannelScope.Joint);
    ToneCurve identity = static value => value;
    var curves = new ToneCurve[maps.Length];
    for (var channel = 0; channel < maps.Length; ++channel) {
      var map = maps[channel];
      curves[channel] = map is null ? identity : value => map[Equalization.Bin(value)];
    }

    return _PerColor(image, channel => curves[Math.Min(channel, curves.Length - 1)]);
  }

  /// <summary>Scales how colourful every pixel is, leaving its lightness and its hue alone.</summary>
  /// <param name="image">The picture to correct.</param>
  /// <param name="percent">100 leaves the picture alone, 0 makes it grey, above 100 deepens it.</param>
  public static RawImage Saturation(RawImage image, double percent) => Modulate(image, 100.0, percent, 0.0);

  /// <summary>Turns every hue by the same angle, leaving lightness and saturation alone.</summary>
  /// <param name="image">The picture to correct.</param>
  /// <param name="degrees">
  /// How far round the colour wheel to turn; 0 and any multiple of 360 leave the picture alone.
  /// </param>
  public static RawImage HueRotation(RawImage image, double degrees)
    => Modulate(image, 100.0, 100.0, degrees);

  /// <summary>Scales lightness and saturation and turns hue, in one pass through HSL.</summary>
  /// <param name="image">The picture to correct.</param>
  /// <param name="lightnessPercent">100 leaves lightness alone.</param>
  /// <param name="saturationPercent">100 leaves saturation alone, 0 makes the picture grey.</param>
  /// <param name="hueDegrees">How far round the colour wheel to turn; 0 leaves hue alone.</param>
  /// <remarks>
  /// One pass rather than three, because each of the three is a round trip through HSL and the trip
  /// is not free of loss: it clips, and three clips leave marks a single one does not.
  /// <para/>
  /// On a grey picture this reduces to scaling the grey. Saturation is already zero, so scaling it
  /// changes nothing and there is no hue for an angle to turn — which is the same answer a colour
  /// pipeline would give, arrived at without expanding one component into three.
  /// </remarks>
  public static RawImage Modulate(
    RawImage image, double lightnessPercent, double saturationPercent, double hueDegrees) {
    ArgumentNullException.ThrowIfNull(image);
    if (saturationPercent < 0.0)
      throw new ArgumentOutOfRangeException(
        nameof(saturationPercent), saturationPercent, "Saturation cannot be negative.");

    var lightness = 0.01 * lightnessPercent;
    var saturation = 0.01 * saturationPercent;
    var hueOffset = hueDegrees / 360.0;

    // Asking for no change and getting a round trip through HSL back would cost a component value
    // here and there to rounding, for nothing.
    if (lightness == 1.0 && saturation == 1.0 && hueOffset % 1.0 == 0.0)
      return image;

    return _PerPixel(image, (red, green, blue) => {
      var (hue, colourfulness, brightness) = Hsl.FromRgb(red, green, blue);
      return Hsl.ToRgb(hue + hueOffset, colourfulness * saturation, brightness * lightness);
    });
  }

  /// <summary>A range narrower than this is not a range, and stretching it would be a division by zero.</summary>
  private const double _FLAT = 1.0e-9;

  private static ToneCurve _LevelCurve(double blackPoint, double whitePoint, double gamma) {
    var span = whitePoint - blackPoint;
    if (Math.Abs(span) < _FLAT)
      return static value => value;

    var scale = 1.0 / span;
    var exponent = 1.0 / gamma;
    return value => {
      var scaled = scale * (value - blackPoint);
      return scaled < 0.0 ? scaled : Math.Pow(scaled, exponent);
    };
  }

  private static (double Min, double Max) _Range(PixelSurface surface, int firstChannel, int lastChannel) {
    double min = 1.0, max = 0.0;
    var samples = surface.Samples;
    var stride = surface.Channels;

    for (var at = 0; at < samples.Length; at += stride)
      for (var channel = firstChannel; channel < lastChannel; ++channel) {
        var value = samples[at + channel];
        if (value < min)
          min = value;
        if (value > max)
          max = value;
      }

    return (min, max);
  }

  /// <summary>Runs a per-component curve over the colour components, and only those.</summary>
  private static RawImage _PerColor(RawImage image, Func<int, ToneCurve> curveFor) {
    if (image.IsIndexed)
      return _PalettePerColor(image, curveFor);

    var surface = PixelSurfaces.Read(image);
    var samples = surface.Samples;
    var stride = surface.Channels;
    var colors = surface.ColorChannels;

    for (var channel = 0; channel < colors; ++channel) {
      var curve = curveFor(channel);
      for (var at = channel; at < samples.Length; at += stride)
        samples[at] = (float)curve(samples[at]);
    }

    return PixelSurfaces.Write(surface, image);
  }

  /// <summary>Runs a per-pixel colour map over the picture.</summary>
  private static RawImage _PerPixel(RawImage image, Func<double, double, double, (double, double, double)> map) {
    if (image.IsIndexed)
      return _PalettePerPixel(image, map);

    var surface = PixelSurfaces.Read(image);
    var samples = surface.Samples;
    var stride = surface.Channels;

    if (surface.IsGrayscale) {
      // A grey pixel has no hue and no saturation, so the three components it would expand into
      // come back equal. Taking one of them is not an approximation.
      for (var at = 0; at < samples.Length; at += stride) {
        var grey = samples[at];
        samples[at] = (float)map(grey, grey, grey).Item1;
      }

      return PixelSurfaces.Write(surface, image);
    }

    for (var at = 0; at < samples.Length; at += stride) {
      var (red, green, blue) = map(samples[at], samples[at + 1], samples[at + 2]);
      samples[at] = (float)red;
      samples[at + 1] = (float)green;
      samples[at + 2] = (float)blue;
    }

    return PixelSurfaces.Write(surface, image);
  }

  private static RawImage _PalettePerColor(RawImage image, Func<int, ToneCurve> curveFor) {
    var palette = _CopyPalette(image, out var entries);
    ToneCurve[] curves = [curveFor(0), curveFor(1), curveFor(2)];

    for (var entry = 0; entry < entries; ++entry)
      for (var channel = 0; channel < 3; ++channel) {
        var at = entry * 3 + channel;
        palette[at] = _ToByte(curves[channel](palette[at] * (1.0 / 255.0)));
      }

    return _WithPalette(image, palette);
  }

  private static RawImage _PalettePerPixel(
    RawImage image, Func<double, double, double, (double, double, double)> map) {
    var palette = _CopyPalette(image, out var entries);

    for (var entry = 0; entry < entries; ++entry) {
      var at = entry * 3;
      var (red, green, blue) = map(
        palette[at] * (1.0 / 255.0), palette[at + 1] * (1.0 / 255.0), palette[at + 2] * (1.0 / 255.0));
      palette[at] = _ToByte(red);
      palette[at + 1] = _ToByte(green);
      palette[at + 2] = _ToByte(blue);
    }

    return _WithPalette(image, palette);
  }

  private static byte[] _CopyPalette(RawImage image, out int entries) {
    var palette = image.Palette
      ?? throw new ArgumentException("An indexed picture without a palette has no colours to correct.", nameof(image));

    entries = Math.Min(image.PaletteCount, palette.Length / 3);
    return (byte[])palette.Clone();
  }

  private static RawImage _WithPalette(RawImage image, byte[] palette) => new() {
    Width = image.Width,
    Height = image.Height,
    Format = image.Format,
    PixelData = image.PixelData,
    Palette = palette,
    PaletteCount = image.PaletteCount,
    AlphaTable = image.AlphaTable,
    Metadata = image.Metadata,
    ColorInfo = image.ColorInfo,
  };

  private static byte _ToByte(double value)
    => value <= 0.0 ? (byte)0 : value >= 1.0 ? (byte)255 : (byte)(value * 255.0 + 0.5);
}
