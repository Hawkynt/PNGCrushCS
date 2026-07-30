using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.IffRgbn;

namespace FileFormat.IffRgbn.Tests;

[TestFixture]
public sealed class IffRgbnReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffRgbnReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffRgbnReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rgbn"));
    Assert.Throws<FileNotFoundException>(() => IffRgbnReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffRgbnReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => IffRgbnReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[12];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'Z';
    Assert.Throws<InvalidDataException>(() => IffRgbnReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormType_ThrowsInvalidDataException() {
    var bad = new byte[40];
    bad[0] = (byte)'F'; bad[1] = (byte)'O'; bad[2] = (byte)'R'; bad[3] = (byte)'M';
    BinaryPrimitives.WriteInt32BigEndian(bad.AsSpan(4), 20);
    bad[8] = (byte)'I'; bad[9] = (byte)'L'; bad[10] = (byte)'B'; bad[11] = (byte)'M';
    Assert.Throws<InvalidDataException>(() => IffRgbnReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgbn_ParsesDimensions() {
    var original = new IffRgbnFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3],
    };

    var data = IffRgbnWriter.ToBytes(original);
    var result = IffRgbnReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelData.Length, Is.EqualTo(2 * 2 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgbn_ParsesPixelColors() {
    // Create file with a known pixel: R=0xFF -> quantize to 15 -> expand to 255
    var original = new IffRgbnFile {
      Width = 1,
      Height = 1,
      PixelData = [0xFF, 0x00, 0x00],
    };

    var data = IffRgbnWriter.ToBytes(original);
    var result = IffRgbnReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(255)); // R = 15 * 17 = 255
      Assert.That(result.PixelData[1], Is.EqualTo(0));   // G = 0 * 17 = 0
      Assert.That(result.PixelData[2], Is.EqualTo(0));   // B = 0 * 17 = 0
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RleRepeat_ExpandsCorrectly() {
    // Manually build a RGBN file with repeat encoding
    using var ms = new MemoryStream();
    ms.Write(Encoding.ASCII.GetBytes("FORM"));
    _WriteInt32BE(ms, 0); // placeholder
    ms.Write(Encoding.ASCII.GetBytes("RGBN"));

    // BMHD chunk: 4x1 image
    ms.Write(Encoding.ASCII.GetBytes("BMHD"));
    _WriteInt32BE(ms, 20);
    var bmhd = new byte[20];
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(0), 4);  // width
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(2), 1);  // height
    bmhd[8] = 13; // numPlanes
    ms.Write(bmhd);

    // BODY chunk: 1 pixel unit with repeat=3 -> 4 pixels total
    ms.Write(Encoding.ASCII.GetBytes("BODY"));
    _WriteInt32BE(ms, 2);
    var hi = (byte)(0x0F << 4 | 0x00); // R=15, G=0
    var lo = (byte)(0x08 << 4 | 3);    // B=8, genlock=0, repeat=3 -> emit 4 times
    ms.WriteByte(hi);
    ms.WriteByte(lo);

    // Fix FORM size
    var bytes = ms.ToArray();
    BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4), bytes.Length - 8);

    var result = IffRgbnReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(1));
      Assert.That(result.PixelData.Length, Is.EqualTo(4 * 3));
      for (var i = 0; i < 4; ++i) {
        Assert.That(result.PixelData[i * 3], Is.EqualTo(255), $"Pixel {i} R");      // 15 * 17
        Assert.That(result.PixelData[i * 3 + 1], Is.EqualTo(0), $"Pixel {i} G");    // 0 * 17
        Assert.That(result.PixelData[i * 3 + 2], Is.EqualTo(136), $"Pixel {i} B");  // 8 * 17
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgbn_ParsesCorrectly() {
    var original = new IffRgbnFile {
      Width = 2,
      Height = 1,
      PixelData = [0xAA, 0xBB, 0xCC, 0x11, 0x22, 0x33],
    };

    var data = IffRgbnWriter.ToBytes(original);
    using var stream = new MemoryStream(data);
    var result = IffRgbnReader.FromStream(stream);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_GenlockBitIgnored_ParsesCorrectly() {
    // Manually build a file where genlock bit is set
    using var ms = new MemoryStream();
    ms.Write(Encoding.ASCII.GetBytes("FORM"));
    _WriteInt32BE(ms, 0);
    ms.Write(Encoding.ASCII.GetBytes("RGBN"));

    ms.Write(Encoding.ASCII.GetBytes("BMHD"));
    _WriteInt32BE(ms, 20);
    var bmhd = new byte[20];
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(0), 1);
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(2), 1);
    bmhd[8] = 13;
    ms.Write(bmhd);

    ms.Write(Encoding.ASCII.GetBytes("BODY"));
    _WriteInt32BE(ms, 2);
    var hi = (byte)(0x0A << 4 | 0x05); // R=10, G=5
    var lo = (byte)(0x07 << 4 | 0x08); // B=7, genlock=1, repeat=0
    ms.WriteByte(hi);
    ms.WriteByte(lo);

    var bytes = ms.ToArray();
    BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4), bytes.Length - 8);

    var result = IffRgbnReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(170)); // 10 * 17
      Assert.That(result.PixelData[1], Is.EqualTo(85));   // 5 * 17
      Assert.That(result.PixelData[2], Is.EqualTo(119));  // 7 * 17
    });
  }

  private static void _WriteInt32BE(Stream stream, int value) {
    Span<byte> buf = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(buf, value);
    stream.Write(buf);
  }
}
