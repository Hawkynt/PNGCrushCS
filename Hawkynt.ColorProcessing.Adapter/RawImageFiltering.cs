using System;
using System.Runtime.InteropServices;
using System.Drawing.Extensions.ColorProcessing.Resizing;
using FileFormat.Core;
using Hawkynt.ColorProcessing.Codecs;
using Hawkynt.ColorProcessing.ColorMath;
using Hawkynt.ColorProcessing.Filtering;
using Hawkynt.ColorProcessing.Metrics;
using Hawkynt.ColorProcessing.Metrics.Rgb;
using Hawkynt.ColorProcessing.Resizing;
using Hawkynt.ColorProcessing.Spaces.Perceptual;
using Hawkynt.ColorProcessing.Storage;
using Hawkynt.ColorProcessing.Working;

namespace Hawkynt.ColorProcessing.Adapter;

/// <summary>
/// Runs the colour library's pixel filters and pixel-art rescalers over a <see cref="RawImage"/>.
/// </summary>
/// <remarks>
/// <para>
/// The same situation as <see cref="RawImageQuantization"/>: every filter and rescaler in the colour
/// library works on typed pointers through its scaler pipeline, and the only thing that tied them to
/// Windows was the adapter that locked a <c>Bitmap</c> to get those pointers. This is that adapter over
/// a pinned byte array. Nothing here decides what a pixel becomes; it hands the library a frame and
/// takes the frame back.
/// </para>
/// <para>
/// The routing is the library's own, taken over unchanged so that a filter gives the same answer here
/// as through its bitmap entry point: a filter that asks for frame access goes through the resample
/// pipeline in linear light with an Oklab key (with the one repair to its encoder that
/// <see cref="LinearToSrgb"/> explains), a multi-pass filter is run as many times as it says,
/// and everything else goes through the fast path on the stored BGRA components. Rescalers take the
/// fast path too, which is also the bitmap entry point's default.
/// </para>
/// <para>
/// The result is BGRA32 when the picture had transparency and RGB24 when it did not. A palette
/// picture comes back as one of the two, because a filtered or doubled picture has colours, or pixel
/// positions, the palette never described.
/// </para>
/// </remarks>
public static class RawImageFiltering {

  /// <summary>Applies a same-size pixel filter.</summary>
  /// <typeparam name="TFilter">The filter, as the value type the library declares it as.</typeparam>
  /// <param name="image">The picture to filter. It is not changed.</param>
  /// <param name="filter">The filter, configured.</param>
  public static RawImage Filter<TFilter>(RawImage image, TFilter filter) where TFilter : struct, IPixelFilter {
    ArgumentNullException.ThrowIfNull(image);

    var source = _Bgra(image);
    if (filter is IFrameFilter { UsesFrameAccess: true } frameFilter)
      return _Like(image, frameFilter.InvokeFrameKernel<
        LinearRgbaF, OklabF, Bgra8888,
        Srgb32ToLinearRgbaF, LinearRgbaFToOklabF, LinearToSrgb, Frame>(
        new FrameCallback<LinearRgbaF, OklabF, Srgb32ToLinearRgbaF, LinearRgbaFToOklabF, LinearToSrgb>(source),
        source.Width, source.Height));

    var passes = filter is IMultiPassFilter multiPass ? Math.Max(1, multiPass.PassCount) : 1;
    var current = source;
    for (var pass = 0; pass < passes; ++pass)
      current = _Fast(current, filter);

    return _Like(image, current);
  }

  /// <summary>Enlarges a picture by a pixel-art rule, at the factor the rescaler is configured for.</summary>
  /// <typeparam name="TScaler">The rescaler, as the value type the library declares it as.</typeparam>
  /// <param name="image">The picture to enlarge. It is not changed.</param>
  /// <param name="scaler">The rescaler, configured.</param>
  public static RawImage Upscale<TScaler>(RawImage image, TScaler scaler) where TScaler : struct, IRescaler {
    ArgumentNullException.ThrowIfNull(image);
    return _Like(image, _Fast(_Bgra(image), scaler));
  }

  private static Frame _Fast<TFilter>(Frame source, TFilter filter) where TFilter : struct, IPixelFilter
    => filter.InvokeKernel<
      Bgra8888, Bgra8888, Bgra8888,
      CompuPhaseSquared4<Bgra8888>, ExactEquality<Bgra8888>,
      Color4BLerpInt<Bgra8888>, IdentityEncode<Bgra8888>, Frame>(new KernelCallback(source));

  private static Frame _Fast(Frame source, IRescaler scaler)
    => scaler.InvokeKernel<
      Bgra8888, Bgra8888, Bgra8888,
      CompuPhaseSquared4<Bgra8888>, ExactEquality<Bgra8888>,
      Color4BLerpInt<Bgra8888>, IdentityEncode<Bgra8888>, Frame>(new KernelCallback(source));

  /// <summary>A picture as the pipeline reads it: tightly packed BGRA, one pixel per stride unit.</summary>
  private sealed record Frame(Bgra8888[] Pixels, int Width, int Height);

  private static Frame _Bgra(RawImage image) {
    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32).PixelData;
    var pixels = new Bgra8888[image.Width * image.Height];

    // A copy rather than a reinterpretation of the source's array: a converter may hand back the
    // caller's own buffer when the picture already is BGRA32, and the pipeline must never be able to
    // write into a picture somebody is still holding.
    bgra.AsSpan(0, pixels.Length * 4).CopyTo(MemoryMarshal.AsBytes(pixels.AsSpan()));
    return new(pixels, image.Width, image.Height);
  }

  private static RawImage _Like(RawImage source, Frame frame) {
    var bytes = MemoryMarshal.AsBytes(frame.Pixels.AsSpan()).ToArray();

    // The metadata is carried across as it is, as the resamplers in this suite do: a doubled picture
    // keeps its density and so claims twice the physical size, which is what doubling it means.
    var result = new RawImage {
      Width = frame.Width,
      Height = frame.Height,
      Format = PixelFormat.Bgra32,
      PixelData = bytes,
      Metadata = source.Metadata,
      ColorInfo = source.ColorInfo,
    };

    // A picture that had no transparency does not gain an alpha channel by being filtered: the
    // channel the pipeline carries is all opaque, and handing it on would make every writer after
    // this store a fourth channel of 255s.
    return source.HasAlpha ? result : PixelConverter.Convert(result, PixelFormat.Rgb24);
  }

  /// <summary>Brings linear light back to stored sRGB bytes without banding the shadows.</summary>
  /// <remarks>
  /// <para>
  /// The one departure from the library's own routing, and it is a repair rather than a preference.
  /// The library's encoder looks the answer up in a table indexed by the top eight bits of the
  /// linear value, so linear light is cut into 256 even steps before it is gamma-compressed. The
  /// transfer curve is steepest exactly there: the whole bottom fifth of the stored scale falls into
  /// the first dozen steps, so a flat blue of 20 goes through a frame filter and comes out as 13, and a
  /// dark gradient comes out banded. The decoder on the way in is exact, which is what makes the
  /// damage one-sided.
  /// </para>
  /// <para>
  /// This table has 65,536 entries instead, enough that neighbouring entries differ by well under one
  /// stored level anywhere on the curve. The curve is the one IEC 61966-2-1 defines, with its
  /// constants copied as given: 12.92 below 0.0031308, and 1.055 x^(1/2.4) - 0.055 above.
  /// </para>
  /// </remarks>
  private readonly struct LinearToSrgb : IEncode<LinearRgbaF, Bgra8888> {

    private const int _STEPS = 65536;

    private static readonly byte[] _table = _Build();

    private static byte[] _Build() {
      var table = new byte[_STEPS];
      for (var i = 0; i < _STEPS; ++i) {
        var linear = (i + 0.5) / _STEPS;
        var encoded = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
        table[i] = (byte)Math.Clamp(Math.Round(encoded * 255.0), 0, 255);
      }

      return table;
    }

    public Bgra8888 Encode(in LinearRgbaF color) => new(
      _Compress(color.R), _Compress(color.G), _Compress(color.B),
      (byte)Math.Clamp((int)(color.A * 255f + 0.5f), 0, 255));

    private static byte _Compress(float linear)
      => linear <= 0f ? (byte)0 : linear >= 1f ? (byte)255 : _table[(int)(linear * _STEPS)];
  }

  /// <summary>Receives the concrete kernel and runs the five-by-five pipeline over the frame.</summary>
  private sealed class KernelCallback(Frame source)
    : IKernelCallback<Bgra8888, Bgra8888, Bgra8888, IdentityEncode<Bgra8888>, Frame> {

    public unsafe Frame Invoke<TKernel>(TKernel kernel)
      where TKernel : struct, IScaler<Bgra8888, Bgra8888, Bgra8888, IdentityEncode<Bgra8888>> {
      var width = source.Width * kernel.ScaleX;
      var height = source.Height * kernel.ScaleY;
      var target = new Bgra8888[width * height];

      fixed (Bgra8888* from = source.Pixels)
      fixed (Bgra8888* to = target)
        ScalerPipeline.ExecuteParallel<
          Bgra8888, Bgra8888, Bgra8888,
          IdentityDecode<Bgra8888>, IdentityProject<Bgra8888>, IdentityEncode<Bgra8888>,
          TKernel>(from, source.Width, source.Height, source.Width, to, width, kernel);

      return new(target, width, height);
    }
  }

  /// <summary>Receives the concrete resample kernel for a filter that reaches beyond five by five.</summary>
  private sealed class FrameCallback<TWork, TKey, TDecode, TProject, TEncode>(Frame source)
    : IResampleKernelCallback<TWork, TKey, Bgra8888, TDecode, TProject, TEncode, Frame>
    where TWork : unmanaged, IColorSpace4F<TWork>
    where TKey : unmanaged, IColorSpace
    where TDecode : struct, IDecode<Bgra8888, TWork>
    where TProject : struct, IProject<TWork, TKey>
    where TEncode : struct, IEncode<TWork, Bgra8888> {

    public unsafe Frame Invoke<TKernel>(TKernel kernel)
      where TKernel : struct, IResampleKernel<Bgra8888, TWork, TKey, TDecode, TProject, TEncode> {
      var target = new Bgra8888[source.Width * source.Height];

      fixed (Bgra8888* from = source.Pixels)
      fixed (Bgra8888* to = target)
        ScalerPipeline.ExecuteResampleParallel<Bgra8888, TWork, TKey, TDecode, TProject, TEncode, TKernel>(
          from, source.Width, source.Height, source.Width,
          to, source.Width, source.Height, source.Width,
          kernel);

      return new(target, source.Width, source.Height);
    }
  }
}
