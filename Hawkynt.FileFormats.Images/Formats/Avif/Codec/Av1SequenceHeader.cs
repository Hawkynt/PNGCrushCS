using System;

namespace FileFormat.Avif.Codec;

internal enum Av1ColorPrimaries {
  Bt709 = 1, Unspecified = 2, Bt470M = 4, Bt470Bg = 5, Bt601 = 6, Smpte240 = 7,
  GenericFilm = 8, Bt2020 = 9, Xyz = 10, Smpte431 = 11, Smpte432 = 12, Ebu3213 = 22,
}

internal enum Av1TransferCharacteristics {
  Bt709 = 1, Unspecified = 2, Bt470M = 4, Bt470Bg = 5, Bt601 = 6, Smpte240 = 7,
  Linear = 8, Log100 = 9, Log100Sqrt10 = 10, Iec61966 = 11, Bt1361 = 12, Srgb = 13,
  Bt2020_10 = 14, Bt2020_12 = 15, Smpte2084 = 16, Smpte428 = 17, Hlg = 18,
}

internal enum Av1MatrixCoefficients {
  Identity = 0, Bt709 = 1, Unspecified = 2, Fcc = 4, Bt470Bg = 5, Bt601 = 6,
  Smpte240 = 7, YCgCo = 8, Bt2020Ncl = 9, Bt2020Cl = 10, Smpte2085 = 11,
  ChromaDerivedNcl = 12, ChromaDerivedCl = 13, ICtCp = 14,
}

internal enum Av1ChromaSamplePosition { Unknown = 0, Vertical = 1, Colocated = 2 }

/// <summary>Parsed AV1 sequence header OBU (AV1 specification 5.5).</summary>
internal sealed class Av1SequenceHeader {
  public int SeqProfile { get; set; }
  public bool StillPicture { get; set; }
  public bool ReducedStillPictureHeader { get; set; }
  public int MaxFrameWidthMinus1 { get; set; }
  public int MaxFrameHeightMinus1 { get; set; }
  public int MaxFrameWidth => MaxFrameWidthMinus1 + 1;
  public int MaxFrameHeight => MaxFrameHeightMinus1 + 1;
  public int OperatingPointsCount { get; set; }
  public bool TimingInfoPresent { get; set; }
  public bool DecoderModelInfoPresent { get; set; }
  public bool FrameIdNumbersPresent { get; set; }
  public int DeltaFrameIdLength { get; set; }
  public int AdditionalFrameIdLength { get; set; }
  public bool Use128x128Superblock { get; set; }
  public bool EnableFilterIntra { get; set; }
  public bool EnableIntraEdgeFilter { get; set; }
  public bool EnableInterIntra { get; set; }
  public bool EnableMaskedCompound { get; set; }
  public bool EnableWarpedMotion { get; set; }
  public bool EnableDualFilter { get; set; }
  public bool EnableOrderHint { get; set; }
  public bool EnableJntComp { get; set; }
  public bool EnableRefFrameMvs { get; set; }
  public int OrderHintBits { get; set; }

  /// <summary>AV1 SELECT=2, OFF=0, ON=1 sequence force selector.</summary>
  public int SeqForceScreenContentTools { get; set; } = 2;
  /// <summary>AV1 SELECT=2, OFF=0, ON=1 sequence force selector.</summary>
  public int SeqForceIntegerMv { get; set; } = 2;

  public bool EnableSuperRes { get; set; }
  public bool EnableCdef { get; set; }
  public bool EnableRestoration { get; set; }
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

  public static Av1SequenceHeader Parse(byte[] data, int offset, int length) {
    var reader = new Av1BitReader(data, offset, length);
    var sh = new Av1SequenceHeader {
      SeqProfile = (int)reader.ReadBits(3),
      StillPicture = reader.ReadBool(),
      ReducedStillPictureHeader = reader.ReadBool(),
    };
    if (sh.SeqProfile > 2)
      throw new NotSupportedException($"AV1 Profile {sh.SeqProfile} is not supported.");

    if (sh.ReducedStillPictureHeader) {
      sh.TimingInfoPresent = false;
      sh.DecoderModelInfoPresent = false;
      sh.OperatingPointsCount = 1;
      reader.ReadBits(5); // seq_level_idx[0]
      sh.SeqForceScreenContentTools = 2;
      sh.SeqForceIntegerMv = 2;
    } else {
      sh.TimingInfoPresent = reader.ReadBool();
      if (sh.TimingInfoPresent) {
        reader.ReadBits(32);
        reader.ReadBits(32);
        if (reader.ReadBool())
          reader.ReadUvlc();
        sh.DecoderModelInfoPresent = reader.ReadBool();
        if (sh.DecoderModelInfoPresent) {
          reader.ReadBits(32);
          reader.ReadBits(32);
          reader.ReadBits(5);
          reader.ReadBits(5);
        }
      }

      sh.OperatingPointsCount = (int)reader.ReadBits(5) + 1;
      for (var i = 0; i < sh.OperatingPointsCount; ++i) {
        reader.ReadBits(12);
        var level = (int)reader.ReadBits(5);
        if (level > 7)
          reader.ReadBool();
        if (sh.DecoderModelInfoPresent && reader.ReadBool()) {
          reader.ReadBits(32);
          reader.ReadBits(32);
          reader.ReadBool();
        }
      }
    }

    var widthBits = (int)reader.ReadBits(4) + 1;
    var heightBits = (int)reader.ReadBits(4) + 1;
    sh.MaxFrameWidthMinus1 = (int)reader.ReadBits(widthBits);
    sh.MaxFrameHeightMinus1 = (int)reader.ReadBits(heightBits);

    if (sh.ReducedStillPictureHeader)
      sh.FrameIdNumbersPresent = false;
    else {
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
      sh.SeqForceScreenContentTools = 2;
      sh.SeqForceIntegerMv = 2;
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

      sh.SeqForceScreenContentTools = reader.ReadBool() ? 2 : (reader.ReadBool() ? 1 : 0);
      if (sh.SeqForceScreenContentTools > 0)
        sh.SeqForceIntegerMv = reader.ReadBool() ? 2 : (reader.ReadBool() ? 1 : 0);
      else
        sh.SeqForceIntegerMv = 2;

      if (sh.EnableOrderHint)
        sh.OrderHintBits = (int)reader.ReadBits(3) + 1;
    }

    sh.EnableSuperRes = reader.ReadBool();
    sh.EnableCdef = reader.ReadBool();
    sh.EnableRestoration = reader.ReadBool();
    _ParseColorConfig(reader, sh);
    reader.ReadBool(); // film_grain_params_present
    return sh;
  }

  private static void _ParseColorConfig(Av1BitReader reader, Av1SequenceHeader sh) {
    sh.HighBitDepth = reader.ReadBool();
    if (sh.SeqProfile == 2 && sh.HighBitDepth) {
      sh.TwelveBit = reader.ReadBool();
      sh.BitDepth = sh.TwelveBit ? 12 : 10;
    } else
      sh.BitDepth = sh.HighBitDepth ? 10 : 8;

    sh.MonoChrome = sh.SeqProfile != 1 && reader.ReadBool();
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
      sh.SubsamplingX = sh.SubsamplingY = 0;
    } else {
      sh.ColorRange = reader.ReadBool();
      if (sh.SeqProfile == 0)
        sh.SubsamplingX = sh.SubsamplingY = 1;
      else if (sh.SeqProfile == 1)
        sh.SubsamplingX = sh.SubsamplingY = 0;
      else if (sh.BitDepth == 12) {
        sh.SubsamplingX = reader.ReadBool() ? 1 : 0;
        sh.SubsamplingY = sh.SubsamplingX != 0 && reader.ReadBool() ? 1 : 0;
      } else {
        sh.SubsamplingX = 1;
        sh.SubsamplingY = 0;
      }

      if (sh.SubsamplingX != 0 && sh.SubsamplingY != 0)
        sh.ChromaSamplePosition = (Av1ChromaSamplePosition)reader.ReadBits(2);
    }

    sh.SeparateUvDeltaQ = reader.ReadBool();
  }
}
