using System;
using System.IO;

namespace FileFormat.Codecs.Vc1;

/// <summary>Which of the three profiles of SMPTE 421M a stream is coded in (Annex J.1.1).</summary>
internal enum Vc1Profile {

  Simple = 0,

  Main = 4,

  Advanced = 12,
}

/// <summary>
/// The sequence header of a Simple or Main profile stream: the thirty-two bit <c>STRUCT_C</c> a
/// container carries as the codec's private data (Annex J, Table 263).
/// </summary>
/// <remarks>
/// Simple and Main profile put none of this in the bitstream. There is no sequence header among the
/// pictures at all — the whole of it is metadata the container hands over, which is why a Windows
/// Media Video stream cannot be decoded from its packets alone and why the demuxer's job of carrying
/// the codec's private data across intact is what makes this decodable.
/// <para/>
/// Four of the thirty-two bits are reserved and the standard fixes all four: two shall be zero and two
/// shall be one. That is a strong check on having read the field the right way round — the same four
/// bytes read the other way satisfies none of them — so they are verified rather than skipped.
/// </remarks>
internal readonly record struct Vc1SequenceHeader(
  Vc1Profile Profile,
  int FrameRatePostProcessing,
  int BitRatePostProcessing,
  bool LoopFilter,
  bool MultiResolution,
  bool FastUvMotionCompensation,
  bool ExtendedMotionVectors,
  int DifferentialQuantisation,
  bool VariableSizedTransform,
  bool Overlap,
  bool SyncMarker,
  bool RangeReduction,
  int MaxBFrames,
  int Quantiser,
  bool FrameInterpolation) {

  /// <summary>The length of <c>STRUCT_C</c>, which is a fixed thirty-two bits.</summary>
  internal const int STRUCT_C_SIZE = 4;

  /// <summary>Reads <c>STRUCT_C</c> out of a stream's codec private data.</summary>
  internal static Vc1SequenceHeader ReadFrom(ReadOnlySpan<byte> data) {
    if (data.Length < STRUCT_C_SIZE)
      throw new InvalidDataException(
        $"A Windows Media Video stream states its sequence header in {STRUCT_C_SIZE} bytes of codec private data; this one has {data.Length}.");

    var reader = new Vc1BitReader(data);

    var profile = reader.ReadBits(4);
    var frameRatePostProcessing = reader.ReadBits(3);
    var bitRatePostProcessing = reader.ReadBits(5);
    var loopFilter = reader.ReadBit() != 0;
    var reserved3 = reader.ReadBit();
    var multiResolution = reader.ReadBit() != 0;
    var reserved4 = reader.ReadBit();
    var fastUvMotionCompensation = reader.ReadBit() != 0;
    var extendedMotionVectors = reader.ReadBit() != 0;
    var differentialQuantisation = reader.ReadBits(2);
    var variableSizedTransform = reader.ReadBit() != 0;
    var reserved5 = reader.ReadBit();
    var overlap = reader.ReadBit() != 0;
    var syncMarker = reader.ReadBit() != 0;
    var rangeReduction = reader.ReadBit() != 0;
    var maxBFrames = reader.ReadBits(3);
    var quantiser = reader.ReadBits(2);
    var frameInterpolation = reader.ReadBit() != 0;
    var reserved6 = reader.ReadBit();

    if (profile is not ((int)Vc1Profile.Simple or (int)Vc1Profile.Main or (int)Vc1Profile.Advanced))
      throw new InvalidDataException($"The sequence header states profile {profile}, which is none of Simple, Main or Advanced.");

    // The four reserved bits, checked together because they are what says the header was read the
    // right way round. STRUCT_C is a bit field over four bytes read most significant bit first, and
    // the same four bytes taken as a little-endian number satisfies none of these.
    if (reserved3 != 0 || reserved4 != 1 || reserved5 != 0 || reserved6 != 1)
      throw new InvalidDataException(
        $"The sequence header's reserved bits are {reserved3}{reserved4}{reserved5}{reserved6} where the standard fixes them at 0101, "
        + "so these bytes are not a Simple or Main profile STRUCT_C.");

    return new(
      (Vc1Profile)profile,
      frameRatePostProcessing,
      bitRatePostProcessing,
      loopFilter,
      multiResolution,
      fastUvMotionCompensation,
      extendedMotionVectors,
      differentialQuantisation,
      variableSizedTransform,
      overlap,
      syncMarker,
      rangeReduction,
      maxBFrames,
      quantiser,
      frameInterpolation);
  }
}
