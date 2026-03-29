using System;

namespace FileFormat.Flif.Codec;

/// <summary>
/// Identifies the type of transform in the FLIF transform chain.
/// Transforms are applied in order before encoding and reversed after decoding.
/// </summary>
internal enum FlifTransformType : byte {
  /// <summary>YCoCg lossless color transform: converts RGB to Y (luma), Co (orange chroma), Cg (green chroma).</summary>
  YCoCg = 0,

  /// <summary>Compacts channel ranges to [0, N] by subtracting minimums.</summary>
  ChannelCompact = 1,

  /// <summary>Records per-channel min/max bounds for range coding.</summary>
  Bounds = 2,

  /// <summary>Palette transform: replaces pixel values with palette indices.</summary>
  Palette = 3,

  /// <summary>Frame shape: encodes which pixels are non-trivial (for alpha/transparency).</summary>
  FrameShape = 4,
}

/// <summary>
/// Base class for FLIF pixel transforms. Each transform modifies channel data
/// in-place, shrinking the value range and improving compression.
/// </summary>
internal abstract class FlifTransform {

  /// <summary>The transform type identifier.</summary>
  public abstract FlifTransformType Type { get; }

  /// <summary>Applies the forward transform (used during encoding).</summary>
  public abstract void Apply(int[][] channels, int width, int height);

  /// <summary>Reverses the transform (used during decoding).</summary>
  public abstract void Reverse(int[][] channels, int width, int height);

  /// <summary>
  /// Reads transform parameters from the range decoder and returns the transform.
  /// Returns null if the transform type indicates end of chain.
  /// </summary>
  public static FlifTransform? ReadTransform(FlifRangeDecoder decoder, int channelCount) {
    var hasMore = decoder.DecodeEquiprobable();
    if (hasMore == 0)
      return null;

    var type = (FlifTransformType)decoder.DecodeUniform(4);
    return type switch {
      FlifTransformType.YCoCg => new FlifYCoCgTransform(),
      FlifTransformType.ChannelCompact => FlifChannelCompactTransform.Read(decoder, channelCount),
      FlifTransformType.Bounds => FlifBoundsTransform.Read(decoder, channelCount),
      FlifTransformType.Palette => new FlifPaletteTransform(), // simplified
      FlifTransformType.FrameShape => new FlifFrameShapeTransform(),
      _ => throw new InvalidOperationException($"Unknown FLIF transform type: {type}")
    };
  }

  /// <summary>Writes transform parameters to the range encoder.</summary>
  public void WriteTransform(FlifRangeEncoder encoder) {
    encoder.EncodeEquiprobable(1); // has more transforms
    encoder.EncodeUniform((int)Type, 4);
    WriteParameters(encoder);
  }

  /// <summary>Writes the end-of-chain marker.</summary>
  public static void WriteEndOfChain(FlifRangeEncoder encoder) {
    encoder.EncodeEquiprobable(0); // no more transforms
  }

  /// <summary>Writes type-specific parameters.</summary>
  protected virtual void WriteParameters(FlifRangeEncoder encoder) { }
}

/// <summary>
/// YCoCg lossless color transform. Converts RGB to Y (luma), Co (orange chroma), Cg (green chroma).
/// The transform is perfectly reversible with integer arithmetic.
///
/// Forward (RGB to YCoCg):
///   Co = R - B
///   tmp = B + (Co >> 1)
///   Cg = G - tmp
///   Y  = tmp + (Cg >> 1)
///
/// Reverse (YCoCg to RGB):
///   tmp = Y - (Cg >> 1)
///   G   = Cg + tmp
///   B   = tmp - (Co >> 1)
///   R   = B + Co
/// </summary>
internal sealed class FlifYCoCgTransform : FlifTransform {

  public override FlifTransformType Type => FlifTransformType.YCoCg;

  public override void Apply(int[][] channels, int width, int height) {
    if (channels.Length < 3)
      return;

    var r = channels[0];
    var g = channels[1];
    var b = channels[2];
    var count = width * height;

    for (var i = 0; i < count; ++i) {
      var rv = r[i];
      var gv = g[i];
      var bv = b[i];

      var co = rv - bv;
      var tmp = bv + (co >> 1);
      var cg = gv - tmp;
      var y = tmp + (cg >> 1);

      r[i] = y;   // Channel 0 = Y
      g[i] = co;  // Channel 1 = Co
      b[i] = cg;  // Channel 2 = Cg
    }
  }

  public override void Reverse(int[][] channels, int width, int height) {
    if (channels.Length < 3)
      return;

    var y = channels[0];
    var co = channels[1];
    var cg = channels[2];
    var count = width * height;

    for (var i = 0; i < count; ++i) {
      var yv = y[i];
      var cov = co[i];
      var cgv = cg[i];

      var tmp = yv - (cgv >> 1);
      var gv = cgv + tmp;
      var bv = tmp - (cov >> 1);
      var rv = bv + cov;

      y[i] = rv;    // Channel 0 = R
      co[i] = gv;   // Channel 1 = G
      cg[i] = bv;   // Channel 2 = B
    }
  }
}

/// <summary>
/// Channel compact transform: shifts each channel so its minimum value is 0.
/// Stores the per-channel minimums for reversal.
/// </summary>
internal sealed class FlifChannelCompactTransform : FlifTransform {

  public override FlifTransformType Type => FlifTransformType.ChannelCompact;

  private readonly int[] _minimums;

  private FlifChannelCompactTransform(int[] minimums) {
    _minimums = minimums;
  }

  public override void Apply(int[][] channels, int width, int height) {
    var count = width * height;
    for (var c = 0; c < channels.Length; ++c) {
      var ch = channels[c];
      var min = int.MaxValue;
      for (var i = 0; i < count; ++i)
        min = Math.Min(min, ch[i]);

      _minimums[c] = min;
      if (min != 0)
        for (var i = 0; i < count; ++i)
          ch[i] -= min;
    }
  }

  public override void Reverse(int[][] channels, int width, int height) {
    var count = width * height;
    for (var c = 0; c < channels.Length; ++c) {
      if (c >= _minimums.Length)
        break;
      var min = _minimums[c];
      if (min != 0) {
        var ch = channels[c];
        for (var i = 0; i < count; ++i)
          ch[i] += min;
      }
    }
  }

  public static FlifChannelCompactTransform Read(FlifRangeDecoder decoder, int channelCount) {
    var minimums = new int[channelCount];
    for (var c = 0; c < channelCount; ++c)
      minimums[c] = decoder.DecodeUniform(511) - 255; // range [-255, 255]
    return new FlifChannelCompactTransform(minimums);
  }

  protected override void WriteParameters(FlifRangeEncoder encoder) {
    for (var c = 0; c < _minimums.Length; ++c)
      encoder.EncodeUniform(_minimums[c] + 255, 511);
  }

  public static FlifChannelCompactTransform Create(int channelCount)
    => new(new int[channelCount]);
}

/// <summary>
/// Bounds transform: records per-channel min/max for tighter range coding.
/// The actual pixel data is unchanged; only the valid range metadata is affected.
/// </summary>
internal sealed class FlifBoundsTransform : FlifTransform {

  public override FlifTransformType Type => FlifTransformType.Bounds;

  /// <summary>Per-channel minimum values.</summary>
  public readonly int[] Minimums;

  /// <summary>Per-channel maximum values.</summary>
  public readonly int[] Maximums;

  private FlifBoundsTransform(int[] minimums, int[] maximums) {
    Minimums = minimums;
    Maximums = maximums;
  }

  public override void Apply(int[][] channels, int width, int height) {
    // Bounds transform just records the ranges; no pixel modification
    var count = width * height;
    for (var c = 0; c < channels.Length && c < Minimums.Length; ++c) {
      var ch = channels[c];
      var min = int.MaxValue;
      var max = int.MinValue;
      for (var i = 0; i < count; ++i) {
        min = Math.Min(min, ch[i]);
        max = Math.Max(max, ch[i]);
      }

      Minimums[c] = min;
      Maximums[c] = max;
    }
  }

  public override void Reverse(int[][] channels, int width, int height) {
    // No-op: bounds transform is metadata-only
  }

  public static FlifBoundsTransform Read(FlifRangeDecoder decoder, int channelCount) {
    var mins = new int[channelCount];
    var maxs = new int[channelCount];
    for (var c = 0; c < channelCount; ++c) {
      mins[c] = decoder.DecodeUniform(511) - 255;
      maxs[c] = decoder.DecodeUniform(511) - 255;
      if (maxs[c] < mins[c])
        maxs[c] = mins[c];
    }

    return new FlifBoundsTransform(mins, maxs);
  }

  protected override void WriteParameters(FlifRangeEncoder encoder) {
    for (var c = 0; c < Minimums.Length; ++c) {
      encoder.EncodeUniform(Minimums[c] + 255, 511);
      encoder.EncodeUniform(Maximums[c] + 255, 511);
    }
  }

  public static FlifBoundsTransform Create(int channelCount)
    => new(new int[channelCount], new int[channelCount]);
}

/// <summary>
/// Palette transform: maps colors to palette indices.
/// Simplified implementation for self-produced files.
/// </summary>
internal sealed class FlifPaletteTransform : FlifTransform {

  public override FlifTransformType Type => FlifTransformType.Palette;

  public override void Apply(int[][] channels, int width, int height) {
    // Palette transform not used in our encoder; this is a no-op
  }

  public override void Reverse(int[][] channels, int width, int height) {
    // Palette transform not used in our encoder; this is a no-op
  }
}

/// <summary>
/// Frame shape transform: for images with alpha, marks pixels as visible/invisible.
/// Simplified implementation.
/// </summary>
internal sealed class FlifFrameShapeTransform : FlifTransform {

  public override FlifTransformType Type => FlifTransformType.FrameShape;

  public override void Apply(int[][] channels, int width, int height) {
    // Frame shape not used in our encoder; this is a no-op
  }

  public override void Reverse(int[][] channels, int width, int height) {
    // Frame shape not used in our encoder; this is a no-op
  }
}
