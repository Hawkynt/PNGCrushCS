using System;
using System.IO;
using System.Text;
using FileFormat.AnsiArt;

namespace FileFormat.AnsiArt.Tests;

[TestFixture]
public sealed class AnsiArtTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => AnsiArtReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_PlainText_ProducesCellGrid() {
    var data = Encoding.ASCII.GetBytes("Hi");
    var f = AnsiArtReader.FromBytes(data);
    Assert.That(f.Cells[0].CodePoint, Is.EqualTo((byte)'H'));
    Assert.That(f.Cells[1].CodePoint, Is.EqualTo((byte)'i'));
    Assert.That(f.Cells[0].Foreground, Is.EqualTo(7));
    Assert.That(f.Cells[0].Background, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SgrSetsForeground() {
    // ESC[31m sets red FG (CGA index 4).
    var data = Encoding.ASCII.GetBytes("\x1B[31mR");
    var f = AnsiArtReader.FromBytes(data);
    Assert.That(f.Cells[0].CodePoint, Is.EqualTo((byte)'R'));
    Assert.That(f.Cells[0].Foreground, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SgrBoldUpgradesForegroundToBright() {
    // ESC[1;33m: bold + yellow (which in CGA is brown index 6 → bright = 14 yellow).
    var data = Encoding.ASCII.GetBytes("\x1B[1;33mY");
    var f = AnsiArtReader.FromBytes(data);
    Assert.That(f.Cells[0].Foreground, Is.EqualTo(14));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RecognisesSauce() {
    var artBytes = Encoding.ASCII.GetBytes("ART\r\n");
    var sauce = new byte[128];
    Encoding.ASCII.GetBytes("SAUCE00").CopyTo(sauce.AsSpan(0));
    var full = new byte[artBytes.Length + 1 + 128];
    Array.Copy(artBytes, full, artBytes.Length);
    full[artBytes.Length] = 0x1A;
    Array.Copy(sauce, 0, full, artBytes.Length + 1, 128);
    var f = AnsiArtReader.FromBytes(full);
    Assert.That(f.SauceRecord, Is.Not.Null);
    Assert.That(f.SauceRecord!.Length, Is.EqualTo(128));
    Assert.That(f.Cells[0].CodePoint, Is.EqualTo((byte)'A'));
  }

  [Test]
  [Category("Integration")]
  public void Writer_RoundTrip_PreservesColoursAndText() {
    var data = Encoding.ASCII.GetBytes("\x1B[31;44mRB\x1B[32;40mG");
    var original = AnsiArtReader.FromBytes(data);
    var bytes = AnsiArtWriter.ToBytes(original);
    var reloaded = AnsiArtReader.FromBytes(bytes);
    Assert.That(reloaded.Cells[0].CodePoint, Is.EqualTo((byte)'R'));
    Assert.That(reloaded.Cells[0].Foreground, Is.EqualTo(4));
    Assert.That(reloaded.Cells[0].Background, Is.EqualTo(1));
    Assert.That(reloaded.Cells[1].CodePoint, Is.EqualTo((byte)'B'));
    Assert.That(reloaded.Cells[2].CodePoint, Is.EqualTo((byte)'G'));
    Assert.That(reloaded.Cells[2].Foreground, Is.EqualTo(2));
  }
}
