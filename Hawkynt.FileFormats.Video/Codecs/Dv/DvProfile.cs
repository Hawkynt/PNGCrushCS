using System;
using System.IO;

namespace FileFormat.Codecs.Dv;

/// <summary>How a profile's colour samples are sited against its luma.</summary>
internal enum DvSampling {

  /// <summary>One chroma pair for every four luma columns, every line its own — 525/60 at 25 Mbit.</summary>
  FourOneOne,

  /// <summary>One chroma pair for every two luma columns and every two lines — 625/50 at 25 Mbit.</summary>
  FourTwoZero,

  /// <summary>One chroma pair for every two luma columns, every line its own — DVCPRO50 and DVCPRO HD.</summary>
  FourTwoTwo,
}

/// <summary>
/// One of the arrangements a DV frame may be in: raster, frame rate, sampling, DIF topology and
/// block-layer syntax are fixed together by the recording system.
/// </summary>
/// <remarks>
/// The profile data is the normative/factual part of the format. The standard-definition rows and
/// the SMPTE 370M rows are cross-checked against FFmpeg's LGPL-2.1-or-later
/// <c>libavcodec/dv_profile.c</c>; see <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> beside this file.
/// </remarks>
internal sealed record DvProfile {

  /// <summary>The number of bytes a decoder must see to identify a profile: six DIF blocks.</summary>
  internal const int IdentificationBytes = 6 * 80;

  /// <summary>The bytes one DIF block occupies.</summary>
  internal const int DifBlockSize = 80;

  /// <summary>Blocks per standard-definition macroblock.</summary>
  internal const int BlocksPerMacroblock = 6;

  /// <summary>Macroblocks per video segment.</summary>
  internal const int MacroblocksPerSegment = 5;

  /// <summary>Video segments per DIF sequence.</summary>
  internal const int SegmentsPerSequence = 27;

  /// <summary>SD/DV50 block bit budgets: four 112-bit luma cells and two 80-bit chroma cells.</summary>
  internal static readonly int[] BlockSizes = [112, 112, 112, 112, 80, 80];

  /// <summary>DVCPRO HD block bit budgets: six 80-bit cells followed by two 64-bit chroma cells.</summary>
  internal static readonly int[] Dv100BlockSizes = [80, 80, 80, 80, 80, 80, 64, 64];

  internal required string Name { get; init; }
  internal required int SequenceFlag { get; init; }
  internal required int SignalType { get; init; }
  internal required int FrameSize { get; init; }
  internal required int SequencesPerChannel { get; init; }
  internal required int ChannelCount { get; init; }
  internal required int FrameRateNumerator { get; init; }
  internal required int FrameRateDenominator { get; init; }
  internal required int Width { get; init; }
  internal required int Height { get; init; }
  internal required DvSampling Sampling { get; init; }
  internal required (int Numerator, int Denominator) NarrowAspect { get; init; }
  internal required (int Numerator, int Denominator) WideAspect { get; init; }

  /// <summary>Whether this is SMPTE 370M's 100-Mbit block layer rather than the SD/DV50 one.</summary>
  internal bool IsDv100 { get; init; }

  /// <summary>Whether the coded picture is progressive. SMPTE 370M's 720-line profiles are.</summary>
  internal bool Progressive { get; init; }

  internal int CodedBlocksPerMacroblock => this.IsDv100 ? 8 : BlocksPerMacroblock;
  internal int[] CodedBlockSizes => this.IsDv100 ? Dv100BlockSizes : BlockSizes;

  internal int ChromaWidth => this.Sampling == DvSampling.FourOneOne ? this.Width / 4 : this.Width / 2;
  internal int ChromaHeight => this.Sampling == DvSampling.FourTwoZero ? this.Height / 2 : this.Height;

  /// <summary>The number of coded video segments. Some 50-Hz DVCPRO-HD DIF regions are padding.</summary>
  internal int SegmentCount {
    get {
      var count = 0;
      for (var channel = 0; channel < this.ChannelCount; ++channel)
        for (var sequence = 0; sequence < this.SequencesPerChannel; ++sequence)
          if (this.IsVideoSequenceActive(channel, sequence))
            count += SegmentsPerSequence;
      return count;
    }
  }

  /// <summary>Whether a physical DIF sequence contains coded video for this profile.</summary>
  internal bool IsVideoSequenceActive(int channel, int sequence) {
    if (!this.IsDv100)
      return true;

    // SMPTE 370M 1080/50 leaves sequence 11 empty in channels 1..3; 720/50 codes only
    // sequences 0..9. The DIF skeleton remains physically present in both cases.
    if (this.Width == 1440 && channel != 0 && sequence == 11)
      return false;
    if (this.Width == 960 && this.SequenceFlag == 1 && sequence > 9)
      return false;

    return true;
  }

  // ==============================================================================================
  // Standard definition / DVCPRO50
  // ==============================================================================================

  internal static readonly DvProfile Ntsc25 = new() {
    Name = "DV25 525/60 4:1:1",
    SequenceFlag = 0, SignalType = 0x0, FrameSize = 120000, SequencesPerChannel = 10, ChannelCount = 1,
    FrameRateNumerator = 30000, FrameRateDenominator = 1001,
    Width = 720, Height = 480, Sampling = DvSampling.FourOneOne,
    NarrowAspect = (8, 9), WideAspect = (32, 27),
  };

  internal static readonly DvProfile Pal25Iec = new() {
    Name = "DV25 625/50 4:2:0 (IEC 61834)",
    SequenceFlag = 1, SignalType = 0x0, FrameSize = 144000, SequencesPerChannel = 12, ChannelCount = 1,
    FrameRateNumerator = 25, FrameRateDenominator = 1,
    Width = 720, Height = 576, Sampling = DvSampling.FourTwoZero,
    NarrowAspect = (16, 15), WideAspect = (64, 45),
  };

  internal static readonly DvProfile Pal25Smpte = new() {
    Name = "DV25 625/50 4:1:1 (SMPTE 314M)",
    SequenceFlag = 1, SignalType = 0x0, FrameSize = 144000, SequencesPerChannel = 12, ChannelCount = 1,
    FrameRateNumerator = 25, FrameRateDenominator = 1,
    Width = 720, Height = 576, Sampling = DvSampling.FourOneOne,
    NarrowAspect = (16, 15), WideAspect = (64, 45),
  };

  internal static readonly DvProfile Pal25Iec61883 = new() {
    Name = "DV25 625/50 4:2:0 (IEC 61883-5)",
    SequenceFlag = 1, SignalType = 0x1, FrameSize = 144000, SequencesPerChannel = 12, ChannelCount = 1,
    FrameRateNumerator = 25, FrameRateDenominator = 1,
    Width = 720, Height = 576, Sampling = DvSampling.FourTwoZero,
    NarrowAspect = (16, 15), WideAspect = (64, 45),
  };

  internal static readonly DvProfile Ntsc50 = new() {
    Name = "DVCPRO50 525/60 4:2:2",
    SequenceFlag = 0, SignalType = 0x4, FrameSize = 240000, SequencesPerChannel = 10, ChannelCount = 2,
    FrameRateNumerator = 30000, FrameRateDenominator = 1001,
    Width = 720, Height = 480, Sampling = DvSampling.FourTwoTwo,
    NarrowAspect = (8, 9), WideAspect = (32, 27),
  };

  internal static readonly DvProfile Pal50 = new() {
    Name = "DVCPRO50 625/50 4:2:2",
    SequenceFlag = 1, SignalType = 0x4, FrameSize = 288000, SequencesPerChannel = 12, ChannelCount = 2,
    FrameRateNumerator = 25, FrameRateDenominator = 1,
    Width = 720, Height = 576, Sampling = DvSampling.FourTwoTwo,
    NarrowAspect = (16, 15), WideAspect = (64, 45),
  };

  // ==============================================================================================
  // SMPTE 370M / DVCPRO HD
  // ==============================================================================================

  internal static readonly DvProfile DvcproHd1080I60 = new() {
    Name = "DVCPRO HD 1080/60i",
    SequenceFlag = 0, SignalType = 0x14, FrameSize = 480000, SequencesPerChannel = 10, ChannelCount = 4,
    FrameRateNumerator = 30000, FrameRateDenominator = 1001,
    Width = 1280, Height = 1080, Sampling = DvSampling.FourTwoTwo,
    NarrowAspect = (1, 1), WideAspect = (3, 2), IsDv100 = true,
  };

  internal static readonly DvProfile DvcproHd1080I50 = new() {
    Name = "DVCPRO HD 1080/50i",
    SequenceFlag = 1, SignalType = 0x14, FrameSize = 576000, SequencesPerChannel = 12, ChannelCount = 4,
    FrameRateNumerator = 25, FrameRateDenominator = 1,
    Width = 1440, Height = 1080, Sampling = DvSampling.FourTwoTwo,
    NarrowAspect = (1, 1), WideAspect = (4, 3), IsDv100 = true,
  };

  internal static readonly DvProfile DvcproHd720P60 = new() {
    Name = "DVCPRO HD 720/60p",
    SequenceFlag = 0, SignalType = 0x18, FrameSize = 240000, SequencesPerChannel = 10, ChannelCount = 2,
    FrameRateNumerator = 60000, FrameRateDenominator = 1001,
    Width = 960, Height = 720, Sampling = DvSampling.FourTwoTwo,
    NarrowAspect = (1, 1), WideAspect = (4, 3), IsDv100 = true, Progressive = true,
  };

  internal static readonly DvProfile DvcproHd720P50 = new() {
    Name = "DVCPRO HD 720/50p",
    SequenceFlag = 1, SignalType = 0x18, FrameSize = 288000, SequencesPerChannel = 12, ChannelCount = 2,
    FrameRateNumerator = 50, FrameRateDenominator = 1,
    Width = 960, Height = 720, Sampling = DvSampling.FourTwoTwo,
    NarrowAspect = (1, 1), WideAspect = (4, 3), IsDv100 = true, Progressive = true,
  };

  internal static readonly DvProfile[] All = [
    Ntsc25, Pal25Iec, Pal25Smpte, Ntsc50, Pal50,
    DvcproHd1080I60, DvcproHd1080I50, DvcproHd720P60, DvcproHd720P50,
    Pal25Iec61883,
  ];

  // ==============================================================================================
  // Identification
  // ==============================================================================================

  internal static DvProfile Identify(ReadOnlySpan<byte> frame) {
    if (frame.Length < IdentificationBytes)
      throw new InvalidDataException(
        $"A DV frame states its profile in its first {IdentificationBytes} bytes; this packet is only {frame.Length}.");

    var sequenceFlag = (frame[3] & 0x80) >> 7;
    var sourcePack = frame[80 * 5 + 48 + 3];
    var signalType = sourcePack & 0x1f;
    var fiftyHertz = (sourcePack & 0x20) != 0;
    var applicationId = frame[4] & 0x07;

    if (sequenceFlag == 1 && signalType == 0 && applicationId != 0)
      return Pal25Smpte;

    if (sequenceFlag == 0 && fiftyHertz && signalType == Pal25Iec.SignalType && frame.Length == Pal25Iec.FrameSize)
      return Pal25Iec;

    foreach (var profile in All)
      if (profile.SequenceFlag == sequenceFlag && profile.SignalType == signalType)
        return profile;

    // Consumer IEC 61834-3 HD uses a different block layer from SMPTE 370M. Name it explicitly so
    // callers do not mistake "unsupported" for an unrecognised/corrupt DV signal type.
    if (signalType == 0x02)
      throw new NotSupportedException(
        $"This is an IEC 61834-3 HD-DVCR {(sequenceFlag == 0 ? "1125/60" : "1250/50")} frame. "
        + "Its 50-Mbit consumer HD block layer is distinct from SMPTE 370M DVCPRO HD.");

    if ((frame[3] & 0x7f) == 0x3f && sourcePack == 0xff)
      return sequenceFlag == 0 ? Ntsc25 : Pal25Iec;

    throw new InvalidDataException(
      $"This DV frame states signal type {signalType} in a {(sequenceFlag == 1 ? "625/50" : "525/60")} system, "
      + "which is no supported IEC 61834, SMPTE 314M or SMPTE 370M arrangement.");
  }

  internal static DvProfile? ForPicture(int width, int height, DvSampling sampling) {
    foreach (var profile in All)
      if (profile.Width == width && profile.Height == height && profile.Sampling == sampling)
        return profile;
    return null;
  }

  internal static DvProfile? ForRaster(int width, int height) {
    foreach (var profile in All)
      if (profile.Width == width && profile.Height == height)
        return profile;
    return null;
  }
}
