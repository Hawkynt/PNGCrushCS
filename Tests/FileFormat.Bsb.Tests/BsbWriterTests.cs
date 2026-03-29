using System;
using System.Text;
using FileFormat.Bsb;

namespace FileFormat.Bsb.Tests;

[TestFixture]
public sealed class BsbWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BsbWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderContainsVER() {
    var file = _CreateMinimalFile(2, 1);
    var bytes = BsbWriter.ToBytes(file);
    var headerEnd = Array.IndexOf(bytes, (byte)0x00);
    var header = Encoding.ASCII.GetString(bytes, 0, headerEnd);

    Assert.That(header, Does.Contain("VER/"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderContainsBSB() {
    var file = _CreateMinimalFile(4, 2);
    var bytes = BsbWriter.ToBytes(file);
    var headerEnd = Array.IndexOf(bytes, (byte)0x00);
    var header = Encoding.ASCII.GetString(bytes, 0, headerEnd);

    Assert.That(header, Does.Contain("BSB/"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderContainsDimensions() {
    var file = _CreateMinimalFile(320, 240);
    var bytes = BsbWriter.ToBytes(file);
    var headerEnd = Array.IndexOf(bytes, (byte)0x00);
    var header = Encoding.ASCII.GetString(bytes, 0, headerEnd);

    Assert.That(header, Does.Contain("RA=320,240"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderContainsName() {
    var file = new BsbFile {
      Width = 2,
      Height = 1,
      PixelData = new byte[2],
      Palette = [255, 0, 0],
      PaletteCount = 1,
      Name = "MyChart",
    };

    var bytes = BsbWriter.ToBytes(file);
    var headerEnd = Array.IndexOf(bytes, (byte)0x00);
    var header = Encoding.ASCII.GetString(bytes, 0, headerEnd);

    Assert.That(header, Does.Contain("NA=MyChart"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderContainsPaletteEntries() {
    var file = new BsbFile {
      Width = 1,
      Height = 1,
      PixelData = [0],
      Palette = [128, 64, 32, 10, 20, 30],
      PaletteCount = 2,
    };

    var bytes = BsbWriter.ToBytes(file);
    var headerEnd = Array.IndexOf(bytes, (byte)0x00);
    var header = Encoding.ASCII.GetString(bytes, 0, headerEnd);

    Assert.That(header, Does.Contain("RGB/0,128,64,32"));
    Assert.That(header, Does.Contain("RGB/1,10,20,30"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasNulTerminator() {
    var file = _CreateMinimalFile(2, 1);
    var bytes = BsbWriter.ToBytes(file);
    var nulIndex = Array.IndexOf(bytes, (byte)0x00);

    Assert.That(nulIndex, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasRowIndexTable() {
    var file = _CreateMinimalFile(2, 2);
    var bytes = BsbWriter.ToBytes(file);

    // After NUL, there should be a row index table (4 bytes per row)
    var nulIndex = Array.IndexOf(bytes, (byte)0x00);
    Assert.That(bytes.Length, Is.GreaterThan(nulIndex + 1 + 2 * 4));
  }

  [Test]
  [Category("Unit")]
  public void BuildHeader_ContainsAllRgbEntries() {
    var file = new BsbFile {
      Width = 1,
      Height = 1,
      PixelData = [0],
      Palette = [0, 0, 0, 255, 255, 255, 128, 128, 128],
      PaletteCount = 3,
    };

    var header = BsbWriter._BuildHeader(file);

    Assert.That(header, Does.Contain("RGB/0,0,0,0"));
    Assert.That(header, Does.Contain("RGB/1,255,255,255"));
    Assert.That(header, Does.Contain("RGB/2,128,128,128"));
  }

  [Test]
  [Category("Unit")]
  public void EncodePixelData_AllSameColor_ProducesValidOutput() {
    var pixels = new byte[] { 3, 3, 3, 3 };
    var rows = BsbWriter._EncodePixelData(pixels, 4, 1, 7);

    Assert.That(rows, Has.Length.EqualTo(1));
    Assert.That(rows[0].Length, Is.GreaterThan(0));
    // Last byte of each row should be 0x00 terminator
    Assert.That(rows[0][^1], Is.EqualTo(0x00));
  }

  private static BsbFile _CreateMinimalFile(int width, int height) => new() {
    Width = width,
    Height = height,
    PixelData = new byte[width * height],
    Palette = [0, 0, 0],
    PaletteCount = 1,
    Name = "NOAA",
  };
}
