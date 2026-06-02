using System;
using System.IO;
using FileFormat.TextMode;
using FileFormat.XBin;

namespace FileFormat.XBin.Tests;

[TestFixture]
public sealed class XBinTests {

  private static XBinFile _BuildSimple(int cols, int rows) {
    var cells = new TextCell[cols * rows];
    for (var i = 0; i < cells.Length; ++i)
      cells[i] = new TextCell((byte)('A' + (i & 0x1F)), Foreground: (byte)(i & 0x0F), Background: 0);
    return new XBinFile {
      ColumnCount = cols, RowCount = rows, FontHeight = 16, Flags = XBinFlags.NonBlink, Cells = cells,
    };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => XBinReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_Throws()
    => Assert.Throws<InvalidDataException>(() => XBinReader.FromBytes(new byte[5]));

  [Test]
  [Category("Unit")]
  public void FromBytes_BadMagic_Throws()
    => Assert.Throws<InvalidDataException>(() => XBinReader.FromBytes(new byte[40]));

  [Test]
  [Category("Integration")]
  public void Writer_RoundTrip_Plain_PreservesCells() {
    var original = _BuildSimple(20, 5);
    var bytes = XBinWriter.ToBytes(original);
    Assert.That(bytes[0], Is.EqualTo((byte)'X'));
    Assert.That(bytes[3], Is.EqualTo((byte)'N'));
    Assert.That(bytes[4], Is.EqualTo(0x1A));
    var loaded = XBinReader.FromBytes(bytes);
    Assert.That(loaded.ColumnCount, Is.EqualTo(20));
    Assert.That(loaded.RowCount, Is.EqualTo(5));
    Assert.That(loaded.Cells.Length, Is.EqualTo(100));
    for (var i = 0; i < 100; ++i) {
      Assert.That(loaded.Cells[i].CodePoint, Is.EqualTo(original.Cells[i].CodePoint));
      Assert.That(loaded.Cells[i].Foreground, Is.EqualTo(original.Cells[i].Foreground));
    }
  }

  [Test]
  [Category("Integration")]
  public void Reader_DecompressesRleStream() {
    // Build a tiny image (2×1) with mode-3 full-run: one (cp, attr) pair repeated 2 times.
    var hdr = new byte[] {
      (byte)'X', (byte)'B', (byte)'I', (byte)'N', 0x1A,
      0x02, 0x00,  // width = 2
      0x01, 0x00,  // height = 1
      0x10,        // font height = 16
      (byte)XBinFlags.Compressed,
    };
    // ctrl = (mode 3 << 6) | (count-1 = 1) = 0xC1; then 'X' + attr 0x07.
    var img = new byte[] { 0xC1, (byte)'X', 0x07 };
    var all = new byte[hdr.Length + img.Length];
    Array.Copy(hdr, all, hdr.Length);
    Array.Copy(img, 0, all, hdr.Length, img.Length);
    var loaded = XBinReader.FromBytes(all);
    Assert.That(loaded.Cells.Length, Is.EqualTo(2));
    Assert.That(loaded.Cells[0].CodePoint, Is.EqualTo((byte)'X'));
    Assert.That(loaded.Cells[1].CodePoint, Is.EqualTo((byte)'X'));
    Assert.That(loaded.Cells[0].Foreground, Is.EqualTo(7));
  }
}
