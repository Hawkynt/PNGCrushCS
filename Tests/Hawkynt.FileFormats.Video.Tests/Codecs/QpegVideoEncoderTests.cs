using System.IO;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// QPEG encode, checked packet by packet through the decoder beside it and end to end through AVI.
/// </summary>
/// <remarks>
/// FFmpeg has a QPEG decoder and no encoder. The external conformance fixture therefore judges these
/// packets in the useful direction: this encoder writes an AVI and FFmpeg has to decode it, while the
/// unit tests here pin the exact lossless result and the subset of opcodes this writer deliberately
/// emits.
/// </remarks>
[TestFixture]
public sealed class QpegVideoEncoderTests {

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAcceptsAndCreates() {
    var palette = _Palette(16);
    var encoder = QpegVideoEncoder.Create(_Requested(20, 12, palette, bitsPerPixel: 8));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("QPEG")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("QPEG")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(20));
      Assert.That(stream.Height, Is.EqualTo(12));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(40 + 16 * 4));
    });

    // A span cannot be captured by the Assert.Multiple lambda below.
    var format = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format), Is.EqualTo(40), "biSize");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format[4..]), Is.EqualTo(20), "biWidth");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format[8..]), Is.EqualTo(12), "biHeight");
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(format[14..]), Is.EqualTo(8), "biBitCount");
      Assert.That(format.AsSpan(16, 4).ToArray(), Is.EqualTo("QPEG"u8.ToArray()), "biCompression");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format[32..]), Is.EqualTo(16), "biClrUsed");
    });

    Assert.That(QpegVideoDecoder.Accepts(stream), Is.True);
    Assert.That(QpegVideoDecoder.Create(stream), Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegistered() {
    var requested = _Requested(8, 4);

    Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("Q-Team QPEG"));
    Assert.That(VideoFormatRegistry.CanEncode(requested), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(requested), Is.InstanceOf<QpegVideoEncoder>());
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithoutAPaletteCanBeDescribedAfterItsFirstPicture() {
    var palette = _Palette(8);
    var encoder = QpegVideoEncoder.Create(_Requested(4, 3));

    Assert.Throws<InvalidOperationException>(() => encoder.DescribeStream());
    Assert.That(encoder.TryEncode(_Picture(4, 3, palette, [0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3]), 0, out _), Is.True);

    Assert.That(encoder.DescribeStream().CodecPrivateData.Length, Is.EqualTo(40 + 8 * 4));
  }

  // ============================================================================================
  // Complete frames
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheFirstPictureIsAnIntraKeyFrameAndRoundTripsExactly() {
    var palette = _Palette(32);
    var picture = _Pattern(37, 11, palette, phase: 0);
    var encoder = QpegVideoEncoder.Create(_Requested(37, 11));

    Assert.That(encoder.TryEncode(picture, 3, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.Data.Span[132], Is.EqualTo(0xE0));
      Assert.That(packet.Data.Span[133], Is.EqualTo(0x10));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(packet.Data.Span), Is.EqualTo((uint)packet.Data.Length));
    });

    var decoder = QpegVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    _AssertSame(picture, decoded);
  }

  [Test]
  [Category("Unit")]
  public void ALongRepeatedValueUsesTheLongIntraRun() {
    var palette = _Palette(2);
    var pixels = Enumerable.Repeat((byte)1, 64).ToArray();
    var encoder = QpegVideoEncoder.Create(_Requested(64, 1));

    Assert.That(encoder.TryEncode(_Picture(64, 1, palette, pixels), 0, out var packet), Is.True);

    Assert.That(packet.Data.Span[134..].ToArray(), Is.EqualTo(new byte[] { 0xF0, 0x3E, 0x01, 0xFC }));
  }

  [Test]
  [Category("Unit")]
  public void ASequenceRoundTripsExactly() {
    var palette = _Palette(64);
    var pictures = Enumerable.Range(0, 7).Select(frame => _Pattern(73, 19, palette, frame)).ToArray();
    var encoder = QpegVideoEncoder.Create(_Requested(73, 19));
    var packets = new List<CodedPacket>();

    for (var i = 0; i < pictures.Length; ++i) {
      Assert.That(encoder.TryEncode(pictures[i], i, out var packet), Is.True);
      packets.Add(packet);
    }

    var decoder = QpegVideoDecoder.Create(encoder.DescribeStream());
    for (var i = 0; i < packets.Count; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True);
      _AssertSame(pictures[i], decoded, $"frame {i}");
    }
  }

  // ============================================================================================
  // Delta frames
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnIdenticalSecondPictureIsAnInterframe() {
    var palette = _Palette(256);
    var picture = _Pattern(64, 20, palette, phase: 2);
    var encoder = QpegVideoEncoder.Create(_Requested(64, 20));

    Assert.That(encoder.TryEncode(picture, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(picture, 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.IsKeyFrame, Is.False);
      Assert.That(second.Data.Span[133], Is.EqualTo(0x00));
      Assert.That(second.Data.Span[134..].ToArray(), Is.EqualTo(new byte[] {
        0x81, 0xFF, // 575 unchanged
        0x81, 0xFF, // another 575
        0x80, 0x42, // remaining 130 = 64 + 66
        0xE0,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ALongSkipAndOneChangedPixelRoundTrip() {
    var palette = _Palette(256);
    var firstPixels = Enumerable.Range(0, 700).Select(i => (byte)(i * 37)).ToArray();
    var secondPixels = (byte[])firstPixels.Clone();
    secondPixels[^1] ^= 0x55;
    var firstPicture = _Picture(700, 1, palette, firstPixels);
    var secondPicture = _Picture(700, 1, palette, secondPixels);
    var encoder = QpegVideoEncoder.Create(_Requested(700, 1));

    Assert.That(encoder.TryEncode(firstPicture, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(secondPicture, 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(second.IsKeyFrame, Is.False);
      Assert.That(second.Data.Span[133], Is.EqualTo(0x00));
      Assert.That(second.Data.Span[134..].ToArray(), Is.EqualTo(new byte[] {
        0x81, 0xFF, // 575
        0x80, 0x3C, // 124
        0xC0, secondPixels[^1],
        0xE0,
      }));
    });

    var decoder = QpegVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(first, out _), Is.True);
    Assert.That(decoder.TryDecode(second, out var decoded), Is.True);
    _AssertSame(secondPicture, decoded);
  }

  [Test]
  [Category("Unit")]
  public void ChangedRunsAndLiteralsRoundTripInAnInterframe() {
    var palette = _Palette(256);
    var before = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
    var after = (byte[])before.Clone();
    Array.Fill(after, (byte)7, 80, 65);
    for (var i = 180; i < 205; ++i)
      after[i] = (byte)(255 - i);

    var encoder = QpegVideoEncoder.Create(_Requested(256, 1));
    Assert.That(encoder.TryEncode(_Picture(256, 1, palette, before), 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(_Picture(256, 1, palette, after), 1, out var second), Is.True);
    Assert.That(second.IsKeyFrame, Is.False);

    var decoder = QpegVideoDecoder.Create(encoder.DescribeStream());
    decoder.TryDecode(first, out _);
    Assert.That(decoder.TryDecode(second, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(after));
  }

  // ============================================================================================
  // Through a container
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheFramesSurviveAnAviAndComeBackThroughTheRegistry() {
    var palette = _Palette(64);
    var pictures = Enumerable.Range(0, 4).Select(frame => _Pattern(47, 13, palette, frame)).ToArray();
    var encoder = QpegVideoEncoder.Create(_Requested(47, 13));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var decoded = VideoFormatRegistry.DecodeFrames(avi).Select(frame => frame.Image).ToList();

    Assert.That(decoded.Count, Is.EqualTo(pictures.Length));
    for (var i = 0; i < pictures.Length; ++i)
      _AssertSame(pictures[i], decoded[i], $"frame {i}");
  }

  // ============================================================================================
  // Packet plumbing
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TimestampsPassThroughAndNothingIsHeldBack() {
    var palette = _Palette(2);
    var picture = _Picture(2, 2, palette, [0, 1, 1, 0]);
    var encoder = QpegVideoEncoder.Create(_Requested(2, 2, index: 3));

    Assert.That(encoder.TryEncode(picture, 42, out var stamped), Is.True);
    Assert.That(encoder.TryEncode(picture, null, out var unstamped), Is.True);

    Assert.Multiple(() => {
      Assert.That(stamped.StreamIndex, Is.EqualTo(3));
      Assert.That(stamped.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(stamped.DecodeTimestamp, Is.EqualTo(42));
      Assert.That(unstamped.PresentationTimestamp, Is.Null);
      Assert.That(unstamped.DecodeTimestamp, Is.Null);
      Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
    });
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsRefused()
    => Assert.Throws<NotSupportedException>(() => QpegVideoEncoder.Create(_Requested(4, 4, kind: MediaStreamKind.Audio)));

  [Test]
  [Category("Unit")]
  public void ADepthOtherThanEightIsRefused([Values(4, 16, 24, 32)] int depth)
    => Assert.Throws<NotSupportedException>(() => QpegVideoEncoder.Create(_Requested(4, 4, bitsPerPixel: depth)));

  [Test]
  [Category("Unit")]
  public void ADirectColourPictureIsRefusedRatherThanQuantised() {
    var encoder = QpegVideoEncoder.Create(_Requested(2, 2));
    var picture = new RawImage {
      Width = 2, Height = 2, Format = PixelFormat.Rgb24, PixelData = new byte[12],
    };

    Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void AChangedPaletteIsRefused() {
    var firstPalette = _Palette(4);
    var secondPalette = (byte[])firstPalette.Clone();
    secondPalette[0] ^= 0xFF;
    var encoder = QpegVideoEncoder.Create(_Requested(2, 2));

    encoder.TryEncode(_Picture(2, 2, firstPalette, [0, 1, 2, 3]), 0, out _);

    Assert.Throws<InvalidDataException>(() =>
      encoder.TryEncode(_Picture(2, 2, secondPalette, [0, 1, 2, 3]), 1, out _));
  }

  [Test]
  [Category("Unit")]
  public void AnIndexPastThePaletteIsRefused() {
    var palette = _Palette(2);
    var encoder = QpegVideoEncoder.Create(_Requested(2, 1));

    Assert.Throws<InvalidDataException>(() =>
      encoder.TryEncode(_Picture(2, 1, palette, [0, 2]), 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void TransparentPaletteEntriesAreRefused() {
    var palette = _Palette(2);
    var picture = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
      Palette = palette,
      PaletteCount = 2,
      AlphaTable = [255, 0],
    };
    var encoder = QpegVideoEncoder.Create(_Requested(1, 1));

    Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Requested(
    int width,
    int height,
    byte[]? palette = null,
    int bitsPerPixel = 0,
    int index = 0,
    MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = index,
    Kind = kind,
    Codec = CodecTag.FromCharacters("QPEG"),
    Handler = CodecTag.FromCharacters("QPEG"),
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    CodecPrivateData = palette == null ? ReadOnlyMemory<byte>.Empty : _Format(width, height, palette),
  };

  private static byte[] _Format(int width, int height, byte[] palette) {
    var entries = palette.Length / 3;
    var format = new byte[40 + entries * 4];
    BinaryPrimitives.WriteInt32LittleEndian(format, 40);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), height);
    BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(14), 8);
    "QPEG"u8.CopyTo(format.AsSpan(16));
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(32), entries);
    for (var entry = 0; entry < entries; ++entry) {
      var at = 40 + entry * 4;
      format[at] = palette[entry * 3 + 2];
      format[at + 1] = palette[entry * 3 + 1];
      format[at + 2] = palette[entry * 3];
    }

    return format;
  }

  private static byte[] _Palette(int entries) {
    var palette = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 73);
    }

    return palette;
  }

  private static RawImage _Pattern(int width, int height, byte[] palette, int phase) {
    var entries = palette.Length / 3;
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var block = (x / 5 + y / 3) % entries;
      if (((x + y + phase) % 17) == 0)
        block = (block + phase * 7 + 1) % entries;
      pixels[y * width + x] = (byte)block;
    }

    return _Picture(width, height, palette, pixels);
  }

  private static RawImage _Picture(int width, int height, byte[] palette, byte[] pixels) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Indexed8,
    PixelData = pixels,
    Palette = palette,
    PaletteCount = palette.Length / 3,
  };

  private static void _AssertSame(RawImage expected, RawImage actual, string? because = null) {
    Assert.Multiple(() => {
      Assert.That(actual.Width, Is.EqualTo(expected.Width), because);
      Assert.That(actual.Height, Is.EqualTo(expected.Height), because);
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Indexed8), because);
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData), because);
      Assert.That(actual.PaletteCount, Is.EqualTo(expected.PaletteCount), because);
      Assert.That(actual.Palette, Is.EqualTo(expected.Palette), because);
    });
  }
}
