using System;
using System.Collections.Generic;
using FileFormat.Pdf.Document;
using FileFormat.Pdf.Rendering.Icc;

namespace FileFormat.Pdf.Rendering;

/// <summary>Colour conversion through an embedded ICC profile, where the profile is one this can evaluate.</summary>
internal static class PdfIccProfile {

  /// <summary>A colour model that evaluates the profile, or null to fall back on the alternate space.</summary>
  public static PdfColorModel? TryCreate(PdfDocument document, PdfStreamObject stream, int components, PdfColorModel alternate, double[]? range) {
    byte[] data;
    try {
      data = document.Decode(stream);
    } catch (Exception e) when (e is NotSupportedException or InvalidOperationException or System.IO.InvalidDataException) {
      return null;
    }

    var profile = IccProfile.TryParse(data);
    if (profile == null || profile.Channels != components || profile.IsLinkOrAbstractOrNamed || profile.ColorSpace is IccProfile.Lab or IccProfile.Xyz)
      return null;

    IccTransform? relative;
    try {
      relative = IccTransform.Create(profile, IccIntent.RelativeColorimetric);
    } catch (Exception e) when (e is InvalidOperationException or IndexOutOfRangeException or ArgumentException) {
      return null;
    }

    return relative == null ? null : new IccColorModel(new IccShared(profile, relative), IccIntent.RelativeColorimetric, alternate, range);
  }

  /// <summary>The PDF rendering intent names of §8.6.5.8 (unknown names are relative colorimetric).</summary>
  public static IccIntent IntentOf(string? name) => name switch {
    "Perceptual" => IccIntent.Perceptual,
    "Saturation" => IccIntent.Saturation,
    "AbsoluteColorimetric" => IccIntent.AbsoluteColorimetric,
    _ => IccIntent.RelativeColorimetric,
  };

  private sealed class IccShared(IccProfile profile, IccTransform relative) {
    private readonly IccTransform?[] _transforms = [null, relative, null, null];
    private readonly bool[] _tried = [false, true, false, false];
    public readonly Dictionary<long, int>[] Caches = [[], [], [], []];

    public IccTransform Transform(IccIntent intent) {
      var i = (int)intent;
      if (!this._tried[i]) {
        this._tried[i] = true;
        try {
          this._transforms[i] = IccTransform.Create(profile, intent);
        } catch (Exception e) when (e is InvalidOperationException or IndexOutOfRangeException or ArgumentException) {
          this._transforms[i] = null;
        }
      }

      return this._transforms[i] ?? this._transforms[1]!;
    }
  }

  /// <summary>
  /// An ICCBased space evaluated through its profile. Components are taken to 8 bits the way poppler
  /// does (its 16.16 colour component through colToByte) before the profile sees them, and each
  /// distinct colour is converted once.
  /// </summary>
  private sealed class IccColorModel(IccShared shared, IccIntent intent, PdfColorModel alternate, double[]? range) : PdfColorModel {
    public override int Components => alternate.Components;
    public override string Name => "ICCBased";
    public override double[] InitialColor() => alternate.InitialColor();

    public override (double Min, double Max) DefaultDecode(int component, int bitsPerComponent)
      => range != null && range.Length >= 2 * component + 2 ? (range[2 * component], range[2 * component + 1]) : alternate.DefaultDecode(component, bitsPerComponent);

    public override PdfColorModel ForIntent(string? name) {
      var wanted = IntentOf(name);
      return wanted == intent ? this : new IccColorModel(shared, wanted, alternate, range);
    }

    public override void ToRgb(ReadOnlySpan<double> c, Span<double> rgb) {
      var n = alternate.Components;
      Span<byte> bytes = stackalloc byte[n];
      long key = 0;
      for (var i = 0; i < n; ++i) {
        var v = Math.Clamp(c[i], 0, 1);
        // dblToCol then colToByte: (int)(v * 65536), then ((x << 8) - x + 0x8000) >> 16.
        var col = (int)(v * 65536);
        bytes[i] = (byte)Math.Min(255, ((col << 8) - col + 0x8000) >> 16);
        key = (key << 8) | bytes[i];
      }

      var cache = shared.Caches[(int)intent];
      var cacheable = n <= 7;
      if (!cacheable || !cache.TryGetValue(key, out var packed)) {
        Span<byte> o = stackalloc byte[3];
        shared.Transform(intent).Convert(bytes, o);
        packed = o[0] << 16 | o[1] << 8 | o[2];
        if (cacheable && cache.Count < 1 << 20)
          cache[key] = packed;
      }

      rgb[0] = (packed >> 16) / 255.0;
      rgb[1] = ((packed >> 8) & 0xFF) / 255.0;
      rgb[2] = (packed & 0xFF) / 255.0;
    }
  }
}
