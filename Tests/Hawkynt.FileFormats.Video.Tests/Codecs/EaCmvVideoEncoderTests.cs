using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Ea;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Electronic Arts CMV encoding checked through the decoder, EA container and FFmpeg.</summary>
[TestFixture]
public sealed class EaCmvVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void DescribesARegisteredEightBitStreamTheDecoderAccepts() {
    var requested = _Requested(8, 8, index: 2);
    var encoder = EaCmvVideoEncoder.Create(requested);
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Index, Is.EqualTo(2));
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("cmv ")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("cmv ")));
      Assert.That(stream.Width, Is.EqualTo(8));
      Assert.That(stream.Height, Is.EqualTo(8));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
      Assert.That(VideoFormatRegistry.AllEncoders.Select(static e => e.CodecName), Does.Contain("Electronic Arts CMV"));
      Assert.That(VideoFormatRegistry.CanEncode(requested), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(requested), Is.InstanceOf<EaCmvVideoEncoder>());
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<EaCmvVideoDecoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void FirstPictureCarriesACompleteHeaderAndIntraRaster() {
    var picture = _Picture(8, 4, 7);
    var encoder = EaCmvVideoEncoder.Create(_Requested(8, 4));

    encoder.TryEncode(picture, 3, out var packet);
    var chunks = _Chunks(packet.Data.Span);

    Assert.Multiple(() => {
      Assert.That(chunks.Select(static c => c.FourCc), Is.EqualTo(new[] { "MVIh", "MVIf" }));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(chunks[0].Payload.AsSpan(4)), Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(chunks[0].Payload.AsSpan(6)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(chunks[0].Payload.AsSpan(10)), Is.EqualTo(15));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(chunks[0].Payload.AsSpan(12)), Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(chunks[0].Payload.AsSpan(14)), Is.EqualTo(256));
      Assert.That(chunks[0].Payload.AsSpan(0x10).ToArray(), Is.EqualTo(picture.Palette));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(chunks[1].Payload), Is.Zero);
      Assert.That(chunks[1].Payload.AsSpan(2).ToArray(), Is.EqualTo(picture.PixelData));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(3));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void InterPicturesUsePreviousSecondLastAndLiteralBlocks() {
    var first = _Picture(8, 4, 3);
    var encoder = EaCmvVideoEncoder.Create(_Requested(8, 4));
    encoder.TryEncode(first, 0, out _);

    encoder.TryEncode(first, 1, out var unchangedPacket);
    var unchanged = _Chunks(unchangedPacket.Data.Span).Single();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(unchanged.Payload), Is.EqualTo(1));
      Assert.That(unchanged.Payload.AsSpan(2).ToArray(), Is.EqualTo(new byte[] { 0x77, 0x77 }));
    });

    var other = _Picture(8, 4, 4);
    encoder.TryEncode(other, 2, out _); // now history is first, other
    encoder.TryEncode(first, 3, out var secondLastPacket);
    var secondLast = _Chunks(secondLastPacket.Data.Span).Single();
    Assert.Multiple(() => {
      Assert.That(secondLast.Payload.AsSpan(2, 2).ToArray(), Is.EqualTo(new byte[] { 0xFF, 0xFF }));
      Assert.That(secondLast.Payload.AsSpan(4).ToArray(), Is.EqualTo(new byte[] { 0x77, 0x77 }));
    });

    var changedPixels = first.PixelData.ToArray();
    for (var y = 0; y < 4; ++y)
    for (var x = 4; x < 8; ++x)
      changedPixels[y * 8 + x] = (byte)(40 + y * 4 + x - 4);
    var changed = _Picture(8, 4, changedPixels);

    // Start a fresh history so the first block is a zero-vector copy and the second has no match.
    encoder = EaCmvVideoEncoder.Create(_Requested(8, 4));
    encoder.TryEncode(first, 0, out _);
    encoder.TryEncode(changed, 1, out var literalPacket);
    var literal = _Chunks(literalPacket.Data.Span).Single();
    Assert.Multiple(() => {
      Assert.That(literal.Payload[2], Is.EqualTo(0x77));
      Assert.That(literal.Payload[3], Is.EqualTo(0xFF));
      Assert.That(literal.Payload[4], Is.EqualTo(0xFF));
      Assert.That(literal.Payload.AsSpan(5).ToArray(), Is.EqualTo(new byte[] {
        40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void PaletteChangesRestateOnlyTheChangedSpan() {
    var first = _Picture(4, 4, 0);
    var palette = first.Palette!.ToArray();
    palette[5 * 3 + 1] ^= 0x5A;
    palette[9 * 3 + 2] ^= 0xA5;
    var second = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Indexed8,
      PixelData = first.PixelData.ToArray(),
      Palette = palette,
      PaletteCount = 256,
    };
    var encoder = EaCmvVideoEncoder.Create(_Requested(4, 4));
    encoder.TryEncode(first, 0, out _);
    encoder.TryEncode(second, 1, out var packet);
    var chunks = _Chunks(packet.Data.Span);

    Assert.Multiple(() => {
      Assert.That(chunks.Select(static c => c.FourCc), Is.EqualTo(new[] { "MVIh", "MVIf" }));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(chunks[0].Payload.AsSpan(12)), Is.EqualTo(5));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(chunks[0].Payload.AsSpan(14)), Is.EqualTo(5));
      Assert.That(chunks[0].Payload.AsSpan(0x10).ToArray(), Is.EqualTo(palette.AsSpan(15, 15).ToArray()));
    });
  }

  [Test]
  [Category("Unit")]
  public void SequenceRoundTripsThroughCodecAndEaContainer() {
    var pictures = new[] { _Picture(8, 8, 1), _Picture(8, 8, 2), _Picture(8, 8, 1) };
    var encoder = EaCmvVideoEncoder.Create(_Requested(8, 8));
    var decoder = EaCmvVideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();

    for (var i = 0; i < pictures.Length; ++i) {
      encoder.TryEncode(pictures[i], i, out var packet);
      packets.Add(packet);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      _AssertSame(pictures[i], decoded, $"codec frame {i}");
    }

    packets.AddRange(encoder.Flush());
    var file = VideoIO.Mux<EaWriter>([encoder.DescribeStream()], packets);
    var container = EaContainer.FromBytes(file);
    var decodedFrames = VideoFormatRegistry.DecodeFrames(file).Select(static frame => frame.Image).ToArray();

    Assert.Multiple(() => {
      Assert.That(container.VideoFrameCount, Is.EqualTo(3));
      Assert.That(container.Width, Is.EqualTo(8));
      Assert.That(container.Height, Is.EqualTo(8));
      Assert.That(container.FrameRate, Is.EqualTo(15));
      Assert.That(decodedFrames, Has.Length.EqualTo(3));
    });
    for (var i = 0; i < pictures.Length; ++i)
      _AssertSame(pictures[i], decodedFrames[i], $"container frame {i}");
  }

  [Test]
  [Category("Unit")]
  public void OddDimensionsStayIntraAndFlushEndsTheRunOnce() {
    var picture = _Picture(5, 3, 7);
    var encoder = EaCmvVideoEncoder.Create(_Requested(5, 3));
    encoder.TryEncode(picture, 0, out _);
    encoder.TryEncode(picture, 1, out var packet);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(_Chunks(packet.Data.Span).Single().Payload), Is.Zero);
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(_Chunks(encoder.Flush().Single().Data.Span).Single().FourCc, Is.EqualTo("MVIe"));
      Assert.That(encoder.Flush(), Is.Empty);
      Assert.Throws<InvalidOperationException>(() => encoder.TryEncode(picture, 2, out _));
    });
  }

  [Test]
  [Category("Unit")]
  public void UnsupportedInputsAreRefusedRatherThanQuantised() {
    Assert.Throws<NotSupportedException>(() => EaCmvVideoEncoder.Create(_Requested(0, 4)));
    Assert.Throws<NotSupportedException>(() => EaCmvVideoEncoder.Create(_Requested(4, 4, bitsPerPixel: 16)));
    Assert.Throws<NotSupportedException>(() => EaCmvVideoEncoder.Create(_Requested(4, 4, kind: MediaStreamKind.Audio)));
    Assert.Throws<NotSupportedException>(() => EaCmvVideoEncoder.Create(_Requested(4, 4, frameRate: new Rational(30000, 1001))));

    var encoder = EaCmvVideoEncoder.Create(_Requested(4, 4));
    var rgb = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Rgb24, PixelData = new byte[48] };
    Assert.Throws<NotSupportedException>(() => encoder.TryEncode(rgb, 0, out _));

    var picture = _Picture(4, 4, 0);
    var shortData = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[15],
      Palette = picture.Palette,
      PaletteCount = 256,
    };
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(shortData, 0, out _));

    var invalidIndex = _Picture(4, 4, 0);
    invalidIndex.PixelData[0] = 2;
    var shortPalette = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Indexed8,
      PixelData = invalidIndex.PixelData,
      Palette = invalidIndex.Palette![..6],
      PaletteCount = 2,
    };
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(shortPalette, 0, out _));
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsACmvWrittenHere() {
    FFmpegOracle.RequireAvailable();

    var encoder = EaCmvVideoEncoder.Create(_Requested(8, 8));
    var packets = new List<CodedPacket>();
    encoder.TryEncode(_Picture(8, 8, 3), 0, out var first);
    encoder.TryEncode(_Picture(8, 8, 3), 1, out var second);
    packets.Add(first);
    packets.Add(second);
    packets.AddRange(encoder.Flush());
    var file = VideoIO.Mux<EaWriter>([encoder.DescribeStream()], packets);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".cmv");

    try {
      File.WriteAllBytes(path, file);
      var (decoded, detail) = FFmpegOracle.TryDecodeFirstFrame(path, 8, 8);
      Assert.That(decoded, Is.True, detail);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  private static MediaStreamInfo _Requested(
    int width,
    int height,
    int bitsPerPixel = 0,
    int index = 0,
    MediaStreamKind kind = MediaStreamKind.Video,
    Rational? frameRate = null) => new() {
    Index = index,
    Kind = kind,
    Codec = CodecTag.FromCharacters("cmv "),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 15),
    FrameRate = frameRate ?? new Rational(15, 1),
  };

  private static RawImage _Picture(int width, int height, byte value) {
    var pixels = new byte[width * height];
    Array.Fill(pixels, value);
    return _Picture(width, height, pixels);
  }

  private static RawImage _Picture(int width, int height, byte[] pixels) {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i ^ 0x5A);
    }
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 256,
    };
  }

  private static void _AssertSame(RawImage expected, RawImage actual, string because) {
    Assert.Multiple(() => {
      Assert.That(actual.Width, Is.EqualTo(expected.Width), because);
      Assert.That(actual.Height, Is.EqualTo(expected.Height), because);
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Indexed8), because);
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData), because);
      Assert.That(actual.Palette, Is.EqualTo(expected.Palette), because);
    });
  }

  private static (string FourCc, byte[] Payload)[] _Chunks(ReadOnlySpan<byte> data) {
    var result = new List<(string FourCc, byte[] Payload)>();
    for (var at = 0; at < data.Length;) {
      var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]));
      result.Add((
        System.Text.Encoding.ASCII.GetString(data.Slice(at, 4)),
        data.Slice(at + 8, length - 8).ToArray()));
      at += length;
    }
    return result.ToArray();
  }
}
