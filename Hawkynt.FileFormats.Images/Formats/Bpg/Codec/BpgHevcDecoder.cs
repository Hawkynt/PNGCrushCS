using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H265;

namespace FileFormat.Bpg.Codec;

/// <summary>Top-level decoder that wires BPG's stripped HEVC stream through the shared H.265 decoder.</summary>
internal static class BpgHevcDecoder {

  /// <summary>Decodes the first picture carried by a BPG file.</summary>
  /// <returns>Gray8 for grayscale or RGB24 for color.</returns>
  public static byte[] Decode(BpgFile bpg) {
    ArgumentNullException.ThrowIfNull(bpg);

    if (bpg.PixelData.Length == 0)
      throw new InvalidDataException("BPG file contains no HEVC header or picture data.");

    // The extra HEVC layer is alpha when alpha1 is set and CMYK's W component otherwise. Both need
    // two independent implicit SPS instances and layer-demultiplexed slice state; do not silently
    // discard that fourth component and return a plausible but wrong RGB picture.
    if (bpg.HasAlpha || bpg.HasAlpha2)
      throw new NotSupportedException("BPG alpha and CMYK auxiliary-plane decoding is not implemented yet.");

    if (bpg.BitDepth > 12)
      throw new NotSupportedException(
        $"This BPG picture uses {bpg.BitDepth}-bit components. The shared H.265 reconstruction path is verified through 12 bits; BPG also permits 13 and 14.");

    if (_TryDecodeUniformPcm(bpg, out var pcm))
      return pcm;

    var offset = 0;
    var sequenceHeader = BpgSequenceHeader.Parse(bpg.PixelData, ref offset);
    var sequence = BpgSyntheticSequenceParameterSet.Create(bpg, sequenceHeader);

    if (offset >= bpg.PixelData.Length)
      throw new InvalidDataException("The BPG HEVC header is not followed by any NAL units.");

    // BPG removes only the first Annex-B start code. Every later NAL keeps its own start code.
    var annexB = new byte[checked(3 + bpg.PixelData.Length - offset)];
    annexB[2] = 1;
    bpg.PixelData.AsSpan(offset).CopyTo(annexB.AsSpan(3));

    var sequenceSets = new Dictionary<int, H265SequenceParameterSet> { [0] = sequence };
    var pictureSets = new Dictionary<int, H265PictureParameterSet>();
    H265FrameDecoder? frame = null;
    H265SliceHeader? precedingIndependent = null;

    foreach (var nal in H265NalReader.SplitAnnexB(annexB)) {
      if (nal.LayerId != 0)
        throw new InvalidDataException(
          $"A BPG without an auxiliary plane carries HEVC nuh_layer_id {nal.LayerId}; only layer 0 is valid here.");

      switch (nal.Type) {
        case H265NalUnitType.VideoParameterSet:
        case H265NalUnitType.SequenceParameterSet:
          throw new InvalidDataException("BPG picture data must omit HEVC VPS and SPS NAL units; their state is carried by the BPG header.");

        case H265NalUnitType.PictureParameterSet: {
          var pps = H265PictureParameterSet.Parse(nal.Payload);
          if (pps.SequenceParameterSetId != 0)
            throw new InvalidDataException(
              $"BPG fixes sps_seq_parameter_set_id at zero, but picture parameter set {pps.Id} names SPS {pps.SequenceParameterSetId}.");
          pictureSets[pps.Id] = pps;
          continue;
        }
      }

      if (!nal.IsSlice)
        continue;

      var header = H265SliceHeader.Parse(nal, sequenceSets, pictureSets, precedingIndependent);
      if (!header.DependentSliceSegment)
        precedingIndependent = header;

      if (header.FirstSliceSegmentInPicture) {
        if (frame != null) {
          if (bpg.IsAnimation)
            break; // BPG explicitly permits a non-animation decoder to display only the first frame.

          throw new InvalidDataException("A non-animated BPG file carries more than one HEVC picture.");
        }

        if (!header.IsIntra)
          throw new InvalidDataException("The first picture of a BPG stream must be an HEVC intra picture.");
        frame = new(header.Sps, header.Pps);
      } else if (frame == null) {
        throw new InvalidDataException("A BPG slice segment continues a picture that was never opened by a first segment.");
      }

      frame.DecodeSliceSegment(header, [[], []]);
    }

    if (frame == null)
      throw new InvalidDataException("The BPG HEVC payload contains no coded picture.");

    frame.RefuseIfIncomplete();
    H265Deblocking.Filter(frame);
    H265SampleAdaptiveOffset.Filter(frame);
    return BpgColorConverter.Convert(frame.Picture, bpg);
  }

  /// <summary>
  /// Decodes the deliberately narrow all-PCM profile this package writes before invoking the general
  /// transform decoder. The result is still ordinary HEVC; this path exists only because PCM crosses
  /// the CABAC/raw-sample boundary the streaming decoder intentionally does not expose yet.
  /// </summary>
  private static bool _TryDecodeUniformPcm(BpgFile bpg, out byte[] pixels) {
    pixels = [];
    if (bpg.BitDepth != 8 || bpg.HasAlpha || bpg.HasAlpha2 || bpg.LimitedRange)
      return false;

    if (bpg is not { PixelFormat: BpgPixelFormat.YCbCr444, ColorSpace: BpgColorSpace.Rgb })
      return false;

    var header = BpgSequenceHeader.TryParse(bpg.PixelData);
    if (header is not {
          PcmEnabled: true, SaoEnabled: false,
          PcmBitDepthLuma: 8, PcmBitDepthChroma: 8,
          Log2CtbSize: _CODING_TREE_BLOCK_LOG2,
          Log2MinLumaCodingBlockSize: _CODING_TREE_BLOCK_LOG2,
        }
        || header.Log2MinPcmCodingBlockSize > _CODING_TREE_BLOCK_LOG2
        || header.Log2MaxPcmCodingBlockSize < _CODING_TREE_BLOCK_LOG2)
      return false;

    var block = 1 << _CODING_TREE_BLOCK_LOG2;
    var codedWidth = (bpg.Width + block - 1) / block * block;
    var codedHeight = (bpg.Height + block - 1) / block * block;
    if (!H265PcmStillCodec.TryDecodeBpgStill(
          bpg.PixelData.AsSpan(header.HevcDataOffset), codedWidth, codedHeight, out var planes))
      return false;

    pixels = _InterleaveGbr(planes, codedWidth, bpg.Width, bpg.Height);
    return true;
  }

  private const int _CODING_TREE_BLOCK_LOG2 = 5;

  private static byte[] _InterleaveGbr(byte[][] planes, int stride, int width, int height) {
    var result = new byte[checked(width * height * 3)];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var from = y * stride + x;
        var to = (y * width + x) * 3;
        result[to] = planes[2][from];
        result[to + 1] = planes[0][from];
        result[to + 2] = planes[1][from];
      }

    return result;
  }
}
