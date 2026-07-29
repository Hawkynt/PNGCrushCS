using System;

namespace FileFormat.Avif.Codec;

/// <summary>Color primaries as per AV1 spec.</summary>
internal enum Av1ColorPrimaries {
  Bt709 = 1,
  Unspecified = 2,
  Bt470M = 4,
  Bt470Bg = 5,
  Bt601 = 6,
  Smpte240 = 7,
  GenericFilm = 8,
  Bt2020 = 9,
  Xyz = 10,
  Smpte431 = 11,
  Smpte432 = 12,
  Ebu3213 = 22,
}

/// <summary>Transfer characteristics as per AV1 spec.</summary>
internal enum Av1TransferCharacteristics {
  Bt709 = 1,
  Unspecified = 2,
  Bt470M = 4,
  Bt470Bg = 5,
  Bt601 = 6,
  Smpte240 = 7,
  Linear = 8,
  Log100 = 9,
  Log100Sqrt10 = 10,
  Iec61966 = 11,
  Bt1361 = 12,
  Srgb = 13,
  Bt2020_10 = 14,
  Bt2020_12 = 15,
  Smpte2084 = 16,
  Smpte428 = 17,
  Hlg = 18,
}

/// <summary>Matrix coefficients as per AV1 spec.</summary>
internal enum Av1MatrixCoefficients {
  Identity = 0,
  Bt709 = 1,
  Unspecified = 2,
  Fcc = 4,
  Bt470Bg = 5,
  Bt601 = 6,
  Smpte240 = 7,
  YCgCo = 8,
  Bt2020Ncl = 9,
  Bt2020Cl = 10,
  Smpte2085 = 11,
  ChromaDerivedNcl = 12,
  ChromaDerivedCl = 13,
  ICtCp = 14,
}

/// <summary>Chroma sample position as per AV1 spec.</summary>
internal enum Av1ChromaSamplePosition {
  Unknown = 0,
  Vertical = 1,
  Colocated = 2,
}

/// <summary>Parsed AV1 sequence header OBU (AV1 spec 5.5).</summary>
internal sealed class Av1SequenceHeader {

  public int SeqProfile { get; set; }
  public bool StillPicture { get; set; }
  public bool ReducedStillPictureHeader { get; set; }
  public int MaxFrameWidthMinus1 { get; set; }
  public int MaxFrameHeightMinus1 { get; set; }
  public int MaxFrameWidth => MaxFrameWidthMinus1 + 1;
  public int MaxFrameHeight => MaxFrameHeightMinus1 + 1;

  // Operating points
  public int OperatingPointsCount { get; set; }

  // Timing info
  public bool TimingInfoPresent { get; set; }
  public bool DecoderModelInfoPresent { get; set; }

  // Frame ID
  public bool FrameIdNumbersPresent { get; set; }
  public int DeltaFrameIdLength { get; set; }
  public int AdditionalFrameIdLength { get; set; }

  // Resolution
  public bool Use128x128Superblock { get; set; }
  public bool EnableFilterIntra { get; set; }
  public bool EnableIntraEdgeFilter { get; set; }

  // Inter features (not used for still images)
  public bool EnableInterIntra { get; set; }
  public bool EnableMaskedCompound { get; set; }
  public bool EnableWarpedMotion { get; set; }
  public bool EnableDualFilter { get; set; }
  public bool EnableOrderHint { get; set; }
  public bool EnableJntComp { get; set; }
  public bool EnableRefFrameMvs { get; set; }
  public int OrderHintBits { get; set; }

  public bool EnableSuperRes { get; set; }
  public bool EnableCdef { get; set; }
  public bool EnableRestoration { get; set; }

  // Color config
  public bool HighBitDepth { get; set; }
  public bool TwelveBit { get; set; }
  public int BitDepth { get; set; } = 8;
  public bool MonoChrome { get; set; }
  public bool ColorDescriptionPresent { get; set; }
  public Av1ColorPrimaries ColorPrimaries { get; set; } = Av1ColorPrimaries.Unspecified;
  public Av1TransferCharacteristics TransferCharacteristics { get; set; } = Av1TransferCharacteristics.Unspecified;
  public Av1MatrixCoefficients MatrixCoefficients { get; set; } = Av1MatrixCoefficients.Unspecified;
  public bool ColorRange { get; set; }
  public int SubsamplingX { get; set; }
  public int SubsamplingY { get; set; }
  public Av1ChromaSamplePosition ChromaSamplePosition { get; set; }
  public bool SeparateUvDeltaQ { get; set; }
  public int NumPlanes => MonoChrome ? 1 : 3;

  /// <summary>Parses a sequence header from an OBU payload.</summary>
  public static Av1SequenceHeader Parse(byte[] data, int offset, int length) {
    var reader = new Av1BitReader(data, offset, length);
    var sh = new Av1SequenceHeader();

    sh.SeqProfile = (int)reader.ReadBits(3);
    sh.StillPicture = reader.ReadBool();
    sh.ReducedStillPictureHeader = reader.ReadBool();

    if (sh.SeqProfile > 2)
      throw new NotSupportedException($"AV1 Profile {sh.SeqProfile} is not supported.");

    if (sh.ReducedStillPictureHeader) {
      sh.TimingInfoPresent = false;
      sh.DecoderModelInfoPresent = false;
      sh.OperatingPointsCount = 1;
      // operating_point_idc[0] = 0 implicitly
      // seq_level_idx[0]
      reader.ReadBits(5);
      // seq_tier[0] = 0 implicitly
    } else {
      sh.TimingInfoPresent = reader.ReadBool();
      if (sh.TimingInfoPresent) {
        // timing_info()
        reader.ReadBits(32); // num_units_in_display_tick
        reader.ReadBits(32); // time_scale
        var equalPictureInterval = reader.ReadBool();
        if (equalPictureInterval)
          reader.ReadUvlc(); // num_ticks_per_picture_minus_1

        sh.DecoderModelInfoPresent = reader.ReadBool();
        if (sh.DecoderModelInfoPresent) {
          reader.ReadBits(32); // buffer_delay_length_minus_1
          reader.ReadBits(32); // num_units_in_decoding_tick
          reader.ReadBits(5);  // buffer_removal_time_length_minus_1
          reader.ReadBits(5);  // frame_presentation_time_length_minus_1
        }
      }

      sh.OperatingPointsCount = (int)reader.ReadBits(5) + 1;
      for (var i = 0; i < sh.OperatingPointsCount; ++i) {
        reader.ReadBits(12); // operating_point_idc[i]
        var seqLevelIdx = (int)reader.ReadBits(5);
        if (seqLevelIdx > 7)
          reader.ReadBool(); // seq_tier[i]

        if (sh.DecoderModelInfoPresent) {
          var decoderModelPresent = reader.ReadBool();
          if (decoderModelPresent) {
            reader.ReadBits(32); // decoder_buffer_delay
            reader.ReadBits(32); // encoder_buffer_delay
            reader.ReadBool();   // low_delay_mode_flag
          }
        }
      }
    }

    var frameWidthBits = (int)reader.ReadBits(4) + 1;
    var frameHeightBits = (int)reader.ReadBits(4) + 1;
    sh.MaxFrameWidthMinus1 = (int)reader.ReadBits(frameWidthBits);
    sh.MaxFrameHeightMinus1 = (int)reader.ReadBits(frameHeightBits);

    if (sh.ReducedStillPictureHeader) {
      sh.FrameIdNumbersPresent = false;
    } else {
      sh.FrameIdNumbersPresent = reader.ReadBool();
      if (sh.FrameIdNumbersPresent) {
        sh.DeltaFrameIdLength = (int)reader.ReadBits(4) + 2;
        sh.AdditionalFrameIdLength = (int)reader.ReadBits(3) + 1;
      }
    }

    sh.Use128x128Superblock = reader.ReadBool();
    sh.EnableFilterIntra = reader.ReadBool();
    sh.EnableIntraEdgeFilter = reader.ReadBool();

    if (sh.ReducedStillPictureHeader) {
      sh.EnableInterIntra = false;
      sh.EnableMaskedCompound = false;
      sh.EnableWarpedMotion = false;
      sh.EnableDualFilter = false;
      sh.EnableOrderHint = false;
      sh.EnableJntComp = false;
      sh.EnableRefFrameMvs = false;
      sh.OrderHintBits = 0;
    } else {
      sh.EnableInterIntra = reader.ReadBool();
      sh.EnableMaskedCompound = reader.ReadBool();
      sh.EnableWarpedMotion = reader.ReadBool();
      sh.EnableDualFilter = reader.ReadBool();
      sh.EnableOrderHint = reader.ReadBool();
      if (sh.EnableOrderHint) {
        sh.EnableJntComp = reader.ReadBool();
        sh.EnableRefFrameMvs = reader.ReadBool();
      }

      var seqForceScreenContentTools = reader.ReadBool() ? 2 : (int)reader.ReadBits(1);
      if (seqForceScreenContentTools > 0) {
        var seqForceIntegerMv = reader.ReadBool() ? 2 : (int)reader.ReadBits(1);
      }

      if (sh.EnableOrderHint)
        sh.OrderHintBits = (int)reader.ReadBits(3) + 1;
    }

    sh.EnableSuperRes = reader.ReadBool();
    sh.EnableCdef = reader.ReadBool();
    sh.EnableRestoration = reader.ReadBool();

    // color_config()
    _ParseColorConfig(reader, sh);

    // film_grain_params_present
    reader.ReadBool();

    return sh;
  }

  private static void _ParseColorConfig(Av1BitReader reader, Av1SequenceHeader sh) {
    sh.HighBitDepth = reader.ReadBool();
    if (sh.SeqProfile == 2 && sh.HighBitDepth) {
      sh.TwelveBit = reader.ReadBool();
      sh.BitDepth = sh.TwelveBit ? 12 : 10;
    } else {
      sh.BitDepth = sh.HighBitDepth ? 10 : 8;
    }

    if (sh.SeqProfile == 1) {
      sh.MonoChrome = false;
    } else {
      sh.MonoChrome = reader.ReadBool();
    }

    sh.ColorDescriptionPresent = reader.ReadBool();
    if (sh.ColorDescriptionPresent) {
      sh.ColorPrimaries = (Av1ColorPrimaries)reader.ReadBits(8);
      sh.TransferCharacteristics = (Av1TransferCharacteristics)reader.ReadBits(8);
      sh.MatrixCoefficients = (Av1MatrixCoefficients)reader.ReadBits(8);
    }

    if (sh.MonoChrome) {
      sh.ColorRange = reader.ReadBool();
      sh.SubsamplingX = 1;
      sh.SubsamplingY = 1;
      sh.ChromaSamplePosition = Av1ChromaSamplePosition.Unknown;
      sh.SeparateUvDeltaQ = false;
      return;
    }

    if (sh.ColorPrimaries == Av1ColorPrimaries.Bt709
        && sh.TransferCharacteristics == Av1TransferCharacteristics.Srgb
        && sh.MatrixCoefficients == Av1MatrixCoefficients.Identity) {
      sh.ColorRange = true;
      sh.SubsamplingX = 0;
      sh.SubsamplingY = 0;
    } else {
      sh.ColorRange = reader.ReadBool();
      if (sh.SeqProfile == 0) {
        sh.SubsamplingX = 1;
        sh.SubsamplingY = 1;
      } else if (sh.SeqProfile == 1) {
        sh.SubsamplingX = 0;
        sh.SubsamplingY = 0;
      } else {
        if (sh.BitDepth == 12) {
          sh.SubsamplingX = reader.ReadBool() ? 1 : 0;
          if (sh.SubsamplingX != 0)
            sh.SubsamplingY = reader.ReadBool() ? 1 : 0;
          else
            sh.SubsamplingY = 0;
        } else {
          sh.SubsamplingX = 1;
          sh.SubsamplingY = 0;
        }
      }
      if (sh.SubsamplingX != 0 && sh.SubsamplingY != 0)
        sh.ChromaSamplePosition = (Av1ChromaSamplePosition)reader.ReadBits(2);
    }

    sh.SeparateUvDeltaQ = reader.ReadBool();
  }
}
