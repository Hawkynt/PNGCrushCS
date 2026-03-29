using System;
using System.IO;
using FileFormat.Lss16;

namespace FileFormat.Lss16.Tests;

[TestFixture]
public sealed class Lss16ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Lss16Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Lss16Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lss"));
    Assert.Throws<FileNotFoundException>(() => Lss16Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Lss16Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[55];
    Assert.Throws<InvalidDataException>(() => Lss16Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[60];
    data[0] = 0xFF;
    data[1] = 0xFF;
    data[2] = 0xFF;
    data[3] = 0xFF;
    data[4] = 4;
    data[6] = 1;
    Assert.Throws<InvalidDataException>(() => Lss16Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = _BuildHeader(0, 1);
    Assert.Throws<InvalidDataException>(() => Lss16Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = _BuildHeader(1, 0);
    Assert.Throws<InvalidDataException>(() => Lss16Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSolidColor_ParsesDimensions() {
    var data = _BuildSolidColorFile(4, 2, 0);

    var result = Lss16Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_PalettePreserved() {
    var data = _BuildHeader(4, 1);
    data[8] = 63;
    data[9] = 32;
    data[10] = 0;

    var fullData = new byte[data.Length + 10];
    Array.Copy(data, fullData, data.Length);

    var result = Lss16Reader.FromBytes(fullData);

    Assert.That(result.Palette[0], Is.EqualTo(63));
    Assert.That(result.Palette[1], Is.EqualTo(32));
    Assert.That(result.Palette[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_PixelDataLength() {
    var data = _BuildSolidColorFile(4, 2, 0);

    var result = Lss16Reader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(4 * 2));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = _BuildSolidColorFile(4, 1, 0);

    using var ms = new MemoryStream(data);
    var result = Lss16Reader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(1));
  }

  private static byte[] _BuildHeader(int width, int height) {
    var data = new byte[Lss16File.HeaderSize];
    data[0] = 0x3D;
    data[1] = 0xF3;
    data[2] = 0x13;
    data[3] = 0x14;
    data[4] = (byte)(width & 0xFF);
    data[5] = (byte)((width >> 8) & 0xFF);
    data[6] = (byte)(height & 0xFF);
    data[7] = (byte)((height >> 8) & 0xFF);
    return data;
  }

  /// <summary>Builds a valid LSS16 file filled with a single color index (using RLE runs).</summary>
  private static byte[] _BuildSolidColorFile(int width, int height, byte colorIndex) {
    using var ms = new MemoryStream();

    var header = _BuildHeader(width, height);
    ms.Write(header, 0, header.Length);

    for (var y = 0; y < height; ++y) {
      var remaining = width;
      byte previous = 0;

      if (colorIndex != previous) {
        var nybbles = new System.Collections.Generic.List<int>();
        nybbles.Add(colorIndex);
        --remaining;

        while (remaining > 0) {
          if (remaining <= 15) {
            nybbles.Add(colorIndex);
            nybbles.Add(remaining);
            remaining = 0;
          } else {
            var chunk = Math.Min(remaining, 255 + 16);
            nybbles.Add(colorIndex);
            nybbles.Add(0);
            var encoded = chunk - 16;
            nybbles.Add((encoded >> 4) & 0x0F);
            nybbles.Add(encoded & 0x0F);
            remaining -= chunk;
          }
        }

        for (var i = 0; i < nybbles.Count; i += 2) {
          var lo = nybbles[i];
          var hi = i + 1 < nybbles.Count ? nybbles[i + 1] : 0;
          ms.WriteByte((byte)(lo | (hi << 4)));
        }
      } else {
        var nybbles = new System.Collections.Generic.List<int>();
        while (remaining > 0) {
          if (remaining <= 15) {
            nybbles.Add(0);
            nybbles.Add(remaining);
            remaining = 0;
          } else {
            var chunk = Math.Min(remaining, 255 + 16);
            nybbles.Add(0);
            nybbles.Add(0);
            var encoded = chunk - 16;
            nybbles.Add((encoded >> 4) & 0x0F);
            nybbles.Add(encoded & 0x0F);
            remaining -= chunk;
          }
        }

        for (var i = 0; i < nybbles.Count; i += 2) {
          var lo = nybbles[i];
          var hi = i + 1 < nybbles.Count ? nybbles[i + 1] : 0;
          ms.WriteByte((byte)(lo | (hi << 4)));
        }
      }
    }

    return ms.ToArray();
  }
}
