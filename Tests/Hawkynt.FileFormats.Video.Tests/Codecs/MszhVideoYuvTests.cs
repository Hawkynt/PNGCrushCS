using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// LCL MSZH's five YUV image layouts, encoder profile selection, split output and the signed chroma
/// representation established by the compatible FFmpeg decoder.
/// </summary>
[TestFixture]
public sealed class MszhVideoYuvTests {

  [Test]
  [Category("Unit")]
  public void DecodesYuv111AsFullResolutionPlanarSamples() {
    var decoder = MszhVideoDecoder.Create(_Stream(2, 2, imageType: 0, compression: 1));
    var packet = new CodedPacket(0, new byte[] {
      // bottom row: (Y, Cb-128, Cr-128)
      1, 2, 12, 2, 3, 13,
      // top row
      11, 22, 32, 12, 23, 33,
    });

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv444P8));
      Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
        11, 12, 1, 2,
        150, 151, 130, 131,
        160, 161, 140, 141,
      }));
      Assert.That(frame.ColorInfo?.Range, Is.EqualTo(RawColorRange.Full));
      Assert.That(frame.ColorInfo?.Matrix, Is.EqualTo(RawMatrixCoefficients.Bt601));
    });
  }

  [Test]
  [Category("Unit")]
  public void DecodesYuv422FourPixelGroupsAndBottomFirstRows() {
    var decoder = MszhVideoDecoder.Create(_Stream(4, 2, imageType: 1, compression: 1));
    var packet = new CodedPacket(0, new byte[] {
      1, 2, 3, 4, 2, 3, 12, 13,
      11, 12, 13, 14, 22, 23, 32, 33,
    });

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv422P8));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      11, 12, 13, 14, 1, 2, 3, 4,
      150, 151, 130, 131,
      160, 161, 140, 141,
    }));
  }

  [Test]
  [Category("Unit")]
  public void DecodesYuv411IntoSampleExactRepeated444Chroma() {
    var decoder = MszhVideoDecoder.Create(_Stream(4, 2, imageType: 3, compression: 1));
    var packet = new CodedPacket(0, new byte[] {
      1, 2, 3, 4, 2, 12,
      11, 12, 13, 14, 22, 32,
    });

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv444P8));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      11, 12, 13, 14, 1, 2, 3, 4,
      150, 150, 150, 150, 130, 130, 130, 130,
      160, 160, 160, 160, 140, 140, 140, 140,
    }));
  }

  [Test]
  [Category("Unit")]
  public void DecodesYuv211AsPlanar422() {
    var decoder = MszhVideoDecoder.Create(_Stream(2, 2, imageType: 4, compression: 1));
    var packet = new CodedPacket(0, new byte[] {
      1, 2, 2, 12,
      11, 12, 22, 32,
    });

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv422P8));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      11, 12, 1, 2,
      150, 130,
      160, 140,
    }));
  }

  [Test]
  [Category("Unit")]
  public void DecodesYuv420BottomRowBeforeTopRowInsideEachTwoRowGroup() {
    var decoder = MszhVideoDecoder.Create(_Stream(2, 2, imageType: 5, compression: 1));
    var packet = new CodedPacket(0, new byte[] {
      3, 4, 1, 2, 2, 12,
    });

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv420P8));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      1, 2, 3, 4,
      130,
      140,
    }));
  }

  [Test]
  [Category("Unit")]
  public void EncoderWritesEachUncompressedYuvWireLayoutExactly() {
    var cases = new[] {
      new {
        Width = 2, Height = 2, ImageType = (byte)0, Format = PixelFormat.Yuv444P8,
        Pixels = new byte[] { 11, 12, 1, 2, 150, 151, 130, 131, 160, 161, 140, 141 },
        Wire = new byte[] { 1, 2, 12, 2, 3, 13, 11, 22, 32, 12, 23, 33 },
      },
      new {
        Width = 4, Height = 2, ImageType = (byte)1, Format = PixelFormat.Yuv422P8,
        Pixels = new byte[] { 11, 12, 13, 14, 1, 2, 3, 4, 150, 151, 130, 131, 160, 161, 140, 141 },
        Wire = new byte[] { 1, 2, 3, 4, 2, 3, 12, 13, 11, 12, 13, 14, 22, 23, 32, 33 },
      },
      new {
        Width = 4, Height = 2, ImageType = (byte)3, Format = PixelFormat.Yuv444P8,
        Pixels = new byte[] {
          11, 12, 13, 14, 1, 2, 3, 4,
          150, 150, 150, 150, 130, 130, 130, 130,
          160, 160, 160, 160, 140, 140, 140, 140,
        },
        Wire = new byte[] { 1, 2, 3, 4, 2, 12, 11, 12, 13, 14, 22, 32 },
      },
      new {
        Width = 2, Height = 2, ImageType = (byte)4, Format = PixelFormat.Yuv422P8,
        Pixels = new byte[] { 11, 12, 1, 2, 150, 130, 160, 140 },
        Wire = new byte[] { 1, 2, 2, 12, 11, 12, 22, 32 },
      },
      new {
        Width = 2, Height = 2, ImageType = (byte)5, Format = PixelFormat.Yuv420P8,
        Pixels = new byte[] { 1, 2, 3, 4, 130, 140 },
        Wire = new byte[] { 3, 4, 1, 2, 2, 12 },
      },
    };

    foreach (var test in cases) {
      var encoder = MszhVideoEncoder.Create(_Stream(test.Width, test.Height, test.ImageType, compression: 1));
      var frame = new RawImage {
        Width = test.Width,
        Height = test.Height,
        Format = test.Format,
        PixelData = test.Pixels,
      };

      Assert.That(encoder.TryEncode(frame, 17, out var packet), Is.True, $"image type {test.ImageType}");
      Assert.That(packet.Data.ToArray(), Is.EqualTo(test.Wire), $"image type {test.ImageType}");
      Assert.That(packet.IsKeyFrame, Is.True, $"image type {test.ImageType}");

      var decoder = MszhVideoDecoder.Create(encoder.DescribeStream());
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True, $"image type {test.ImageType}");
      Assert.That(decoded.Format, Is.EqualTo(test.Format), $"image type {test.ImageType}");
      Assert.That(decoded.PixelData, Is.EqualTo(test.Pixels), $"image type {test.ImageType}");
    }
  }

  [Test]
  [Category("Unit")]
  public void CompressedYuv420RoundTripsThroughMszhCommands() {
    const int width = 8;
    const int height = 2;
    var samples = width * height;
    var chroma = samples / 4;
    var pixels = new byte[samples + chroma * 2];
    pixels.AsSpan(samples, chroma * 2).Fill(128);
    var frame = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = pixels,
    };
    var encoder = MszhVideoEncoder.Create(_Stream(width, height, imageType: 5, compression: 0));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    Assert.That(packet.Data.Length, Is.LessThan(width * height * 3 / 2));

    var decoder = MszhVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void CompressedYuv111UsesRawFallbackWhenItsByteCountCannotFormFourByteCommands() {
    var encoder = MszhVideoEncoder.Create(_Stream(1, 1, imageType: 0, compression: 0));
    var frame = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Yuv444P8,
      PixelData = [77, 130, 140],
    };

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 77, 2, 12 }));

    var decoder = MszhVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void EncoderWritesAndDecoderReadsTheTwoSectionForm() {
    const int width = 4;
    const int height = 2;
    var frame = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv422P8,
      PixelData = new byte[] {
        1, 2, 3, 4, 1, 2, 3, 4,
        128, 128, 128, 128,
        128, 128, 128, 128,
      },
    };
    var encoder = MszhVideoEncoder.Create(_Stream(width, height, imageType: 1, compression: 0, flags: 0x07));

    Assert.That(encoder.TryEncode(frame, 3, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(packet.Data.Span[4..]), Is.EqualTo(8u));
      Assert.That(encoder.DescribeStream().CodecPrivateData.Span[46], Is.EqualTo(0x01), "only the MSZH split flag is emitted");
    });

    var decoder = MszhVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Yuv411WriterRefusesChromaThatWouldNeedResampling() {
    var encoder = MszhVideoEncoder.Create(_Stream(4, 1, imageType: 3, compression: 1));
    var frame = new RawImage {
      Width = 4,
      Height = 1,
      Format = PixelFormat.Yuv444P8,
      PixelData = new byte[] {
        1, 2, 3, 4,
        128, 129, 128, 128,
        128, 128, 128, 128,
      },
    };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(frame, 0, out _));
    Assert.That(failure!.Message, Does.Contain("lossy resampling"));
  }

  [Test]
  [Category("Unit")]
  public void CompressedYuv420GeometryWhosePayloadIsNotWholeFourByteGroupsRefuses() {
    var failure = Assert.Throws<NotSupportedException>(
      () => MszhVideoEncoder.Create(_Stream(2, 2, imageType: 5, compression: 0)));
    Assert.That(failure!.Message, Does.Contain("four-byte command groups"));

    Assert.DoesNotThrow(() => MszhVideoEncoder.Create(_Stream(2, 2, imageType: 5, compression: 1)));
  }

  [Test]
  [Category("Unit")]
  public void OddSubsampledDimensionsRefuseBeforeAnyPacketIsRead() {
    Assert.Throws<InvalidDataException>(() => MszhVideoDecoder.Create(_Stream(3, 2, imageType: 4, compression: 1)));
    Assert.Throws<InvalidDataException>(() => MszhVideoDecoder.Create(_Stream(4, 3, imageType: 5, compression: 1)));
  }

  private static MediaStreamInfo _Stream(
    int width,
    int height,
    byte imageType,
    sbyte compression,
    byte flags = 0
  ) {
    var format = new byte[48];
    BinaryPrimitives.WriteInt32LittleEndian(format, 40);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), height);
    BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(14), 24);
    "MSZH"u8.CopyTo(format.AsSpan(16));
    format[40] = 4;
    format[44] = imageType;
    format[45] = unchecked((byte)compression);
    format[46] = flags;
    format[47] = 1;

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MSZH"),
      Width = width,
      Height = height,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      CodecPrivateData = format,
    };
  }
}
