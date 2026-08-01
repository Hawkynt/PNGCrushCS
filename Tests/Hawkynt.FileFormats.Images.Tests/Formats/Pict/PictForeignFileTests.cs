using System;
using System.Buffers.Binary;
using FileFormat.Core;
using FileFormat.Pict;

namespace FileFormat.Pict.Tests;

/// <summary>Reads a PICT laid out the way a real one is, opcodes and all.</summary>
/// <remarks>
/// A PICT is a recording of drawing commands, and the image is one command among several. The reader
/// gave up at the first opcode it did not recognise, and every real picture sets a clipping region
/// before it draws anything — so it stopped one instruction short of the pixels every time and
/// returned a blank frame. It never got far enough for its second fault to show: a 32-bit pixmap
/// reserves four bytes a pixel in <c>rowBytes</c> but stores only the three colour planes it uses, so
/// a 40-pixel row unpacks to 120 bytes and not the 160 <c>rowBytes</c> claims.
/// </remarks>
[TestFixture]
public sealed class PictForeignFileTests {

  private const int _WIDTH = 40;
  private const int _HEIGHT = 24;

  [Test]
  [Category("Unit")]
  public void Read_PictureWithAClippingRegion_ReachesTheImage() {
    var file = PictReader.FromBytes(_Build());

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(_WIDTH));
      Assert.That(file.Height, Is.EqualTo(_HEIGHT));
      Assert.That(file.BitsPerPixel, Is.EqualTo(24), "a blank frame comes back with no depth at all");
      Assert.That(file.PixelData, Has.Length.EqualTo(_WIDTH * _HEIGHT * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_ThirtyTwoBitPixmap_SeparatesItsColourPlanes() {
    var rgb = PictFile.ToRawImage(PictReader.FromBytes(_Build())).ToRgb24();

    (int X, int Y, byte R, byte G, byte B)[] expected = [
      (10, 6, 255, 0, 0),
      (30, 6, 0, 255, 0),
      (10, 18, 0, 0, 255),
      (30, 18, 255, 255, 255),
    ];

    Assert.Multiple(() => {
      foreach (var (x, y, r, g, b) in expected) {
        var at = ((y * _WIDTH) + x) * 3;
        Assert.That((rgb[at], rgb[at + 1], rgb[at + 2]), Is.EqualTo((r, g, b)), $"pixel {x},{y}");
      }
    });
  }

  /// <summary>
  /// Builds the picture QuickDraw would record for the four-quadrant image: a header, a clipping
  /// region, and one 32-bit DirectBitsRect whose rows are PackBits-compressed a plane at a time.
  /// </summary>
  private static byte[] _Build() {
    var w = new _Writer();
    w.Skip(512);            // the launcher's preamble, which carries nothing
    w.U16(0);               // picture size, unreliable and ignored
    w.Rect(0, 0, _HEIGHT, _WIDTH);
    w.U16(0x0011); w.U16(0x02FF); // VersionOp, version 2

    w.U16(0x0C00);          // HeaderOp
    w.U16(0xFFFE); w.U16(0);
    w.U32(0x00480000); w.U32(0x00480000); // 72 dpi each way
    w.Rect(0, 0, _HEIGHT, _WIDTH);
    w.U32(0);

    // ClipRgn: a region is its own length, then its bounding box. This is the opcode the reader used
    // to stop at.
    w.U16(0x0001);
    w.U16(10);
    w.Rect(0, 0, _HEIGHT, _WIDTH);

    w.U16(0x009A);          // DirectBitsRect
    w.U32(0x000000FF);      // baseAddr
    w.U16(0x8000 | (_WIDTH * 4)); // rowBytes, with the flag that says "this is a pixmap"
    w.Rect(0, 0, _HEIGHT, _WIDTH);
    w.U16(0);               // pmVersion
    w.U16(4);               // packType: PackBits, a colour plane at a time
    w.U32(0);               // packSize
    w.U32(0x00480000); w.U32(0x00480000);
    w.U16(16);              // pixelType: RGBDirect
    w.U16(32);              // pixelSize
    w.U16(3);               // cmpCount — three planes, not four
    w.U16(8);               // cmpSize
    w.U32(0); w.U32(0); w.U32(0); // planeBytes, pmTable, pmReserved
    w.Rect(0, 0, _HEIGHT, _WIDTH); // srcRect
    w.Rect(0, 0, _HEIGHT, _WIDTH); // dstRect
    w.U16(0x40);            // transfer mode

    for (var y = 0; y < _HEIGHT; ++y) {
      var top = y < _HEIGHT / 2;
      // Left half then right half: red|green on top, blue|white below.
      var left = top ? (r: 255, g: 0, b: 0) : (r: 0, g: 0, b: 255);
      var right = top ? (r: 0, g: 255, b: 0) : (r: 255, g: 255, b: 255);

      var row = new byte[_WIDTH * 3];
      for (var x = 0; x < _WIDTH; ++x) {
        var (r, g, b) = x < _WIDTH / 2 ? left : right;
        row[x] = (byte)r;
        row[_WIDTH + x] = (byte)g;
        row[(_WIDTH * 2) + x] = (byte)b;
      }

      var packed = _PackBits(row);
      w.U8((byte)packed.Length); // rowBytes is under 250, so the count is one byte
      w.Bytes(packed);
    }

    w.U16(0x00FF); // EndOfPicture
    return w.ToArray();
  }

  /// <summary>Compresses a run of bytes the way QuickDraw does.</summary>
  private static byte[] _PackBits(byte[] source) {
    var output = new System.Collections.Generic.List<byte>();
    var at = 0;

    while (at < source.Length) {
      var runEnd = at;
      while (runEnd + 1 < source.Length && source[runEnd + 1] == source[at] && runEnd - at < 126)
        ++runEnd;

      if (runEnd > at) {
        output.Add((byte)(sbyte)-(runEnd - at));
        output.Add(source[at]);
        at = runEnd + 1;
        continue;
      }

      // No run here, so copy literally until one starts.
      var literalEnd = at;
      while (literalEnd + 1 < source.Length && source[literalEnd + 1] != source[literalEnd] && literalEnd - at < 126)
        ++literalEnd;

      output.Add((byte)(literalEnd - at));
      for (var i = at; i <= literalEnd; ++i)
        output.Add(source[i]);

      at = literalEnd + 1;
    }

    return output.ToArray();
  }

  private sealed class _Writer {
    private readonly System.Collections.Generic.List<byte> _bytes = [];

    public void Skip(int count) => _bytes.AddRange(new byte[count]);
    public void U8(byte value) => _bytes.Add(value);
    public void Bytes(byte[] value) => _bytes.AddRange(value);

    public void U16(int value) {
      var buffer = new byte[2];
      BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
      _bytes.AddRange(buffer);
    }

    public void U32(uint value) {
      var buffer = new byte[4];
      BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
      _bytes.AddRange(buffer);
    }

    public void Rect(int top, int left, int bottom, int right) {
      U16(top);
      U16(left);
      U16(bottom);
      U16(right);
    }

    public byte[] ToArray() => _bytes.ToArray();
  }
}
