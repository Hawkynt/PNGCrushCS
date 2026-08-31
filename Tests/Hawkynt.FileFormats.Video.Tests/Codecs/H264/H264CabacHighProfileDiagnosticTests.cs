using System;
using System.Reflection;
using System.Security.Cryptography;
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
          var frameType = typeof(H264FrameDecoder);
          var cabacTransformField = frameType.GetField("_cabacTransform8x8", BindingFlags.Instance | BindingFlags.NonPublic);
          var cabacCbpField = frameType.GetField("_cabacCbpLuma", BindingFlags.Instance | BindingFlags.NonPublic);
          var cabacTransform = (bool[])cabacTransformField!.GetValue(frame)!;
          var cabacCbp = (byte[])cabacCbpField!.GetValue(frame)!;

          var deblockingType = typeof(H264Deblocking);
          var boundaryStrength = deblockingType.GetMethod("_BoundaryStrength", BindingFlags.Static | BindingFlags.NonPublic);
          var thresholds = deblockingType.GetMethod("_Thresholds", BindingFlags.Static | BindingFlags.NonPublic);
          Assert.Multiple(() => {
            Assert.That(boundaryStrength, Is.Not.Null);
            Assert.That(thresholds, Is.Not.Null);
          });

          var flags = new char[16];
          var qps = new int[16];
          for (var address = 0; address < flags.Length; ++address) {
            flags[address] = frame.Transform8x8Of(address) ? '8' : '4';
            qps[address] = frame.QpOf(address);
          }

          var picture = frame.Picture;
          var raw = new byte[picture.Luma.Length + picture.Cb.Length + picture.Cr.Length];
          picture.Luma.CopyTo(raw, 0);
          picture.Cb.CopyTo(raw, picture.Luma.Length);
          picture.Cr.CopyTo(raw, picture.Luma.Length + picture.Cb.Length);
          var preDeblockHash = Convert.ToHexString(SHA256.HashData(raw));

          const int mb5 = 5; // (1,1), above the affected MB
          const int mb9 = 9; // (1,2), luma x=16..31 / y=32..47
          var report = $"slice={header.SliceType} qps={string.Join(',', qps)} flags={new string(flags)}\n"
                       + $"mb5.kind={frame.KindOf(mb5)} mb5.qp={frame.QpOf(mb5)} mb5.cbpLuma={cabacCbp[mb5]} "
                       + $"mb5.cabac8x8={cabacTransform[mb5]} effective8x8={frame.Transform8x8Of(mb5)}\n"
                       + $"mb9.kind={frame.KindOf(mb9)} mb9.qp={frame.QpOf(mb9)} mb9.cbpLuma={cabacCbp[mb9]} "
                       + $"mb9.cabac8x8={cabacTransform[mb9]} effective8x8={frame.Transform8x8Of(mb9)}\n"
                       + $"preDeblockSha256={preDeblockHash}";

          foreach (var y in new[] { 32, 40 })
            foreach (var x in new[] { 16, 20, 24, 28 }) {
              var macroblockEdge = y == 32;
              var pMb = (y - 1) / 16 * frame.MacroblockWidth + x / 16;
              var qMb = y / 16 * frame.MacroblockWidth + x / 16;
              var bs = (int)boundaryStrength!.Invoke(null, [frame, x, y - 1, x, y, macroblockEdge])!;
              var t = ((int Alpha, int Beta, int IndexA))thresholds!.Invoke(null, [frame, qMb, pMb, false, 0])!;
              var pBlockX = x >> 2;
              var pBlockY = (y - 1) >> 2;
              var qBlockY = y >> 2;
              report += $"\nedge({x},{y}) bS={bs} alpha={t.Alpha} beta={t.Beta} indexA={t.IndexA} "
                        + $"pMb={pMb}/qp{frame.QpOf(pMb)} qMb={qMb}/qp{frame.QpOf(qMb)} "
                        + $"pCoeff={frame.BlockHasCoefficients(pBlockX, pBlockY)} qCoeff={frame.BlockHasCoefficients(pBlockX, qBlockY)} "
                        + $"pMv={frame.BlockMotionPair(pBlockX, pBlockY)} qMv={frame.BlockMotionPair(pBlockX, qBlockY)} "
                        + $"samples={_HorizontalSamples(picture.Luma, picture.LumaWidth, x, y)}";
            }

          Assert.Fail(report);
          return;
      }
    }

    Assert.Fail("The High-profile diagnostic stream did not contain the expected second coded picture.");
  }

  private static string _HorizontalSamples(byte[] plane, int width, int x, int y)
    => $"p3={plane[(y - 4) * width + x]},p2={plane[(y - 3) * width + x]},p1={plane[(y - 2) * width + x]},"
       + $"p0={plane[(y - 1) * width + x]},q0={plane[y * width + x]},q1={plane[(y + 1) * width + x]},"
       + $"q2={plane[(y + 2) * width + x]},q3={plane[(y + 3) * width + x]}";
}
