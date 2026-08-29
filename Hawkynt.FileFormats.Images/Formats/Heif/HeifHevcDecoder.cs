using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H265;
using FileFormat.Core;

namespace FileFormat.Heif;

/// <summary>Decodes the HEVC coded image carried by one HEIF item.</summary>
/// <remarks>
/// The reconstruction code is the same H.265 implementation the video package uses. The Images
/// project compiles that source directory as linked source to avoid a circular assembly reference:
/// Video already references Images for still-image codecs used by video containers.
/// </remarks>
internal static class HeifHevcDecoder {

  internal static RawImage Decode(ReadOnlyMemory<byte> sample, ReadOnlyMemory<byte> configurationRecord) {
    // HEVC PCM has a CABAC-to-raw handoff in the middle of a coding unit. The general video decoder
    // intentionally still refuses that syntax until its streaming arithmetic engine can expose the
    // handoff generically; the still-image codec has a narrow, standards-based PCM path for exactly
    // the Main-Still-Picture profile emitted by HeifWriter.
    if (H265PcmStillCodec.TryDecode(sample, configurationRecord, out var pcm))
      return pcm;

    var configuration = H265DecoderConfiguration.TryParse(configurationRecord)
                        ?? throw new InvalidDataException(
                          "HEIF: the hvcC property is not a valid HEVCDecoderConfigurationRecord.");

    var sequenceSets = new Dictionary<int, H265SequenceParameterSet>();
    var pictureSets = new Dictionary<int, H265PictureParameterSet>();

    foreach (var bytes in configuration.ParameterSets)
      _AcceptParameterSet(H265NalReader.Parse(bytes), sequenceSets, pictureSets);

    H265FrameDecoder? frame = null;
    H265SequenceParameterSet? pictureSequence = null;

    foreach (var nal in H265NalReader.SplitLengthPrefixed(sample, configuration.LengthSize)) {
      if (nal.LayerId != 0)
        throw new NotSupportedException(
          $"HEIF: the coded image carries H.265 layer {nal.LayerId} (nuh_layer_id). "
          + "Only the base layer is implemented.");

      switch (nal.Type) {
        case H265NalUnitType.VideoParameterSet:
        case H265NalUnitType.SequenceParameterSet:
        case H265NalUnitType.PictureParameterSet:
          _AcceptParameterSet(nal, sequenceSets, pictureSets);
          continue;
      }

      if (!nal.IsSlice)
        continue;

      var header = H265SliceHeader.Parse(nal, sequenceSets, pictureSets);

      if (header.FirstSliceSegmentInPicture) {
        if (frame != null)
          throw new InvalidDataException(
            "HEIF: one coded image item contains more than one H.265 picture. "
            + "Each HEVC image item must identify one coded image.");

        frame = new(header.Sps, header.Pps);
        pictureSequence = header.Sps;
      } else if (frame == null) {
        throw new InvalidDataException(
          "HEIF: an H.265 slice segment continues a picture that was never opened by a first slice segment.");
      }

      frame.DecodeSliceSegment(header, [[], []]);
    }

    if (frame == null || pictureSequence == null)
      throw new InvalidDataException("HEIF: the HEVC image item contains no coded picture.");

    frame.RefuseIfIncomplete();
    H265Deblocking.Filter(frame);
    H265SampleAdaptiveOffset.Filter(frame);

    return new() {
      Width = pictureSequence.DisplayWidth,
      Height = pictureSequence.DisplayHeight,
      Format = PixelFormat.Rgb24,
      PixelData = H265ColorConversion.ToRgb24(
        frame.Picture,
        pictureSequence.CropOffsetX,
        pictureSequence.CropOffsetY,
        pictureSequence.DisplayWidth,
        pictureSequence.DisplayHeight),
    };
  }

  private static void _AcceptParameterSet(
    H265NalUnit nal,
    Dictionary<int, H265SequenceParameterSet> sequenceSets,
    Dictionary<int, H265PictureParameterSet> pictureSets
  ) {
    switch (nal.Type) {
      case H265NalUnitType.SequenceParameterSet: {
        var sps = H265SequenceParameterSet.Parse(nal.Payload);
        sequenceSets[sps.Id] = sps;
        break;
      }

      case H265NalUnitType.PictureParameterSet: {
        var pps = H265PictureParameterSet.Parse(nal.Payload);
        pictureSets[pps.Id] = pps;
        break;
      }
    }
  }
}
