using System;
using System.IO;
using System.Text;
using FileFormat.Nfo;

namespace FileFormat.Nfo.Tests;

[TestFixture]
public sealed class NfoTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => NfoReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nfo"));
    Assert.Throws<FileNotFoundException>(() => NfoReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Empty_ProducesEmptyFile() {
    var f = NfoReader.FromBytes([]);
    Assert.That(f.ColumnCount, Is.EqualTo(0));
    Assert.That(f.RowCount, Is.EqualTo(0));
    Assert.That(f.CellBytes, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TwoCrLfLines_ProducesTwoRowsPaddedTo80() {
    var data = Encoding.ASCII.GetBytes("hello\r\nworld\r\n");
    var f = NfoReader.FromBytes(data);
    Assert.That(f.RowCount, Is.EqualTo(2));
    Assert.That(f.ColumnCount, Is.EqualTo(80));
    Assert.That(f.CellBytes[0], Is.EqualTo((byte)'h'));
    Assert.That(f.CellBytes[80], Is.EqualTo((byte)'w'));
    // Padding past the line: spaces.
    Assert.That(f.CellBytes[5], Is.EqualTo((byte)0x20));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_StripsTrailingEofMarker() {
    var data = new byte[] { (byte)'A', (byte)'B', 0x1A };
    var f = NfoReader.FromBytes(data);
    Assert.That(f.RowCount, Is.EqualTo(1));
    Assert.That(f.CellBytes[0], Is.EqualTo((byte)'A'));
    Assert.That(f.CellBytes[1], Is.EqualTo((byte)'B'));
  }

  [Test]
  [Category("Integration")]
  public void Writer_RoundTrip_PreservesTextContent() {
    // Build a synthetic box-drawing NFO from raw CP437 bytes (skip Encoding.GetEncoding(437)
    // because .NET 8 doesn't ship CP437 without System.Text.Encoding.CodePages).
    byte[] raw = [
      0xC9, 0xCD, 0xCD, 0xBB, 0x0D, 0x0A,   // ╔══╗ CRLF
      0xBA, (byte)'O', (byte)'K', 0xBA, 0x0D, 0x0A, // ║OK║ CRLF
      0xC8, 0xCD, 0xCD, 0xBC, 0x0D, 0x0A,   // ╚══╝ CRLF
    ];
    var original = NfoReader.FromBytes(raw);
    var bytes = NfoWriter.ToBytes(original);
    var reloaded = NfoReader.FromBytes(bytes);
    Assert.That(reloaded.RowCount, Is.EqualTo(3));
    Assert.That(reloaded.CellBytes[0], Is.EqualTo(0xC9)); // ╔
    Assert.That(reloaded.CellBytes[3], Is.EqualTo(0xBB)); // ╗
    Assert.That(reloaded.CellBytes[80 * 2 + 0], Is.EqualTo(0xC8)); // ╚ on row 3
  }
}
