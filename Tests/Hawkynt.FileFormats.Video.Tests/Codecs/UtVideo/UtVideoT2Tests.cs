using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.UtVideo.Tests;

[TestFixture]
public sealed class UtVideoT2Tests {

  [Test]
  [Category("Unit")]
  [TestCase("UMRG", 24)]
  [TestCase("UMRA", 32)]
  [TestCase("UMY2", 24)]
  [TestCase("UMY4", 24)]
  [TestCase("UMH2", 24)]
  [TestCase("UMH4", 24)]
  public void DescriptionNamesTheT2LayoutAndRegistryRoutesItToTheT2Decoder(string code, int bitsPerPixel) {
    var encoder = UtVideoT2Encoder.Create(_Stream(code, 32, 9), 3, 30);
    var stream = encoder.DescribeStream();
    var description = stream.CodecPrivateData.Span;

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters(code)));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(bitsPerPixel));
      Assert.That(description, Has.Length.EqualTo(56));
      Assert.That(description[48], Is.EqualTo(2), "eight-symbol packing mode");
      Assert.That(description[49], Is.EqualTo(2), "three bands less one");
      Assert.That(description[50], Is.EqualTo(3), "temporal and control compression");
      Assert.That(UtVideoDecoder.Accepts(stream), Is.False, "classic Ut Video must not steal T2 routing");
      Assert.That(UtVideoT2Decoder.Accepts(stream), Is.True);
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.TypeOf<UtVideoT2Decoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void ColourIntraFramesRoundTripAcrossPaddedRows(
    [Values("UMRG", "UMRA")] string code,
    [Values(1, 3)] int slices) {
    const int width = 67;
    const int height = 7;
    var format = code == "UMRA" ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
    var picture = _Noise(width, height, format, 17);
    var encoder = UtVideoT2Encoder.Create(_Stream(code, width, height), slices);
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(picture, 12, out var packet), Is.True);
    Assert.That(packet.IsKeyFrame, Is.True);
    Assert.That(packet.Data.Span[0], Is.EqualTo(1));
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(format));
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
    });
  }

  [Test]
  [Category("Unit")]
  public void YuvIntraFramesRoundTripSampleForSample(
    [Values("UMY2", "UMH2", "UMY4", "UMH4")] string code) {
    var width = code.EndsWith('2') ? 70 : 69;
    const int height = 7;
    var format = code.EndsWith('2') ? PixelFormat.Yuv422P8 : PixelFormat.Yuv444P8;
    var picture = _Noise(width, height, format, 29);
    var encoder = UtVideoT2Encoder.Create(_Stream(code, width, height), 3);
    var decoder = UtVideoT2Decoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    var decoded = decoder.DecodePlanes(packet.Data.Span);

    Assert.That(decoded, Has.Length.EqualTo(3));
    for (var plane = 0; plane < 3; ++plane)
      Assert.That(decoded[plane], Is.EqualTo(picture.GetPlaneData(plane).ToArray()), $"{code} plane {plane}");
  }

  [Test]
  [Category("Unit")]
  public void TemporalFramesReferenceOnlyTheirImmediatePredecessorAndRefreshAtTheInterval() {
    const int width = 70;
    const int height = 8;
    var first = _Noise(width, height, PixelFormat.Rgb24, 41);
    var second = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = first.PixelData.ToArray(),
    };
    var third = _Noise(width, height, PixelFormat.Rgb24, 43);
    var fourth = _Noise(width, height, PixelFormat.Rgb24, 47);
    var encoder = UtVideoT2Encoder.Create(_Stream("UMRG", width, height), 3, 3);
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    RawImage[] pictures = [first, second, third, fourth];
    byte[] expectedTypes = [1, 2, 2, 1];
    bool[] expectedKeys = [true, false, false, true];

    for (var i = 0; i < pictures.Length; ++i) {
      Assert.That(encoder.TryEncode(pictures[i], i, out var packet), Is.True);
      Assert.Multiple(() => {
        Assert.That(packet.Data.Span[0], Is.EqualTo(expectedTypes[i]), $"frame {i} type");
        Assert.That(packet.IsKeyFrame, Is.EqualTo(expectedKeys[i]), $"frame {i} key flag");
        Assert.That(packet.DecodeTimestamp, Is.EqualTo(packet.PresentationTimestamp), "T2 does not reorder frames");
      });

      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void DeltaPacketWithoutItsReferenceIsRejected() {
    var encoder = UtVideoT2Encoder.Create(_Stream("UMRG", 16, 4), 2, 5);
    var picture = _Noise(16, 4, PixelFormat.Rgb24, 5);
    Assert.That(encoder.TryEncode(picture, 0, out _), Is.True);
    Assert.That(encoder.TryEncode(picture, 1, out var delta), Is.True);
    var freshDecoder = UtVideoT2Decoder.Create(encoder.DescribeStream());

    Assert.That(delta.Data.Span[0], Is.EqualTo(2));
    Assert.Throws<InvalidDataException>(() => freshDecoder.TryDecode(delta, out _));
  }

  [Test]
  [Category("Unit")]
  public void RawLz4BlockLiteralWriterRoundTripsAndMalformedBackReferenceIsRejected() {
    var source = new byte[1024];
    new Random(73).NextBytes(source);
    var packed = Lz4Block.PackLiteralOnly(source);

    Assert.That(Lz4Block.Unpack(packed, source.Length), Is.EqualTo(source));
    Assert.Throws<InvalidDataException>(() => Lz4Block.Unpack([0, 1, 0], 4));
  }

  [Test]
  [Category("Unit")]
  public void InvalidT2GeometryAndIntervalsAreRefusedBeforeEncoding() {
    Assert.Multiple(() => {
      Assert.Throws<NotSupportedException>(() => UtVideoT2Encoder.Create(_Stream("UMY2", 7, 4), 1));
      Assert.Throws<NotSupportedException>(() => UtVideoT2Encoder.Create(_Stream("UMRG", 8, 3), 4));
      Assert.Throws<ArgumentOutOfRangeException>(() => UtVideoT2Encoder.Create(_Stream("UMRG", 8, 4), 1, 0));
      Assert.Throws<NotSupportedException>(() => UtVideoT2Encoder.Create(_Stream("ULRG", 8, 4), 1));
    });
  }

  private static MediaStreamInfo _Stream(string code, int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Noise(int width, int height, PixelFormat format, int seed) {
    var probe = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    var pixels = new byte[checked((int)probe.MinimumPixelDataLength)];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }
}
