using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H264;
using FileFormat.Core;

namespace FileFormat.Heif;

/// <summary>Decodes the AVC coded image carried by one HEIF item.</summary>
/// <remarks>
/// An AVCI file is a HEIF whose picture is an H.264 access unit rather than an
/// H.265 one, so the container work is the same and only the codec differs. The
/// reconstruction is the H.264 implementation the video package uses, reaching
/// this assembly as linked source for the reason
/// <see cref="HeifHevcDecoder"/> gives.
///
/// <para>A still image is one picture, so none of the machinery a video decoder
/// needs around a frame applies: there is no reference list to build, no picture
/// order to settle and nothing to present later. What is left is the parameter
/// sets from the <c>avcC</c> property, one picture's slices, the deblocking
/// filter, and the crop the sequence parameter set states.</para>
/// </remarks>
internal static class HeifAvcDecoder {

  internal static RawImage Decode(ReadOnlyMemory<byte> sample, ReadOnlyMemory<byte> configurationRecord) {
    var configuration = H264DecoderConfiguration.TryParse(configurationRecord)
                        ?? throw new InvalidDataException(
                          "HEIF: the avcC property is not a valid AVCDecoderConfigurationRecord.");

    var sequenceSets = new Dictionary<int, H264SequenceParameterSet>();
    var pictureSets = new Dictionary<int, H264PictureParameterSet>();

    foreach (var bytes in configuration.SequenceParameterSets)
      _AcceptParameterSet(bytes, sequenceSets, pictureSets);
    foreach (var bytes in configuration.PictureParameterSets)
      _AcceptParameterSet(bytes, sequenceSets, pictureSets);

    H264FrameDecoder? frame = null;
    H264SliceHeader? pictureHeader = null;

    foreach (var nal in H264NalReader.SplitLengthPrefixed(sample, configuration.LengthSize)) {
      switch (nal.Type) {
        case H264NalUnitType.SequenceParameterSet:
        case H264NalUnitType.PictureParameterSet:
          _AcceptParameterSet(nal.Payload, sequenceSets, pictureSets);
          continue;
        case H264NalUnitType.NonIdrSlice:
        case H264NalUnitType.IdrSlice:
          break;
        default:
          continue;
      }

      var reader = new H264BitReader(nal.Payload);
      var header = H264SliceHeader.Parse(ref reader, nal, sequenceSets, pictureSets);

      if (header.FirstMbInSlice == 0) {
        if (frame != null)
          throw new InvalidDataException(
            "HEIF: one coded image item contains more than one H.264 picture. "
            + "An image item has to identify one coded image.");

        frame = new H264FrameDecoder(header.Sps, serial: 0);
        pictureHeader = header;
      } else if (frame == null) {
        throw new InvalidDataException(
          "HEIF: an H.264 slice continues a picture that was never opened by a slice at macroblock zero.");
      }

      // A still image has nothing to predict from, so both reference lists are
      // empty; a stream that leans on one is refused by the decoder rather than
      // predicted from whatever the buffers hold.
      if (header.Pps.EntropyCodingModeFlag)
        frame.DecodeCabacSlice(ref reader, header, [], []);
      else if (header.IsB)
        frame.DecodeBSlice(ref reader, header, [], []);
      else
        frame.DecodeSlice(ref reader, header, []);
    }

    if (frame == null || pictureHeader == null)
      throw new InvalidDataException("HEIF: the AVC image item contains no coded picture.");

    frame.RefuseIfIncomplete();
    H264Deblocking.Filter(frame);

    var sps = pictureHeader.Sps;
    var picture = frame.Picture;
    var yuv = RawImageFactory.FromYuv420P8(
      sps.DisplayWidth,
      sps.DisplayHeight,
      picture.Luma,
      picture.LumaWidth,
      picture.Cb,
      picture.Cr,
      picture.ChromaWidth,
      sps.CropOffsetX,
      sps.CropOffsetY,
      RawImageColorInfo.Bt601Limited);

    // The HEVC path hands back RGB, and an item's codec should not change what
    // the container returns.
    return FastRawImageConverter.Convert(yuv, PixelFormat.Rgb24);
  }

  private static void _AcceptParameterSet(
    ReadOnlyMemory<byte> bytes,
    Dictionary<int, H264SequenceParameterSet> sequenceSets,
    Dictionary<int, H264PictureParameterSet> pictureSets
  ) {
    if (bytes.Length == 0)
      return;

    // The stored parameter sets carry their NAL header; the slice path hands
    // over a payload that has already had it stripped, so both are normalised
    // here by looking at the first byte.
    var span = bytes.Span;
    var type = (H264NalUnitType)(span[0] & 0x1F);
    var payload = type is H264NalUnitType.SequenceParameterSet or H264NalUnitType.PictureParameterSet
      ? bytes[1..]
      : bytes;

    switch (type) {
      case H264NalUnitType.SequenceParameterSet: {
        var sps = H264SequenceParameterSet.Parse(payload.Span);
        sequenceSets[sps.Id] = sps;
        break;
      }

      case H264NalUnitType.PictureParameterSet: {
        var pps = H264PictureParameterSet.Parse(payload.Span);
        pictureSets[pps.Id] = pps;
        break;
      }
    }
  }
}
