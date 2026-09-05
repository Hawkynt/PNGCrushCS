using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// The patches layer (ISO/IEC 18181-1 §C.4.5; libjxl
/// <c>lib/jxl/dec_patch_dictionary.cc</c>).
/// </summary>
/// <remarks>
/// Where a picture repeats itself — the same letter, the same icon, the same
/// leaf — the encoder codes the thing once into a frame of its own that is
/// never shown, and then states where to stamp it. What arrives here is that
/// list: which kept-aside frame each stamp comes from, which rectangle of it,
/// and where each copy goes.
///
/// <para>A stamp is not always a copy. Each states how it combines with what is
/// under it, separately for the colour channels and for every extra channel,
/// and the alpha-blended modes come in two directions depending on which of the
/// two is treated as being on top.</para>
/// </remarks>
internal static class JxlPatches {

  private const int _NumRefPatchContext = 0;
  private const int _ReferenceFrameContext = 1;
  private const int _PatchSizeContext = 2;
  private const int _PatchReferencePositionContext = 3;
  private const int _PatchPositionContext = 4;
  private const int _PatchBlendModeContext = 5;
  private const int _PatchOffsetContext = 6;
  private const int _PatchCountContext = 7;
  private const int _PatchAlphaChannelContext = 8;
  private const int _PatchClampContext = 9;

  /// <summary>libjxl <c>kNumPatchDictionaryContexts</c>.</summary>
  public const int NumPatchDictionaryContexts = 10;

  /// <summary>How many frames a file may keep aside.</summary>
  public const int MaxReferenceFrames = 4;

  /// <summary>
  /// Read the patch dictionary. As with splines, the frame states in its header
  /// flags that it has one; this section carries no flag of its own.
  /// </summary>
  /// <param name="reader">Positioned at the start of the section.</param>
  /// <param name="width">Picture width, which bounds where a stamp may go.</param>
  /// <param name="height">Picture height.</param>
  /// <param name="numExtraChannels">How many extra channels the picture has;
  /// every stamp states a blend mode for each of them as well as for colour.</param>
  /// <param name="referenceSizes">The size of each kept-aside frame, so that a
  /// rectangle claimed from one can be checked against it. A zero size means
  /// nothing has been kept in that slot.</param>
  public static PatchDictionary Decode(
    JxlBitReader reader, int width, int height, int numExtraChannels, (int Width, int Height)[] referenceSizes
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    ArgumentNullException.ThrowIfNull(referenceSizes);

    var entropy = JxlEntropyDecoder.Read(reader, NumPatchDictionaryContexts);

    var pixels = (long)width * height;
    var maxReferencePatches = 1024 + pixels / 4;
    var maxPatches = maxReferencePatches * 4;

    var stride = numExtraChannels + 1;
    var numReferencePatches = entropy.ReadInt(_NumRefPatchContext);
    if (numReferencePatches < 0 || numReferencePatches > maxReferencePatches)
      throw new InvalidDataException($"A frame states {numReferencePatches} patch sources, which is more than it has room for.");

    var rectangles = new List<PatchRectangle>();
    var stamps = new List<PatchStamp>();
    long totalPatches = 0;
    var chooseAlpha = numExtraChannels > 1;

    for (var id = 0; id < numReferencePatches; ++id) {
      var reference = entropy.ReadInt(_ReferenceFrameContext);
      if (reference < 0 || reference >= MaxReferenceFrames || referenceSizes[reference].Width == 0)
        throw new InvalidDataException($"A patch takes its picture from slot {reference}, which holds no frame.");

      var rectangle = new PatchRectangle {
        Reference = reference,
        X0 = entropy.ReadInt(_PatchReferencePositionContext),
        Y0 = entropy.ReadInt(_PatchReferencePositionContext),
        Width = entropy.ReadInt(_PatchSizeContext) + 1,
        Height = entropy.ReadInt(_PatchSizeContext) + 1,
      };

      var (referenceWidth, referenceHeight) = referenceSizes[reference];
      if (rectangle.X0 < 0 || rectangle.Y0 < 0
          || rectangle.X0 + rectangle.Width > referenceWidth
          || rectangle.Y0 + rectangle.Height > referenceHeight)
        throw new InvalidDataException("A patch claims a rectangle that is not inside the frame it comes from.");

      var count = entropy.ReadInt(_PatchCountContext);
      if (count < 0 || count > maxPatches)
        throw new InvalidDataException($"A patch source states {count} copies, which is more than the frame allows.");
      ++count;
      totalPatches += count;
      if (totalPatches > maxPatches)
        throw new InvalidDataException($"The patches state {totalPatches} copies between them, which is more than the frame allows.");

      var rectangleIndex = rectangles.Count;
      rectangles.Add(rectangle);

      for (var i = 0; i < count; ++i) {
        int x;
        int y;
        if (i == 0) {
          x = entropy.ReadInt(_PatchPositionContext);
          y = entropy.ReadInt(_PatchPositionContext);
        } else {
          // Every copy after the first is stated as a step from the one before.
          var previous = stamps[^1];
          x = previous.X + _UnpackSigned(entropy.ReadInt(_PatchOffsetContext));
          y = previous.Y + _UnpackSigned(entropy.ReadInt(_PatchOffsetContext));
        }

        if (x < 0 || y < 0 || x + rectangle.Width > width || y + rectangle.Height > height)
          throw new InvalidDataException($"A patch at {x},{y} does not fit inside the picture.");

        var blending = new PatchBlending[stride];
        for (var j = 0; j < stride; ++j) {
          var mode = entropy.ReadInt(_PatchBlendModeContext);
          if (mode < 0 || mode >= (int)PatchBlendMode.Count)
            throw new InvalidDataException($"A patch states blend mode {mode}, which the format does not define.");

          var blendMode = (PatchBlendMode)mode;
          var alphaChannel = 0;
          if (UsesAlpha(blendMode) && chooseAlpha) {
            alphaChannel = entropy.ReadInt(_PatchAlphaChannelContext);
            if (alphaChannel < 0 || alphaChannel >= numExtraChannels)
              throw new InvalidDataException(
                $"A patch blends against extra channel {alphaChannel}, and there are only {numExtraChannels}.");
          }

          var clamp = UsesClamp(blendMode) && entropy.ReadInt(_PatchClampContext) != 0;
          blending[j] = new PatchBlending { Mode = blendMode, AlphaChannel = alphaChannel, Clamp = clamp };
        }

        stamps.Add(new PatchStamp { X = x, Y = y, RectangleIndex = rectangleIndex, Blending = blending });
      }
    }

    if (!entropy.CheckFinalState())
      throw new InvalidDataException("The patch section did not end where its entropy coder says it should.");

    return new PatchDictionary {
      Rectangles = rectangles.ToArray(),
      Stamps = stamps.ToArray(),
      BlendingStride = stride,
    };
  }

  /// <summary>Whether a mode reads an alpha channel.</summary>
  public static bool UsesAlpha(PatchBlendMode mode)
    => mode is PatchBlendMode.BlendAbove or PatchBlendMode.BlendBelow
      or PatchBlendMode.AlphaWeightedAddAbove or PatchBlendMode.AlphaWeightedAddBelow;

  /// <summary>Whether a mode states whether that alpha is clamped.</summary>
  public static bool UsesClamp(PatchBlendMode mode) => UsesAlpha(mode) || mode == PatchBlendMode.Multiply;

  /// <summary>
  /// Stamp every patch onto the frame.
  /// </summary>
  /// <param name="planes">The frame's planes: three colour ones followed by its
  /// extra channels.</param>
  /// <param name="width">Picture width.</param>
  /// <param name="height">Picture height.</param>
  /// <param name="dictionary">What to stamp and where.</param>
  /// <param name="references">The kept-aside frames, each laid out the same way
  /// as <paramref name="planes"/>.</param>
  /// <param name="referenceSizes">Their sizes.</param>
  /// <param name="premultiplied">Whether the alpha of each extra channel is
  /// already carried in the colour.</param>
  public static void Apply(
    float[][] planes,
    int width,
    int height,
    PatchDictionary dictionary,
    float[][]?[] references,
    (int Width, int Height)[] referenceSizes,
    bool[] premultiplied
  ) {
    ArgumentNullException.ThrowIfNull(planes);
    ArgumentNullException.ThrowIfNull(dictionary);
    ArgumentNullException.ThrowIfNull(references);
    ArgumentNullException.ThrowIfNull(referenceSizes);
    ArgumentNullException.ThrowIfNull(premultiplied);

    var planeCount = planes.Length;
    foreach (var stamp in dictionary.Stamps) {
      var rectangle = dictionary.Rectangles[stamp.RectangleIndex];
      var source = references[rectangle.Reference];
      if (source == null)
        throw new InvalidDataException($"A patch takes its picture from slot {rectangle.Reference}, which holds no frame.");

      var (sourceWidth, _) = referenceSizes[rectangle.Reference];
      for (var dy = 0; dy < rectangle.Height; ++dy)
      for (var dx = 0; dx < rectangle.Width; ++dx) {
        var to = (stamp.Y + dy) * width + stamp.X + dx;
        var from = (rectangle.Y0 + dy) * sourceWidth + rectangle.X0 + dx;

        // The extra channels go first, so that the colour is blended with the
        // alpha as it was before any of this rather than after.
        for (var p = 3; p < planeCount; ++p)
          _Blend(planes, source, planeCount, p, to, from,
            stamp.Blending[Math.Min(p - 2, stamp.Blending.Length - 1)], premultiplied, colour: false);

        var colourBlending = stamp.Blending[0];
        for (var p = 0; p < 3; ++p)
          _Blend(planes, source, planeCount, p, to, from, colourBlending, premultiplied, colour: true);
      }
    }
  }

  private static void _Blend(
    float[][] planes, float[][] source, int planeCount, int plane, int to, int from,
    PatchBlending blending, bool[] premultiplied, bool colour
  ) {
    var background = planes[plane][to];
    var foreground = _At(source, plane, from);

    switch (blending.Mode) {
      case PatchBlendMode.None:
        return;

      case PatchBlendMode.Replace:
        planes[plane][to] = foreground;
        return;

      case PatchBlendMode.Add:
        planes[plane][to] = background + foreground;
        return;

      case PatchBlendMode.Multiply: {
        var f = blending.Clamp ? Math.Clamp(foreground, 0.0f, 1.0f) : foreground;
        planes[plane][to] = background * f;
        return;
      }
    }

    var alphaPlane = 3 + blending.AlphaChannel;
    if (alphaPlane >= planeCount) {
      // Nothing to blend by: libjxl takes the patch as it stands.
      planes[plane][to] = foreground;
      return;
    }

    var above = blending.Mode is PatchBlendMode.BlendAbove or PatchBlendMode.AlphaWeightedAddAbove;
    var bottom = above ? background : foreground;
    var top = above ? foreground : background;
    var topAlpha = above ? _At(source, alphaPlane, from) : planes[alphaPlane][to];
    var bottomAlpha = above ? planes[alphaPlane][to] : _At(source, alphaPlane, from);
    if (blending.Clamp)
      topAlpha = Math.Clamp(topAlpha, 0.0f, 1.0f);

    if (blending.Mode is PatchBlendMode.AlphaWeightedAddAbove or PatchBlendMode.AlphaWeightedAddBelow) {
      planes[plane][to] = bottom + top * topAlpha;
      return;
    }

    // The alpha channel itself composes rather than being composed.
    if (!colour && plane == alphaPlane) {
      planes[plane][to] = 1.0f - (1.0f - topAlpha) * (1.0f - bottomAlpha);
      return;
    }

    if (premultiplied.Length > blending.AlphaChannel && premultiplied[blending.AlphaChannel]) {
      planes[plane][to] = top + bottom * (1.0f - topAlpha);
      return;
    }

    var newAlpha = 1.0f - (1.0f - topAlpha) * (1.0f - bottomAlpha);
    var value = top * topAlpha + bottom * bottomAlpha * (1.0f - topAlpha);
    planes[plane][to] = newAlpha > 0.0f ? value / newAlpha : 0.0f;
  }

  private static float _At(float[][] planes, int plane, int index)
    => plane < planes.Length && index < planes[plane].Length ? planes[plane][index] : 0.0f;

  private static int _UnpackSigned(int packed) {
    var u = (uint)packed;
    return (int)((u >> 1) ^ (~(u & 1) + 1));
  }
}

/// <summary>libjxl <c>PatchBlendMode</c>.</summary>
internal enum PatchBlendMode {
  None = 0,
  Replace = 1,
  Add = 2,
  Multiply = 3,
  BlendAbove = 4,
  BlendBelow = 5,
  AlphaWeightedAddAbove = 6,
  AlphaWeightedAddBelow = 7,
  Count = 8,
}

/// <summary>How one channel of one stamp combines with what is under it.</summary>
internal readonly record struct PatchBlending {
  public PatchBlendMode Mode { get; init; }
  public int AlphaChannel { get; init; }
  public bool Clamp { get; init; }
}

/// <summary>A rectangle of a kept-aside frame that one or more stamps copy.</summary>
internal sealed class PatchRectangle {
  public int Reference { get; init; }
  public int X0 { get; init; }
  public int Y0 { get; init; }
  public int Width { get; init; }
  public int Height { get; init; }
}

/// <summary>One copy of a rectangle, at one place, blended one way.</summary>
internal sealed class PatchStamp {
  public int X { get; init; }
  public int Y { get; init; }
  public int RectangleIndex { get; init; }
  public PatchBlending[] Blending { get; init; } = [];
}

/// <summary>Everything a frame states about its patches.</summary>
internal sealed class PatchDictionary {
  public PatchRectangle[] Rectangles { get; init; } = [];
  public PatchStamp[] Stamps { get; init; } = [];
  public int BlendingStride { get; init; }
}
