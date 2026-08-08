using System;
using System.Text;
using FileFormat.Fbm;

namespace FileFormat.Fbm.Tests;

/// <summary>
/// An FBM header: fixed-width decimal text, not binary integers.
/// </summary>
/// <remarks>
/// What was tested here before was the binary form — a record of big-endian integers with a
/// generated serialiser and a field map. No FBM file has that, which is why the one tool that reads
/// these called our output "invalid number of planes": it was parsing a binary 3 as characters.
/// </remarks>
[TestFixture]
public sealed class FbmHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is256() {
    Assert.That(FbmHeader.StructSize, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void MagicBytes_IsCorrect() {
    Assert.That(FbmHeader.MagicBytes, Is.EqualTo(new byte[] { (byte)'%', (byte)'b', (byte)'i', (byte)'t', (byte)'m', (byte)'a', (byte)'p', 0 }));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesEveryField() {
    var original = new FbmHeader(
      Cols: 640, Rows: 480, Bands: 3, Bits: 8, PhysBits: 8,
      RowLen: 640, PlnLen: 307200, ClrLen: 0, Aspect: 1.0, Title: "Test");

    var buffer = new byte[FbmHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = FbmHeader.ReadFrom(buffer);

    Assert.Multiple(() => {
      Assert.That(parsed.Cols, Is.EqualTo(original.Cols));
      Assert.That(parsed.Rows, Is.EqualTo(original.Rows));
      Assert.That(parsed.Bands, Is.EqualTo(original.Bands));
      Assert.That(parsed.Bits, Is.EqualTo(original.Bits));
      Assert.That(parsed.PhysBits, Is.EqualTo(original.PhysBits));
      Assert.That(parsed.RowLen, Is.EqualTo(original.RowLen));
      Assert.That(parsed.PlnLen, Is.EqualTo(original.PlnLen));
      Assert.That(parsed.ClrLen, Is.EqualTo(original.ClrLen));
      Assert.That(parsed.Aspect, Is.EqualTo(original.Aspect));
      Assert.That(parsed.Title, Is.EqualTo(original.Title));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_PutsTheMagicAndThenDecimalText() {
    var buffer = new byte[FbmHeader.StructSize];
    new FbmHeader(800, 600, 3, 8, 8, 800, 480000, 0, 1.0, string.Empty).WriteTo(buffer);

    // This is the whole point of the format and the whole of what was wrong before: the fields are
    // characters, right-aligned in their columns, and a reader parses them as text.
    Assert.Multiple(() => {
      Assert.That(buffer[..8], Is.EqualTo(FbmHeader.MagicBytes));
      Assert.That(Encoding.ASCII.GetString(buffer, 8, 8).TrimEnd('\0'), Is.EqualTo("    800"), "right-aligned in seven, the eighth byte being the terminator");
      Assert.That(Encoding.ASCII.GetString(buffer, 16, 8).TrimEnd('\0'), Is.EqualTo("    600"));
      Assert.That(Encoding.ASCII.GetString(buffer, 60, 12).TrimEnd('\0'), Is.EqualTo("     480000"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesTheTextARealFileCarries() {
    var buffer = new byte[FbmHeader.StructSize];
    FbmHeader.MagicBytes.CopyTo(buffer, 0);
    Encoding.ASCII.GetBytes("    800\0").CopyTo(buffer, 8);
    Encoding.ASCII.GetBytes("    600\0").CopyTo(buffer, 16);
    Encoding.ASCII.GetBytes("      3\0").CopyTo(buffer, 24);
    Encoding.ASCII.GetBytes("      8\0").CopyTo(buffer, 32);
    Encoding.ASCII.GetBytes("      8\0").CopyTo(buffer, 40);
    Encoding.ASCII.GetBytes("        800\0").CopyTo(buffer, 48);
    Encoding.ASCII.GetBytes("     480000\0").CopyTo(buffer, 60);

    var parsed = FbmHeader.ReadFrom(buffer);

    Assert.Multiple(() => {
      Assert.That(parsed.Cols, Is.EqualTo(800));
      Assert.That(parsed.Rows, Is.EqualTo(600));
      Assert.That(parsed.Bands, Is.EqualTo(3));
      Assert.That(parsed.RowLen, Is.EqualTo(800), "one row of one plane, not of all three");
      Assert.That(parsed.PlnLen, Is.EqualTo(480000), "one whole plane");
    });
  }
}
