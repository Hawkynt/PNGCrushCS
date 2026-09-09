using System;
using System.IO;
using FileFormat.Codecs.H265;

namespace FileFormat.Bpg.Codec;

/// <summary>
/// The header BPG puts in front of its HEVC data, standing in for the sequence parameter set the
/// file does not carry.
/// </summary>
/// <remarks>
/// A BPG file holds neither a video nor a sequence parameter set. What a decoder cannot work out for
/// itself is in these few fields; everything else the format's specification fixes by decree —
/// parameter set ids are zero, the coded picture size is the picture rounded up to whole minimum
/// coding blocks, both bit depths come from the container's own <c>bit_depth_minus_8</c>, and
/// <c>chroma_format_idc</c> comes from its <c>pixel_format</c>.
/// </remarks>
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
  int HevcDataOffset
) {

  /// <summary>
  /// Reads the header out of a BPG file's picture data, or answers null when the bytes are not one.
  /// </summary>
  /// <param name="pictureData">
  /// The <c>hevc_header_and_data()</c> block: the header's length, the header, then the NALs.
  /// </param>
  public static BpgSequenceHeader? TryParse(ReadOnlySpan<byte> pictureData) {
    var offset = 0;
    int headerLength;
    try {
      headerLength = BpgUe7.Read(pictureData, ref offset);
    } catch (InvalidOperationException) {
      return null;
    }

    if (headerLength <= 0 || offset + headerLength > pictureData.Length)
      return null;

    try {
      var reader = new H265BitReader(pictureData.Slice(offset, headerLength));
      var log2MinCb = reader.ReadUnsignedExpGolomb() + 3;
      var log2Ctb = log2MinCb + reader.ReadUnsignedExpGolomb();
      var log2MinTb = reader.ReadUnsignedExpGolomb() + 2;
      var log2MaxTb = log2MinTb + reader.ReadUnsignedExpGolomb();
      var maxTransformHierarchyDepthIntra = reader.ReadUnsignedExpGolomb();
      var sao = reader.ReadFlag();
      var pcm = reader.ReadFlag();

      var pcmDepthLuma = 0;
      var pcmDepthChroma = 0;
      var log2MinPcm = 0;
      var log2MaxPcm = 0;
      if (pcm) {
        pcmDepthLuma = reader.ReadBits(4) + 1;
        pcmDepthChroma = reader.ReadBits(4) + 1;
        log2MinPcm = reader.ReadUnsignedExpGolomb() + 3;
        log2MaxPcm = log2MinPcm + reader.ReadUnsignedExpGolomb();
        reader.Skip(1); // pcm_loop_filter_disabled_flag
      }

      reader.Skip(1); // strong_intra_smoothing_enabled_flag
      if (reader.ReadFlag()) // sps_extension_present_flag
        return null; // the range extension changes the residual syntax; nothing here reads that

      return new(
        log2MinCb, log2Ctb, log2MinTb, log2MaxTb, maxTransformHierarchyDepthIntra,
        sao, pcm, pcmDepthLuma, pcmDepthChroma, log2MinPcm, log2MaxPcm,
        offset + headerLength);
    } catch (InvalidDataException) {
      return null;
    }
  }
}
