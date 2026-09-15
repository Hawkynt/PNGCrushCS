using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.Lcl.Tests;

/// <summary>
/// Small hand-checkable LCL ZLIB vectors covering every image type, both wrapper forms, the historical
/// raw-RGB exception and the format's delta predictor. The expected samples are stated directly rather
/// than copied from a third-party implementation.
/// </summary>
[TestFixture]
public class LclZlibVideoDecoderTests {

  private static readonly CodecTag _Zlib = CodecTag.FromCharacters("ZLIB");

  private static byte[] _PrivateData(
    int width,
    int height,
    byte imageType = 2,
    sbyte compression = 6,
    byte flags = 0,
    byte codec = 3
  ) {
    var data = new byte[40 + 8];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), height);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), 24);
    "ZLIB"u8.CopyTo(data.AsSpan(16));

    data[40] = 4;
    data[44] = imageType;
    data[45] = unchecked((byte)compression);
    data[46] = flags;
    data[47] = codec;
    return data;
  }

  private static MediaStreamInfo _Stream(
    int width,
    int height,
    byte[]? privateData = null,
    CodecTag? codec = null,
    MediaStreamKind kind = MediaStreamKind.Video
  ) => new() {
    Index = 0,
    Kind = kind,
    Codec = codec ?? _Zlib,
    Width = width,
    Height = height,
    CodecPrivateData = privateData ?? _PrivateData(width, height),
  };

  private static byte[] _Zlib(ReadOnlySpan<byte> raw) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
      zlib.Write(raw);
    return output.ToArray();
  }

  private static byte[] _SplitPacket(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) {
    var firstCompressed = _Zlib(first);
    var secondCompressed = _Zlib(second);
    var result = new byte[8 + firstCompressed.Length + secondCompressed.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)firstCompressed.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)first.Length);
    firstCompressed.CopyTo(result.AsSpan(8));
    secondCompressed.CopyTo(result.AsSpan(8 + firstCompressed.Length));
    return result;
  }

  private static RawImage _Decode(int width, int height, byte imageType, ReadOnlySpan<byte> coded, byte flags = 0) {
    var decoder = LclZlibVideoDecoder.Create(
      _Stream(width, height, _PrivateData(width, height, imageType: imageType, flags: flags)));
    Assert.That(decoder.TryDecode(new CodedPacket(0, _Zlib(coded)), out var frame), Is.True);
    return frame;
  }

  private static void _AssertPlanes(RawImage frame, byte[] y, byte[] u, byte[] v) {
    Assert.Multiple(() => {
      Assert.That(frame.GetPlaneData(0).ToArray(), Is.EqualTo(y), "Y plane");
      Assert.That(frame.GetPlaneData(1).ToArray(), Is.EqualTo(u), "U plane");
      Assert.That(frame.GetPlaneData(2).ToArray(), Is.EqualTo(v), "V plane");
      Assert.That(frame.ColorInfo?.Range, Is.EqualTo(RawColorRange.Full));
      Assert.That(frame.ColorInfo?.Matrix, Is.EqualTo(RawMatrixCoefficients.Bt601));
    });
  }

  [Test]
  [Category("Unit")]
  public void AcceptsTheZlibTag() {
    Assert.That(LclZlibVideoDecoder.Accepts(_Stream(16, 16)), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(
      LclZlibVideoDecoder.Accepts(_Stream(16, 16, codec: CodecTag.FromCharacters("MSZH"))),
      Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnAudioStream() {
    Assert.That(LclZlibVideoDecoder.Accepts(_Stream(16, 16, kind: MediaStreamKind.Audio)), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => LclZlibVideoDecoder.Create(_Stream(0, 16)));
    Assert.That(failure!.Message, Does.Contain("0x16"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesPrivateDataTooShortForTheTrailer() {
    var failure = Assert.Throws<InvalidDataException>(() => LclZlibVideoDecoder.Create(_Stream(16, 16, privateData: new byte[40])));
    Assert.That(failure!.Message, Does.Contain("40 byte(s)"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnUnknownImageType() {
    var failure = Assert.Throws<NotSupportedException>(() =>
      LclZlibVideoDecoder.Create(_Stream(16, 16, _PrivateData(16, 16, imageType: 6))));
    Assert.That(failure!.Message, Does.Contain("image type 6"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesTheSiblingCodecMarker() {
    var failure = Assert.Throws<InvalidDataException>(() =>
      LclZlibVideoDecoder.Create(_Stream(16, 16, _PrivateData(16, 16, codec: 1))));
    Assert.That(failure!.Message, Does.Contain("not ZLIB"));
  }

  [Test]
  [Category("Unit")]
  [TestCase(-2)]
  [TestCase(10)]
  public void RefusesAnInvalidCompressionLevel(int compression) {
    var failure = Assert.Throws<NotSupportedException>(() =>
      LclZlibVideoDecoder.Create(_Stream(16, 16, _PrivateData(16, 16, compression: (sbyte)compression))));
    Assert.That(failure!.Message, Does.Contain("compression level"));
  }

  [Test]
  [Category("Unit")]
  public void UsesValueFourForThePngFilterFlag() {
    var frame = _Decode(
      2,
      1,
      imageType: 2,
      coded: new byte[] { 10, 20, 30, 1, 1, 255 },
      flags: 0x04);

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 9, 19, 31 }));
  }

  [Test]
  [Category("Unit")]
  public void IgnoresAnUnknownFlagInsteadOfMistakingItForThePngFilter() {
    var frame = _Decode(1, 1, imageType: 2, coded: new byte[] { 3, 4, 5 }, flags: 0x08);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 3, 4, 5 }));
  }

  [Test]
  [Category("Unit")]
  public void RefusesOddDimensionsWhereThePackingCannotRepresentThem() {
    Assert.Multiple(() => {
      Assert.Throws<NotSupportedException>(() =>
        LclZlibVideoDecoder.Create(_Stream(3, 2, _PrivateData(3, 2, imageType: 4))));
      Assert.Throws<NotSupportedException>(() =>
        LclZlibVideoDecoder.Create(_Stream(2, 3, _PrivateData(2, 3, imageType: 5))));
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesDimensionsWhoseSizeArithmeticOverflows() {
    var failure = Assert.Throws<InvalidDataException>(() =>
      LclZlibVideoDecoder.Create(_Stream(int.MaxValue, int.MaxValue, _PrivateData(int.MaxValue, int.MaxValue, imageType: 0))));
    Assert.That(failure!.Message, Does.Contain("arithmetic overflows"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesDimensionsWhoseCanonicalFrameCannotFitManagedMemory() {
    const int height = 400_000_000;
    var failure = Assert.Throws<InvalidDataException>(() =>
      LclZlibVideoDecoder.Create(_Stream(3, height, _PrivateData(3, height, imageType: 1))));
    Assert.That(failure!.Message, Does.Contain("too large"));
  }

  [Test]
  [Category("Unit")]
  public void APaddedRgbRowIsUnpackedToItsExactPixelCount() {
    var decoder = LclZlibVideoDecoder.Create(_Stream(3, 2));
    var coded = new byte[] {
      1, 2, 3, 4, 5, 6, 7, 8, 9, 0xff, 0xff, 0xff,
      10, 11, 12, 13, 14, 15, 16, 17, 18, 0xaa, 0xaa, 0xaa,
    };

    Assert.That(decoder.TryDecode(new CodedPacket(0, _Zlib(coded)), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
      Assert.That(frame.PixelData[..9], Is.EqualTo(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18 }));
      Assert.That(frame.PixelData[9..], Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnUnpaddedRgbRowIsReadAsTightlyPacked() {
    var decoder = LclZlibVideoDecoder.Create(_Stream(3, 2));
    var bottom = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    var top = new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18 };
    var coded = bottom.Concat(top).ToArray();

    Assert.That(decoder.TryDecode(new CodedPacket(0, _Zlib(coded)), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.PixelData[..9], Is.EqualTo(top));
      Assert.That(frame.PixelData[9..], Is.EqualTo(bottom));
    });
  }

  [Test]
  [Category("Unit")]
  public void AShortValidZlibFrameIsZeroFilledLikeTheReferenceDecoder() {
    var decoder = LclZlibVideoDecoder.Create(_Stream(4, 2));

    Assert.That(decoder.TryDecode(new CodedPacket(0, _Zlib(new byte[] { 1, 2, 3, 4 })), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      1, 2, 3, 4, 0, 0, 0, 0, 0, 0, 0, 0,
    }));
  }

  [Test]
  [Category("Unit")]
  public void RefusesZlibOutputBeyondTheDeclaredFrameCapacity() {
    var decoder = LclZlibVideoDecoder.Create(_Stream(1, 1));
    var failure = Assert.Throws<InvalidDataException>(() =>
      decoder.TryDecode(new CodedPacket(0, _Zlib(new byte[] { 1, 2, 3, 4, 5 })), out _));
    Assert.That(failure!.Message, Does.Contain("expands beyond"));
  }

  [Test]
  [Category("Unit")]
  public void ReadsTheOriginalCodecsRawRgbNormalCompressionException() {
    var decoder = LclZlibVideoDecoder.Create(_Stream(2, 2, _PrivateData(2, 2, compression: -1)));
    var bottom = new byte[] { 1, 2, 3, 4, 5, 6 };
    var top = new byte[] { 11, 12, 13, 14, 15, 16 };
    var coded = bottom.Concat(top).ToArray();

    Assert.That(decoder.TryDecode(new CodedPacket(0, coded), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(top.Concat(bottom).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void ReadsTwoIndependentlyCompressedSections() {
    var decoder = LclZlibVideoDecoder.Create(_Stream(4, 2, _PrivateData(4, 2, flags: 0x01)));
    var bottom = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    var top = new byte[] { 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };

    Assert.That(decoder.TryDecode(new CodedPacket(0, _SplitPacket(bottom, top)), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(top.Concat(bottom).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void RefusesATruncatedSplitHeader() {
    var decoder = LclZlibVideoDecoder.Create(_Stream(4, 2, _PrivateData(4, 2, flags: 0x01)));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new CodedPacket(0, new byte[7]), out _));
    Assert.That(failure!.Message, Does.Contain("eight-byte split header"));
  }

  [Test]
  [Category("Unit")]
  public void DecodesYuv111AsCanonical444() {
    var frame = _Decode(2, 2, 0, new byte[] {
      10, 0, 0, 20, 1, 255,
      30, 2, 254, 40, 3, 253,
    });

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv444P8));
    _AssertPlanes(
      frame,
      y: new byte[] { 30, 40, 10, 20 },
      u: new byte[] { 130, 131, 128, 129 },
      v: new byte[] { 126, 125, 128, 127 });
  }

  [Test]
  [Category("Unit")]
  public void DecodesYuv422InFourPixelGroups() {
    var frame = _Decode(4, 2, 1, new byte[] {
      1, 2, 3, 4, 0, 1, 0, 255,
      5, 6, 7, 8, 2, 3, 254, 253,
    });

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv422P8));
    _AssertPlanes(
      frame,
      y: new byte[] { 5, 6, 7, 8, 1, 2, 3, 4 },
      u: new byte[] { 130, 131, 128, 129 },
      v: new byte[] { 126, 125, 128, 127 });
  }

  [Test]
  [Category("Unit")]
  public void DecodesYuv422PartialHorizontalGroupLikeTheReferenceDecoder() {
    var frame = _Decode(5, 1, 1, new byte[] { 1, 2, 3, 4, 0, 1, 2, 3 });

    _AssertPlanes(
      frame,
      y: new byte[] { 1, 2, 3, 4, 0 },
      u: new byte[] { 128, 129, 129 },
      v: new byte[] { 130, 131, 131 });
  }

  [Test]
  [Category("Unit")]
  public void DecodesYuv411WithoutDiscardingItsNativeSampling() {
    var frame = _Decode(4, 2, 3, new byte[] {
      1, 2, 3, 4, 0, 0,
      5, 6, 7, 8, 1, 255,
    });

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv411P8));
    Assert.That(frame.GetPlaneDimensions(1), Is.EqualTo((1, 2)));
    _AssertPlanes(
      frame,
      y: new byte[] { 5, 6, 7, 8, 1, 2, 3, 4 },
      u: new byte[] { 129, 128 },
      v: new byte[] { 127, 128 });
    Assert.That(frame.ToBgra32(), Has.Length.EqualTo(4 * 2 * 4));
  }

  [Test]
  [Category("Unit")]
  public void DecodesYuv211AsCanonical422() {
    var frame = _Decode(2, 2, 4, new byte[] {
      1, 2, 0, 0,
      3, 4, 1, 255,
    });

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv422P8));
    _AssertPlanes(
      frame,
      y: new byte[] { 3, 4, 1, 2 },
      u: new byte[] { 129, 128 },
      v: new byte[] { 127, 128 });
  }

  [Test]
  [Category("Unit")]
  public void DecodesYuv420InBottomUpTwoRowBlocks() {
    var frame = _Decode(2, 2, 5, new byte[] { 1, 2, 3, 4, 0, 255 });

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv420P8));
    _AssertPlanes(
      frame,
      y: new byte[] { 3, 4, 1, 2 },
      u: new byte[] { 128 },
      v: new byte[] { 127 });
  }

  [Test]
  [Category("Unit")]
  public void AppliesThePredictorToYuv420ComponentsIndependently() {
    var frame = _Decode(2, 2, 5, new byte[] { 255, 255, 253, 255, 251, 250 }, flags: 0x04);

    _AssertPlanes(
      frame,
      y: new byte[] { 3, 4, 1, 2 },
      u: new byte[] { 133 },
      v: new byte[] { 134 });
  }
}
