extern alias Images;

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Core;
using Images::FileFormat.Jpeg;
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

    Assert.That(decoder.TryDecode(new(0, _TdscFrame(32, 32, (0, 0, 32, 32, 0x52415720u, full))), out var first), Is.True);
    Assert.That(first.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(first.PixelData.AsSpan(0, 3).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
    Assert.That(first.PixelData.AsSpan(^3).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));

    Assert.That(decoder.TryDecode(new(0, _TdscFrame(32, 32, (1, 1, 2, 2, 0x52415720u, new byte[] { 9, 8, 7 }))), out var second), Is.True);
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
      () => tdsc.TryDecode(new(0, _TdscFrame(32, 32, (31, 31, 33, 32, 0x52415720u, [1, 2, 3]))), out _),
      Throws.TypeOf<InvalidDataException>()
    );
  }

  // ============================================================================================
  // Vectors taken from the reference decoder
  //
  // Every expectation below was produced by muxing exactly the packet the test builds into an AVI
  // and reading the picture back out of FFmpeg 9.0.1 (`libavcodec/vmnc.c`, `libavcodec/tdsc.c`).
  // They are here because the tests above only prove this decoder agrees with itself; these prove
  // it agrees with the decoder every other player uses.
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void VmncWidensFiveBitChannelsTheWayTheReferenceDecoderDoes() {
    var decoder = VmncVideoDecoder.Create(_Stream("VMnc", 32, 1, 16));
    var descriptor = new byte[] {
      16, 16, 0, 1,       // 16 stored bits, depth 16, little-endian, true colour
      0, 31, 0, 31, 0, 31, // channel maxima
      10, 5, 0,           // channel shifts
      0, 0, 0,            // padding
    };
    var levels = new byte[64];
    for (var value = 0; value < 32; ++value)
      BinaryPrimitives.WriteUInt16LittleEndian(levels.AsSpan(value * 2), (ushort)((value << 10) | (value << 5) | value));

    var packet = _VmncPacket(
      _VmncChunk(0, 0, 0, 0, 0x574D5669, descriptor),
      _VmncChunk(0, 0, 32, 1, 0, levels)
    );

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    // A five-bit channel repeats its own bits: 3 widens to 24, not to the 25 a proportional
    // rounding would produce.
    var expected = new byte[] {
      0, 8, 16, 24, 33, 41, 49, 57, 66, 74, 82, 90, 99, 107, 115, 123,
      132, 140, 148, 156, 165, 173, 181, 189, 198, 206, 214, 222, 231, 239, 247, 255,
    };
    for (var value = 0; value < 32; ++value)
      Assert.That(frame.PixelData.AsSpan(value * 3, 3).ToArray(),
        Is.EqualTo(new[] { expected[value], expected[value], expected[value] }),
        $"five-bit level {value}");
  }

  [Test]
  [Category("Unit")]
  public void VmncHextileSubrectangleMayReachPastItsOwnTile() {
    var decoder = VmncVideoDecoder.Create(_Stream("VMnc", 20, 2, 16));
    var firstTile = new byte[] {
      2 | 4 | 8,   // background, foreground and sub-rectangles
      0xEB, 0x0C,  // background
      0x00, 0x7C,  // red foreground
      1,           // one sub-rectangle
      0x10,        // at x=1, y=0 …
      0xF1,        // … sixteen wide and two tall, which leaves this sixteen-pixel tile
    };
    var secondTile = new byte[] { 2, 0xE0, 0x03 };

    Assert.That(decoder.TryDecode(new(0, _VmncPacket(_VmncChunk(0, 0, 20, 2, 5, [.. firstTile, .. secondTile]))), out var frame), Is.True);
    var row = new byte[60];
    row[0] = 24; row[1] = 57; row[2] = 90;
    for (var x = 1; x < 16; ++x)
      row[x * 3] = 255;
    for (var x = 16; x < 20; ++x)
      row[x * 3 + 1] = 255;
    Assert.That(frame.PixelData.AsSpan(0, 60).ToArray(), Is.EqualTo(row));
    Assert.That(frame.PixelData.AsSpan(60, 60).ToArray(), Is.EqualTo(row));
  }

  [Test]
  [Category("Unit")]
  public void VmncCursorIsExclusiveOredOverTheCanvas() {
    var decoder = VmncVideoDecoder.Create(_Stream("VMnc", 6, 4, 16));
    var canvas = new byte[48];
    for (var i = 0; i < 24; ++i)
      BinaryPrimitives.WriteUInt16LittleEndian(canvas.AsSpan(i * 2), (7 << 10) | (9 << 5) | 11);
    var bits = new byte[12];
    var mask = new byte[12];
    for (var i = 0; i < 6; ++i) {
      BinaryPrimitives.WriteUInt16LittleEndian(bits.AsSpan(i * 2), (ushort)(i % 2 == 0 ? 0x7FFF : 0));
      BinaryPrimitives.WriteUInt16LittleEndian(mask.AsSpan(i * 2), (ushort)(i % 3 == 0 ? 31 : 0));
    }

    var first = _VmncPacket(
      _VmncChunk(0, 0, 6, 4, 0, canvas),
      _VmncChunk(0, 0, 3, 2, 0x574D5664, [0, 0, .. bits, .. mask]),
      _VmncChunk(1, 1, 0, 0, 0x574D5666, [])
    );
    Assert.That(decoder.TryDecode(new(0, first), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(_Filled([57, 74, 90], 24,
      (7, [57, 74, 165]), (8, [0, 0, 0]), (13, [0, 0, 255]), (15, [0, 0, 0]))));

    Assert.That(decoder.TryDecode(new(0, _VmncPacket(_VmncChunk(3, 2, 0, 0, 0x574D5666, []))), out var moved), Is.True);
    Assert.That(moved.PixelData, Is.EqualTo(_Filled([57, 74, 90], 24,
      (15, [57, 74, 165]), (16, [0, 0, 0]), (21, [0, 0, 255]), (23, [0, 0, 0]))));
  }

  [Test]
  [Category("Unit")]
  public void VmncUnknownEncodingEndsThePacketAndKeepsThePicture() {
    var decoder = VmncVideoDecoder.Create(_Stream("VMnc", 4, 1, 16));
    var canvas = new byte[8];
    for (var i = 0; i < 4; ++i)
      BinaryPrimitives.WriteUInt16LittleEndian(canvas.AsSpan(i * 2), (31 << 10) | (16 << 5) | 3);

    var packet = _VmncPacket(
      _VmncChunk(0, 0, 4, 1, 0, canvas),
      _VmncChunk(0, 0, 0, 0, 0x12345678, [])
    );

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(_Filled([255, 132, 24], 4)));
  }

  [Test]
  [Category("Unit")]
  public void Vmnc32BitRawKeepsTheReferenceChannelOrder() {
    var decoder = VmncVideoDecoder.Create(_Stream("VMnc", 4, 1, 32));
    var canvas = new byte[16];
    for (var i = 0; i < 4; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(canvas.AsSpan(i * 4), (uint)(((200 + i) << 16) | ((100 + i) << 8) | (50 + i)));

    Assert.That(decoder.TryDecode(new(0, _VmncPacket(_VmncChunk(0, 0, 4, 1, 0, canvas))), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      200, 100, 50, 201, 101, 51, 202, 102, 52, 203, 103, 53,
    }));
  }

  [Test]
  [Category("Unit")]
  public void TdscRawTileMatchesTheReferenceDecoderPixelForPixel() {
    var decoder = TdscVideoDecoder.Create(_Stream("TDSC", 24, 8, 24));

    Assert.That(decoder.TryDecode(new(0, _TdscFrame(24, 8, (0, 0, 24, 8, 0x52415720u, _TdscCanvas(24 * 8)))), out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(frame.PixelData, Is.EqualTo(_TdscCanvas(24 * 8)));
  }

  [Test]
  [Category("Unit")]
  public void TdscBgraCursorBlendsTheWayTheReferenceDecoderDoes() {
    var decoder = TdscVideoDecoder.Create(_Stream("TDSC", 24, 8, 24));
    var sprite = new byte[24];
    for (var i = 0; i < 6; ++i) {
      sprite[i * 4] = (byte)(i * 37);
      sprite[i * 4 + 1] = (byte)(i * 53);
      sprite[i * 4 + 2] = (byte)(i * 71);
      sprite[i * 4 + 3] = (byte)(i * 97);
    }
    var withCursor = _TdscConcat(
      _TdscFrameBody(24, 8, (0, 0, 24, 8, 0x52415720u, _TdscCanvas(24 * 8))),
      _TdscDtsmBody([
        .. BitConverter.GetBytes(3), .. BitConverter.GetBytes(0),
        .. BitConverter.GetBytes(3), .. BitConverter.GetBytes(1),
        1, 0, 0, 0, 3, 0, 2, 0,
        .. BitConverter.GetBytes(0x20010004),
        .. new byte[8],
        .. sprite,
      ]));

    Assert.That(decoder.TryDecode(new(0, withCursor), out var frame), Is.True);
    Assert.That(_Window(frame, 24, 2, 1, 4, 2), Is.EqualTo(new byte[] {
      182, 82, 242, 131, 79, 36, 103, 106, 118, 203, 121, 73,
      96, 140, 175, 125, 182, 110, 176, 25, 112, 115, 177, 1,
    }));

    var moved = _TdscDtsm([
      .. BitConverter.GetBytes(2), .. BitConverter.GetBytes(0),
      .. BitConverter.GetBytes(9), .. BitConverter.GetBytes(4),
    ]);
    Assert.That(decoder.TryDecode(new(0, moved), out var second), Is.True);
    Assert.That(_Window(second, 24, 2, 1, 4, 2), Is.EqualTo(new byte[] {
      182, 82, 242, 189, 95, 15, 196, 108, 44, 203, 121, 73,
      94, 138, 170, 101, 151, 199, 108, 164, 228, 115, 177, 1,
    }));
  }

  [Test]
  [Category("Unit")]
  public void TdscMonochromeCursorMatchesTheReferenceDecoder() {
    var decoder = TdscVideoDecoder.Create(_Stream("TDSC", 24, 8, 24));
    var packet = _TdscConcat(
      _TdscFrameBody(24, 8, (0, 0, 24, 8, 0x52415720u, _TdscCanvas(24 * 8))),
      _TdscDtsmBody([
        .. BitConverter.GetBytes(3), .. BitConverter.GetBytes(0),
        .. BitConverter.GetBytes(2), .. BitConverter.GetBytes(1),
        0, 0, 0, 0, 4, 0, 2, 0,
        .. BitConverter.GetBytes(0x01010004),
        0b1011_0000, 0, 0, 0,
        0b0101_0000, 0, 0, 0,
        0b1100_0000, 0, 0, 0,
        0b1010_0000, 0, 0, 0,
      ]));

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(_Window(frame, 24, 2, 1, 4, 2), Is.EqualTo(new byte[] {
      182, 82, 242, 254, 254, 254, 196, 108, 44, 203, 121, 73,
      254, 254, 254, 101, 151, 199, 254, 254, 254, 115, 177, 1,
    }));
  }

  [Test]
  [Category("Unit")]
  public void BothRemainingScreenCodecsAreRegistered() {
    var names = VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName).ToArray();
    Assert.That(names, Does.Contain("VMware Screen Codec / VMware Video"));
    Assert.That(names, Does.Contain("TDSC"));
  }


  /// <summary>The canvas the TDSC vectors paint, which is what the reference decoder hands back.</summary>
  private static byte[] _TdscCanvas(int pixels) {
    var result = new byte[pixels * 3];
    for (var i = 0; i < pixels; ++i) {
      result[i * 3] = (byte)(i * 7);
      result[i * 3 + 1] = (byte)(i * 13);
      result[i * 3 + 2] = (byte)(i * 29);
    }
    return result;
  }

  private static byte[] _Window(RawImage frame, int width, int x, int y, int windowWidth, int windowHeight) {
    var result = new byte[windowWidth * windowHeight * 3];
    for (var row = 0; row < windowHeight; ++row)
      frame.PixelData.AsSpan(((y + row) * width + x) * 3, windowWidth * 3)
        .CopyTo(result.AsSpan(row * windowWidth * 3));
    return result;
  }

  /// <summary>One pixel repeated, with the named pixels replaced.</summary>
  private static byte[] _Filled(byte[] pixel, int count, params (int Index, byte[] Pixel)[] patches) {
    var result = new byte[count * 3];
    for (var i = 0; i < count; ++i)
      pixel.CopyTo(result, i * 3);
    foreach (var (index, replacement) in patches)
      replacement.CopyTo(result, index * 3);
    return result;
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

  private static byte[] _TdscFrame(int width, int height, params (int X, int Y, int X2, int Y2, uint Mode, byte[] Data)[] tiles)
    => _Zlib(_TdscFrameBody(width, height, tiles));

  private static byte[] _TdscConcat(byte[] first, byte[] second) => _Zlib([.. first, .. second]);

  private static byte[] _TdscFrameBody(int width, int height, params (int X, int Y, int X2, int Y2, uint Mode, byte[] Data)[] tiles) {
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
    return inflated.ToArray();
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

  private static byte[] _TdscDtsm(byte[] payload) => _Zlib(_TdscDtsmBody(payload));

  private static byte[] _TdscDtsmBody(byte[] payload) {
    using var inflated = new MemoryStream();
    using (var writer = new BinaryWriter(inflated, System.Text.Encoding.UTF8, leaveOpen: true)) {
      writer.Write(0x4D535444u); // DTSM
      writer.Write((uint)payload.Length);
      writer.Write(payload);
    }
    return inflated.ToArray();
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
