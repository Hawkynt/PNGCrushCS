using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;
using FileFormat.FlicVideo;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class FlicDtaTests {

  [Test]
  [Category("Unit")]
  public void DtaBrunDecodesRgb565Pixels() {
    // One row, one packet, repeat RGB565 word 0x1234 twice.
    var decoder = FlicVideoDecoder.Create(_Stream(2, 1, 16));
    var chunk = _Chunk(FliChunkType.DTA_BRUN, [1, 2, 0x34, 0x12]);

    Assert.That(decoder.TryDecode(new CodedPacket(0, chunk), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb565));
      Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0x34, 0x12, 0x34, 0x12 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void DtaDeltaReferencesOnlyThePreviousCanvas() {
    var decoder = FlicVideoDecoder.Create(_Stream(3, 2, 24));
    var first = _Chunk(FliChunkType.DTA_COPY, [
      1, 2, 3, 4, 5, 6, 7, 8, 9,
      10, 11, 12, 13, 14, 15, 16, 17, 18,
    ]);
    // one changed line; skip row 0, one packet; skip one pixel, copy one BGR pixel
    var delta = _Chunk(FliChunkType.DTA_LC, [
      1, 0,
      0xFF, 0xFF,
      1, 0,
      1, 1, 90, 91, 92,
    ]);

    Assert.That(decoder.TryDecode(new CodedPacket(0, first), out _), Is.True);
    Assert.That(decoder.TryDecode(new CodedPacket(0, delta), out var frame), Is.True);

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      1, 2, 3, 4, 5, 6, 7, 8, 9,
      10, 11, 12, 90, 91, 92, 16, 17, 18,
    }));
  }

  [Test]
  [Category("Unit")]
  public void Rgb555IsWidenedWithoutChangingItsCodedReferenceCanvas() {
    var decoder = FlicVideoDecoder.Create(_Stream(2, 1, 15));
    // red max, then blue max
    var copy = _Chunk(FliChunkType.DTA_COPY, [0x00, 0x7C, 0x1F, 0x00]);

    Assert.That(decoder.TryDecode(new CodedPacket(0, copy), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 255, 0, 0, 0, 0, 255 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void EightBitEncoderUsesSs2ForASmallSecondFrameChange() {
    var first = _Indexed(64, 2, 0);
    for (var i = 0; i < first.PixelData.Length; ++i)
      first.PixelData[i] = (byte)(i * 37 + 11);
    var second = _Indexed(64, 2, 0);
    first.PixelData.CopyTo(second.PixelData, 0);
    second.PixelData[70] ^= 0x5A;
    var encoder = FlicVideoEncoder.Create(_Stream(64, 2, 8));

    Assert.That(encoder.TryEncode(first, 0, out var key), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var delta), Is.True);

    Assert.Multiple(() => {
      Assert.That(key.IsKeyFrame, Is.True);
      Assert.That(delta.IsKeyFrame, Is.False);
      Assert.That(_ChunkTypes(delta.Data.Span), Does.Contain(FliChunkType.SS2));
    });

    var decoder = FlicVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(key, out _), Is.True);
    Assert.That(decoder.TryDecode(delta, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(second.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Rgb565EncoderUsesDtaLcAndRoundTrips() {
    const int width = 80;
    var first = new RawImage { Width = width, Height = 2, Format = PixelFormat.Rgb565, PixelData = new byte[width * 4] };
    for (var pixel = 0; pixel < width * 2; ++pixel)
      BinaryPrimitives.WriteUInt16LittleEndian(first.PixelData.AsSpan(pixel * 2), checked((ushort)(0x1000 + pixel)));
    var second = new RawImage { Width = width, Height = 2, Format = PixelFormat.Rgb565, PixelData = (byte[])first.PixelData.Clone() };
    BinaryPrimitives.WriteUInt16LittleEndian(second.PixelData.AsSpan((width + 11) * 2), 0x5678);
    var encoder = FlicVideoEncoder.Create(_Stream(width, 2, 16));
    var decoder = FlicVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(first, 0, out var key), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var delta), Is.True);
    Assert.That(_ChunkTypes(key.Data.Span), Does.Contain(FliChunkType.DTA_BRUN));
    Assert.That(_ChunkTypes(delta.Data.Span), Does.Contain(FliChunkType.DTA_LC));
    Assert.That(delta.IsKeyFrame, Is.False);

    Assert.That(decoder.TryDecode(key, out _), Is.True);
    Assert.That(decoder.TryDecode(delta, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(second.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void TrueColourMuxUsesTheDtaMagicAndCarriesDepth() {
    var encoder = FlicVideoEncoder.Create(_Stream(3, 2, 24));
    var frame = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Bgr24,
      PixelData = Enumerable.Range(0, 18).Select(i => (byte)(i + 1)).ToArray(),
    };
    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var bytes = VideoIO.Mux<FliWriter>([encoder.DescribeStream()], [packet]);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)), Is.EqualTo(FliReader.MAGIC_DTA));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12)), Is.EqualTo(24));
    });

    var container = FliContainer.FromBytes(bytes);
    Assert.That(FliContainer.Streams(container)[0].BitsPerPixel, Is.EqualTo(24));
    var decoded = VideoFormatRegistry.DecodeFrames(bytes).Single().Image;
    Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FifteenBitWriterRefusesColoursThatWouldNeedQuantisation() {
    var encoder = FlicVideoEncoder.Create(_Stream(1, 1, 15));
    var exact = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = [255, 0, 0] };
    var inexact = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = [254, 0, 0] };

    Assert.That(encoder.TryEncode(exact, 0, out _), Is.True);
    Assert.That(Assert.Throws<NotSupportedException>(() => encoder.TryEncode(inexact, 1, out _))!.Message, Does.Contain("RGB555"));
  }

  [Test]
  [Category("Unit")]
  public void UnsupportedDepthStillRefusesByName() {
    Assert.That(
      Assert.Throws<NotSupportedException>(() => FlicVideoDecoder.Create(_Stream(4, 4, 32)))!.Message,
      Does.Contain("32 bits per pixel"));
    Assert.That(
      Assert.Throws<NotSupportedException>(() => FlicVideoEncoder.Create(_Stream(4, 4, 32)))!.Message,
      Does.Contain("32"));
  }

  private static MediaStreamInfo _Stream(int width, int height, int depth) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("FLIC"),
    Handler = CodecTag.FromCharacters("FLIC"),
    Width = width,
    Height = height,
    BitsPerPixel = depth,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Indexed(int width, int height, byte value) {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)i;
      palette[i * 3 + 2] = (byte)i;
    }
    var pixels = new byte[width * height];
    Array.Fill(pixels, value);
    return new() { Width = width, Height = height, Format = PixelFormat.Indexed8, PixelData = pixels, Palette = palette, PaletteCount = 256 };
  }

  private static byte[] _Chunk(ushort type, IReadOnlyList<byte> payload) {
    var result = new byte[6 + payload.Count];
    BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)result.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), type);
    for (var i = 0; i < payload.Count; ++i) result[6 + i] = payload[i];
    return result;
  }

  private static ushort[] _ChunkTypes(ReadOnlySpan<byte> data) {
    var result = new List<ushort>();
    for (var at = 0; at < data.Length;) {
      var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]));
      result.Add(BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]));
      at += size;
    }
    return result.ToArray();
  }
}
