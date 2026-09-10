using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Mpeg4.Tests;

/// <summary>The MPEG-4 Part 2 writer against the decoder beside it and the public codec registry.</summary>
[TestFixture]
public sealed class Mpeg4VideoEncoderTests {

  [TestCase(16, 16)]
  [TestCase(64, 48)]
  [TestCase(127, 95)]
  [Category("Unit")]
  public void RectangularPictureSizesAreAccepted(int width, int height) {
    var stream = Mpeg4VideoEncoder.Create(_Stream(width, height)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Width, Is.EqualTo(width));
      Assert.That(stream.Height, Is.EqualTo(height));
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("mp4v")));
    });
  }

  [TestCase(0, 16)]
  [TestCase(16, 0)]
  [TestCase(8192, 16)]
  [TestCase(16, 8192)]
  [Category("Unit")]
  public void DimensionsTheVolCannotStateAreRefused(int width, int height) {
    var refusal = Assert.Throws<NotSupportedException>(() => Mpeg4VideoEncoder.Create(_Stream(width, height)));

    Assert.That(refusal!.Message, Does.Contain($"{width}x{height}"));
  }

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsRefused() {
    var refusal = Assert.Throws<NotSupportedException>(() => Mpeg4VideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("mp4v"),
      Width = 16,
      Height = 16,
    }));

    Assert.That(refusal!.Message, Does.Contain("video"));
  }

  [Test]
  [Category("Unit")]
  public void AChangedPictureSizeIsRefused() {
    var encoder = Mpeg4VideoEncoder.Create(_Stream(32, 32));
    var refusal = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Picture(48, 32, 0), 0, out _));

    Assert.That(refusal!.Message, Does.Contain("48x32"));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedSourcePixelsAreRefusedBeforeTheConverterReadsThem() {
    var encoder = Mpeg4VideoEncoder.Create(_Stream(32, 32));
    var frame = new RawImage { Width = 32, Height = 32, Format = PixelFormat.Rgb24, PixelData = [1, 2, 3] };

    var refusal = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));

    Assert.That(refusal!.Message, Does.Contain("enough pixel data"));
  }

  [Test]
  [Category("Unit")]
  public void EveryPacketIsARandomAccessPointWithItsOwnVolAndVop() {
    var encoder = Mpeg4VideoEncoder.Create(_Stream(32, 32));

    for (var index = 0; index < 3; ++index) {
      Assert.That(encoder.TryEncode(_Picture(32, 32, index), index, out var packet), Is.True);

      Assert.Multiple(() => {
        Assert.That(packet.IsKeyFrame, Is.True);
        Assert.That(packet.DecodeTimestamp, Is.EqualTo(index));
        Assert.That(packet.PresentationTimestamp, Is.EqualTo(index));
        Assert.That(_ContainsStartCode(packet.Data.Span, Mpeg4StartCode.FirstVideoObjectLayer), Is.True, "VOL");
        Assert.That(_ContainsStartCode(packet.Data.Span, Mpeg4StartCode.VideoObjectPlane), Is.True, "VOP");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void WhatTheEncoderWritesIsWhatTheDecoderReadsInOrder() {
    const int width = 64;
    const int height = 48;
    const int frames = 5;
    const double worstMeanSquaredError = 100d;

    var encoder = Mpeg4VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var index = 0; index < frames; ++index) {
      var source = _Picture(width, height, index);
      sources.Add(source);
      Assert.That(encoder.TryEncode(source, index, out var packet), Is.True);
      packets.Add(packet);
    }

    var decoder = Mpeg4VideoDecoder.Create(encoder.DescribeStream());
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);
    decoded.AddRange(decoder.Flush());

    Assert.That(decoded, Has.Count.EqualTo(frames));
    for (var index = 0; index < frames; ++index) {
      Assert.Multiple(() => {
        Assert.That(decoded[index].Width, Is.EqualTo(width));
        Assert.That(decoded[index].Height, Is.EqualTo(height));
        Assert.That(decoded[index].Format, Is.EqualTo(PixelFormat.Rgb24));
      });

      var error = _MeanSquaredError(sources[index].PixelData, decoded[index].PixelData);
      Assert.That(error, Is.LessThan(worstMeanSquaredError),
        $"frame {index} came back {error:F1} squared levels from what went in");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheStreamDescriptionIsVfwMp4vAndTheDecoderAcceptsIt() {
    var stream = Mpeg4VideoEncoder.Create(_Stream(64, 48)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("mp4v")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("mp4v")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(40));
      Assert.That(Mpeg4VideoDecoder.Accepts(stream), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryReachesBothHalvesOfMpeg4PartTwo() {
    var stream = _Stream(64, 48);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Mpeg4VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Mpeg4VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBackByTheEncoder()
    => Assert.That(Mpeg4VideoEncoder.Create(_Stream(64, 48)).Flush(), Is.Empty);

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("mp4v"),
    TimeBase = new Rational(1001, 30000),
    FrameRate = new Rational(30000, 1001),
    Width = width,
    Height = height,
  };

  /// <summary>
  /// A greyscale texture. Constant chrominance keeps the error measured here on the transform and
  /// quantiser rather than on a choice of 4:2:0 upsampling convention.
  /// </summary>
  private static RawImage _Picture(int width, int height, int frame) {
    var pixels = new byte[width * height * 3];
    var left = frame * 5 % Math.Max(1, width - 16);
    var top = frame * 3 % Math.Max(1, height - 16);

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var inside = x >= left && x < left + 16 && y >= top && y < top + 16;
        var value = (byte)(inside ? 216 : 32 + ((x >> 2) * 7 + (y >> 2) * 11) % 144);
        var at = (y * width + x) * 3;
        pixels[at] = value;
        pixels[at + 1] = value;
        pixels[at + 2] = value;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static bool _ContainsStartCode(ReadOnlySpan<byte> bytes, byte code) {
    for (var i = 0; i + 3 < bytes.Length; ++i)
      if (bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 1 && bytes[i + 3] == code)
        return true;

    return false;
  }

  private static double _MeanSquaredError(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length));
    long squared = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var difference = expected[i] - actual[i];
      squared += difference * difference;
    }

    return (double)squared / expected.Length;
  }
}
