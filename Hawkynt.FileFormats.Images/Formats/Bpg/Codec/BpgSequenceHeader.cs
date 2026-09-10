using System;
using System.IO;
using FileFormat.Codecs.H265;

namespace FileFormat.Bpg.Codec;

/// <summary>
/// The header BPG puts in front of its HEVC data, standing in for the sequence parameter set the
/// file deliberately omits.
/// </summary>
internal sealed record BpgSequenceHeader(
  int Log2MinLumaCodingBlockSize,
  int Log2CtbSize,
  int Log2MinTransformBlockSize,
  int Log2MaxTransformBlockSize,
  int MaxTransformHierarchyDepthIntra,
  bool SaoEnabled,
  bool PcmEnabled,
  int PcmBitDepthLuma,
  int PcmBitDepthChroma,
  int Log2MinPcmCodingBlockSize,
  int Log2MaxPcmCodingBlockSize,
  bool PcmLoopFilterDisabled,
  bool StrongIntraSmoothingEnabled,
  int HevcDataOffset
) {

  /// <summary>Reads one <c>hevc_header()</c>, advancing to the byte after it.</summary>
  internal static BpgSequenceHeader Parse(ReadOnlySpan<byte> pictureData, ref int offset) {
    var headerLength = BpgUe7.Read(pictureData, ref offset);
    if (headerLength <= 0)
      throw new InvalidDataException("A BPG HEVC header must contain at least one byte.");
    if (headerLength > pictureData.Length - offset)
      throw new InvalidDataException(
        $"A BPG HEVC header declares {headerLength} byte(s), but only {pictureData.Length - offset} remain.");

    var headerEnd = checked(offset + headerLength);
    var reader = new H265BitReader(pictureData.Slice(offset, headerLength));

    var log2MinCb = checked(reader.ReadUnsignedExpGolomb() + 3);
    var log2Ctb = checked(log2MinCb + reader.ReadUnsignedExpGolomb());
    var log2MinTb = checked(reader.ReadUnsignedExpGolomb() + 2);
    var log2MaxTb = checked(log2MinTb + reader.ReadUnsignedExpGolomb());
    var maxTransformHierarchyDepthIntra = reader.ReadUnsignedExpGolomb();
    var sao = reader.ReadFlag();
    var pcm = reader.ReadFlag();

    var pcmDepthLuma = 0;
    var pcmDepthChroma = 0;
    var log2MinPcm = 0;
    var log2MaxPcm = 0;
    var pcmLoopFilterDisabled = false;
    if (pcm) {
      pcmDepthLuma = reader.ReadBits(4) + 1;
      pcmDepthChroma = reader.ReadBits(4) + 1;
      log2MinPcm = checked(reader.ReadUnsignedExpGolomb() + 3);
      log2MaxPcm = checked(log2MinPcm + reader.ReadUnsignedExpGolomb());
      pcmLoopFilterDisabled = reader.ReadFlag();
    }

    var strongIntraSmoothing = reader.ReadFlag();
    if (reader.ReadFlag()) {
      var rangeExtension = reader.ReadFlag();
      var otherExtensions = reader.ReadBits(7);
      if (otherExtensions != 0)
        throw new NotSupportedException(
          $"This BPG HEVC header sets sps_extension_7bits to 0x{otherExtensions:x2}. Only the range extension syntax defined by BPG 0.9.5 is implemented.");

      if (rangeExtension)
        _RefuseUnsupportedRangeExtensions(ref reader);
    }

    while (reader.BitsRemaining > 0)
      if (reader.ReadBit() != 0)
        throw new InvalidDataException("BPG HEVC trailing_bits must be zero up to the next byte boundary.");

    offset = headerEnd;
    return new(
      log2MinCb, log2Ctb, log2MinTb, log2MaxTb, maxTransformHierarchyDepthIntra,
      sao, pcm, pcmDepthLuma, pcmDepthChroma, log2MinPcm, log2MaxPcm,
      pcmLoopFilterDisabled, strongIntraSmoothing, headerEnd);
  }

  /// <summary>Reads the first BPG HEVC header, or answers null when it cannot describe a supported sequence.</summary>
  public static BpgSequenceHeader? TryParse(ReadOnlySpan<byte> pictureData) {
    var offset = 0;
    try {
      return Parse(pictureData, ref offset);
    } catch (InvalidDataException) {
      return null;
    } catch (NotSupportedException) {
      return null;
    } catch (OverflowException) {
      return null;
    }
  }

  private static void _RefuseUnsupportedRangeExtensions(ref H265BitReader reader) {
    string[] names = [
      "transform_skip_rotation_enabled_flag",
      "transform_skip_context_enabled_flag",
      "implicit_rdpcm_enabled_flag",
      "explicit_rdpcm_enabled_flag",
      "extended_precision_processing_flag",
      "intra_smoothing_disabled_flag",
      "high_precision_offsets_enabled_flag",
      "persistent_rice_adaptation_enabled_flag",
      "cabac_bypass_alignment_enabled_flag",
    ];

    foreach (var name in names)
      if (reader.ReadFlag())
        throw new NotSupportedException(
          $"This BPG picture sets {name}. The shared HEVC decoder does not implement that range-extension tool.");
  }
}
