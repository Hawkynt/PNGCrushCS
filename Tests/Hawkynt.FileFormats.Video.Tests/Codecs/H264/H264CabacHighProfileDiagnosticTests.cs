using System;
using System.Reflection;
using FileFormat.Core;
using FileFormat.H264Video;

namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264CabacHighProfileDiagnosticTests {
  [Test]
  public void ReportAffectedPFrameDeblockingState() {
    var vectorField = typeof(H264CabacHighProfileConformanceTests).GetField(
      "_CABAC_HIGH_8X8_IPBB",
      BindingFlags.NonPublic | BindingFlags.Static);
    Assert.That(vectorField, Is.Not.Null);
    var encodedBytes = Convert.FromBase64String((string)vectorField!.GetRawConstantValue()!);

    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("avc1"),
    };
    var decoder = H264VideoDecoder.Create(stream);
    var decoderType = typeof(H264VideoDecoder);
    var acceptParameterSet = decoderType.GetMethod(
      "_AcceptParameterSet",
      BindingFlags.Instance | BindingFlags.NonPublic,
      binder: null,
      types: [typeof(H264NalUnit)],
      modifiers: null);
    var decodeSlice = decoderType.GetMethod(
      "_DecodeSlice",
      BindingFlags.Instance | BindingFlags.NonPublic,
      binder: null,
      types: [typeof(H264NalUnit)],
      modifiers: null);
    var frameField = decoderType.GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic);
    var headerField = decoderType.GetField("_pictureHeader", BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.Multiple(() => {
      Assert.That(acceptParameterSet, Is.Not.Null);
      Assert.That(decodeSlice, Is.Not.Null);
      Assert.That(frameField, Is.Not.Null);
      Assert.That(headerField, Is.Not.Null);
    });

    var decodedPictures = 0;
    foreach (var nal in H264NalReader.SplitAnnexB(encodedBytes)) {
      switch (nal.Type) {
        case H264NalUnitType.SequenceParameterSet:
        case H264NalUnitType.PictureParameterSet:
          acceptParameterSet!.Invoke(decoder, [nal]);
          break;

        case H264NalUnitType.IdrSlice:
        case H264NalUnitType.NonIdrSlice:
          decodeSlice!.Invoke(decoder, [nal]);
          ++decodedPictures;
          if (decodedPictures != 2)
            break;

          var frame = (H264FrameDecoder)frameField!.GetValue(decoder)!;
          var header = (H264SliceHeader)headerField!.GetValue(decoder)!;
          var cabacTransformField = typeof(H264FrameDecoder).GetField(
            "_cabacTransform8x8", BindingFlags.Instance | BindingFlags.NonPublic);
          var cabacCbpField = typeof(H264FrameDecoder).GetField(
            "_cabacCbpLuma", BindingFlags.Instance | BindingFlags.NonPublic);
          var cabacTransform = (bool[])cabacTransformField!.GetValue(frame)!;
          var cabacCbp = (byte[])cabacCbpField!.GetValue(frame)!;

          const int mbAddr = 9; // (1,2), luma x=16..31 / y=32..47
          var flags = new char[16];
          for (var address = 0; address < flags.Length; ++address)
            flags[address] = frame.Transform8x8Of(address) ? '8' : '4';

          var report = $"slice={header.SliceType} mb9.kind={frame.KindOf(mbAddr)} mb9.qp={frame.QpOf(mbAddr)} "
                       + $"mb9.cbpLuma={cabacCbp[mbAddr]} mb9.cabac8x8={cabacTransform[mbAddr]} "
                       + $"mb9.effective8x8={frame.Transform8x8Of(mbAddr)} flags={new string(flags)}";

          // Internal vertical edges x=20 and x=28. Report the two 4x4 blocks on each side for
          // the rows where the final FFmpeg oracle differs from this decoder.
          foreach (var (x, y) in new[] { (20, 33), (28, 33), (20, 39), (28, 39) }) {
            var pBlockX = (x - 1) >> 2;
            var qBlockX = x >> 2;
            var blockY = y >> 2;
            report += $"\nedge({x},{y}) "
                      + $"p=({pBlockX},{blockY}) coeff={frame.BlockHasCoefficients(pBlockX, blockY)} "
                      + $"mv={frame.BlockMotionPair(pBlockX, blockY)}; "
                      + $"q=({qBlockX},{blockY}) coeff={frame.BlockHasCoefficients(qBlockX, blockY)} "
                      + $"mv={frame.BlockMotionPair(qBlockX, blockY)}";
          }

          Assert.Fail(report);
          return;
      }
    }

    Assert.Fail("The High-profile diagnostic stream did not contain the expected second coded picture.");
  }
}
