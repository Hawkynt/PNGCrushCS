using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class LgplScreenCaptureDecoderTests {

  [Test]
  [Category("Unit")]
  public void ScreenpressoKeyframeIsInflatedAndFlipped() {
    var decoder = ScreenpressoVideoDecoder.Create(_Stream("SPV1", 2, 2, 24));
    var native = new byte[] {
      0, 0, 255, 0, 0, 255, 0, 0,
      255, 0, 0, 255, 0, 0, 0, 0,
    };
    var packet = _Concat(new byte[] { 1, 8 }, _Zlib(native));

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      255, 0, 0, 255, 0, 0,
      0, 0, 255, 0, 0, 255,
    }));
  }

  [Test]
  [Category("Unit")]
  public void ScreenpressoDeltaAddsToCurrentFrameInFlippedRowOrder() {
    var decoder = ScreenpressoVideoDecoder.Create(_Stream("SPV1", 2, 1, 24));
    var key = _Concat(new byte[] { 1, 8 }, _Zlib(new byte[] { 10, 20, 30, 40, 50, 60, 0, 0 }));
    Assert.That(decoder.TryDecode(new(0, key), out _), Is.True);

    var delta = _Concat(new byte[] { 0, 8 }, _Zlib(new byte[] { 1, 2, 3, 4, 5, 6, 0, 0 }));
    Assert.That(decoder.TryDecode(new(0, delta), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 11, 22, 33, 44, 55, 66 }));
  }

  [Test]
  [Category("Unit")]
  public void MwscRepeatsA24BitFillAcrossTheBottomUpWalk() {
    var decoder = MwscVideoDecoder.Create(_Stream("MWSC", 2, 1, 24));
    var commands = new byte[] { 3, 2, 1, 2 };

    Assert.That(decoder.TryDecode(new(0, _Zlib(commands)), out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 3, 2, 1, 3, 2, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void MwscCopyOpcodeTakesPixelsFromThePreviousFrame() {
    var decoder = MwscVideoDecoder.Create(_Stream("MWSC", 2, 1, 24));
    Assert.That(decoder.TryDecode(new(0, _Zlib(new byte[] { 9, 8, 7, 2 })), out var first), Is.True);
    Assert.That(decoder.TryDecode(new(0, _Zlib(new byte[] { 2, 0, 0, 255 })), out var second), Is.True);
    Assert.That(second.PixelData, Is.EqualTo(first.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void WcmvInlineRectangleUpdatesThePersistentCanvas() {
    var decoder = WcmvVideoDecoder.Create(_Stream("WCMV", 2, 1, 24));
    var pixels = new byte[] { 1, 2, 3, 4, 5, 6 };
    var compressed = _Zlib(pixels);
    var packet = new byte[2 + 8 + 1 + compressed.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(8), 1);
    packet[10] = checked((byte)compressed.Length);
    compressed.CopyTo(packet, 11);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(pixels));
    Assert.That(decoder.TryDecode(new(0, new byte[] { 0, 0 }), out var repeat), Is.True);
    Assert.That(repeat.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void MsccReconstructsItsObfuscatedZlibHeaderThenRunsGeneralizedRle() {
    var decoder = MsccVideoDecoder.Create(_Stream("MSCC", 2, 1, 24));
    var rle = new byte[] { 2, 3, 2, 1, 0, 1 };
    var zlib = _Zlib(rle);
    var packet = new byte[zlib.Length + 2];
    packet[0] = 0;
    packet[1] = 0;
    packet[2] = zlib[0];
    zlib.AsSpan(1).CopyTo(packet.AsSpan(3));

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 3, 2, 1, 3, 2, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void RsccRawTileUpdatesThePersistentCanvas() {
    var decoder = RsccVideoDecoder.Create(_Stream("RSCC", 2, 1, 24));
    var packet = new byte[] {
      1, 0,
      0, 0, 2, 0, 0, 0, 1, 0,
      6,
      1, 2, 3, 4, 5, 6,
    };

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
    Assert.That(decoder.TryDecode(new(0, new byte[] { 0, 0 }), out _), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AllFiveCodecsAreRegistered() {
    var names = VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName).ToArray();
    Assert.That(names, Does.Contain("Screenpresso"));
    Assert.That(names, Does.Contain("MatchWare Screen Capture Codec"));
    Assert.That(names, Does.Contain("WinCAM Motion Video"));
    Assert.That(names, Does.Contain("Mandsoft / Screen Recorder Gold"));
    Assert.That(names, Does.Contain("innoHeim/Rsupport Screen Capture Codec"));
  }

  private static MediaStreamInfo _Stream(string codec, int width, int height, short bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
  };

  private static byte[] _Zlib(ReadOnlySpan<byte> data) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
      zlib.Write(data);
    return output.ToArray();
  }

  private static byte[] _Concat(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) {
    var result = new byte[first.Length + second.Length];
    first.CopyTo(result);
    second.CopyTo(result.AsSpan(first.Length));
    return result;
  }
}
