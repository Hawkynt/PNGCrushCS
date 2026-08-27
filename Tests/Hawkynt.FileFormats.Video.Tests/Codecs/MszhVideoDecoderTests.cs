using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// MSZH's LGPL-reference-derived coding: four-byte literals, overlapping backward copies, LCL's
/// two-section packet form, raw-frame fallback, and RGB24 row packing/orientation.
/// </summary>
[TestFixture]
public sealed class MszhVideoDecoderTests {

  [Test]
  [Category("Unit")]
  public void TheMszhCodeIsTaken()
    => Assert.That(MszhVideoDecoder.Accepts(_Stream("MSZH")), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken()
    => Assert.That(MszhVideoDecoder.Accepts(_Stream("ZLIB")), Is.False);

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _StreamWithFormat(2, 2, compression: 0);

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("LCL MSZH"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<MszhVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void LiteralCommandsCopyFourBytesAndRowsAreReturnedTopFirstWithoutPadding() {
    // 2x2 RGB24 has six useful bytes per row and an eight-byte coded stride. Four literal commands
    // reconstruct sixteen bytes: bottom row + padding first, then top row + padding.
    var decoder = MszhVideoDecoder.Create(_StreamWithFormat(2, 2, compression: 0));
    var payload = new byte[] {
      0x00,
      1, 2, 3, 4, 5, 6, 0xA0, 0xA1,
      11, 12, 13, 14, 15, 16, 0xB0, 0xB1,
    };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      11, 12, 13, 14, 15, 16,
      1, 2, 3, 4, 5, 6,
    }));
  }

  [Test]
  [Category("Unit")]
  public void ABackReferenceMayOverlapBytesItIsCurrentlyWriting() {
    var decoder = MszhVideoDecoder.Create(_StreamWithFormat(2, 2, compression: 0));

    // First mask bit zero: literal 1,2,3,4. Second mask bit one: descriptor 0x1004 has distance 4
    // and (2 + 1) four-byte groups, so the twelve copied bytes repeatedly walk through the four bytes
    // just reconstructed and fill the sixteen-byte frame.
    var payload = new byte[] { 0x40, 1, 2, 3, 4, 0x04, 0x10 };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      1, 2, 3, 4, 1, 2,
      1, 2, 3, 4, 1, 2,
    }));
  }

  [Test]
  [Category("Unit")]
  public void AZeroDistanceBackReferenceProducesZeroBytes() {
    var decoder = MszhVideoDecoder.Create(_StreamWithFormat(2, 2, compression: 0));

    // First command is a back-reference before anything exists. Descriptor 0x3800 requests all
    // sixteen decoded bytes (8 groups would be 32 bytes, clamped to the frame), with distance zero.
    var payload = new byte[] { 0x80, 0x00, 0x38 };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[12]));
  }

  [Test]
  [Category("Unit")]
  public void MultithreadFlagDecodesTwoIndependentEqualOutputSections() {
    var decoder = MszhVideoDecoder.Create(_StreamWithFormat(2, 2, compression: 0, flags: 0x01));
    var first = new byte[] { 0x00, 1, 2, 3, 4, 5, 6, 0xA0, 0xA1 };
    var second = new byte[] { 0x00, 11, 12, 13, 14, 15, 16, 0xB0, 0xB1 };
    var payload = new byte[8 + first.Length + second.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)first.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 8);
    first.CopyTo(payload, 8);
    second.CopyTo(payload, 8 + first.Length);

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      11, 12, 13, 14, 15, 16,
      1, 2, 3, 4, 5, 6,
    }));
  }

  [Test]
  [Category("Unit")]
  public void CompressedModeStillAcceptsACompleteRawPaddedFrame() {
    var decoder = MszhVideoDecoder.Create(_StreamWithFormat(2, 2, compression: 0));
    var payload = new byte[] {
      1, 2, 3, 4, 5, 6, 0xA0, 0xA1,
      11, 12, 13, 14, 15, 16, 0xB0, 0xB1,
    };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      11, 12, 13, 14, 15, 16,
      1, 2, 3, 4, 5, 6,
    }));
  }

  [Test]
  [Category("Unit")]
  public void ExplicitUncompressedModeAcceptsPackedRowsToo() {
    var decoder = MszhVideoDecoder.Create(_StreamWithFormat(2, 2, compression: 1));
    var payload = new byte[] {
      1, 2, 3, 4, 5, 6,
      11, 12, 13, 14, 15, 16,
    };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      11, 12, 13, 14, 15, 16,
      1, 2, 3, 4, 5, 6,
    }));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedLiteralRefuses() {
    var decoder = MszhVideoDecoder.Create(_StreamWithFormat(2, 2, compression: 0));
    var payload = new byte[] { 0x00, 1, 2, 3 };

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, payload), out _));
  }

  [Test]
  [Category("Unit")]
  public void SplitPacketWhoseDeclaredHalvesDoNotCoverTheFrameRefuses() {
    var decoder = MszhVideoDecoder.Create(_StreamWithFormat(2, 2, compression: 0, flags: 0x01));
    var payload = new byte[10];
    BinaryPrimitives.WriteUInt32LittleEndian(payload, 1);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 4); // two halves would cover only 8 of 16 bytes

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, payload), out _));
  }

  [Test]
  [Category("Unit")]
  public void NonRgbLclImageTypeRefuses() {
    var stream = _StreamWithFormat(2, 2, compression: 0, imageType: 5);
    Assert.Throws<NotSupportedException>(() => MszhVideoDecoder.Create(stream));
  }

  private static MediaStreamInfo _Stream(string tag) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(tag),
  };

  private static MediaStreamInfo _StreamWithFormat(
    int width,
    int height,
    sbyte compression,
    byte flags = 0,
    byte imageType = 2
  ) {
    var format = new byte[48];
    BinaryPrimitives.WriteUInt32LittleEndian(format, 40);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), height);
    BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(14), 24);
    format[44] = imageType;
    format[45] = unchecked((byte)compression);
    format[46] = flags;
    format[47] = 1; // CODEC_MSZH

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MSZH"),
      Width = width,
      Height = height,
      CodecPrivateData = format,
    };
  }
}
