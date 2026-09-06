using System;
using System.IO;

namespace FileFormat.Codecs.Dv;

/// <summary>How a profile's colour samples are sited against its luma.</summary>
internal enum DvSampling {

  /// <summary>One chroma pair for every four luma columns, every line its own — 525/60 at 25 Mbit.</summary>
  FourOneOne,

  /// <summary>One chroma pair for every two luma columns and every two lines — 625/50 at 25 Mbit.</summary>
  FourTwoZero,

  /// <summary>One chroma pair for every two luma columns, every line its own — DVCPRO50.</summary>
  FourTwoTwo,
}

/// <summary>
/// One of the handful of arrangements a DV frame may be in: a raster, a frame rate, a sampling and a
/// frame size, all fixed together.
/// </summary>
/// <remarks>
/// DV is a tape format before it is a file format, and that is what makes a profile table possible at
/// all. A frame is a whole number of 80-byte DIF blocks, the block count is a property of the
/// broadcast system rather than of the picture, and the encoder had to fill a fixed length of tape
/// whatever the picture was. So there is no negotiation: a 625/50 frame at 25 Mbit is 144000 bytes
/// and 720x576 and nothing else, and a stream that says otherwise is broken rather than unusual.
/// <para/>
/// The table is FFmpeg's <c>dv_profiles</c> from <c>libavcodec/dv_profile.c</c>, restricted to the
/// standard-definition rows. The three high-definition rows — DVCPRO HD at 1080i and 720p — are named
/// in <see cref="_HighDefinitionName"/> so that a frame carrying one is refused by name instead of
/// being decoded as whichever standard-definition row happens to have the same signal type bits.
/// </remarks>
internal sealed record DvProfile {

  /// <summary>The number of bytes a decoder must see to identify a profile: six DIF blocks.</summary>
  internal const int IdentificationBytes = 6 * 80;

  /// <summary>The bytes one 80-byte DIF block occupies.</summary>
  internal const int DifBlockSize = 80;

  /// <summary>Blocks per macroblock — six for every standard-definition profile.</summary>
  internal const int BlocksPerMacroblock = 6;

  /// <summary>Macroblocks per video segment.</summary>
  internal const int MacroblocksPerSegment = 5;

  /// <summary>Video segments per DIF sequence.</summary>
  internal const int SegmentsPerSequence = 27;

  /// <summary>
  /// The bit budget of each block of a macroblock, in the order the blocks are written.
  /// </summary>
  /// <remarks>
  /// Four luma blocks of 112 bits and two colour blocks of 80: 14 and 10 bytes, which with the
  /// one-byte macroblock header is the 77 bytes of a video DIF block's payload. A block that needs
  /// more than its own budget spills into the space its neighbours did not use, which is what the
  /// three passes of the block layer are about.
  /// </remarks>
  internal static readonly int[] BlockSizes = [112, 112, 112, 112, 80, 80];

  /// <summary>The name this arrangement goes by, used in refusals and in stream descriptions.</summary>
  internal required string Name { get; init; }

  /// <summary>The DIF sequence flag: 0 for a 525/60 system, 1 for a 625/50 one.</summary>
  internal required int SequenceFlag { get; init; }

  /// <summary>The signal type the VAUX source pack states: 0 or 1 for 25 Mbit, 4 for DVCPRO50.</summary>
  internal required int SignalType { get; init; }

  /// <summary>The whole frame, in bytes.</summary>
  internal required int FrameSize { get; init; }

  /// <summary>DIF sequences per channel — ten for 525/60, twelve for 625/50.</summary>
  internal required int SequencesPerChannel { get; init; }

  /// <summary>DIF channels per frame — one at 25 Mbit, two at 50.</summary>
  internal required int ChannelCount { get; init; }

  /// <summary>The frame rate's numerator.</summary>
  internal required int FrameRateNumerator { get; init; }

  /// <summary>The frame rate's denominator.</summary>
  internal required int FrameRateDenominator { get; init; }

  /// <summary>Picture width in pixels — 720 for every standard-definition profile.</summary>
  internal required int Width { get; init; }

  /// <summary>Picture height in pixels.</summary>
  internal required int Height { get; init; }

  /// <summary>How this profile sites its colour samples.</summary>
  internal required DvSampling Sampling { get; init; }

  /// <summary>The sample aspect ratio of a 4:3 frame, as numerator and denominator.</summary>
  internal required (int Numerator, int Denominator) NarrowAspect { get; init; }

  /// <summary>The sample aspect ratio of a 16:9 frame, as numerator and denominator.</summary>
  internal required (int Numerator, int Denominator) WideAspect { get; init; }

  /// <summary>The width of one colour plane in samples.</summary>
  internal int ChromaWidth => this.Sampling == DvSampling.FourOneOne ? this.Width / 4 : this.Width / 2;

  /// <summary>The height of one colour plane in samples.</summary>
  internal int ChromaHeight => this.Sampling == DvSampling.FourTwoZero ? this.Height / 2 : this.Height;

  /// <summary>The video segments a whole frame is made of.</summary>
  internal int SegmentCount => this.ChannelCount * this.SequencesPerChannel * SegmentsPerSequence;

  // ==============================================================================================
  // The table
  // ==============================================================================================

  /// <summary>525/60 at 25 Mbit — IEC 61834 and SMPTE 314M agree on this one.</summary>
  internal static readonly DvProfile Ntsc25 = new() {
    Name = "DV25 525/60 4:1:1",
    SequenceFlag = 0,
    SignalType = 0x0,
    FrameSize = 120000,
    SequencesPerChannel = 10,
    ChannelCount = 1,
    FrameRateNumerator = 30000,
    FrameRateDenominator = 1001,
    Width = 720,
    Height = 480,
    Sampling = DvSampling.FourOneOne,
    NarrowAspect = (8, 9),
    WideAspect = (32, 27),
  };

  /// <summary>625/50 at 25 Mbit as IEC 61834 defines it, colour sampled 4:2:0.</summary>
  internal static readonly DvProfile Pal25Iec = new() {
    Name = "DV25 625/50 4:2:0 (IEC 61834)",
    SequenceFlag = 1,
    SignalType = 0x0,
    FrameSize = 144000,
    SequencesPerChannel = 12,
    ChannelCount = 1,
    FrameRateNumerator = 25,
    FrameRateDenominator = 1,
    Width = 720,
    Height = 576,
    Sampling = DvSampling.FourTwoZero,
    NarrowAspect = (16, 15),
    WideAspect = (64, 45),
  };

  /// <summary>625/50 at 25 Mbit as SMPTE 314M defines it, colour sampled 4:1:1 — DVCPRO25 in Europe.</summary>
  internal static readonly DvProfile Pal25Smpte = new() {
    Name = "DV25 625/50 4:1:1 (SMPTE 314M)",
    SequenceFlag = 1,
    SignalType = 0x0,
    FrameSize = 144000,
    SequencesPerChannel = 12,
    ChannelCount = 1,
    FrameRateNumerator = 25,
    FrameRateDenominator = 1,
    Width = 720,
    Height = 576,
    Sampling = DvSampling.FourOneOne,
    NarrowAspect = (16, 15),
    WideAspect = (64, 45),
  };

  /// <summary>625/50 at 25 Mbit as IEC 61883-5 states it, which differs only in the signal type it writes.</summary>
  internal static readonly DvProfile Pal25Iec61883 = new() {
    Name = "DV25 625/50 4:2:0 (IEC 61883-5)",
    SequenceFlag = 1,
    SignalType = 0x1,
    FrameSize = 144000,
    SequencesPerChannel = 12,
    ChannelCount = 1,
    FrameRateNumerator = 25,
    FrameRateDenominator = 1,
    Width = 720,
    Height = 576,
    Sampling = DvSampling.FourTwoZero,
    NarrowAspect = (16, 15),
    WideAspect = (64, 45),
  };

  /// <summary>525/60 at 50 Mbit — DVCPRO50, two DIF channels of the same geometry.</summary>
  internal static readonly DvProfile Ntsc50 = new() {
    Name = "DVCPRO50 525/60 4:2:2",
    SequenceFlag = 0,
    SignalType = 0x4,
    FrameSize = 240000,
    SequencesPerChannel = 10,
    ChannelCount = 2,
    FrameRateNumerator = 30000,
    FrameRateDenominator = 1001,
    Width = 720,
    Height = 480,
    Sampling = DvSampling.FourTwoTwo,
    NarrowAspect = (8, 9),
    WideAspect = (32, 27),
  };

  /// <summary>625/50 at 50 Mbit — DVCPRO50.</summary>
  internal static readonly DvProfile Pal50 = new() {
    Name = "DVCPRO50 625/50 4:2:2",
    SequenceFlag = 1,
    SignalType = 0x4,
    FrameSize = 288000,
    SequencesPerChannel = 12,
    ChannelCount = 2,
    FrameRateNumerator = 25,
    FrameRateDenominator = 1,
    Width = 720,
    Height = 576,
    Sampling = DvSampling.FourTwoTwo,
    NarrowAspect = (16, 15),
    WideAspect = (64, 45),
  };

  /// <summary>Every profile this decoder reads, in the order a frame is matched against them.</summary>
  internal static readonly DvProfile[] All = [Ntsc25, Pal25Iec, Pal25Smpte, Ntsc50, Pal50, Pal25Iec61883];

  // ==============================================================================================
  // Identification
  // ==============================================================================================

  /// <summary>
  /// Works out which profile a frame is in, from the frame itself.
  /// </summary>
  /// <remarks>
  /// The two fields that decide it are the DIF sequence flag in the header block and the signal type
  /// in the VAUX source pack, which sits at a fixed offset because every DIF block is 80 bytes and
  /// the first six of every sequence are control data. Nothing about the container is consulted: a DV
  /// frame carries its own geometry, which is what lets a single frame be cut out of a tape dump and
  /// still be read.
  /// <para/>
  /// Two special cases are FFmpeg's and are kept because real files need them. A 625/50 frame that
  /// says signal type 0 but sets the track application ID is SMPTE 314M's 4:1:1 arrangement rather
  /// than IEC 61834's 4:2:0 — the two are the same size and the same signal type, and the application
  /// ID is the only thing that separates them. And a frame flagged 525/60 whose VAUX pack says 625/50
  /// and whose length is a 625/50 frame is a 625/50 frame written by something that got the flag
  /// wrong; several capture cards did.
  /// </remarks>
  /// <exception cref="NotSupportedException">The frame states a high-definition profile.</exception>
  /// <exception cref="InvalidDataException">The frame states no profile this decoder knows.</exception>
  internal static DvProfile Identify(ReadOnlySpan<byte> frame) {
    if (frame.Length < IdentificationBytes)
      throw new InvalidDataException(
        $"A DV frame states its profile in its first {IdentificationBytes} bytes; this packet is only {frame.Length}.");

    var sequenceFlag = (frame[3] & 0x80) >> 7;
    var sourcePack = frame[80 * 5 + 48 + 3];
    var signalType = sourcePack & 0x1f;
    var fiftyHertz = (sourcePack & 0x20) != 0;
    var applicationId = frame[4] & 0x07;

    if ((signalType & 0x10) != 0)
      throw new NotSupportedException(
        $"This is a {_HighDefinitionName(signalType, sequenceFlag)} frame. DVCPRO HD is not decoded here — its "
        + "macroblocks carry eight blocks rather than six and its quantiser is a different table — and a frame "
        + "that states it is refused rather than read as the standard-definition profile with the same flags.");

    // 625/50 at 4:1:1 is the one arrangement two fields cannot tell apart: SMPTE 314M and IEC 61834
    // write the same size and the same signal type, and only the application ID separates them.
    if (sequenceFlag == 1 && signalType == 0 && applicationId != 0)
      return Pal25Smpte;

    if (sequenceFlag == 0 && fiftyHertz && signalType == Pal25Iec.SignalType && frame.Length == Pal25Iec.FrameSize)
      return Pal25Iec;

    foreach (var profile in All)
      if (profile.SequenceFlag == sequenceFlag && profile.SignalType == signalType)
        return profile;

    // QuickTime 3 wrote frames whose header reserved bits are all set and whose VAUX source pack is
    // absent altogether. The sequence flag is still right, and it is the only thing left to go on.
    if ((frame[3] & 0x7f) == 0x3f && sourcePack == 0xff)
      return sequenceFlag == 0 ? Ntsc25 : Pal25Iec;

    throw new InvalidDataException(
      $"This DV frame states signal type {signalType} in a {(sequenceFlag == 1 ? "625/50" : "525/60")} system, "
      + "which is no arrangement IEC 61834, SMPTE 314M or SMPTE 370M defines.");
  }

  /// <summary>
  /// Picks the profile a picture of this shape is written as.
  /// </summary>
  /// <remarks>
  /// Geometry and sampling together, because for 625/50 they are not enough on their own: 720x576
  /// names two 25 Mbit profiles that differ only in where the colour samples sit. The frame rate is
  /// not consulted at all — at standard definition each raster has exactly one rate.
  /// </remarks>
  internal static DvProfile? ForPicture(int width, int height, DvSampling sampling) {
    foreach (var profile in All)
      if (profile.Width == width && profile.Height == height && profile.Sampling == sampling)
        return profile;

    return null;
  }

  /// <summary>The name of the high-definition profile a signal type and sequence flag between them state.</summary>
  private static string _HighDefinitionName(int signalType, int sequenceFlag) => (signalType, sequenceFlag) switch {
    (0x14, 0) => "DVCPRO HD 1080i60",
    (0x14, 1) => "DVCPRO HD 1080i50",
    (0x18, 0) => "DVCPRO HD 720p60",
    (0x18, 1) => "DVCPRO HD 720p50",
    _ => $"high-definition DV (signal type {signalType})",
  };
}
