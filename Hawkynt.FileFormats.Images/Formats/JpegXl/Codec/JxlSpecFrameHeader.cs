using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>JPEG XL frame type (libjxl <c>FrameType</c> in
/// <c>frame_header.h</c>).</summary>
internal enum JxlFrameType : byte {
  Regular = 0,
  DcFrame = 1,
  ReferenceOnly = 2,
  SkipProgressive = 3,
}

/// <summary>JPEG XL frame encoding (libjxl <c>FrameEncoding</c>).</summary>
internal enum JxlFrameEncoding : byte {
  VarDct = 0,
  Modular = 1,
}

/// <summary>JPEG XL frame color transform (libjxl <c>ColorTransform</c>).</summary>
internal enum JxlColorTransform : byte {
  Xyb = 0,
  None = 1,
  YCbCr = 2,
}

/// <summary>
/// Spec-conformant FrameHeader parser. Mirrors libjxl
/// <c>FrameHeader::VisitFields</c> (lib/jxl/frame_header.cc) field-by-field
/// so that the bit stream position after the call matches the reference
/// decoder exactly. Only fields downstream consumers currently use are
/// surfaced; the rest are still read so the bit position advances correctly.
/// </summary>
internal sealed class JxlSpecFrameHeader {

  public bool AllDefault { get; init; }
  public JxlFrameType FrameType { get; init; } = JxlFrameType.Regular;
  public JxlFrameEncoding Encoding { get; init; } = JxlFrameEncoding.VarDct;
  public ulong Flags { get; init; }
  public JxlColorTransform ColorTransform { get; init; } = JxlColorTransform.Xyb;

  /// <summary>True when <see cref="ColorTransform"/> is YCbCr. Kept as a
  /// convenience for legacy callers; new code should consult
  /// <see cref="ColorTransform"/> directly.</summary>
  public bool DoYCbCr => ColorTransform == JxlColorTransform.YCbCr;

  public uint UpsamplingShift { get; init; }
  public uint GroupSizeShift { get; init; } = 1;
  public uint XQmScale { get; init; } = 3;
  public uint BQmScale { get; init; } = 2;
  public uint NumPasses { get; init; } = 1;
  public bool IsLast { get; init; } = true;

  /// <summary>Where the frame sits in the picture, and how much of it the frame
  /// covers. A frame that does not cover the picture is drawn over what is
  /// already there.</summary>
  public int OriginX { get; init; }
  public int OriginY { get; init; }
  public int FrameWidth { get; init; }
  public int FrameHeight { get; init; }

  /// <summary>How the frame is combined with what is under it: 0 replace,
  /// 1 add, 2 blend, 3 alpha-weighted add, 4 multiply.</summary>
  public uint BlendMode { get; init; }

  /// <summary>Which stored frame it is combined with.</summary>
  public uint BlendSource { get; init; }

  /// <summary>Which extra channel carries the alpha it blends by.</summary>
  public uint BlendAlphaChannel { get; init; }

  /// <summary>Whether the alpha is clamped before blending.</summary>
  public bool BlendClamp { get; init; }

  /// <summary>Which slot the frame is kept in for a later one to refer to,
  /// or zero for none.</summary>
  public uint SaveAsReference { get; init; }

  /// <summary>True when the frame covers less than the whole picture.</summary>
  public bool IsPartialFrame { get; init; }

  /// <summary>
  /// How long the frame is shown, in the file's own ticks. Only meaningful when
  /// the file is an animation; zero there means the frame is a layer of the one
  /// that follows rather than something shown on its own.
  /// </summary>
  public uint Duration { get; init; }
  public bool SaveBeforeColorTransform { get; init; }
  public string Name { get; init; } = "";

  /// <summary>Gaborish loop-filter parameters when the frame's restoration
  /// filter section was parsed and Gaborish was enabled. <c>null</c> when
  /// disabled or when the loop filter was at all_default.</summary>
  public GaborishParams? GaborishParameters { get; init; }

  /// <summary>EPF (edge-preserving filter) parameters when EPF was enabled.
  /// <c>null</c> when iters=0 or all_default.</summary>
  public EpfParams? EpfParameters { get; init; }

  // libjxl Flags bitset
  private const ulong _kNoise = 1;
  private const ulong _kPatches = 2;
  private const ulong _kSplines = 16;
  private const ulong _kUseDcFrame = 0x20;
  private const ulong _kSkipAdaptiveDcSmoothing = 0x80;

  /// <summary>libjxl <c>LoopFilter::epf_iters</c> default.</summary>
  private const int _DefaultEpfIters = 2;

  /// <summary>
  /// Decode a FrameHeader. Mirrors libjxl <c>FrameHeader::VisitFields</c>
  /// step-for-step.
  /// </summary>
  /// <param name="imageWidth">The picture's width, which is what a frame's own
  /// size is compared against to tell whether it covers the picture. Zero means
  /// unknown, and then a frame is taken to cover it.</param>
  /// <param name="imageHeight">The picture's height, likewise.</param>
  public static JxlSpecFrameHeader Decode(
    JxlBitReader r, JxlImageMetadata? imageMetadata = null, int imageWidth = 0, int imageHeight = 0
  ) {
    ArgumentNullException.ThrowIfNull(r);

    // libjxl `frame_header.cc`: when `nonserialized_metadata` is null,
    // `xyb_encoded` defaults to TRUE (so the default frame encoding is
    // VarDCT). Mirror that here for the metadata-less convenience overload
    // used by parser unit tests.
    var xybEncoded = imageMetadata?.XybEncoded ?? true;
    var numExtraChannels = (int)(imageMetadata?.NumExtraChannels ?? 0);
    var haveAnimation = imageMetadata?.HaveAnimation ?? false;
    var haveTimecodes = imageMetadata?.Animation.HaveTimecodes ?? false;

    // 0. all_default
    var allDefault = r.ReadBool();
    if (allDefault) {
      // libjxl: SetDefault, which defaults every nested bundle too — including
      // the loop filter, whose own defaults are the smoothing filter on and two
      // passes of the edge-preserving one. Leaving those unset here reads as
      // "no filtering at all", and a frame that states nothing is exactly the
      // frame that expects both.
      return new() {
        AllDefault = true,
        FrameType = JxlFrameType.Regular,
        Encoding = xybEncoded ? JxlFrameEncoding.VarDct : JxlFrameEncoding.Modular,
        ColorTransform = xybEncoded ? JxlColorTransform.Xyb : JxlColorTransform.None,
        Flags = 0,
        GaborishParameters = new GaborishParams { Enabled = true },
        EpfParameters = new EpfParams { Iters = _DefaultEpfIters },
      };
    }

    // 1. frame_type = U32(Val(0), Val(1), Val(2), Val(3)). Each Val() is a
    // pure-selector encoding (no extra bits), so the U32 is identical to
    // ReadBits(2) — the 2-bit selector value IS the frame type.
    var frameType = (JxlFrameType)r.ReadBits(2);

    // 2. is_modular Bool. False → kVarDCT; True → kModular.
    var isModular = r.ReadBool();
    var encoding = isModular ? JxlFrameEncoding.Modular : JxlFrameEncoding.VarDct;

    // 3. flags U64.
    var flags = r.ReadU64();

    // 4. color_transform: when xyb_encoded, forced to kXYB (no bits read).
    //    Otherwise read Bool alternate; kYCbCr if true, kNone if false.
    JxlColorTransform colorTransform;
    if (xybEncoded) {
      colorTransform = JxlColorTransform.Xyb;
    } else {
      var alternate = r.ReadBool();
      colorTransform = alternate ? JxlColorTransform.YCbCr : JxlColorTransform.None;
    }

    // 5. ChromaSubsampling for YCbCr (and !UseDcFrame). 3 components × 2 bits.
    if (colorTransform == JxlColorTransform.YCbCr && (flags & _kUseDcFrame) == 0) {
      for (var i = 0; i < 3; ++i)
        r.ReadBits(2);
    }

    // 6. Upsampling: U32(Val(1), Val(2), Val(4), Val(8)) when !UseDcFrame.
    //    Plus per-extra-channel ec_upsampling.
    var upShift = 1u;
    if ((flags & _kUseDcFrame) == 0) {
      upShift = r.ReadU32(1, 0, 2, 0, 4, 0, 8, 0);
      for (var i = 0; i < numExtraChannels; ++i)
        r.ReadU32(1, 0, 2, 0, 4, 0, 8, 0);
    }

    // 7. group_size_shift: Bits(2) when Modular. Default 1.
    var groupSizeShift = 1u;
    if (encoding == JxlFrameEncoding.Modular)
      groupSizeShift = r.ReadBits(2);

    // 8. x_qm_scale, b_qm_scale: Bits(3) each when VarDCT && color_transform=kXYB.
    var xqm = 3u;
    var bqm = 2u;
    if (encoding == JxlFrameEncoding.VarDct && colorTransform == JxlColorTransform.Xyb) {
      xqm = r.ReadBits(3);
      bqm = r.ReadBits(3);
    }

    // 9. Passes (libjxl Passes::VisitFields) when !ReferenceOnly.
    var numPasses = 1u;
    if (frameType != JxlFrameType.ReferenceOnly)
      numPasses = _ReadPasses(r);

    // 10. dc_level for DCFrame: U32(Val(1), Val(2), Val(3), Val(4)).
    if (frameType == JxlFrameType.DcFrame)
      r.ReadU32(1, 0, 2, 0, 3, 0, 4, 0);

    // 11. custom_size_or_origin and conditional crop fields when !DCFrame.
    var customSizeOrOrigin = false;
    var isPartialFrame = false;
    var originX = 0;
    var originY = 0;
    var frameWidth = imageWidth;
    var frameHeight = imageHeight;
    if (frameType != JxlFrameType.DcFrame) {
      customSizeOrOrigin = r.ReadBool();
      if (customSizeOrOrigin) {
        // U32 enc: Bits(8), BitsOffset(11, 256), BitsOffset(14, 2304),
        //          BitsOffset(30, 18688).
        // Origin (signed-packed) only for Regular / SkipProgressive.
        if (frameType == JxlFrameType.Regular || frameType == JxlFrameType.SkipProgressive) {
          originX = _UnpackSigned(r.ReadU32(0, 8, 256, 11, 2304, 14, 18688, 30));
          originY = _UnpackSigned(r.ReadU32(0, 8, 256, 11, 2304, 14, 18688, 30));
        }
        frameWidth = (int)r.ReadU32(0, 8, 256, 11, 2304, 14, 18688, 30);
        frameHeight = (int)r.ReadU32(0, 8, 256, 11, 2304, 14, 18688, 30);
        if (frameWidth == 0 || frameHeight == 0)
          throw new System.IO.InvalidDataException("A frame states a crop with no width or no height.");

        // Whether the frame covers the whole picture, which decides one field
        // further down: a frame that does not cover it has to say which older
        // frame it is drawn over, even when it replaces rather than blends.
        // Treating every frame as covering skips that field and puts the whole
        // rest of the frame at the wrong offset.
        if (frameType is JxlFrameType.Regular or JxlFrameType.SkipProgressive) {
          isPartialFrame |= originX > 0;
          isPartialFrame |= originY > 0;
          isPartialFrame |= imageWidth > 0 && frameWidth + originX < imageWidth;
          isPartialFrame |= imageHeight > 0 && frameHeight + originY < imageHeight;
        }
      }
    }

    // 12. BlendingInfo, ec_blending_info, animation, is_last for Regular/SkipProgressive.
    var isLast = true;
    var blendMode = 0u; // default kReplace
    var blendSource = 0u;
    var blendAlphaChannel = 0u;
    var blendClamp = false;
    var duration = 0u;
    if (frameType == JxlFrameType.Regular || frameType == JxlFrameType.SkipProgressive) {
      (blendMode, blendSource, blendAlphaChannel, blendClamp) =
        _ReadBlendingInfo(r, numExtraChannels, isPartialFrame);
      for (var i = 0; i < numExtraChannels; ++i)
        _ReadBlendingInfo(r, numExtraChannels, isPartialFrame);
      if (haveAnimation) {
        // duration: U32(Val(0), Val(1), Bits(8), Bits(32))
        duration = r.ReadU32(0, 0, 1, 0, 0, 8, 0, 32);
        if (haveTimecodes)
          r.ReadBits(32);
      }
      isLast = r.ReadBool();
    } else {
      isLast = false;
    }

    // 13. save_as_reference for !DCFrame && !is_last.
    var saveAsReference = 0u;
    if (frameType != JxlFrameType.DcFrame && !isLast)
      saveAsReference = r.ReadU32(0, 0, 1, 0, 2, 0, 3, 0);

    // 14. save_before_color_transform.
    //     - For Regular/SkipProgressive when CanBeReferenced && replace && !partial.
    //     - For ReferenceOnly always (default true).
    var saveBeforeCT = false;
    if (frameType != JxlFrameType.DcFrame) {
      var canBeReferenced = !isLast && saveAsReference != 0; // duration check elided
      var conditionRegular = canBeReferenced
        && blendMode == 0 /* kReplace */
        && !isPartialFrame
        && (frameType == JxlFrameType.Regular || frameType == JxlFrameType.SkipProgressive);
      if (conditionRegular) {
        saveBeforeCT = r.ReadBool();
      } else if (frameType == JxlFrameType.ReferenceOnly) {
        saveBeforeCT = r.ReadBool();
      }
    } else {
      saveBeforeCT = true;
    }

    // 15. Name (UTF-8): U32(Val(0), BitsOffset(4, 0), BitsOffset(5, 16),
    //                       BitsOffset(10, 48)) bytes.
    var nameLen = (int)r.ReadU32(0, 0, 0, 4, 16, 5, 48, 10);
    var name = "";
    if (nameLen > 0) {
      var bytes = new byte[nameLen];
      for (var i = 0; i < nameLen; ++i)
        bytes[i] = r.ReadByte();
      name = System.Text.Encoding.UTF8.GetString(bytes);
    }

    // 16. LoopFilter (always present, with its own all_default flag). Sub-fields
    //     vary based on whether the frame is modular.
    var (gabParams, epfParams) = _ReadLoopFilter(r, isModular);

    // 17. Frame extensions: U64 mask + per-extension U64 size + payload bits.
    _ReadExtensions(r);

    return new() {
      AllDefault = false,
      FrameType = frameType,
      Encoding = encoding,
      Flags = flags,
      ColorTransform = colorTransform,
      UpsamplingShift = upShift,
      GroupSizeShift = groupSizeShift,
      XQmScale = xqm,
      BQmScale = bqm,
      NumPasses = numPasses,
      IsLast = isLast,
      OriginX = originX,
      OriginY = originY,
      FrameWidth = frameWidth,
      FrameHeight = frameHeight,
      BlendMode = blendMode,
      BlendSource = blendSource,
      BlendAlphaChannel = blendAlphaChannel,
      BlendClamp = blendClamp,
      SaveAsReference = saveAsReference,
      IsPartialFrame = isPartialFrame,
      Duration = duration,
      SaveBeforeColorTransform = saveBeforeCT,
      Name = name,
      GaborishParameters = gabParams,
      EpfParameters = epfParams,
    };
  }

  /// <summary>Read libjxl <c>Passes::VisitFields</c>: num_passes plus, when
  /// num_passes > 1, num_downsample, shift array, downsample array, last_pass
  /// array.</summary>
  private static uint _ReadPasses(JxlBitReader r) {
    // num_passes: U32(Val(1), Val(2), Val(3), BitsOffset(3, 4))
    var n = r.ReadU32(1, 0, 2, 0, 3, 0, 4, 3);
    if (n != 1) {
      // num_downsample: U32(Val(0), Val(1), Val(2), BitsOffset(1, 3))
      var numDownsample = r.ReadU32(0, 0, 1, 0, 2, 0, 3, 1);
      // shift[i] for i = 0..num_passes-2 (last shift is implicit 0).
      for (var i = 0u; i < n - 1; ++i)
        r.ReadBits(2);
      // downsample[i] for i = 0..num_downsample-1: U32(Val(1), Val(2), Val(4), Val(8))
      for (var i = 0u; i < numDownsample; ++i)
        r.ReadU32(1, 0, 2, 0, 4, 0, 8, 0);
      // last_pass[i]: U32(Val(0), Val(1), Val(2), Bits(3))
      for (var i = 0u; i < numDownsample; ++i)
        r.ReadU32(0, 0, 1, 0, 2, 0, 0, 3);
    }
    return n;
  }

  /// <summary>Read libjxl <c>BlendingInfo::VisitFields</c>: mode + conditional
  /// alpha_channel + clamp + source.</summary>
  /// <returns>The blend mode (0=kReplace, 1=kAdd, 2=kBlend, 3=kAlphaWeightedAdd, 4=kMul).</returns>
  private static (uint Mode, uint Source, uint AlphaChannel, bool Clamp) _ReadBlendingInfo(
    JxlBitReader r, int numExtraChannels, bool isPartialFrame
  ) {
    // mode: U32(Val(0), Val(1), Val(2), BitsOffset(2, 3))
    var mode = r.ReadU32(0, 0, 1, 0, 2, 0, 3, 2);
    var hasBlendOrAwa = (mode == 2 /* kBlend */ || mode == 3 /* kAlphaWeightedAdd */);
    var alphaChannel = 0u;
    if (numExtraChannels > 0 && hasBlendOrAwa) {
      // alpha_channel: U32(Val(0), Val(1), Val(2), BitsOffset(3, 3))
      alphaChannel = r.ReadU32(0, 0, 1, 0, 2, 0, 3, 3);
      if (alphaChannel >= numExtraChannels)
        throw new System.IO.InvalidDataException(
          $"A frame blends against extra channel {alphaChannel}, and there are only {numExtraChannels}.");
    }

    var clamp = false;
    if ((numExtraChannels > 0 && hasBlendOrAwa) || mode == 4 /* kMul */)
      clamp = r.ReadBool();

    // source: U32(Val(0), Val(1), Val(2), Val(3)) only when mode != kReplace || partial.
    var source = 0u;
    if (mode != 0 /* kReplace */ || isPartialFrame)
      source = r.ReadU32(0, 0, 1, 0, 2, 0, 3, 0);
    return (mode, source, alphaChannel, clamp);
  }

  private static int _UnpackSigned(uint packed) => (int)((packed >> 1) ^ (~(packed & 1) + 1));

  /// <summary>Read libjxl <c>LoopFilter::VisitFields</c>. Always reads
  /// 1-bit all_default; if 0 then gab/EPF/extensions.</summary>
  private static (GaborishParams? Gab, EpfParams? Epf) _ReadLoopFilter(JxlBitReader r, bool isModular) {
    var allDefault = r.ReadBool();
    if (allDefault) {
      // libjxl Bundle::SetDefault for LoopFilter: gab on, two passes of EPF.
      return (new GaborishParams { Enabled = true }, new EpfParams { Iters = _DefaultEpfIters });
    }

    // gab Bool (default true)
    var gab = r.ReadBool();
    GaborishParams? gabParams = null;
    if (gab) {
      var gabCustom = r.ReadBool();
      if (gabCustom) {
        // 6 × F16 = 6 × 16 bits.
        for (var i = 0; i < 6; ++i)
          r.ReadBits(16);
      }
      gabParams = new GaborishParams { Enabled = true };
    } else {
      gabParams = new GaborishParams { Enabled = false };
    }

    // epf_iters: Bits(2) (default 2)
    var epfIters = r.ReadBits(2);
    EpfParams? epfParams = null;
    if (epfIters > 0) {
      // sharp_custom only for !modular
      if (!isModular) {
        var sharpCustom = r.ReadBool();
        if (sharpCustom) {
          for (var i = 0; i < 8; ++i) // kEpfSharpEntries = 8
            r.ReadBits(16); // F16
        }
      }
      var weightCustom = r.ReadBool();
      if (weightCustom) {
        for (var i = 0; i < 5; ++i)
          r.ReadBits(16); // 3 channel scales + 2 zero-flush thresholds
      }
      var sigmaCustom = r.ReadBool();
      if (sigmaCustom) {
        if (!isModular)
          r.ReadBits(16); // epf_quant_mul
        for (var i = 0; i < 3; ++i)
          r.ReadBits(16); // pass0_sigma_scale, pass2_sigma_scale, border_sad_mul
      }
      if (isModular)
        r.ReadBits(16); // epf_sigma_for_modular
      epfParams = new EpfParams { Iters = (int)epfIters };
    }

    // LoopFilter has its own extensions block.
    _ReadExtensions(r);
    return (gabParams, epfParams);
  }

  /// <summary>Read a U64 extensions bitmask + per-extension U64 size +
  /// payload bits. For unknown extensions the payload is consumed but not
  /// surfaced.</summary>
  private static void _ReadExtensions(JxlBitReader r) {
    var ext = r.ReadU64();
    if (ext == 0)
      return;
    var sizes = new ulong[64];
    for (var i = 0; i < 64; ++i)
      if ((ext & (1UL << i)) != 0)
        sizes[i] = r.ReadU64();
    for (var i = 0; i < 64; ++i)
      for (var b = 0UL; b < sizes[i]; ++b)
        r.ReadBits(1);
  }
}
