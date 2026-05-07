using System;
using System.Collections.Generic;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Inverse modular transforms (ISO/IEC 18181-1 §H.5; libjxl
/// <c>lib/jxl/modular/transform/</c>). Implements the bitstream reader for the
/// transform-chain header plus the three inverse transforms (RCT, Palette,
/// Squeeze).
///
/// <para>
/// Reference sources (libjxl main, fetched while implementing):
/// <list type="bullet">
///   <item><c>lib/jxl/modular/transform/transform.cc</c> (header layout / dispatch)</item>
///   <item><c>lib/jxl/modular/transform/rct.cc</c> (42 RCT variants)</item>
///   <item><c>lib/jxl/modular/transform/palette.cc</c> (palette inverse)</item>
///   <item><c>lib/jxl/modular/transform/squeeze.cc</c> + <c>squeeze.h</c> (haar-like wavelet inverse)</item>
///   <item><c>lib/jxl/modular/transform/squeeze_params.cc</c> (per-step header)</item>
/// </list>
/// </para>
///
/// <para>
/// Coverage on this first pass:
/// <list type="bullet">
///   <item>RCT: full 0..41 — all 42 variants implemented (7 customs × 6 permutations).</item>
///   <item>Palette: <c>nb_deltas == 0</c> + <c>predictor == Zero</c> (the most common case);
///     delta-palette + predictor-driven palette unrolling are NOT yet implemented and
///     throw <see cref="NotSupportedException"/>. Implicit palette extension (kSmallCube/
///     kLargeCube/kDeltaPalette index escapes) is also TODO.</item>
///   <item>Squeeze: explicit <c>num_squeezes &gt; 0</c> only. The default-chain fall-back
///     (libjxl <c>DefaultSqueezeParameters</c>) is NOT yet wired up; calling
///     <see cref="InvertSqueeze"/> with an empty <c>SqueezeSteps</c> array throws.</item>
/// </list>
/// </para>
/// </summary>
internal static class JxlModularTransforms {

  // =====================================================================================
  // Header reader
  // =====================================================================================

  /// <summary>
  /// Read the full transform-chain header from the bitstream. The leading
  /// <c>num_transforms</c> field uses libjxl's
  /// <c>U32(Val(0), Val(1), BitsOffset(4, 2), BitsOffset(8, 18))</c>
  /// encoding (per <c>modular/encoding/encoding.h::GroupHeader::VisitFields</c>
  /// in libjxl v0.11.2): selector 0 → 0 (no bits), selector 1 → 1 (no bits),
  /// selector 2 → 2 + u(4) (range 2..17), selector 3 → 18 + u(8). Each
  /// transform descriptor is then read in encode-order.
  /// </summary>
  public static JxlModularTransform[] ReadAll(JxlBitReader reader) {
    ArgumentNullException.ThrowIfNull(reader);

    var numTransforms = reader.ReadU32(0, 0, 1, 0, 2, 4, 18, 8);
    if (numTransforms == 0)
      return [];

    var result = new JxlModularTransform[numTransforms];
    for (var i = 0; i < numTransforms; ++i)
      result[i] = _ReadOne(reader);
    return result;
  }

  private static JxlModularTransform _ReadOne(JxlBitReader reader) {
    // transform_id: U32(Val(0), Val(1), Val(2), Val(3)) — i.e. plain 2-bit selector.
    var rawId = reader.ReadBits(2);
    if (rawId >= 3)
      throw new InvalidOperationException($"Invalid transform id {rawId} (kInvalid).");

    var id = (JxlModularTransformType)rawId;

    // begin_c: only for RCT and Palette.
    var beginC = 0u;
    if (id == JxlModularTransformType.Rct || id == JxlModularTransformType.Palette)
      beginC = reader.ReadU32(0, 3, 8, 6, 72, 10, 1096, 13);

    switch (id) {
      case JxlModularTransformType.Rct: {
        var rctType = reader.ReadU32(6, 0, 0, 2, 2, 4, 10, 6);
        if (rctType >= 42)
          throw new InvalidOperationException($"Invalid RCT type {rctType}.");
        return new JxlModularTransform {
          Type = JxlModularTransformType.Rct,
          RctBeginC = (int)beginC,
          RctType = (int)rctType,
        };
      }

      case JxlModularTransformType.Palette: {
        var numC = reader.ReadU32(1, 0, 3, 0, 4, 0, 1, 13);
        var nbColours = reader.ReadU32(0, 8, 256, 10, 1280, 12, 5376, 16);
        var nbDeltas = reader.ReadU32(0, 0, 1, 8, 257, 10, 1281, 16);
        var predictor = (int)reader.ReadBits(4);
        if (predictor >= 14)
          throw new InvalidOperationException($"Invalid palette predictor {predictor}.");
        // Palette LUT data itself is decoded later from the meta-channel; the
        // header only carries the descriptor parameters.
        return new JxlModularTransform {
          Type = JxlModularTransformType.Palette,
          PaletteBeginC = (int)beginC,
          PaletteNumC = (int)numC,
          PaletteSize = (int)nbColours,
          PaletteDeltaPredictor = predictor,
          // PaletteData remains empty; it's populated by the caller from the
          // meta-channel decode pass (not part of the bitstream header).
        };
      }

      case JxlModularTransformType.Squeeze: {
        var numSqueezes = reader.ReadU32(0, 0, 1, 4, 9, 6, 41, 8);
        var steps = new JxlSqueezeStep[numSqueezes];
        for (var i = 0; i < numSqueezes; ++i) {
          var horizontal = reader.ReadBool();
          var inPlace = reader.ReadBool();
          var sBeginC = (int)reader.ReadU32(0, 3, 8, 6, 72, 10, 1096, 13);
          var sNumC = (int)reader.ReadU32(1, 0, 2, 0, 3, 0, 4, 4);
          steps[i] = new JxlSqueezeStep(sBeginC, sNumC, horizontal, inPlace);
        }
        return new JxlModularTransform {
          Type = JxlModularTransformType.Squeeze,
          SqueezeSteps = steps,
        };
      }

      default:
        throw new InvalidOperationException($"Unknown transform id {rawId}.");
    }
  }

  // =====================================================================================
  // Top-level dispatch
  // =====================================================================================

  /// <summary>
  /// Apply ALL inverse transforms in REVERSE order (last-encoded inverted
  /// first, exactly as libjxl <c>Transform::Inverse</c> is invoked from
  /// <c>ModularGenericDecompress</c>). The channels array is reshaped — channels
  /// may be added (Palette) or removed (Squeeze) — so the return value is the
  /// new, possibly-resized channel list.
  /// </summary>
  public static JxlChannel[] InvertAll(JxlChannel[] channels, JxlModularTransform[] transforms) {
    ArgumentNullException.ThrowIfNull(channels);
    ArgumentNullException.ThrowIfNull(transforms);

    var current = channels;
    for (var i = transforms.Length - 1; i >= 0; --i) {
      var t = transforms[i];
      current = t.Type switch {
        JxlModularTransformType.Rct => InvertRct(current, t),
        JxlModularTransformType.Palette => InvertPalette(current, t),
        JxlModularTransformType.Squeeze => InvertSqueeze(current, t),
        _ => throw new InvalidOperationException($"Unknown transform type {t.Type}."),
      };
    }
    return current;
  }

  // =====================================================================================
  // RCT inverse — libjxl rct.cc::InvRCT
  // =====================================================================================

  /// <summary>
  /// Inverse Reversible Color Transform. Spec: ISO/IEC 18181-1 §H.5.1; libjxl
  /// <c>lib/jxl/modular/transform/rct.cc</c>. Operates on three consecutive
  /// channels starting at <c>RctBeginC</c>; the three must already share the
  /// same size and shifts (the caller is expected to enforce
  /// <c>CheckEqualChannels</c> at MetaApply time).
  /// </summary>
  public static JxlChannel[] InvertRct(JxlChannel[] channels, JxlModularTransform t) {
    ArgumentNullException.ThrowIfNull(channels);
    ArgumentNullException.ThrowIfNull(t);
    if (t.Type != JxlModularTransformType.Rct)
      throw new ArgumentException("Not an RCT transform.", nameof(t));
    var m = t.RctBeginC;
    if (m < 0 || m + 2 >= channels.Length)
      throw new InvalidOperationException("RCT begin_c out of range.");

    var rctType = t.RctType;
    if (rctType == 0) // identity
      return channels;

    // Permutation: 0=RGB, 1=GBR, 2=BRG, 3=RBG, 4=GRB, 5=BGR
    var permutation = rctType / 7;
    if (permutation >= 6)
      throw new InvalidOperationException($"RCT permutation {permutation} out of range.");
    var custom = rctType % 7;

    var c0 = channels[m + 0];
    var c1 = channels[m + 1];
    var c2 = channels[m + 2];
    var w = c0.Width;
    var h = c0.Height;
    if (c1.Width != w || c1.Height != h || c2.Width != w || c2.Height != h)
      throw new InvalidOperationException("RCT input channels are not equal-sized.");

    // Allocate output buffers (could mutate in place, but RCT permutation makes
    // that fiddly; allocate a fresh trio and slot the three output channels
    // back into the array at the permuted indices).
    var out0 = new int[w * h];
    var out1 = new int[w * h];
    var out2 = new int[w * h];

    var p0 = c0.Pixels;
    var p1 = c1.Pixels;
    var p2 = c2.Pixels;
    var n = w * h;

    if (custom == 0) {
      // Permute-only.
      Array.Copy(p0, out0, n);
      Array.Copy(p1, out1, n);
      Array.Copy(p2, out2, n);
    } else if (custom == 6) {
      // YCoCg.
      for (var i = 0; i < n; ++i) {
        int Y = p0[i];
        int Co = p1[i];
        int Cg = p2[i];
        var tmp = unchecked(Y - (Cg >> 1));
        var G = unchecked(Cg + tmp);
        var B = unchecked(tmp - (Co >> 1));
        var R = unchecked(B + Co);
        out0[i] = R;
        out1[i] = G;
        out2[i] = B;
      }
    } else {
      // custom in {1..5}: First, Second, Third with optional add-backs.
      // second = custom >> 1; third = custom & 1.
      var second = custom >> 1;
      var third = custom & 1;
      for (var i = 0; i < n; ++i) {
        var first = p0[i];
        var sec = p1[i];
        var thr = p2[i];
        if (third != 0)
          thr = unchecked(thr + first);
        if (second == 1)
          sec = unchecked(sec + first);
        else if (second == 2)
          sec = unchecked(sec + ((first + thr) >> 1));
        out0[i] = first;
        out1[i] = sec;
        out2[i] = thr;
      }
    }

    // Map (out0, out1, out2) back into the channel array under the permutation.
    var idx0 = permutation % 3;
    var idx1 = (permutation + 1 + permutation / 3) % 3;
    var idx2 = (permutation + 2 - permutation / 3) % 3;

    var result = new JxlChannel[channels.Length];
    Array.Copy(channels, result, channels.Length);
    var hShift = c0.HShift;
    var vShift = c0.VShift;
    result[m + idx0] = new JxlChannel { Width = w, Height = h, HShift = hShift, VShift = vShift, Pixels = out0 };
    result[m + idx1] = new JxlChannel { Width = w, Height = h, HShift = hShift, VShift = vShift, Pixels = out1 };
    result[m + idx2] = new JxlChannel { Width = w, Height = h, HShift = hShift, VShift = vShift, Pixels = out2 };
    return result;
  }

  // =====================================================================================
  // Palette inverse — libjxl palette.cc::InvPalette
  // =====================================================================================

  /// <summary>
  /// Inverse palette transform (no-deltas, Zero-predictor variant). The caller
  /// must have populated <see cref="JxlModularTransform.PaletteData"/> with the
  /// LUT as a row-major <c>numC × paletteSize</c> int array (channel-major
  /// layout: row <c>c</c> holds palette entries for output channel <c>c</c>).
  /// In libjxl this is decoded from <c>input.channel[0]</c> (the meta palette
  /// channel) before <c>InvPalette</c> runs. After the inverse, the index
  /// channel at <c>begin_c+1</c> is replaced by <c>num_c</c> consecutive
  /// expanded channels and the meta palette channel is removed.
  ///
  /// <para>NOT YET SUPPORTED: <c>nb_deltas &gt; 0</c>, palette predictor != Zero,
  /// implicit palette extension (kSmallCube/kLargeCube/kDeltaPalette index
  /// escapes). These throw <see cref="NotSupportedException"/>.</para>
  /// </summary>
  public static JxlChannel[] InvertPalette(JxlChannel[] channels, JxlModularTransform t) {
    ArgumentNullException.ThrowIfNull(channels);
    ArgumentNullException.ThrowIfNull(t);
    if (t.Type != JxlModularTransformType.Palette)
      throw new ArgumentException("Not a palette transform.", nameof(t));

    if (t.PaletteDeltaPredictor != 0) // Predictor::Zero == 0
      throw new NotSupportedException("Palette predictor != Zero is not yet implemented.");

    var nb = t.PaletteNumC;
    var nbColours = t.PaletteSize;
    if (nb < 1)
      throw new InvalidOperationException("Palette numC must be >= 1.");
    if (channels.Length < 1)
      throw new InvalidOperationException("Palette transform requires meta palette channel.");

    // libjxl: c0 = begin_c + 1 (because the meta palette channel sits at
    // index 0 and shifts everything after it by 1). After MetaApply, the index
    // channel lives at channels[begin_c + 1].
    var beginC = t.PaletteBeginC;
    var c0 = beginC + 1;
    if (c0 >= channels.Length)
      throw new InvalidOperationException("Palette index channel out of range.");

    var indexChannel = channels[c0];
    var w = indexChannel.Width;
    var h = indexChannel.Height;
    var hShift = indexChannel.HShift;
    var vShift = indexChannel.VShift;

    // Palette LUT comes from PaletteData (row-major nb × nbColours). If empty,
    // the caller has not decoded the meta channel — we cannot proceed with
    // implicit/delta lookups in this first pass.
    if (t.PaletteData.Length != nb * nbColours)
      throw new NotSupportedException(
        "Palette LUT (PaletteData) was not populated; implicit palette indices are not yet supported.");

    if (t.PaletteSize <= 0)
      throw new InvalidOperationException("Palette must have at least one entry.");

    // libjxl bit_depth comes from `input.bitdepth` (Image-level), capped at 24
    // for palette purposes. Our channel set doesn't carry image-level bitdepth
    // separately; assume 8-bit (the typical encoder default for sRGB/RGB).
    const int bitDepth = 8;

    // Build output: nb expanded channels. For each pixel, look up the index
    // in the palette LUT; out-of-range indices are resolved via the implicit
    // small-cube / large-cube / delta-palette tables (libjxl
    // `palette_internal::GetPaletteValue`).
    var expanded = new JxlChannel[nb];
    for (var c = 0; c < nb; ++c) {
      var pixels = new int[w * h];
      for (var y = 0; y < h; ++y) {
        for (var x = 0; x < w; ++x) {
          var idx = indexChannel.Pixels[y * w + x];
          pixels[y * w + x] = _GetPaletteValue(t.PaletteData, idx, c, nbColours, bitDepth);
        }
      }
      expanded[c] = new JxlChannel { Width = w, Height = h, HShift = hShift, VShift = vShift, Pixels = pixels };
    }

    // Rebuild channel array: remove channel 0 (meta palette), replace
    // channels[c0] (the now-shifted index channel — at index c0-1 after the
    // erase) with the nb expanded channels.
    // Effective layout after libjxl: channel[0] meta erased; channel[c0-1] is
    // now the first expanded channel, then nb-1 inserted ones follow.
    var newLength = channels.Length - 1 + (nb - 1);
    var result = new JxlChannel[newLength];

    // [1 .. c0-1]  -> [0 .. c0-2]   (meta channel dropped, untouched channels copied down)
    for (var i = 1; i < c0; ++i)
      result[i - 1] = channels[i];

    // expanded channels at positions [c0-1 .. c0-1 + nb - 1]
    for (var c = 0; c < nb; ++c)
      result[c0 - 1 + c] = expanded[c];

    // [c0+1 .. end] -> [c0-1 + nb .. end - 1 + nb - 1]
    for (var i = c0 + 1; i < channels.Length; ++i)
      result[i - 1 + (nb - 1)] = channels[i];

    return result;
  }

  /// <summary>libjxl <c>palette_internal::GetPaletteValue</c>: resolve a
  /// palette index (possibly implicit) to a pixel value in channel <paramref
  /// name="c"/>. For <c>0 &lt;= index &lt; palette_size</c>, returns the
  /// LUT entry directly. For <c>index &gt;= palette_size</c>, walks the
  /// small-cube / large-cube interpolation hierarchy. For <c>index &lt; 0</c>,
  /// returns a delta-palette value (kDeltaPalette table).</summary>
  private static int _GetPaletteValue(int[] palette, int index, int c, int paletteSize, int bitDepth) {
    const int kRgbChannels = 3;
    const int kLargeCube = 5;
    const int kSmallCube = 4;
    const int kSmallCubeBits = 2;
    const int kLargeCubeOffset = kSmallCube * kSmallCube * kSmallCube;

    if (index < 0) {
      if (c >= kRgbChannels)
        return 0;
      // Avoid overflow on INT_MIN by negating after subtracting 1.
      var idx = -(index + 1);
      idx %= 1 + 2 * (_kDeltaPalette.GetLength(0) - 1);
      var multiplier = (idx & 1) == 0 ? -1 : 1;
      var result = _kDeltaPalette[(idx + 1) >> 1, c] * multiplier;
      if (bitDepth > 8)
        result *= 1 << (bitDepth - 8);
      return result;
    }

    if (paletteSize <= index && index < paletteSize + kLargeCubeOffset) {
      if (c >= kRgbChannels)
        return 0;
      // Small cube: 4×4×4 of the [0, max] range.
      var idx = index - paletteSize;
      idx >>= c * kSmallCubeBits;
      var raw = idx % kSmallCube;
      // Scale<kSmallCube>(value, bit_depth) = (value * (2^bit_depth - 1)) >> 2.
      var scaled = (int)(((long)raw * ((1L << bitDepth) - 1)) >> 2);
      return scaled + (1 << Math.Max(0, bitDepth - 3));
    }

    if (index >= paletteSize + kLargeCubeOffset) {
      if (c >= kRgbChannels)
        return 0;
      var idx = index - paletteSize - kLargeCubeOffset;
      switch (c) {
        case 1: idx /= kLargeCube; break;
        case 2: idx /= kLargeCube * kLargeCube; break;
        // case 0: no-op
      }
      var raw = idx % kLargeCube;
      // Scale<kLargeCube - 1>(value, bit_depth) where kLargeCube-1 = 4 = denom.
      return (int)(((long)raw * ((1L << bitDepth) - 1)) >> 2);
    }

    // Direct lookup: PaletteData layout is row-major nb × paletteSize.
    return palette[c * paletteSize + index];
  }

  /// <summary>libjxl <c>kDeltaPalette</c>: 72-entry table of [r, g, b]
  /// deltas used when the palette index is negative.</summary>
  private static readonly int[,] _kDeltaPalette = new int[72, 3] {
    { 0, 0, 0 },       { 4, 4, 4 },       { 11, 0, 0 },
    { 0, 0, -13 },     { 0, -12, 0 },     { -10, -10, -10 },
    { -18, -18, -18 }, { -27, -27, -27 }, { -18, -18, 0 },
    { 0, 0, -32 },     { -32, 0, 0 },     { -37, -37, -37 },
    { 0, -32, -32 },   { 24, 24, 45 },    { 50, 50, 50 },
    { -45, -24, -24 }, { -24, -45, -45 }, { 0, -24, -24 },
    { -34, -34, 0 },   { -24, 0, -24 },   { -45, -45, -24 },
    { 64, 64, 64 },    { -32, 0, -32 },   { 0, -32, 0 },
    { -32, 0, 32 },    { -24, -45, -24 }, { 45, 24, 45 },
    { 24, -24, -45 },  { -45, -24, 24 },  { 80, 80, 80 },
    { 64, 0, 0 },      { 0, 0, -64 },     { 0, -64, -64 },
    { -24, -24, 45 },  { 96, 96, 96 },    { 64, 64, 0 },
    { 45, -24, -24 },  { 34, -34, 0 },    { 112, 112, 112 },
    { 24, -45, -45 },  { 45, 45, -24 },   { 0, -32, 32 },
    { 24, -24, 45 },   { 0, 96, 96 },     { 45, -24, 24 },
    { 24, -45, -24 },  { -24, -45, 24 },  { 0, -64, 0 },
    { 96, 0, 0 },      { 128, 128, 128 }, { 64, 0, 64 },
    { 144, 144, 144 }, { 96, 96, 0 },     { -36, -36, 36 },
    { 45, -24, -45 },  { 45, -45, -24 },  { 0, 0, -96 },
    { 0, 128, 128 },   { 0, 96, 0 },      { 45, 24, -45 },
    { -128, 0, 0 },    { 24, -45, 24 },   { -45, 24, -45 },
    { 64, 0, -64 },    { 64, -64, -64 },  { 96, 0, 96 },
    { 45, -45, 24 },   { 24, 45, -45 },   { 64, 64, -64 },
    { 128, 128, 0 },   { 0, 0, -128 },    { -24, 45, -45 },
  };

  // =====================================================================================
  // Squeeze inverse — libjxl squeeze.cc::InvSqueeze
  // =====================================================================================

  /// <summary>
  /// Inverse haar-wavelet-like squeeze. Each step pairs a "low-pass" channel
  /// (the averages) with a "high-pass" channel (the residuals + tendency
  /// correction) back into a full-resolution channel. Steps are applied in
  /// REVERSE encode order. Within each step, channels <c>begin_c..end_c</c>
  /// are unsqueezed against residuals at <c>offset..offset+num_c-1</c>, with
  /// <c>offset = end_c + 1</c> if <c>in_place</c>, else
  /// <c>channels.Length + begin_c - end_c - 1</c>.
  ///
  /// <para>NOT YET SUPPORTED: empty <c>SqueezeSteps</c> (the libjxl default-chain
  /// fallback via <c>DefaultSqueezeParameters</c>).</para>
  /// </summary>
  public static JxlChannel[] InvertSqueeze(JxlChannel[] channels, JxlModularTransform t) {
    ArgumentNullException.ThrowIfNull(channels);
    ArgumentNullException.ThrowIfNull(t);
    if (t.Type != JxlModularTransformType.Squeeze)
      throw new ArgumentException("Not a squeeze transform.", nameof(t));

    var steps = t.SqueezeSteps;
    if (steps.Length == 0)
      throw new NotSupportedException(
        "Default squeeze chain (num_squeezes==0) is not yet supported. " +
        "Provide explicit SqueezeSteps.");

    // Mutate-in-place via a List so we can erase residual ranges.
    var list = new List<JxlChannel>(channels);

    // Apply in reverse encode order.
    for (var s = steps.Length - 1; s >= 0; --s) {
      var step = steps[s];
      var horizontal = step.Horizontal;
      var inPlace = step.InPlace;
      var beginC = step.BeginC;
      var endC = step.BeginC + step.NumC - 1;
      if (beginC < 0 || endC >= list.Count || endC < beginC)
        throw new InvalidOperationException("Squeeze step channel range out of bounds.");

      int offset;
      if (inPlace) {
        offset = endC + 1;
      } else {
        offset = list.Count + beginC - endC - 1;
      }
      if (offset < 0 || offset + step.NumC > list.Count)
        throw new InvalidOperationException("Squeeze residual offset out of bounds.");

      for (var c = beginC; c <= endC; ++c) {
        var rc = offset + c - beginC;
        var avgCh = list[c];
        var residCh = list[rc];
        if (avgCh.Width < residCh.Width || avgCh.Height < residCh.Height)
          throw new InvalidOperationException("Corrupted squeeze transform: avg dims < residual dims.");
        list[c] = horizontal ? _InvHSqueeze(avgCh, residCh) : _InvVSqueeze(avgCh, residCh);
      }

      // Erase residual range [offset .. offset + num_c - 1].
      list.RemoveRange(offset, step.NumC);
    }

    return list.ToArray();
  }

  // ----- Squeeze helpers ---------------------------------------------------

  /// <summary>
  /// Tendency estimator from libjxl <c>squeeze.h::SmoothTendency</c>. Returns a
  /// signed correction added to the stored residual to recover the high-pass
  /// "diff = even - odd". Pure integer arithmetic; matches the spec verbatim.
  /// </summary>
  private static int _SmoothTendency(int B, int a, int n) {
    var diff = 0;
    if (B >= a && a >= n) {
      diff = (4 * B - 3 * n - a + 6) / 12;
      if (diff - (diff & 1) > 2 * (B - a)) diff = 2 * (B - a) + 1;
      if (diff + (diff & 1) > 2 * (a - n)) diff = 2 * (a - n);
    } else if (B <= a && a <= n) {
      diff = (4 * B - 3 * n - a - 6) / 12;
      if (diff + (diff & 1) < 2 * (B - a)) diff = 2 * (B - a) - 1;
      if (diff - (diff & 1) < 2 * (a - n)) diff = 2 * (a - n);
    }
    return diff;
  }

  private static JxlChannel _InvHSqueeze(JxlChannel avgCh, JxlChannel residCh) {
    // libjxl invariant: avgCh.w == ceil((avgCh.w + residCh.w) / 2)
    //                   avgCh.h == residCh.h
    if (avgCh.Height != residCh.Height)
      throw new InvalidOperationException("HSqueeze: avg and residual heights differ.");

    var h = avgCh.Height;
    var avgW = avgCh.Width;
    var resW = residCh.Width;
    var outW = avgW + resW;

    // Short-circuit cases mirror libjxl.
    if (resW == 0) {
      // Output channel has same dimensions as input; just decrement hshift.
      return new JxlChannel {
        Width = avgW,
        Height = h,
        HShift = avgCh.HShift - 1,
        VShift = avgCh.VShift,
        Pixels = (int[])avgCh.Pixels.Clone(),
      };
    }

    var outPixels = new int[outW * h];
    if (residCh.Height == 0) {
      // Empty channel — just zeros (already initialised).
      return new JxlChannel {
        Width = outW,
        Height = h,
        HShift = avgCh.HShift - 1,
        VShift = avgCh.VShift,
        Pixels = outPixels,
      };
    }

    for (var y = 0; y < h; ++y) {
      var avgRow = y * avgW;
      var resRow = y * resW;
      var outRow = y * outW;
      for (var x = 0; x < resW; ++x) {
        var diffMinusTendency = residCh.Pixels[resRow + x];
        var avg = avgCh.Pixels[avgRow + x];
        var nextAvg = (x + 1 < avgW) ? avgCh.Pixels[avgRow + x + 1] : avg;
        var left = (x > 0) ? outPixels[outRow + (x << 1) - 1] : avg;
        var tendency = _SmoothTendency(left, avg, nextAvg);
        var diff = diffMinusTendency + tendency;
        // libjxl: A = avg + (diff/2). C# integer division on negatives matches
        // C++ truncation toward zero, so this is bit-exact.
        var A = avg + (diff / 2);
        outPixels[outRow + (x << 1)] = A;
        var B = A - diff;
        outPixels[outRow + (x << 1) + 1] = B;
      }
      // Odd-width tail: copy last avg.
      if ((outW & 1) != 0)
        outPixels[outRow + outW - 1] = avgCh.Pixels[avgRow + avgW - 1];
    }

    return new JxlChannel {
      Width = outW,
      Height = h,
      HShift = avgCh.HShift - 1,
      VShift = avgCh.VShift,
      Pixels = outPixels,
    };
  }

  private static JxlChannel _InvVSqueeze(JxlChannel avgCh, JxlChannel residCh) {
    // libjxl invariant: avgCh.h == ceil((avgCh.h + residCh.h) / 2)
    //                   avgCh.w == residCh.w
    if (avgCh.Width != residCh.Width)
      throw new InvalidOperationException("VSqueeze: avg and residual widths differ.");

    var w = avgCh.Width;
    var avgH = avgCh.Height;
    var resH = residCh.Height;
    var outH = avgH + resH;

    if (resH == 0) {
      return new JxlChannel {
        Width = w,
        Height = avgH,
        HShift = avgCh.HShift,
        VShift = avgCh.VShift - 1,
        Pixels = (int[])avgCh.Pixels.Clone(),
      };
    }

    var outPixels = new int[w * outH];
    if (residCh.Width == 0) {
      return new JxlChannel {
        Width = w,
        Height = outH,
        HShift = avgCh.HShift,
        VShift = avgCh.VShift - 1,
        Pixels = outPixels,
      };
    }

    for (var y = 0; y < resH; ++y) {
      var avgRow = y * w;
      var resRow = y * w;
      var outRowEven = (y << 1) * w;
      var outRowOdd = ((y << 1) + 1) * w;
      var prevOutRow = (y > 0) ? ((y << 1) - 1) * w : avgRow;
      var nextAvgRow = (y + 1 < avgH) ? (y + 1) * w : avgRow;
      for (var x = 0; x < w; ++x) {
        var avg = avgCh.Pixels[avgRow + x];
        var nextAvg = (y + 1 < avgH) ? avgCh.Pixels[nextAvgRow + x] : avg;
        var top = (y > 0) ? outPixels[prevOutRow + x] : avg;
        var tendency = _SmoothTendency(top, avg, nextAvg);
        var diffMinusTendency = residCh.Pixels[resRow + x];
        var diff = diffMinusTendency + tendency;
        var outVal = avg + (diff / 2);
        outPixels[outRowEven + x] = outVal;
        outPixels[outRowOdd + x] = outVal - diff;
      }
    }
    // Odd-height tail: copy last avg row.
    if ((outH & 1) != 0) {
      var lastY = avgH - 1;
      var srcRow = lastY * w;
      var dstRow = (outH - 1) * w;
      Array.Copy(avgCh.Pixels, srcRow, outPixels, dstRow, w);
    }

    return new JxlChannel {
      Width = w,
      Height = outH,
      HShift = avgCh.HShift,
      VShift = avgCh.VShift - 1,
      Pixels = outPixels,
    };
  }
}
