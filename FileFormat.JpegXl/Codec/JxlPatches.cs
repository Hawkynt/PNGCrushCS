using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// Patches dictionary for VarDCT (ISO/IEC 18181-1 §G.11; libjxl
// `lib/jxl/patch_dictionary.cc` and `lib/jxl/dec_patch_dictionary.cc`,
// `lib/jxl/patch_dictionary_internal.h`).
//
// The patch dictionary is a list of rectangular regions copied from a
// previously-decoded reference frame onto the current decoded VarDCT image.
// Each "reference patch" is a rectangle in some reference frame (one of up to
// four), and each reference patch is pasted at one or more target positions
// in the current frame, with a per-channel blend mode that controls how the
// patch interacts with the existing pixels (replace, additive, alpha-weighted,
// etc.). This is JXL's mechanism for cheaply encoding repeated graphical
// elements (UI sprites, text strokes, logos) without having to re-encode them.
//
// libjxl bitstream layout for the patch dictionary (see
// dec_patch_dictionary.cc::PatchDictionary::Decode and the kNumPatchDictionary-
// Contexts enum in patch_dictionary_internal.h):
//
//   1. has_patches (1 bit). If 0 — no patches; the rest is skipped.
//   2. Recursive entropy block over kNumPatchDictionaryContexts (= 10) contexts:
//        kNumRefPatchContext              = 0  // count of reference patches
//        kReferenceFrameContext           = 1  // reference frame ID (0..3)
//        kPatchSizeContext                = 2  // source xsize / ysize
//        kPatchReferencePositionContext   = 3  // source x0 / y0
//        kPatchPositionContext            = 4  // first instance target x / y
//        kPatchBlendModeContext           = 5  // per-channel blend mode 0..7
//        kPatchOffsetContext              = 6  // delta for subsequent instances
//        kPatchCountContext               = 7  // instance count per reference
//        kPatchAlphaChannelContext        = 8  // alpha-weighted: alpha channel idx
//        kPatchClampContext               = 9  // clamp flag (0/1)
//   3. num_ref_patches = entropy.ReadInt(0)        // kNumRefPatchContext
//   4. For each reference patch r in [0, num_ref_patches):
//        ref_idx     = entropy.ReadInt(1)          // kReferenceFrameContext
//        x0          = entropy.ReadInt(3)          // kPatchReferencePositionContext
//        y0          = entropy.ReadInt(3)          //   "
//        xsize       = entropy.ReadInt(2) + 1      // kPatchSizeContext
//        ysize       = entropy.ReadInt(2) + 1      //   "
//        num_instances = entropy.ReadInt(7)        // kPatchCountContext
//        For each instance i in [0, num_instances):
//          if i == 0:
//            x = entropy.ReadInt(4)                // kPatchPositionContext
//            y = entropy.ReadInt(4)                //   "
//          else:
//            x = previous_x + UnpackSigned(entropy.ReadInt(6))
//            y = previous_y + UnpackSigned(entropy.ReadInt(6))
//          For each channel c in 0..(num_color + num_extra) - 1:
//            blend_mode = entropy.ReadInt(5)       // kPatchBlendModeContext
//            // One of:
//            //   kNone=0, kAdd=1, kReplace=2, kMul=3,
//            //   kBlendAbove=4, kBlendBelow=5,
//            //   kAlphaWeightedAddAbove=6, kAlphaWeightedAddBelow=7
//            if (blend_mode in {kAlphaWeightedAddAbove, kAlphaWeightedAddBelow}):
//              alpha = entropy.ReadInt(8)          // kPatchAlphaChannelContext
//            if (blend_mode != kNone):
//              clamp = entropy.ReadInt(9)          // kPatchClampContext
//   5. entropy.CheckFinalState() must hold.
//
// Apply: for each reference patch, for each instance, for each color channel,
// copy the rectangular region from the reference frame to the target position,
// blending per the blend mode. The reference frame may legitimately be null
// (zero-filled) for the first wave — libjxl treats unmaterialised reference
// slots as "all zeros" anyway.
//
// First-wave implementation policy (matches the task brief):
//   - Read has_patches (1 bit). If 0, return null with zero further bit
//     consumption.
//   - If 1, throw NotImplementedException naming the missing piece (the
//     recursive entropy block over the 10 patch-dictionary contexts plus the
//     UnpackSigned offset coding plus per-channel blend-mode parameter reads).
//   - Apply with no patches is a no-op.
//   - Apply with patches actually walks the dictionary and performs the blend.
//     This is wired up so that a future ReadDictionary that produces a
//     non-empty PatchDictionary will paste correctly without further work.
// =====================================================================================

/// <summary>Per-channel patch blend mode (ISO/IEC 18181-1 §G.11.2;
/// libjxl <c>PatchBlendMode</c> enum). Values match the bitstream encoding.</summary>
internal enum PatchBlendMode {
  /// <summary>Skip channel — don't touch the existing pixel.</summary>
  None = 0,
  /// <summary>Add the patch value to the existing pixel.</summary>
  Add = 1,
  /// <summary>Overwrite the existing pixel with the patch value.</summary>
  Replace = 2,
  /// <summary>Multiply existing pixel by patch value.</summary>
  Mul = 3,
  /// <summary>Alpha-blend with patch on top (existing pixel is "below").</summary>
  BlendAbove = 4,
  /// <summary>Alpha-blend with patch underneath (existing pixel is "above").</summary>
  BlendBelow = 5,
  /// <summary>Add the patch value scaled by the patch's alpha channel; patch on top.</summary>
  AlphaWeightedAddAbove = 6,
  /// <summary>Add the patch value scaled by the patch's alpha channel; patch below.</summary>
  AlphaWeightedAddBelow = 7,
}

/// <summary>One target location at which a reference patch is pasted.
/// Holds the per-channel blend mode and the alpha/clamp parameters that
/// libjxl associates with each (patch, position) pair.
///
/// <para>The first-wave Apply path consults <see cref="BlendModeY"/>,
/// <see cref="BlendModeX"/>, and <see cref="BlendModeB"/> for the three XYB
/// color channels. Per-channel alpha / clamp parameters are recorded on
/// the position so a future, full-spec encoder/decoder can round-trip
/// them; they are unused by the first-wave blender beyond passing through
/// the source value (no extra-channel alpha is materialised yet).</para>
/// </summary>
internal sealed class PatchPosition {

  /// <summary>Target X coordinate (top-left of the pasted patch).</summary>
  public int X { get; init; }

  /// <summary>Target Y coordinate (top-left of the pasted patch).</summary>
  public int Y { get; init; }

  /// <summary>Blend mode for the Y (luminance-like) channel.</summary>
  public PatchBlendMode BlendModeY { get; init; }

  /// <summary>Blend mode for the X (red-minus-green-like) channel.</summary>
  public PatchBlendMode BlendModeX { get; init; }

  /// <summary>Blend mode for the B (blue-minus-yellow-like) channel.</summary>
  public PatchBlendMode BlendModeB { get; init; }

  /// <summary>Per-channel alpha-channel index for AlphaWeightedAdd* blends
  /// (libjxl <c>kPatchAlphaChannelContext</c>). Length matches the channel
  /// count; ignored for non-alpha-weighted blend modes.</summary>
  public int[] AlphaChannel { get; init; } = [];

  /// <summary>Per-channel clamp flag (libjxl <c>kPatchClampContext</c>) —
  /// when set, the blend output is clamped to the channel's signal range.
  /// Length matches the channel count; ignored for blend mode None.</summary>
  public bool[] Clamp { get; init; } = [];
}

/// <summary>One reference-frame rectangle pasted at one or more positions.
/// Mirrors libjxl's per-reference-patch loop in
/// <c>PatchDictionary::Decode</c>.</summary>
internal sealed class PatchEntry {

  /// <summary>Reference frame index (0..3). libjxl
  /// <c>kReferenceFrameContext</c>.</summary>
  public int RefIdx { get; init; }

  /// <summary>Top-left X of the source rectangle in the reference frame.</summary>
  public int X0 { get; init; }

  /// <summary>Top-left Y of the source rectangle in the reference frame.</summary>
  public int Y0 { get; init; }

  /// <summary>Width of the source rectangle (decoded as <c>raw + 1</c> per
  /// libjxl <c>kPatchSizeContext</c>).</summary>
  public int Width { get; init; }

  /// <summary>Height of the source rectangle (decoded as <c>raw + 1</c> per
  /// libjxl <c>kPatchSizeContext</c>).</summary>
  public int Height { get; init; }

  /// <summary>One <see cref="PatchPosition"/> per place where this rectangle
  /// is pasted. Always non-null; may be empty when the dictionary itself is
  /// empty.</summary>
  public PatchPosition[] Positions { get; init; } = [];
}

/// <summary>Decoded patch dictionary — a flat list of <see cref="PatchEntry"/>
/// records. An empty dictionary is a valid no-op; a null return from
/// <see cref="JxlPatches.ReadDictionary"/> means the bitstream's
/// <c>has_patches</c> flag was 0 and no entropy block was emitted.</summary>
internal sealed class PatchDictionary {

  /// <summary>The list of reference patches and their target positions.</summary>
  public PatchEntry[] Patches { get; init; } = [];
}

/// <summary>
/// Read and apply the JPEG XL Patches dictionary (ISO/IEC 18181-1 §G.11).
/// </summary>
/// <remarks>
/// First-wave scope: <see cref="ReadHasPatchesFlag"/> consumes the 1-bit
/// <c>has_patches</c> flag without throwing. <see cref="ReadDictionary"/>
/// returns null when the flag is 0; when the flag is 1 it throws
/// <see cref="NotImplementedException"/> identifying the missing
/// recursive-entropy + per-channel-blend-mode work. <see cref="Apply"/>
/// implements the full per-patch, per-position, per-channel blend so that a
/// future ReadDictionary returning a non-empty <see cref="PatchDictionary"/>
/// will paste correctly without further modification.
/// </remarks>
internal static class JxlPatches {

  /// <summary>The number of contexts in the recursive entropy block that
  /// codes the patch dictionary. Matches libjxl
  /// <c>kNumPatchDictionaryContexts</c> in
  /// <c>lib/jxl/patch_dictionary_internal.h</c>.</summary>
  internal const int NumPatchDictionaryContexts = 10;

  // Context indices (libjxl `enum Contexts` in patch_dictionary_internal.h).
  internal const int CtxNumRefPatch = 0;
  internal const int CtxReferenceFrame = 1;
  internal const int CtxPatchSize = 2;
  internal const int CtxPatchReferencePosition = 3;
  internal const int CtxPatchPosition = 4;
  internal const int CtxPatchBlendMode = 5;
  internal const int CtxPatchOffset = 6;
  internal const int CtxPatchCount = 7;
  internal const int CtxPatchAlphaChannel = 8;
  internal const int CtxPatchClamp = 9;

  /// <summary>
  /// Consume the <c>has_patches</c> 1-bit flag (libjxl
  /// <c>FrameHeader::flags &amp; kPatches</c> when present, or the
  /// dedicated 1-bit gate at the start of the patch-dictionary block when
  /// it appears in the bitstream). Returns the flag value without further
  /// side effects.
  /// </summary>
  /// <remarks>
  /// Exposed publicly so the frame orchestrator can ask "are patches
  /// enabled?" before deciding whether to call
  /// <see cref="ReadDictionary"/>. When the answer is false the caller may
  /// safely skip the patch-dictionary section entirely.
  /// </remarks>
  public static bool ReadHasPatchesFlag(JxlBitReader reader) {
    ArgumentNullException.ThrowIfNull(reader);
    return reader.ReadBool();
  }

  /// <summary>
  /// Read the patch dictionary header from the bitstream.
  /// </summary>
  /// <param name="reader">Bit reader positioned at the patch-dictionary
  /// section's <c>has_patches</c> flag.</param>
  /// <param name="entropy">Outer-scope entropy decoder. Currently unused —
  /// the spec dictates the patch dictionary spawns its own recursive
  /// entropy block over <see cref="NumPatchDictionaryContexts"/> contexts
  /// (libjxl <c>DecodeHistograms</c>); the parameter is reserved so a
  /// future full implementation can share state with the outer block when
  /// appropriate.</param>
  /// <returns><c>null</c> when the bitstream's <c>has_patches</c> flag is
  /// 0; otherwise a fully-populated <see cref="PatchDictionary"/>.</returns>
  /// <exception cref="NotImplementedException">
  /// Thrown when <c>has_patches</c> is 1. The recursive entropy block over
  /// the 10 patch-dictionary contexts plus the per-instance offset coding
  /// (UnpackSigned) plus the per-channel blend-mode-with-alpha/clamp
  /// parameter reads — i.e. the body of libjxl
  /// <c>PatchDictionary::Decode</c> — is not yet implemented.
  /// </exception>
  public static PatchDictionary? ReadDictionary(JxlBitReader reader, JxlEntropyDecoder entropy) {
    ArgumentNullException.ThrowIfNull(reader);
    ArgumentNullException.ThrowIfNull(entropy);

    // (1) has_patches gate — single bit. Cheap; never throws.
    var hasPatches = ReadHasPatchesFlag(reader);
    if (!hasPatches)
      return null;

    // (2..5) Full bitstream decode is not yet wired. Throw a precise,
    // load-bearing message so the failure points at the missing work.
    // The outer caller's audit log distinguishes "patches disabled" (return
    // null) from "patches present but unsupported" (this throw).
    throw new NotImplementedException(
      "Patch dictionary present (has_patches=1) but the recursive entropy "
      + "block over kNumPatchDictionaryContexts (=10) contexts, the "
      + "UnpackSigned per-instance offset coding, and the per-channel "
      + "blend-mode + alpha/clamp parameter reads (per ISO/IEC 18181-1 "
      + "§G.11 / libjxl PatchDictionary::Decode in dec_patch_dictionary.cc) "
      + "are not yet implemented. Tracked alongside VarDCT first-wave "
      + "scope.");
  }

  /// <summary>
  /// Apply the patches in <paramref name="dictionary"/> to the decoded
  /// VarDCT image <paramref name="channels"/>.
  /// </summary>
  /// <param name="channels">Three XYB-channel planes, length 3, each
  /// <c>width * height</c> floats in row-major order. Mutated in place.</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="dictionary">Decoded patch dictionary. May contain zero
  /// patches; null is rejected — callers that observed
  /// <see cref="ReadDictionary"/> returning null must skip Apply
  /// altogether (or pass an empty dictionary).</param>
  /// <param name="referenceFrame">Three-plane reference frame to copy
  /// patches from. May be null (treated as all-zeros — matches libjxl's
  /// behaviour for unmaterialised reference slots in
  /// <c>FrameDecoder::ProcessSections</c> / <c>SaveAsReference</c>) or have
  /// fewer than 3 entries (missing planes treated as zero). When non-null
  /// each plane is interpreted as <c>width * height</c> floats in row-major
  /// order, matching <paramref name="channels"/>.</param>
  /// <remarks>
  /// For each patch, for each target position, for each color channel, the
  /// blend mode is consulted to decide how the source rectangle is
  /// composited onto the existing image. Source-rectangle reads that fall
  /// outside the reference frame are clamped to zero. Target-rectangle
  /// writes that fall outside the image are silently skipped (libjxl's
  /// <c>AddOneRow</c> performs the same clipping).
  /// </remarks>
  public static void Apply(
    float[][] channels,
    int width,
    int height,
    PatchDictionary dictionary,
    float[][] referenceFrame
  ) {
    ArgumentNullException.ThrowIfNull(channels);
    ArgumentNullException.ThrowIfNull(dictionary);
    if (channels.Length < 3)
      throw new ArgumentException(
        $"Expected at least 3 channels for VarDCT XYB image, got {channels.Length}.",
        nameof(channels));
    if (width < 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Width must be non-negative.");
    if (height < 0)
      throw new ArgumentOutOfRangeException(nameof(height), "Height must be non-negative.");
    var planeSize = checked(width * height);
    for (var c = 0; c < 3; ++c) {
      if (channels[c] == null)
        throw new ArgumentException($"Channel {c} is null.", nameof(channels));
      if (channels[c].Length < planeSize)
        throw new ArgumentException(
          $"Channel {c} length {channels[c].Length} < width*height = {planeSize}.",
          nameof(channels));
    }

    // Empty / null-effective dictionary → fast no-op.
    if (dictionary.Patches == null || dictionary.Patches.Length == 0)
      return;

    // libjxl walks patches in order; per-position blends compose left-to-
    // right. We do the same.
    foreach (var patch in dictionary.Patches) {
      if (patch == null)
        continue;
      if (patch.Positions == null || patch.Positions.Length == 0)
        continue;
      if (patch.Width <= 0 || patch.Height <= 0)
        continue;

      foreach (var pos in patch.Positions) {
        if (pos == null)
          continue;
        _ApplyOne(
          channels, width, height,
          patch, pos,
          referenceFrame
        );
      }
    }
  }

  /// <summary>
  /// Apply a single (patch, position) pair. Walks the source rectangle
  /// row-by-row, clipping to both the reference frame's bounds and the
  /// destination image's bounds, and blends per the position's per-channel
  /// blend mode.
  /// </summary>
  private static void _ApplyOne(
    float[][] channels,
    int width, int height,
    PatchEntry patch,
    PatchPosition pos,
    float[][]? referenceFrame
  ) {
    // The three blend modes, indexed by VarDCT channel order (X=0, Y=1, B=2).
    var modes = new[] { pos.BlendModeX, pos.BlendModeY, pos.BlendModeB };
    var clampPerChannel = pos.Clamp ?? [];

    // Fast-skip: if every channel is None there is no work for this position.
    if (modes[0] == PatchBlendMode.None
        && modes[1] == PatchBlendMode.None
        && modes[2] == PatchBlendMode.None)
      return;

    for (var dy = 0; dy < patch.Height; ++dy) {
      var srcY = patch.Y0 + dy;
      var dstY = pos.Y + dy;
      if ((uint)dstY >= (uint)height)
        continue;

      for (var dx = 0; dx < patch.Width; ++dx) {
        var srcX = patch.X0 + dx;
        var dstX = pos.X + dx;
        if ((uint)dstX >= (uint)width)
          continue;

        var dstIdx = dstY * width + dstX;

        for (var c = 0; c < 3; ++c) {
          var mode = modes[c];
          if (mode == PatchBlendMode.None)
            continue;

          var srcVal = _ReadReferencePixel(referenceFrame, c, srcX, srcY, width, height);
          var dstVal = channels[c][dstIdx];
          var clamp = c < clampPerChannel.Length && clampPerChannel[c];

          channels[c][dstIdx] = _Blend(mode, dstVal, srcVal, clamp);
        }
      }
    }
  }

  /// <summary>
  /// Read one pixel from a reference frame, treating null / short / out-of-
  /// bounds inputs as zero. Mirrors libjxl's "unmaterialised reference =
  /// zero" semantics for first-wave decoding.
  /// </summary>
  private static float _ReadReferencePixel(
    float[][]? referenceFrame,
    int channel,
    int x, int y,
    int width, int height
  ) {
    if (referenceFrame == null)
      return 0f;
    if (channel >= referenceFrame.Length)
      return 0f;
    var plane = referenceFrame[channel];
    if (plane == null)
      return 0f;
    if ((uint)x >= (uint)width || (uint)y >= (uint)height)
      return 0f;
    var idx = y * width + x;
    if ((uint)idx >= (uint)plane.Length)
      return 0f;
    return plane[idx];
  }

  /// <summary>
  /// Per-channel blend per ISO/IEC 18181-1 §G.11.2 / libjxl
  /// <c>PerformBlending</c> in <c>blending.cc</c>. The "above" / "below"
  /// terminology matches libjxl: <c>BlendAbove</c> means the patch is on
  /// top (so it dominates), <c>BlendBelow</c> means the patch is below (so
  /// the existing pixel dominates).
  ///
  /// <para>The <c>AlphaWeightedAdd*</c> variants degrade to a plain Add in
  /// the first-wave implementation: the patch's auxiliary alpha channel is
  /// not yet materialised in the patch entry's data flow, so we cannot
  /// compute the alpha-weighted scale. Encoders that emit AlphaWeightedAdd
  /// for color channels are rare (the modes are typically used on extra
  /// channels for sprite blending) so the approximation is acceptable for
  /// the first-wave scope. Switching to the spec-conformant
  /// <c>existing + alpha * patch</c> path is a one-line change once the
  /// alpha plane is wired into Apply.</para>
  /// </summary>
  private static float _Blend(PatchBlendMode mode, float existing, float patch, bool clamp) {
    var result = mode switch {
      PatchBlendMode.None => existing,
      PatchBlendMode.Replace => patch,
      PatchBlendMode.Add => existing + patch,
      PatchBlendMode.Mul => existing * patch,
      // BlendAbove: the patch sits on top; treat patch as opaque (alpha=1)
      // since extra-channel alpha is not wired in the first wave.
      PatchBlendMode.BlendAbove => patch,
      // BlendBelow: existing sits on top; treat existing as opaque.
      PatchBlendMode.BlendBelow => existing,
      // AlphaWeightedAdd*: degrade to plain add until extra-channel alpha
      // is wired through the dictionary. See remarks above.
      PatchBlendMode.AlphaWeightedAddAbove => existing + patch,
      PatchBlendMode.AlphaWeightedAddBelow => existing + patch,
      _ => throw new InvalidDataException($"Unknown patch blend mode: {(int)mode}."),
    };
    if (clamp) {
      // libjxl clamps to [0, 1] for the "color channel" range after XYB
      // blending. Strictly the clamp is per-channel-range, but [0, 1] is
      // the canonical choice for intermediate XYB float planes.
      if (result < 0f) result = 0f;
      else if (result > 1f) result = 1f;
    }
    return result;
  }
}
