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

  // ============================================================================================
  // Description and registration
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAccepts() {
    var encoder = FlicVideoEncoder.Create(_Requested(20, 12));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("FLIC")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("FLIC")));
      Assert.That(stream.Width, Is.EqualTo(20));
      Assert.That(stream.Height, Is.EqualTo(12));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(25, 1)));
    });

    Assert.That(FlicVideoDecoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<FlicVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegistered() {
    var requested = _Requested(8, 4, codec: "FLIC");

    Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("FLIC"));
    Assert.That(VideoFormatRegistry.CanEncode(requested), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(requested), Is.InstanceOf<FlicVideoEncoder>());
  }

  // ============================================================================================
  // Packets and round trips
  // ============================================================================================

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
  public void TheFirstFrameStatesTheWholePaletteAndAByteRunPicture() {
    var picture = LosslessEncoderPictures.Noise(7, 3, PixelFormat.Indexed8, seed: 4);
    var encoder = FlicVideoEncoder.Create(_Requested(7, 3));

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(_ChunkTypes(packet.Data.Span), Is.EqualTo(new ushort[] { FliChunkType.COLOR256, FliChunkType.BRUN }));
      Assert.That(packet.IsKeyFrame, Is.True);
    });

    var color = _ChunkPayload(packet.Data.Span, FliChunkType.COLOR256);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(color), Is.EqualTo(1), "one palette packet");
      Assert.That(color[2], Is.Zero, "starts at entry zero");
      Assert.That(color[3], Is.Zero, "zero means all 256 entries");
      Assert.That(color[4..], Is.EqualTo(picture.Palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void ABlackPictureUsesBlackInsteadOfWritingRuns() {
    var picture = LosslessEncoderPictures.Noise(5, 3, PixelFormat.Indexed8, seed: 8);
    Array.Clear(picture.PixelData);
    var encoder = FlicVideoEncoder.Create(_Requested(5, 3));

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    Assert.That(_ChunkTypes(packet.Data.Span), Is.EqualTo(new ushort[] { FliChunkType.COLOR256, FliChunkType.BLACK }));
    Assert.That(packet.IsKeyFrame, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AnUnchangedPictureCarriesNoSubChunksAndDependsOnTheFrameBefore() {
    var picture = LosslessEncoderPictures.Noise(9, 4, PixelFormat.Indexed8, seed: 11);
    var encoder = FlicVideoEncoder.Create(_Requested(9, 4));

    Assert.That(encoder.TryEncode(picture, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(picture, 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.Data.IsEmpty, Is.True);
      Assert.That(second.IsKeyFrame, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void APaletteOnlyChangeLeavesTheCanvasAlone() {
    var first = LosslessEncoderPictures.Noise(11, 5, PixelFormat.Indexed8, seed: 13);
    var second = LosslessEncoderPictures.Repainted(first, seed: 14);
    var encoder = FlicVideoEncoder.Create(_Requested(11, 5));
    var decoder = FlicVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(first, 0, out var firstPacket), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var secondPacket), Is.True);
    Assert.That(decoder.TryDecode(firstPacket, out _), Is.True);
    Assert.That(decoder.TryDecode(secondPacket, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(_ChunkTypes(secondPacket.Data.Span), Is.EqualTo(new ushort[] { FliChunkType.COLOR256 }));
      Assert.That(secondPacket.IsKeyFrame, Is.False);
    });
    LosslessEncoderPictures.AssertSame(second, decoded, "palette-only frame");
  }

  [Test]
  [Category("Unit")]
  public void LongRunsAndLongLiteralsCrossPacketLimitsAndRoundTrip() {
    const int width = 1000;
    const int height = 2;
    var picture = LosslessEncoderPictures.Noise(width, height, PixelFormat.Indexed8, seed: 20);
    Array.Fill(picture.PixelData, (byte)7, 0, 400);
    Array.Fill(picture.PixelData, (byte)9, 1200, 500);
    var encoder = FlicVideoEncoder.Create(_Requested(width, height));
    var decoder = FlicVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    LosslessEncoderPictures.AssertSame(picture, decoded, "runs beyond one signed-byte count");
  }

  [Test]
  [Category("Unit")]
  public void TheFramesSurviveTheFlcContainerAndRegistry() {
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
  [Category("Unit")]
  public void TimestampsPassThroughUntouched() {
    var encoder = FlicVideoEncoder.Create(_Requested(4, 4, index: 3));
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Indexed8, seed: 40);

    Assert.That(encoder.TryEncode(picture, 42, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.EqualTo(3));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(42));
    });
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBack() {
    var encoder = FlicVideoEncoder.Create(_Requested(4, 4));
    Assert.That(encoder.TryEncode(LosslessEncoderPictures.Noise(4, 4, PixelFormat.Indexed8, seed: 41), 0, out _), Is.True);

    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }

  // ============================================================================================
  // External interoperability
  // ============================================================================================

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

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsRefused()
    => Assert.Throws<NotSupportedException>(() => FlicVideoEncoder.Create(_Requested(4, 4, kind: MediaStreamKind.Audio)));

  [Test]
  [Category("Unit")]
  public void AGeometryOutsideTheHeaderIsRefused([Values(0, 65536)] int width) {
    var failure = Assert.Throws<NotSupportedException>(() => FlicVideoEncoder.Create(_Requested(width, 4)));
    Assert.That(failure!.Message, Does.Contain($"{width}x4"));
  }

  [Test]
  [Category("Unit")]
  public void ADepthOtherThanEightIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => FlicVideoEncoder.Create(_Requested(4, 4, bitsPerPixel: 16)));
    Assert.That(failure!.Message, Does.Contain("16 bits per pixel"));
  }

  [Test]
  [Category("Unit")]
  public void ATrueColourPictureIsNotSilentlyQuantised() {
    var encoder = FlicVideoEncoder.Create(_Requested(4, 4));
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Rgb24, seed: 60);

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain(nameof(PixelFormat.Rgb24)));
  }

  [Test]
  [Category("Unit")]
  public void AGeometryChangeMidStreamIsRefused() {
    var encoder = FlicVideoEncoder.Create(_Requested(8, 8));
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Indexed8, seed: 61);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }

  [Test]
  [Category("Unit")]
  public void AShortPixelBufferIsRefused() {
    var encoder = FlicVideoEncoder.Create(_Requested(4, 4));
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Indexed8, seed: 62);
    picture.PixelData = new byte[15];

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("enough pixel data"));
  }

  [Test]
  [Category("Unit")]
  public void APictureWithoutAPaletteIsRefused() {
    var encoder = FlicVideoEncoder.Create(_Requested(4, 4));
    var picture = LosslessEncoderPictures.With(
      LosslessEncoderPictures.Noise(4, 4, PixelFormat.Indexed8, seed: 63),
      dropPalette: true);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("without a palette"));
  }

  [Test]
  [Category("Unit")]
  public void AnIndexOutsideTheDeclaredPaletteIsRefused() {
    var encoder = FlicVideoEncoder.Create(_Requested(2, 1));
    var palette = new byte[2 * 3];
    var picture = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0, 2],
      Palette = palette,
      PaletteCount = 2,
    };

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("palette index 2"));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static ushort[] _ChunkTypes(ReadOnlySpan<byte> data) {
    var result = new List<ushort>();
    var at = 0;
    while (at < data.Length) {
      var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]));
      result.Add(BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]));
      at += size;
    }

    return result.ToArray();
  }

  private static byte[] _ChunkPayload(ReadOnlySpan<byte> data, ushort wantedType) {
    var at = 0;
    while (at < data.Length) {
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
