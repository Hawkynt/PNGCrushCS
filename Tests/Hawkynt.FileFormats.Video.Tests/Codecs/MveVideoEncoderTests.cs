using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Core;
using FileFormat.InterplayMve;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class MveVideoEncoderTests {

  private const byte _SET_PALETTE_COMPRESSED = 0x0D;
  private const byte _SKIP_MAP = 0x0E;
  private const byte _DECODING_MAP = 0x0F;
  private const byte _VIDEO_DATA_06 = 0x06;
  private const byte _VIDEO_DATA_10 = 0x10;
  private const byte _VIDEO_DATA_11 = 0x11;

  [Test]
  [Category("Unit")]
  public void EightBitEncoderRoundTripsAndUsesTwoPicturesBack() {
    var stream = _Stream(16, 8, 8);
    var encoder = MveVideoEncoder.Create(stream);
    var decoder = MveVideoDecoder.Create(encoder.DescribeStream());

    var first = _IndexedBlocks(16, 8, 1, 2);
    var second = _IndexedBlocks(16, 8, 3, 4);

    encoder.TryEncode(first, 0, out var packet0);
    encoder.TryEncode(second, 1, out var packet1);
    encoder.TryEncode(first, 2, out var packet2);

    Assert.That(decoder.TryDecode(packet0, out var decoded0), Is.True);
    Assert.That(decoder.TryDecode(packet1, out var decoded1), Is.True);
    Assert.That(decoder.TryDecode(packet2, out var decoded2), Is.True);
    Assert.That(decoded0.PixelData, Is.EqualTo(first.PixelData));
    Assert.That(decoded1.PixelData, Is.EqualTo(second.PixelData));
    Assert.That(decoded2.PixelData, Is.EqualTo(first.PixelData));

    var map = _FindOpcode(packet2.Data.Span, _DECODING_MAP);
    Assert.That(map[0], Is.EqualTo(0x11), "both 8x8 blocks should use the two-pictures-back block mode");
    Assert.That(packet2.IsKeyFrame, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void SixteenBitEncoderRoundTripsRgb555AndReusesPreviousPicture() {
    var stream = _Stream(8, 8, 16);
    var encoder = MveVideoEncoder.Create(stream);
    var decoder = MveVideoDecoder.Create(encoder.DescribeStream());
    var red = _Rgb24(8, 8, 255, 0, 0);

    encoder.TryEncode(red, 0, out var first);
    encoder.TryEncode(red, 1, out var second);

    Assert.That(decoder.TryDecode(first, out var decoded0), Is.True);
    Assert.That(decoded0.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(decoded0.PixelData, Is.EqualTo(red.PixelData));

    Assert.That(decoder.TryDecode(second, out var decoded1), Is.True);
    Assert.That(decoded1.PixelData, Is.EqualTo(red.PixelData));
    Assert.That(_FindOpcode(second.Data.Span, _DECODING_MAP)[0] & 0x0F, Is.EqualTo(0x0));
  }

  [Test]
  [Category("Unit")]
  public void CompressedPaletteInstallsOnlyTheMaskedEntries() {
    var decoder = MveVideoDecoder.Create(_Stream(8, 8, 8));
    using var compressed = new MemoryStream();
    for (var group = 0; group < 32; ++group) {
      compressed.WriteByte(group == 1 ? (byte)0x01 : (byte)0x00);
      if (group == 1)
        compressed.Write([63, 0, 0]);
    }

    Assert.That(decoder.TryDecode(new(0, _Opcode(_SET_PALETTE_COMPRESSED, compressed.ToArray())), out _), Is.False);
    Assert.That(decoder.TryDecode(new(0, _Opcode(_DECODING_MAP, [0x0E])), out _), Is.False);
    Assert.That(decoder.TryDecode(new(0, _Opcode(_VIDEO_DATA_11, _VideoPayload(1, 1, [8]))), out var picture), Is.True);

    Assert.That(picture.Palette![8 * 3], Is.EqualTo(255));
    Assert.That(picture.Palette[8 * 3 + 1], Is.Zero);
    Assert.That(picture.Palette[8 * 3 + 2], Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void Legacy06RawPictureDecodes() {
    var decoder = MveVideoDecoder.Create(_Stream(8, 8, 8));
    var pixels = Enumerable.Range(0, 64).Select(static i => (byte)i).ToArray();
    var payload = new byte[14 + 2 + 64];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10), 1);
    BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(14), 0);
    pixels.CopyTo(payload, 16);

    Assert.That(decoder.TryDecode(new(0, _Opcode(_VIDEO_DATA_06, payload)), out var picture), Is.True);
    Assert.That(picture.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void Legacy10SkipMapSelectsARawChangedBlock() {
    var decoder = MveVideoDecoder.Create(_Stream(8, 8, 8));
    var pixels = Enumerable.Range(0, 64).Select(static i => (byte)(255 - i)).ToArray();

    Assert.That(decoder.TryDecode(new(0, _Opcode(_SKIP_MAP, [0xFF, 0xFF])), out _), Is.False); // signed -1: changed
    Assert.That(decoder.TryDecode(new(0, _Opcode(_DECODING_MAP, [0, 0])), out _), Is.False);
    Assert.That(decoder.TryDecode(new(0, _Opcode(_VIDEO_DATA_10, _VideoPayload(1, 1, pixels))), out var picture), Is.True);
    Assert.That(picture.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Conformance")]
  public void EncoderAndMveWriterProduceAFileFFmpegCanDecode() {
    FFmpegOracle.RequireAvailable();
    var requested = _Stream(64, 48, 8);
    var encoder = MveVideoEncoder.Create(requested);
    encoder.TryEncode(_IndexedBlocks(64, 48, 1, 2), 0, out var packet);
    var stream = encoder.DescribeStream();
    var file = VideoIO.Mux<MveWriter>([stream], [packet]);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mve");

    try {
      File.WriteAllBytes(path, file);
      var (decoded, detail) = FFmpegOracle.TryDecodeFirstFrame(path, 64, 48);
      Assert.That(decoded, Is.True, detail);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("IMVE"),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _IndexedBlocks(int width, int height, byte first, byte second) {
    var palette = new byte[256 * 3];
    palette[first * 3] = 255;
    palette[second * 3 + 1] = 255;
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        pixels[y * width + x] = ((x / 8 + y / 8) & 1) == 0 ? first : second;
    return new() { Width = width, Height = height, Format = PixelFormat.Indexed8, PixelData = pixels, Palette = palette, PaletteCount = 256 };
  }

  private static RawImage _Rgb24(int width, int height, byte r, byte g, byte b) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = r;
      pixels[i * 3 + 1] = g;
      pixels[i * 3 + 2] = b;
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static byte[] _Opcode(byte type, byte[] payload) {
    var result = new byte[payload.Length + 4];
    BinaryPrimitives.WriteUInt16LittleEndian(result, checked((ushort)payload.Length));
    result[2] = type;
    payload.CopyTo(result, 4);
    return result;
  }

  private static byte[] _VideoPayload(int widthBlocks, int heightBlocks, byte[] data) {
    var result = new byte[14 + data.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), checked((ushort)widthBlocks));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), checked((ushort)heightBlocks));
    data.CopyTo(result, 14);
    return result;
  }

  private static ReadOnlySpan<byte> _FindOpcode(ReadOnlySpan<byte> packet, byte type) {
    var at = 0;
    while (at < packet.Length) {
      var length = BinaryPrimitives.ReadUInt16LittleEndian(packet[at..]);
      if (packet[at + 2] == type)
        return packet.Slice(at + 4, length);
      at += 4 + length;
    }
    Assert.Fail($"opcode 0x{type:X2} not found");
    return default;
  }
}
