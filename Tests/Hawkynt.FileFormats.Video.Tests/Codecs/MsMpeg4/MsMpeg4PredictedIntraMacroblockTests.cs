using System;
using System.Linq;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.MsMpeg4.Tests;

[TestFixture]
public sealed class MsMpeg4PredictedIntraMacroblockTests {

  [TestCase("MPG4", 1)]
  [TestCase("MP42", 2)]
  [TestCase("MP43", 3)]
  [Category("Unit")]
  public void SceneChangeUsesTheIntraMacroblockFormInsideAPredictedPicture(string tag, int versionValue) {
    var version = (MsMpeg4Version)versionValue;
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(tag),
      Width = 16,
      Height = 16,
      FrameRate = new(25, 1),
      TimeBase = new(1, 25),
    };

    var encoder = MsMpeg4VideoEncoder.Create(stream);
    Assert.That(encoder.TryEncode(_Solid(16, 16, 24), 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(_Solid(16, 16, 224), 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.IsKeyFrame, Is.False, "an intra macroblock does not turn its P picture into a key frame");
    });

    var reader = new Mpeg4BitReader(second.Data.Span);
    var header = MsMpeg4PictureHeader.Parse(ref reader, version, macroblockHeight: 1, previousSliceHeight: 1);
    Assert.That(header.CodingType, Is.EqualTo(MsMpeg4PictureHeader.PredictiveCoded));
    Assert.That(reader.ReadBit(), Is.Zero, "the scene-change macroblock must be coded rather than skipped");

    switch (version) {
      case MsMpeg4Version.Version1:
        Assert.That(MsMpeg4Tables.InterMacroblock.Read(ref reader) & 4, Is.Not.Zero,
          "version 1 carries the intra flag in bit 2 of H.263 MCBPC");
        break;

      case MsMpeg4Version.Version2:
        Assert.That(MsMpeg4Tables.V2MacroblockType.Read(ref reader) & 4, Is.Not.Zero,
          "version 2 carries the intra flag in bit 2 of its macroblock type");
        break;

      default:
        Assert.That(MsMpeg4Tables.MacroblockNonIntra.Read(ref reader) & 0x40, Is.Zero,
          "version 3 carries an intra macroblock in the bit-6-clear half of its P-picture table");
        break;
    }

    var decoder = MsMpeg4VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(first, out _), Is.True);
    Assert.That(decoder.TryDecode(second, out var decoded), Is.True);
    Assert.That(decoded.PixelData.Average(value => (double)value), Is.GreaterThan(180d));
  }

  private static RawImage _Solid(int width, int height, byte value) {
    var pixels = new byte[width * height * 3];
    Array.Fill(pixels, value);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }
}
