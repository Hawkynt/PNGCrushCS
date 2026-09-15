using System;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.MsMpeg4.Tests;

[TestFixture]
public sealed class MsMpeg4EncoderRegistryTests {

  private static readonly string[] _Tags = [
    "MPG4", "MP41", "DIV1",
    "MP42", "DIV2",
    "MP43", "DIV3", "DIV4", "DIV5", "DIV6", "DVX3", "AP41", "AP42", "COL0", "COL1", "MPG3",
  ];

  [TestCaseSource(nameof(_Tags))]
  [Category("Unit")]
  public void EveryAliasTheEncoderWritesIsReachableThroughTheRegistry(string tag) {
    var stream = _Stream(tag);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True, tag);
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<MsMpeg4VideoEncoder>(), tag);
      Assert.That(MsMpeg4VideoEncoder.Create(stream).DescribeStream().Codec, Is.EqualTo(stream.Codec), tag);
    });
  }

  [Test]
  [Category("Unit")]
  public void DirectFactoryRefusesAnUnknownTagInsteadOfWritingVersionThreeUnderIt() {
    var stream = _Stream("ZZZZ");

    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.False);

    var refusal = Assert.Throws<NotSupportedException>(() => MsMpeg4VideoEncoder.Create(stream));
    Assert.That(refusal!.Message, Does.Contain("not one of its version 1, 2 or 3 FourCCs"));
  }

  private static MediaStreamInfo _Stream(string tag) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(tag),
    Width = 64,
    Height = 64,
  };
}
