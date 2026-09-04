using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Core;
using FileFormat.Jpeg;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class RemainingScreenCaptureDecoderTests {

  [Test]
  [Category("Unit")]
  public void VmncRawRgb555RectangleDecodesToRgb24() {
    var decoder = VmncVideoDecoder.Create(_Stream("VMnc", 2, 1, 16));
    var packet = _VmncPacket(
      _VmncChunk(0, 0, 2, 1, 0, [0x00, 0x7C, 0xE0, 0x03])
    );

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 255, 0, 0, 0, 255, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void VmncHextilePaintsBackgroundAndSubrectangle() {
    var decoder = VmncVideoDecoder.Create(_Stream("VMnc", 2, 1, 16));
    var hextile = new byte[] {
      0x0E,             // background + foreground + subrectangles
      0x1F, 0x00,       // blue background (RGB555)
      0x00, 0x7C,       // red foreground
      0x01,             // one subrectangle
      0x00, 0x00,       // x=0,y=0,w=1,h=1
    };

    Assert.That(decoder.TryDecode(new(0, _VmncPacket(_VmncChunk(0, 0, 2, 1, 5, hextile))), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 255, 0, 0, 0, 0, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void VmncUsesRfbPixelDescriptorForEightBitTrueColor() {
    var decoder = VmncVideoDecoder.Create(_Stream("VMnc", 2, 1, 8));
    var pixelFormat = new byte[] {
      8, 8, 0, 1,
      0, 7, 0, 7, 0, 3,
      0, 3, 6,
      0, 0, 0,
    };
    var packet = _VmncPacket(
      _VmncChunk(0, 0, 0, 0, 0x574D5669, pixelFormat),
      _VmncChunk(0, 0, 2, 1, 0, [0x07, 0xC0])
    );

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 255, 0, 0, 0, 0, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void VmncCursorIsAppliedWithoutDestroyingReferenceCanvas() {
    var decoder = VmncVideoDecoder.Create(_Stream("VMnc", 2, 1, 16));
    var defineCursor = _VmncChunk(0, 0, 1, 1, 0x574D5664, [
      0, 0,       // cursor prefix
      0, 0,       // AND bits
      0, 0x7C,    // XOR mask = red
    ]);
    var moveCursor = _VmncChunk(1, 0, 0, 0, 0x574D5666, []);
    var packet = _VmncPacket(
      _VmncChunk(0, 0, 2, 1, 0, [0x1F, 0, 0x1F, 0]),
      defineCursor,
      moveCursor
    );

    Assert.That(decoder.TryDecode(new(0, packet), out var withCursor), Is.True);
    Assert.That(withCursor.PixelData, Is.EqualTo(new byte[] { 0, 0, 255, 255, 0, 0 }));

    Assert.That(decoder.TryDecode(new(0, _VmncPacket(_VmncChunk(0, 0, 0, 0, 0x574D5666, []))), out var moved), Is.True);
    Assert.That(moved.PixelData, Is.EqualTo(new byte[] { 255, 0, 0, 0, 0, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void TdscRawTileUpdatesPersistentCanvas() {
    var decoder = TdscVideoDecoder.Create(_Stream("TSDC", 32, 32, 24));
    var full = Enumerable.Range(0, 32 * 32)
      .SelectMany(_ => new byte[] { 1, 2, 3 })
      .ToArray();

    Assert.That(decoder.TryDecode(new(0, _TdscFrame(32, 32, (0, 0, 32, 32, 0x57415220u, full))), out var first), Is.True);
    Assert.That(first.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(first.PixelData.AsSpan(0, 3).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
    Assert.That(first.PixelData.AsSpan(^3).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));

    Assert.That(decoder.TryDecode(new(0, _TdscFrame(32, 32, (1, 1, 2, 2, 0x57415220u, new byte[] { 9, 8, 7 }))), out var second), Is.True);
    Assert.That(second.PixelData.AsSpan((1 * 32 + 1) * 3, 3).ToArray(), Is.EqualTo(new byte[] { 9, 8, 7 }));
    Assert.That(second.PixelData.AsSpan(0, 3).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
  }

  [Test]
  [Category("Unit")]
  public void TdscJpegTileUsesRepositoryJpegDecoder() {
    var decoder = TdscVideoDecoder.Create(_Stream("TSDC", 32, 32, 24));
    var rgb = Enumerable.Repeat(new byte[] { 80, 80, 80 }, 8 * 8).SelectMany(static value => value).ToArray();
    var jpeg = FormatIO.Encode<JpegFile>(new RawImage {
      Width = 8,
      Height = 8,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    });

    Assert.That(decoder.TryDecode(new(0, _TdscFrame(32, 32, (0, 0, 8, 8, 0x4A504547u, jpeg))), out var frame), Is.True);
    Assert.That(frame.PixelData[0], Is.EqualTo(80).Within(3));
    Assert.That(frame.PixelData[1], Is.EqualTo(80).Within(3));
    Assert.That(frame.PixelData[2], Is.EqualTo(80).Within(3));
  }

  [Test]
  [Category("Unit")]
  public void TdscBgraCursorIsCompositedOnlyOnReturnedFrame() {
    var decoder = TdscVideoDecoder.Create(_Stream("TSDC", 32, 32, 24));
    var cursor = _TdscCursorPacket(0, 0, [0, 0, 255, 255]);

    Assert.That(decoder.TryDecode(new(0, cursor), out var frame), Is.True);
    Assert.That(frame.PixelData.AsSpan(0, 3).ToArray(), Is.EqualTo(new byte[] { 0, 0, 254 }));

    var moved = _TdscCursorPositionPacket(1, 0);
    Assert.That(decoder.TryDecode(new(0, moved), out var second), Is.True);
    Assert.That(second.PixelData.AsSpan(0, 3).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0 }));
    Assert.That(second.PixelData.AsSpan(3, 3).ToArray(), Is.EqualTo(new byte[] { 0, 0, 254 }));
  }

  [Test]
  [Category("Unit")]
  public void MalformedScreenUpdatesAreRejected() {
    var vmnc = VmncVideoDecoder.Create(_Stream("VMnc", 2, 1, 16));
    Assert.That(
      () => vmnc.TryDecode(new(0, _VmncPacket(_VmncChunk(0, 0, 2, 1, 5, [0x01, 0, 0]))), out _),
      Throws.TypeOf<InvalidDataException>()
    );

    var tdsc = TdscVideoDecoder.Create(_Stream("TSDC", 32, 32, 24));
    Assert.That(
      () => tdsc.TryDecode(new(0, _TdscFrame(32, 32, (31, 31, 33, 32, 0x57415220u, [1, 2, 3]))), out _),
      Throws.TypeOf<InvalidDataException>()
    );
  }

  [Test]
  [Category("Unit")]
  public void BothRemainingScreenCodecsAreRegistered() {
    var names = VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName).ToArray();
    Assert.That(names, Does.Contain("VMware Screen Codec / VMware Video"));
    Assert.That(names, Does.Contain("TDSC"));
  }

  private static MediaStreamInfo _Stream(string codec, int width, int height, short bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
  };

  private static byte[] _VmncPacket(params byte[][] chunks) {
    using var output = new MemoryStream();
    output.WriteByte(0);
    output.WriteByte(0);
    _WriteUInt16BigEndian(output, checked((ushort)chunks.Length));
    foreach (var chunk in chunks)
      output.Write(chunk);
    return output.ToArray();
  }

  private static byte[] _VmncChunk(ushort x, ushort y, ushort width, ushort height, uint encoding, byte[] payload) {
    using var output = new MemoryStream();
    _WriteUInt16BigEndian(output, x);
    _WriteUInt16BigEndian(output, y);
    _WriteUInt16BigEndian(output, width);
    _WriteUInt16BigEndian(output, height);
    _WriteUInt32BigEndian(output, encoding);
    output.Write(payload);
    return output.ToArray();
  }

  private static byte[] _TdscFrame(int width, int height, params (int X, int Y, int X2, int Y2, uint Mode, byte[] Data)[] tiles) {
    using var inflated = new MemoryStream();
    using (var writer = new BinaryWriter(inflated, System.Text.Encoding.UTF8, leaveOpen: true)) {
      writer.Write(0x46534454u); // TDSF
      writer.Write((uint)tiles.Length);
      writer.Write(0u);
      writer.Write(0x30u);
      writer.Write(40u);
      writer.Write(width);
      writer.Write(-height);
      writer.Write((ushort)1);
      writer.Write((ushort)24);
      writer.Write(new byte[24]);
      foreach (var tile in tiles) {
        writer.Write(0x42534454u); // TDSB
        writer.Write((uint)tile.Data.Length);
        writer.Write(tile.Mode);
        writer.Write(0u);
        writer.Write(tile.X);
        writer.Write(tile.Y);
        writer.Write(tile.X2);
        writer.Write(tile.Y2);
        writer.Write(tile.Data);
      }
    }
    return _Zlib(inflated.ToArray());
  }

  private static byte[] _TdscCursorPacket(int x, int y, byte[] bgra) {
    using var payload = new MemoryStream();
    using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, leaveOpen: true)) {
      writer.Write(3u);
      writer.Write(0u);
      writer.Write(x);
      writer.Write(y);
      writer.Write((ushort)0);
      writer.Write((ushort)0);
      writer.Write((ushort)1);
      writer.Write((ushort)1);
      writer.Write(0x20010004u);
      writer.Write(new byte[4]);
      writer.Write(bgra);
    }
    return _TdscDtsm(payload.ToArray());
  }

  private static byte[] _TdscCursorPositionPacket(int x, int y) {
    using var payload = new MemoryStream();
    using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, leaveOpen: true)) {
      writer.Write(2u);
      writer.Write(0u);
      writer.Write(x);
      writer.Write(y);
    }
    return _TdscDtsm(payload.ToArray());
  }

  private static byte[] _TdscDtsm(byte[] payload) {
    using var inflated = new MemoryStream();
    using (var writer = new BinaryWriter(inflated, System.Text.Encoding.UTF8, leaveOpen: true)) {
      writer.Write(0x4D535444u); // DTSM
      writer.Write((uint)payload.Length);
      writer.Write(payload);
    }
    return _Zlib(inflated.ToArray());
  }

  private static byte[] _Zlib(ReadOnlySpan<byte> data) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
      zlib.Write(data);
    return output.ToArray();
  }

  private static void _WriteUInt16BigEndian(Stream output, ushort value) {
    Span<byte> bytes = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
    output.Write(bytes);
  }

  private static void _WriteUInt32BigEndian(Stream output, uint value) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
    output.Write(bytes);
  }
}
