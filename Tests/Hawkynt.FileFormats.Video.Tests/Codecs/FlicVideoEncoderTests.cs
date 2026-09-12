using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.FlicVideo;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;
using Hawkynt.FileFormats.Video.Tests.Codecs;

namespace FileFormat.Codecs.Tests;

/// <summary>The FLIC encoder, checked through its decoder, its container and FFmpeg.</summary>
[TestFixture]
public sealed class FlicVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void DescribesARegisteredStreamTheDecoderAccepts() {
    var requested = _Requested(20, 12, codec: "FLIC");
    var encoder = FlicVideoEncoder.Create(requested);
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("FLIC")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("FLIC")));
      Assert.That(stream.Width, Is.EqualTo(20));
      Assert.That(stream.Height, Is.EqualTo(12));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("FLIC"));
      Assert.That(VideoFormatRegistry.CanEncode(requested), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(requested), Is.InstanceOf<FlicVideoEncoder>());
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<FlicVideoDecoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void ASequenceRoundTripsExactly([Values(1, 3, 7, 63, 64, 320)] int width) {
    var height = width switch { 1 => 1, 3 => 2, 7 => 5, 63 => 9, 64 => 8, _ => 21 };
    var pictures = LosslessEncoderPictures.Sequence(width, height, PixelFormat.Indexed8, 8, seed: width);
    var encoder = FlicVideoEncoder.Create(_Requested(width, height));
    var decoder = FlicVideoDecoder.Create(encoder.DescribeStream());

    for (var i = 0; i < pictures.Length; ++i) {
      Assert.That(encoder.TryEncode(pictures[i], i, out var packet), Is.True);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      LosslessEncoderPictures.AssertSame(pictures[i], decoded, $"frame {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FirstFrameStatesAllColoursAndAByteRunPicture() {
    var picture = LosslessEncoderPictures.Noise(7, 3, PixelFormat.Indexed8, seed: 4);
    var encoder = FlicVideoEncoder.Create(_Requested(7, 3));

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    Assert.That(_ChunkTypes(packet.Data.Span), Is.EqualTo(new ushort[] { FliChunkType.COLOR256, FliChunkType.BRUN }));
    Assert.That(packet.IsKeyFrame, Is.True);

    var color = _ChunkPayload(packet.Data.Span, FliChunkType.COLOR256);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(color), Is.EqualTo(1));
      Assert.That(color[2], Is.Zero);
      Assert.That(color[3], Is.Zero, "zero means all 256 entries");
      Assert.That(color[4..], Is.EqualTo(picture.Palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void BlackAndUnchangedPicturesUseTheirSmallSpellings() {
    var black = LosslessEncoderPictures.Noise(9, 4, PixelFormat.Indexed8, seed: 8);
    Array.Clear(black.PixelData);
    var encoder = FlicVideoEncoder.Create(_Requested(9, 4));

    Assert.That(encoder.TryEncode(black, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(black, 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(_ChunkTypes(first.Data.Span), Is.EqualTo(new ushort[] { FliChunkType.COLOR256, FliChunkType.BLACK }));
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.Data.IsEmpty, Is.True);
      Assert.That(second.IsKeyFrame, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void PaletteOnlyChangeLeavesTheCanvasAlone() {
    var first = LosslessEncoderPictures.Noise(11, 5, PixelFormat.Indexed8, seed: 13);
    var second = LosslessEncoderPictures.Repainted(first, seed: 14);
    var encoder = FlicVideoEncoder.Create(_Requested(11, 5));
    var decoder = FlicVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(first, 0, out var firstPacket), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var secondPacket), Is.True);
    Assert.That(decoder.TryDecode(firstPacket, out _), Is.True);
    Assert.That(decoder.TryDecode(secondPacket, out var decoded), Is.True);

    Assert.That(_ChunkTypes(secondPacket.Data.Span), Is.EqualTo(new ushort[] { FliChunkType.COLOR256 }));
    Assert.That(secondPacket.IsKeyFrame, Is.False);
    LosslessEncoderPictures.AssertSame(second, decoded, "palette-only frame");
  }

  [Test]
  [Category("Unit")]
  public void LongRunsAndLiteralsCrossSignedByteLimits() {
    const int width = 1000;
    var picture = LosslessEncoderPictures.Noise(width, 2, PixelFormat.Indexed8, seed: 20);
    Array.Fill(picture.PixelData, (byte)7, 0, 400);
    Array.Fill(picture.PixelData, (byte)9, 1200, 500);
    var encoder = FlicVideoEncoder.Create(_Requested(width, 2));
    var decoder = FlicVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    LosslessEncoderPictures.AssertSame(picture, decoded, "runs beyond one count byte");
  }

  [Test]
  [Category("Unit")]
  public void FramesSurviveTheFlcContainerAndRegistry() {
    var pictures = LosslessEncoderPictures.Sequence(37, 14, PixelFormat.Indexed8, 5, seed: 31);
    var encoder = FlicVideoEncoder.Create(_Requested(37, 14));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var flc = VideoIO.Mux<FliWriter>([encoder.DescribeStream()], packets);
    var decoded = VideoFormatRegistry.DecodeFrames(flc).Select(frame => frame.Image).ToList();

    Assert.That(decoded.Count, Is.EqualTo(pictures.Length));
    for (var i = 0; i < pictures.Length; ++i)
      LosslessEncoderPictures.AssertSame(pictures[i], decoded[i], $"frame {i}");
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsAnOddWidthFlcWrittenHere() {
    FFmpegOracle.RequireAvailable();

    var picture = LosslessEncoderPictures.Noise(7, 5, PixelFormat.Indexed8, seed: 52);
    var encoder = FlicVideoEncoder.Create(_Requested(7, 5));
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    var flc = VideoIO.Mux<FliWriter>([encoder.DescribeStream()], [packet]);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".flc");

    try {
      File.WriteAllBytes(path, flc);
      var (decoded, detail) = FFmpegOracle.TryDecodeFirstFrame(path, 7, 5);
      Assert.That(decoded, Is.True, detail);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Unit")]
  public void TimestampsPassThroughAndNothingIsHeldBack() {
    var encoder = FlicVideoEncoder.Create(_Requested(4, 4, index: 3));
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Indexed8, seed: 40);

    Assert.That(encoder.TryEncode(picture, 42, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.EqualTo(3));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(42));
      Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void UnsupportedInputsAreRefusedByName() {
    Assert.Throws<NotSupportedException>(() => FlicVideoEncoder.Create(_Requested(4, 4, kind: MediaStreamKind.Audio)));
    Assert.Throws<NotSupportedException>(() => FlicVideoEncoder.Create(_Requested(0, 4)));
    Assert.Throws<NotSupportedException>(() => FlicVideoEncoder.Create(_Requested(65536, 4)));
    Assert.Throws<NotSupportedException>(() => FlicVideoEncoder.Create(_Requested(4, 4, bitsPerPixel: 16)));

    var encoder = FlicVideoEncoder.Create(_Requested(4, 4));
    var rgb = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Rgb24, seed: 60);
    Assert.That(
      Assert.Throws<NotSupportedException>(() => encoder.TryEncode(rgb, 0, out _))!.Message,
      Does.Contain(nameof(PixelFormat.Rgb24)));

    var small = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Indexed8, seed: 62);
    var shortBuffer = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[15],
      Palette = small.Palette,
      PaletteCount = small.PaletteCount,
    };
    Assert.That(
      Assert.Throws<InvalidDataException>(() => encoder.TryEncode(shortBuffer, 0, out _))!.Message,
      Does.Contain("enough pixel data"));

    var noPalette = LosslessEncoderPictures.With(small, dropPalette: true);
    Assert.That(
      Assert.Throws<InvalidDataException>(() => encoder.TryEncode(noPalette, 0, out _))!.Message,
      Does.Contain("without a palette"));

    var invalidIndex = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Indexed8,
      PixelData = [0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
      Palette = new byte[6],
      PaletteCount = 2,
    };
    Assert.That(
      Assert.Throws<InvalidDataException>(() => encoder.TryEncode(invalidIndex, 0, out _))!.Message,
      Does.Contain("palette index 2"));
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

  private static byte[] _ChunkPayload(ReadOnlySpan<byte> data, ushort wantedType) {
    for (var at = 0; at < data.Length;) {
      var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]));
      var type = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]);
      if (type == wantedType)
        return data.Slice(at + 6, size - 6).ToArray();
      at += size;
    }

    Assert.Fail($"packet carries no FLIC sub-chunk of type {wantedType}");
    return [];
  }

  private static MediaStreamInfo _Requested(
    int width,
    int height,
    int bitsPerPixel = 0,
    int index = 0,
    MediaStreamKind kind = MediaStreamKind.Video,
    string? codec = null) => new() {
    Index = index,
    Kind = kind,
    Codec = codec == null ? CodecTag.None : CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}
