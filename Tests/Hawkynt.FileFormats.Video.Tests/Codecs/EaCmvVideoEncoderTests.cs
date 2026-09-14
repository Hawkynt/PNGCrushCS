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
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 15)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(15, 1)));
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

    Assert.That(encoder.TryEncode(picture, 3, out var packet), Is.True);
    var chunks = _Chunks(packet.Data.Span).ToArray();

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
  public void AnUnchangedPictureUsesPreviousFrameMotion() {
    var picture = _Picture(8, 4, 11);
    var encoder = EaCmvVideoEncoder.Create(_Requested(8, 4));
    encoder.TryEncode(picture, 0, out _);

    encoder.TryEncode(picture, 1, out var packet);
    var frame = _Chunks(packet.Data.Span).Single();

    Assert.Multiple(() => {
      Assert.That(frame.FourCc, Is.EqualTo("MVIf"));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload), Is.EqualTo(1));
      Assert.That(frame.Payload.AsSpan(2, 2).ToArray(), Is.EqualTo(new byte[] { 0x77, 0x77 }));
      Assert.That(frame.Payload.Length, Is.EqualTo(4));
      Assert.That(packet.IsKeyFrame, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void ABlockMayReachBackTwoPictures() {
    var first = _Picture(8, 4, 1);
    var second = _Picture(8, 4, 2);
    var encoder = EaCmvVideoEncoder.Create(_Requested(8, 4));
    encoder.TryEncode(first, 0, out _);
    encoder.TryEncode(second, 1, out _);

    encoder.TryEncode(first, 2, out var packet);
    var frame = _Chunks(packet.Data.Span).Single();

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload), Is.EqualTo(1));
      Assert.That(frame.Payload.AsSpan(2, 2).ToArray(), Is.EqualTo(new byte[] { 0xFF, 0xFF }));
      Assert.That(frame.Payload.AsSpan(4).ToArray(), Is.EqualTo(new byte[] { 0x77, 0x77 }));
      Assert.That(packet.IsKeyFrame, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void AChangedBlockFallsBackToTheSixteenIndexLiteralEscape() {
    var first = _Picture(8, 4, 3);
    var secondPixels = first.PixelData.ToArray();
    for (var y = 0; y < 4; ++y)
    for (var x = 4; x < 8; ++x)
      secondPixels[y * 8 + x] = (byte)(40 + y * 4 + x - 4);
    var second = _Picture(8, 4, secondPixels);

    var encoder = EaCmvVideoEncoder.Create(_Requested(8, 4));
    encoder.TryEncode(first, 0, out _);
    encoder.TryEncode(second, 1, out var packet);
    var frame = _Chunks(packet.Data.Span).Single();

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload), Is.EqualTo(1));
      Assert.That(frame.Payload[2], Is.EqualTo(0x77));
      Assert.That(frame.Payload[3], Is.EqualTo(0xFF));
      Assert.That(frame.Payload[4], Is.EqualTo(0xFF));
      Assert.That(frame.Payload.AsSpan(5).ToArray(), Is.EqualTo(new byte[] {
        40, 41, 42, 43,
        44, 45, 46, 47,
        48, 49, 50, 51,
        52, 53, 54, 55,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void PaletteChangesRestateOnlyTheChangedSpanBeforeThePicture() {
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
    var chunks = _Chunks(packet.Data.Span).ToArray();

    Assert.Multiple(() => {
      Assert.That(chunks.Select(static c => c.FourCc), Is.EqualTo(new[] { "MVIh", "MVIf" }));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(chunks[0].Payload.AsSpan(12)), Is.EqualTo(5));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(chunks[0].Payload.AsSpan(14)), Is.EqualTo(5));
      Assert.That(chunks[0].Payload.AsSpan(0x10).ToArray(), Is.EqualTo(palette.AsSpan(5 * 3, 5 * 3).ToArray()));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASequenceRoundTripsExactlyThroughCodecAndEaContainer() {
    var first = _Picture(8, 8, 1);
    var second = _Picture(8, 8, 2);
    var third = _Picture(8, 8, first.PixelData.ToArray());
    var pictures = new[] { first, second, third };
    var encoder = EaCmvVideoEncoder.Create(_Requested(8, 8));
    var decoder = EaCmvVideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();

    for (var i = 0; i < pictures.Length; ++i) {
      Assert.That(encoder.TryEncode(pictures[i], i, out var packet), Is.True);
      packets.Add(packet);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      _AssertSame(pictures[i], decoded, $"codec frame {i}");
    }

    packets.AddRange(encoder.Flush());
    var file = VideoIO.Mux<EaWriter>([encoder.DescribeStream()], packets);
    var container = EaContainer.FromBytes(file);
    var decodedFrames = VideoFormatRegistry.DecodeFrames(file).Select(static frame => frame.Image).ToArray();

    Assert.Multiple(() => {
      Assert.That(container.VideoCodec, Is.EqualTo(EaVideoCodecKind.Cmv));
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
  public void OddDimensionsUseIntraPicturesInsteadOfInventingPartialInterBlocks() {
    var picture = _Picture(5, 3, 7);
    var encoder = EaCmvVideoEncoder.Create(_Requested(5, 3));
    encoder.TryEncode(picture, 0, out _);

    encoder.TryEncode(picture, 1, out var packet);
    var frame = _Chunks(packet.Data.Span).Single();

    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload), Is.Zero);
    Assert.That(packet.IsKeyFrame, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FlushEmitsTheEndChunkOnceAndThenEncodingRefuses() {
    var encoder = EaCmvVideoEncoder.Create(_Requested(4, 4));
    encoder.TryEncode(_Picture(4, 4, 0), 0, out _);

    var first = encoder.Flush().Single();
    var second = encoder.Flush().ToArray();

    Assert.Multiple(() => {
      Assert.That(_Chunks(first.Data.Span).Single().FourCc, Is.EqualTo("MVIe"));
      Assert.That(second, Is.Empty);
      Assert.Throws<InvalidOperationException>(() => encoder.TryEncode(_Picture(4, 4, 0), 1, out _));
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
      Assert.That(actual.PaletteCount, Is.EqualTo(256), because);
    });
  }

  private static IEnumerable<(string FourCc, byte[] Payload)> _Chunks(ReadOnlySpan<byte> data) {
    // Iterator methods cannot retain a span, so materialise the packet once for structural assertions.
    var bytes = data.ToArray();
    for (var at = 0; at < bytes.Length;) {
      var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4)));
      var fourCc = System.Text.Encoding.ASCII.GetString(bytes, at, 4);
      yield return (fourCc, bytes.AsSpan(at + 8, length - 8).ToArray());
      at += length;
    }
  }
}
