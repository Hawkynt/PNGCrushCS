using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class AvrnVideoEncoderTests {

  private static readonly CodecTag _Avrn = CodecTag.FromCharacters("AVRn");

  private static MediaStreamInfo _Stream(int width = 4, int height = 2) => new() {
    Index = 2,
    Kind = MediaStreamKind.Video,
    Codec = _Avrn,
    Handler = _Avrn,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
    DeclaredFrameCount = 3,
    Name = "AVRn test",
  };

  [Test]
  [Category("Unit")]
  public void DescribesProgressiveResolutionOneToOneWithAvidMarker() {
    var encoder = AvrnVideoEncoder.Create(_Stream());
    var stream = encoder.DescribeStream();
    var format = stream.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(AvrnVideoEncoder.Codec, Is.EqualTo(_Avrn));
      Assert.That(stream.Codec, Is.EqualTo(_Avrn));
      Assert.That(stream.Handler, Is.EqualTo(_Avrn));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(4));
      Assert.That(stream.Height, Is.EqualTo(2));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(16));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(25, 1)));
      Assert.That(stream.Name, Is.EqualTo("AVRn test"));
      Assert.That(format.Length, Is.EqualTo(71));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(format), Is.EqualTo(40));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format[4..]), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format[8..]), Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(format[14..]), Is.EqualTo(16));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(format[16..]), Is.EqualTo(_Avrn.Value));
      Assert.That(format.AsSpan(68, 3).ToArray(), Is.EqualTo("1:1"u8.ToArray()));
      Assert.That(format.AsSpan(44, 4).ToArray(), Is.Not.EqualTo("1:1("u8.ToArray()));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodesUyvyLosslesslyAndDecoderReadsItBack() {
    var source = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Yuv422P8,
      PixelData = [
        // Y
        20, 40, 60, 80, 21, 41, 61, 81,
        // Cb
        10, 50, 11, 51,
        // Cr
        30, 70, 31, 71,
      ],
    };
    var encoder = AvrnVideoEncoder.Create(_Stream());

    Assert.That(encoder.TryEncode(source, 7, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.EqualTo(2));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] {
        10, 20, 30, 40, 50, 60, 70, 80,
        11, 21, 31, 41, 51, 61, 71, 81,
      }));
    });

    var decoder = AvrnVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv422P8));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesMidStreamGeometryChange() {
    var encoder = AvrnVideoEncoder.Create(_Stream());
    var wrong = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Yuv422P8,
      PixelData = new byte[8],
    };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrong, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void RefusesOddWidthBecauseUyvyHasTwoPixelMacropixels()
    => Assert.Throws<NotSupportedException>(() => AvrnVideoEncoder.Create(_Stream(width: 3)));

  [Test]
  [Category("Unit")]
  public void RefusesNonVideoStream() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = _Avrn,
      Width = 4,
      Height = 2,
    };

    Assert.Throws<NotSupportedException>(() => AvrnVideoEncoder.Create(stream));
  }
}
