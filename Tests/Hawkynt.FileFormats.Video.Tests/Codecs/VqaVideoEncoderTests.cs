using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Vqa;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;
using Hawkynt.FileFormats.Video.Tests.Codecs;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class VqaVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void DescribesARegisteredStreamTheDecoderAccepts() {
    var requested = _Requested(8, 2, bitsPerPixel: 8);
    var encoder = VqaVideoEncoder.Create(requested);
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("WSVQ")));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(42));
      Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("Westwood VQA Video"));
      Assert.That(VideoFormatRegistry.CanEncode(requested), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(requested), Is.InstanceOf<VqaVideoEncoder>());
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<VqaVideoDecoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void VersionTwoPalettisedFrameRoundTripsIndicesAndPaletteExactly() {
    var picture = _Indexed(8, 2, [0, 1, 2, 3, 4, 5, 6, 7, 10, 10, 10, 10, 10, 10, 10, 10]);
    var encoder = VqaVideoEncoder.Create(_Requested(8, 2, bitsPerPixel: 8));
    var decoder = VqaVideoDecoder.Create(encoder.DescribeStream());

    encoder.TryEncode(picture, 4, out var packet);
    decoder.TryDecode(packet, out var decoded);

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(4));
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
      Assert.That(decoded.Palette, Is.EqualTo(picture.Palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void VersionOneWriterUsesItsDefinedPointerLayout() {
    var header = _Header(version: 1, highColour: false, width: 8, height: 2);
    var picture = _Indexed(8, 2, [0, 1, 2, 3, 4, 5, 6, 7, 31, 31, 31, 31, 31, 31, 31, 31]);
    var encoder = VqaVideoEncoder.Create(_Requested(8, 2, bitsPerPixel: 8, privateData: header));
    var decoder = VqaVideoDecoder.Create(encoder.DescribeStream());

    encoder.TryEncode(picture, 0, out var packet);
    decoder.TryDecode(packet, out var decoded);

    Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void HiColorSecondFrameUsesPreviousFrameSkips() {
    var first = _RgbBlocks((255, 0, 0), (0, 255, 0));
    var second = _RgbBlocks((255, 0, 0), (0, 0, 255));
    var encoder = VqaVideoEncoder.Create(_Requested(8, 2, bitsPerPixel: 15));
    var decoder = VqaVideoDecoder.Create(encoder.DescribeStream());

    encoder.TryEncode(first, 0, out var firstPacket);
    encoder.TryEncode(second, 1, out var secondPacket);
    decoder.TryDecode(firstPacket, out _);
    decoder.TryDecode(secondPacket, out var decoded);

    Assert.Multiple(() => {
      Assert.That(firstPacket.IsKeyFrame, Is.True);
      Assert.That(secondPacket.IsKeyFrame, Is.False);
      Assert.That(decoded.PixelData, Is.EqualTo(second.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void HiColorFullyChangedFrameIsAKeyFrame() {
    var first = _RgbBlocks((255, 0, 0), (0, 255, 0));
    var second = _RgbBlocks((0, 0, 255), (255, 255, 255));
    var encoder = VqaVideoEncoder.Create(_Requested(8, 2, bitsPerPixel: 15));

    encoder.TryEncode(first, 0, out _);
    encoder.TryEncode(second, 1, out var packet);

    Assert.That(packet.IsKeyFrame, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void OverlayAlphaUsesPixelLevelPreviousFrameReferences() {
    var header = _Header(version: 3, highColour: true, width: 4, height: 2, overlay: true);
    var first = _RgbaSolid(4, 2, 255, 0, 0, 255);
    var second = _RgbaSolid(4, 2, 0, 0, 255, 255);
    second.PixelData[3] = 0;
    var encoder = VqaVideoEncoder.Create(_Requested(4, 2, bitsPerPixel: 15, privateData: header));
    var decoder = VqaVideoDecoder.Create(encoder.DescribeStream());

    encoder.TryEncode(first, 0, out var firstPacket);
    encoder.TryEncode(second, 1, out var secondPacket);
    decoder.TryDecode(firstPacket, out _);
    decoder.TryDecode(secondPacket, out var decoded);

    Assert.Multiple(() => {
      Assert.That(secondPacket.IsKeyFrame, Is.False);
      Assert.That(decoded.PixelData[..3], Is.EqualTo(new byte[] { 255, 0, 0 }), "transparent pixel preserves red");
      Assert.That(decoded.PixelData[3..6], Is.EqualTo(new byte[] { 0, 0, 255 }), "opaque pixel becomes blue");
    });
  }

  [Test]
  [Category("Unit")]
  public void VqaContainerRoundTripsEncodedHiColorFrames() {
    var pictures = new[] {
      _RgbBlocks((255, 0, 0), (0, 255, 0)),
      _RgbBlocks((255, 0, 0), (0, 0, 255)),
      _RgbBlocks((255, 255, 255), (0, 0, 255)),
    };
    var encoder = VqaVideoEncoder.Create(_Requested(8, 2, bitsPerPixel: 15));
    var packets = pictures.Select((picture, i) => {
      encoder.TryEncode(picture, i, out var packet);
      return packet;
    }).ToArray();

    var file = VideoIO.Mux<VqaWriter>([encoder.DescribeStream()], packets);
    var decoded = VideoFormatRegistry.DecodeFrames(file).Select(x => x.Image).ToArray();

    Assert.That(decoded.Length, Is.EqualTo(pictures.Length));
    for (var i = 0; i < decoded.Length; ++i)
      Assert.That(decoded[i].PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsAFileWrittenHere() {
    FFmpegOracle.RequireAvailable();

    var picture = _Indexed(8, 2, [0, 1, 2, 3, 4, 5, 6, 7, 12, 12, 12, 12, 12, 12, 12, 12]);
    var encoder = VqaVideoEncoder.Create(_Requested(8, 2, bitsPerPixel: 8));
    encoder.TryEncode(picture, 0, out var packet);
    var file = VideoIO.Mux<VqaWriter>([encoder.DescribeStream()], [packet]);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".vqa");

    try {
      File.WriteAllBytes(path, file);
      var (ok, detail) = FFmpegOracle.TryDecodeFirstFrame(path, 8, 2);
      Assert.That(ok, Is.True, detail);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Unit")]
  public void UnrepresentablePaletteAndFractionalOverlayAlphaRefuseRatherThanQuantiseSilently() {
    var indexed = _Indexed(4, 2, [0, 0, 0, 0, 0, 0, 0, 0]);
    indexed.Palette![0] = 1;
    var paletteEncoder = VqaVideoEncoder.Create(_Requested(4, 2, bitsPerPixel: 8));
    Assert.That(
      Assert.Throws<NotSupportedException>(() => paletteEncoder.TryEncode(indexed, 0, out _))!.Message,
      Does.Contain("six-bit"));

    var overlayHeader = _Header(version: 3, highColour: true, width: 4, height: 2, overlay: true);
    var overlay = _RgbaSolid(4, 2, 0, 0, 0, 255);
    overlay.PixelData[3] = 128;
    var overlayEncoder = VqaVideoEncoder.Create(_Requested(4, 2, bitsPerPixel: 15, privateData: overlayHeader));
    Assert.That(
      Assert.Throws<NotSupportedException>(() => overlayEncoder.TryEncode(overlay, 0, out _))!.Message,
      Does.Contain("one bit"));
  }

  [Test]
  [Category("Unit")]
  public void GeometryAndPixelDepthContradictionsRefuse() {
    Assert.Throws<NotSupportedException>(() => VqaVideoEncoder.Create(_Requested(7, 2, bitsPerPixel: 8)));
    Assert.Throws<NotSupportedException>(() => VqaVideoEncoder.Create(_Requested(8, 2, bitsPerPixel: 12)));

    var header = _Header(version: 3, highColour: true, width: 8, height: 2);
    Assert.Throws<NotSupportedException>(() => VqaVideoEncoder.Create(_Requested(8, 2, bitsPerPixel: 8, privateData: header)));
  }

  private static MediaStreamInfo _Requested(int width, int height, int bitsPerPixel, byte[]? privateData = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("WSVQ"),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new(1, 15),
    FrameRate = new(15, 1),
    CodecPrivateData = privateData ?? ReadOnlyMemory<byte>.Empty,
  };

  private static byte[] _Header(int version, bool highColour, int width, int height, bool overlay = false) {
    var header = new byte[42];
    BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)version);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), (ushort)((highColour ? 0x10 : 0) | (overlay ? 0x04 : 0)));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)height);
    header[10] = 4;
    header[11] = 2;
    header[12] = 15;
    header[13] = highColour ? (byte)0 : (byte)8;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), highColour ? (ushort)0 : (ushort)256);
    return header;
  }

  private static RawImage _Indexed(int width, int height, byte[] pixels) {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      var value = ChannelScaling.Expand6(i & 63);
      palette[i * 3] = value;
      palette[i * 3 + 1] = value;
      palette[i * 3 + 2] = value;
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

  private static RawImage _RgbBlocks((byte R, byte G, byte B) left, (byte R, byte G, byte B) right) {
    var data = new byte[8 * 2 * 3];
    for (var y = 0; y < 2; ++y)
      for (var x = 0; x < 8; ++x) {
        var colour = x < 4 ? left : right;
        var at = (y * 8 + x) * 3;
        data[at] = colour.R;
        data[at + 1] = colour.G;
        data[at + 2] = colour.B;
      }
    return new() { Width = 8, Height = 2, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static RawImage _RgbaSolid(int width, int height, byte r, byte g, byte b, byte a) {
    var data = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      data[i * 4] = r;
      data[i * 4 + 1] = g;
      data[i * 4 + 2] = b;
      data[i * 4 + 3] = a;
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }
}
