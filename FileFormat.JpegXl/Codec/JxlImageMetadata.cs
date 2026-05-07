using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// Spec-conformant ImageMetadata parser (ISO/IEC 18181-1 §3.6.3 / Annex A "Bundles").
//
// The JXL codestream layout is: signature (FF 0A) | SizeHeader | ImageMetadata | ICC? | frames…
// ImageMetadata immediately follows SizeHeader at the bit level (NOT byte-aligned) and has
// many conditional fields. The all_default flag (one bit) is the common case for typical
// libjxl encoder output and short-circuits all field reads to defaults.
//
// Validated against the spec: byte-level conformance tests in
// JxlImageMetadataTests cover all_default + the most common non-default paths.
// =====================================================================================

/// <summary>JPEG XL image orientation (§3.6.3, EXIF orientation values 1–8).</summary>
internal enum JxlOrientation : byte {
  Identity = 1,
  FlipHorizontal = 2,
  Rotate180 = 3,
  FlipVertical = 4,
  Transpose = 5,
  Rotate90Cw = 6,
  AntiTranspose = 7,
  Rotate90Ccw = 8,
}

/// <summary>JPEG XL bit-depth descriptor (§3.6.3 BitDepth bundle).</summary>
internal readonly record struct JxlBitDepth {

  /// <summary>True if floating-point samples; false if unsigned integer.</summary>
  public bool FloatingPoint { get; init; }

  /// <summary>Bits per sample (integer mode) or mantissa bits (float mode).</summary>
  public uint BitsPerSample { get; init; }

  /// <summary>Float-mode exponent bits (only meaningful when <see cref="FloatingPoint"/> is true).</summary>
  public uint ExpBitsPerSample { get; init; }

  /// <summary>Default: unsigned 8-bit integer samples.</summary>
  public static JxlBitDepth Default => new() { FloatingPoint = false, BitsPerSample = 8, ExpBitsPerSample = 0 };

  public static JxlBitDepth Decode(JxlBitReader r) {
    var floatSample = r.ReadBool();
    if (!floatSample) {
      // Integer: bits_per_sample = U32(8, 10, 12, 1+u(6))
      var bits = r.ReadU32(8, 0, 10, 0, 12, 0, 1, 6);
      return new() { FloatingPoint = false, BitsPerSample = bits, ExpBitsPerSample = 0 };
    }
    // Float: total bits = U32(32, 16, 24, 1+u(6)), exp_bits = 1 + u(4)
    var totalBits = r.ReadU32(32, 0, 16, 0, 24, 0, 1, 6);
    var expBits = 1u + r.ReadBits(4);
    return new() { FloatingPoint = true, BitsPerSample = totalBits, ExpBitsPerSample = expBits };
  }
}

/// <summary>JPEG XL preview header (§3.6.3) — small thumbnail bundled before main image.</summary>
internal readonly record struct JxlPreviewHeader {
  public uint Width { get; init; }
  public uint Height { get; init; }

  public static JxlPreviewHeader Decode(JxlBitReader r) {
    // Per spec PreviewHeader uses smaller distributions than the main SizeHeader.
    var div8 = r.ReadBool();
    uint h, w;
    if (div8) {
      h = 8 * r.ReadU32(16, 0, 32, 0, 1, 5, 33, 9);
    } else {
      h = r.ReadU32(1, 6, 65, 8, 321, 10, 1345, 12);
    }
    var ratio = r.ReadBits(3);
    if (ratio == 0) {
      w = div8 ? 8 * r.ReadU32(16, 0, 32, 0, 1, 5, 33, 9) : r.ReadU32(1, 6, 65, 8, 321, 10, 1345, 12);
    } else {
      w = _ApplyRatio(ratio, h);
    }
    return new() { Width = w, Height = h };
  }

  private static uint _ApplyRatio(uint ratio, uint height) => ratio switch {
    1 => height,
    2 => (uint)((ulong)height * 12 / 10),
    3 => (uint)((ulong)height * 4 / 3),
    4 => (uint)((ulong)height * 3 / 2),
    5 => (uint)((ulong)height * 16 / 9),
    6 => (uint)((ulong)height * 5 / 4),
    7 => height * 2,
    _ => height,
  };
}

/// <summary>JPEG XL animation header (§3.6.3).</summary>
internal readonly record struct JxlAnimationHeader {
  public uint TpsNumerator { get; init; }
  public uint TpsDenominator { get; init; }
  public uint NumLoops { get; init; }
  public bool HaveTimecodes { get; init; }

  public static JxlAnimationHeader Decode(JxlBitReader r) {
    var num = r.ReadU32(100, 0, 1000, 0, 1, 10, 1, 30);
    var den = r.ReadU32(1, 0, 1001, 0, 1, 8, 1, 10);
    var loops = r.ReadU32(0, 0, 0, 3, 0, 16, 0, 32);
    var haveTimecodes = r.ReadBool();
    return new() { TpsNumerator = num, TpsDenominator = den, NumLoops = loops, HaveTimecodes = haveTimecodes };
  }
}

/// <summary>Per-extra-channel info (§3.6.3 ExtraChannelInfo bundle).</summary>
internal readonly record struct JxlExtraChannelInfo {
  public uint Type { get; init; }              // 0=Alpha, 1=Depth, 2=SpotColor, 3=SelectionMask, 4=Black, 5=CFA, 6=Thermal, …
  public JxlBitDepth BitDepth { get; init; }
  public uint DimShift { get; init; }          // Channel dimension shift relative to main image
  public string Name { get; init; }            // Optional name (UTF-8)
  public bool AlphaAssociated { get; init; }   // Premultiplied-alpha flag (alpha channels only)

  public static JxlExtraChannelInfo Decode(JxlBitReader r) {
    var dAll = r.ReadBool();
    if (dAll) {
      // Defaults: Alpha, 8-bit unsigned int, dim_shift=0, no name, not associated
      return new() {
        Type = 0,
        BitDepth = JxlBitDepth.Default,
        DimShift = 0,
        Name = "",
        AlphaAssociated = false,
      };
    }
    var type = r.ReadU32(0, 0, 1, 0, 2, 4, 1, 8);
    var bitDepth = JxlBitDepth.Decode(r);
    var dimShift = r.ReadU32(0, 0, 3, 0, 4, 0, 1, 3);
    var nameLen = (int)r.ReadU32(0, 0, 0, 4, 16, 5, 48, 10);
    var name = "";
    if (nameLen > 0) {
      var bytes = new byte[nameLen];
      for (var i = 0; i < nameLen; ++i)
        bytes[i] = r.ReadByte();
      name = System.Text.Encoding.UTF8.GetString(bytes);
    }
    // Alpha-associated flag exists only for type=Alpha
    var alphaAssoc = type == 0 && r.ReadBool();
    // Spot color extra info: r/g/b/a as F16 — skipped but bits must be consumed
    if (type == 2) {
      r.ReadBits(16);
      r.ReadBits(16);
      r.ReadBits(16);
      r.ReadBits(16);
    }
    if (type == 5) // CFA: cfa_channel u32(1, u(2), 3+u(4), 19+u(8))
      r.ReadU32(1, 0, 1, 2, 3, 4, 19, 8);
    return new() {
      Type = type,
      BitDepth = bitDepth,
      DimShift = dimShift,
      Name = name,
      AlphaAssociated = alphaAssoc,
    };
  }
}

/// <summary>Color encoding bundle (§3.6.3). Only enough is parsed to advance past it correctly;
/// the actual color-space pipeline stays default for now (sRGB).</summary>
internal readonly record struct JxlColorEncoding {
  public bool AllDefault { get; init; }
  public bool WantIcc { get; init; }
  public uint ColorSpace { get; init; }   // 0=RGB, 1=Gray, 2=XYB, 3=Unknown
  public uint WhitePoint { get; init; }
  public uint Primaries { get; init; }
  public uint TransferFunction { get; init; }
  public uint RenderingIntent { get; init; }

  public static JxlColorEncoding Decode(JxlBitReader r) {
    var allDefault = r.ReadBool();
    if (allDefault)
      return new() { AllDefault = true, WantIcc = false, ColorSpace = 0, WhitePoint = 1, Primaries = 1, TransferFunction = 13, RenderingIntent = 0 };

    var wantIcc = r.ReadBool();
    var colorSpace = r.ReadU32(0, 0, 1, 0, 2, 0, 4, 0);   // selectors map to 0/1/2/4 directly
    var whitePoint = 1u;
    var primaries = 1u;
    var tf = 13u;
    var ri = 0u;
    if (!wantIcc && colorSpace != 1) {  // not Gray, not ICC: read primaries
      whitePoint = r.ReadU32(1, 0, 2, 0, 10, 0, 1, 2);
      if (whitePoint == 2)
        for (var i = 0; i < 2; ++i) _ReadCustomXy(r);
      primaries = r.ReadU32(1, 0, 2, 0, 11, 0, 1, 2);
      if (primaries == 2)
        for (var i = 0; i < 6; ++i) _ReadCustomXy(r);
    }
    if (!wantIcc) {
      var hasGamma = r.ReadBool();
      if (hasGamma)
        tf = r.ReadBits(24);
      else
        tf = r.ReadU32(1, 0, 8, 0, 13, 0, 1, 4);
      ri = r.ReadU32(0, 0, 1, 0, 2, 0, 1, 2);
    }
    return new() {
      AllDefault = false, WantIcc = wantIcc, ColorSpace = colorSpace,
      WhitePoint = whitePoint, Primaries = primaries, TransferFunction = tf, RenderingIntent = ri,
    };
  }

  private static void _ReadCustomXy(JxlBitReader r) {
    // CustomXY: each coordinate is a signed s32 with a u32 distribution.
    // u32(u(19), 524288 + u(19), 1048576 + u(20), 2097152 + u(21))
    r.ReadU32(0, 19, 524288, 19, 1048576, 20, 2097152, 21);
    r.ReadU32(0, 19, 524288, 19, 1048576, 20, 2097152, 21);
  }
}

/// <summary>Tone mapping bundle (§3.6.3). Always parsed when extra_fields is set.</summary>
internal readonly record struct JxlToneMapping {
  public bool AllDefault { get; init; }
  public float IntensityTarget { get; init; }     // Default 255 (SDR display)
  public float MinNits { get; init; }             // Default 0
  public bool RelativeToMaxDisplay { get; init; }
  public float LinearBelow { get; init; }

  public static JxlToneMapping Decode(JxlBitReader r) {
    var dAll = r.ReadBool();
    if (dAll)
      return new() { AllDefault = true, IntensityTarget = 255f, MinNits = 0f, RelativeToMaxDisplay = false, LinearBelow = 0f };
    // intensity_target, min_nits: F16 (16 bits each). We don't need the actual float math here,
    // just to advance past them.
    var it = _ReadF16(r);
    var mn = _ReadF16(r);
    var rel = r.ReadBool();
    var lb = _ReadF16(r);
    return new() { AllDefault = false, IntensityTarget = it, MinNits = mn, RelativeToMaxDisplay = rel, LinearBelow = lb };
  }

  private static float _ReadF16(JxlBitReader r) {
    var bits = (ushort)r.ReadBits(16);
    var sign = (bits >> 15) & 1;
    var exp = (bits >> 10) & 0x1F;
    var frac = bits & 0x3FF;
    if (exp == 0)
      return sign != 0 ? -0f : 0f;
    if (exp == 31)
      return frac == 0 ? (sign != 0 ? float.NegativeInfinity : float.PositiveInfinity) : float.NaN;
    var mantissa = (1 + frac / 1024.0);
    var value = mantissa * Math.Pow(2, exp - 15);
    return (float)(sign != 0 ? -value : value);
  }
}

/// <summary>JPEG XL Image Metadata bundle (§3.6.3). Sits between SizeHeader and the first frame.</summary>
internal sealed class JxlImageMetadata {

  public bool AllDefault { get; init; }
  public bool ExtraFields { get; init; }
  public JxlOrientation Orientation { get; init; } = JxlOrientation.Identity;
  public bool HaveIntrinsicSize { get; init; }
  public uint IntrinsicWidth { get; init; }
  public uint IntrinsicHeight { get; init; }
  public bool HavePreview { get; init; }
  public JxlPreviewHeader Preview { get; init; }
  public bool HaveAnimation { get; init; }
  public JxlAnimationHeader Animation { get; init; }
  public JxlBitDepth BitDepth { get; init; } = JxlBitDepth.Default;
  public bool Modular16BitBuffers { get; init; } = true;
  public uint NumExtraChannels { get; init; }
  public JxlExtraChannelInfo[] ExtraChannelInfo { get; init; } = [];
  public bool XybEncoded { get; init; } = true;
  public JxlColorEncoding ColorEncoding { get; init; }
  public JxlToneMapping ToneMapping { get; init; }
  public ulong Extensions { get; init; }

  public static JxlImageMetadata Decode(JxlBitReader r) {
    var allDefault = r.ReadBool();
    if (allDefault)
      return new() {
        AllDefault = true,
        ColorEncoding = new JxlColorEncoding {
          AllDefault = true, WantIcc = false, ColorSpace = 0,
          WhitePoint = 1, Primaries = 1, TransferFunction = 13, RenderingIntent = 0,
        },
        ToneMapping = new JxlToneMapping { AllDefault = true, IntensityTarget = 255f },
      };

    var extraFields = r.ReadBool();
    var orient = JxlOrientation.Identity;
    var haveIs = false;
    uint isW = 0, isH = 0;
    var haveP = false;
    var prev = default(JxlPreviewHeader);
    var haveA = false;
    var anim = default(JxlAnimationHeader);

    if (extraFields) {
      orient = (JxlOrientation)(1 + r.ReadBits(3));
      haveIs = r.ReadBool();
      if (haveIs) {
        // Intrinsic size uses the same SizeHeader format
        var (w, h) = JxlSizeHeader.Decode(r);
        isW = (uint)w; isH = (uint)h;
      }
      haveP = r.ReadBool();
      if (haveP) prev = JxlPreviewHeader.Decode(r);
      haveA = r.ReadBool();
      if (haveA) anim = JxlAnimationHeader.Decode(r);
    }

    var bitDepth = JxlBitDepth.Decode(r);
    var modular16 = r.ReadBool();
    var numExtra = r.ReadU32(0, 0, 1, 0, 2, 4, 1, 12);
    var extraInfos = new JxlExtraChannelInfo[numExtra];
    for (var i = 0; i < numExtra; ++i)
      extraInfos[i] = JxlExtraChannelInfo.Decode(r);

    var xyb = r.ReadBool();
    var colorEnc = JxlColorEncoding.Decode(r);
    var toneMap = extraFields ? JxlToneMapping.Decode(r) : new JxlToneMapping { AllDefault = true, IntensityTarget = 255f };
    // Extensions: u64 bitmask followed, per set bit, by a u64 size-in-bits and that many
    // payload bits (§3.4 Extensions Bundle, §C.2.4 in libjxl). We don't decode any extension
    // payloads — we just need to advance past them so subsequent reads stay aligned.
    //
    // Lenient EOF handling: libjxl's reference parser defers EOF errors to BitReader::Close
    // rather than failing inside the visit pass (it tracks "overread" bits and validates at
    // the end). We mirror that here for metadata-only decoding: if an extension's encoded
    // size or its payload runs past the end of the codestream — which can happen for tiny
    // libjxl test files where extension space was reserved but no actual frames follow — we
    // stop advancing rather than aborting the entire metadata bundle. The metadata fields
    // already populated above are still meaningful, and any downstream frame parser will
    // surface the truncation through its own EOF check.
    // Per libjxl `fields.cc::ReadVisitor::BeginExtensions/EndExtensions`:
    //   1. Read U64 extensions bitmask.
    //   2. For each set bit: read U64 size — sizes are stored consecutively
    //      (NOT interleaved with their payloads).
    //   3. After all sizes are read: skip `sum(sizes)` bits in one operation.
    //
    // Our previous implementation read size+payload+size+payload, which
    // reads payload from inside the size field of the next extension when
    // there's >1 extension.
    var ext = r.ReadU64();
    if (ext != 0) {
      ulong totalExtBits = 0;
      var remainingMask = ext;
      while (remainingMask != 0) {
        if (!r.HasBits(73))
          break;
        totalExtBits = checked(totalExtBits + r.ReadU64());
        remainingMask &= remainingMask - 1;
      }
      if (totalExtBits > 0)
        r.Skip(checked((long)totalExtBits));
    }

    // Per libjxl `dec_frame.cc`, when ColorEncoding.want_icc=true the bitstream
    // contains an ICC profile blob AFTER the ImageMetadata bundle (i.e. after
    // extensions). Reading it here keeps subsequent FrameHeader reads aligned.
    // Currently throws NotImplementedException — the libjxl predictor port is
    // pending; partial-metadata extraction in JpegXlReader.TryReadSpec catches
    // this and surfaces dimensions even when ICC blocks full decode.
    if (colorEnc.WantIcc)
      JxlIccProfileDecoder.Read(r);

    return new() {
      AllDefault = false,
      ExtraFields = extraFields,
      Orientation = orient,
      HaveIntrinsicSize = haveIs,
      IntrinsicWidth = isW,
      IntrinsicHeight = isH,
      HavePreview = haveP,
      Preview = prev,
      HaveAnimation = haveA,
      Animation = anim,
      BitDepth = bitDepth,
      Modular16BitBuffers = modular16,
      NumExtraChannels = numExtra,
      ExtraChannelInfo = extraInfos,
      XybEncoded = xyb,
      ColorEncoding = colorEnc,
      ToneMapping = toneMap,
      Extensions = ext,
    };
  }
}
