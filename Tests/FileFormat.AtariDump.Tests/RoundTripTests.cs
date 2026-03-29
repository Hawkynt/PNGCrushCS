using System;
using NUnit.Framework;
using FileFormat.AtariDump;

namespace FileFormat.AtariDump.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AtariDumpFile {
      Width = 320,
      Height = 192,
      PixelData = new byte[7680],
    };
    var bytes = AtariDumpWriter.ToBytes(original);
    var roundTripped = AtariDumpReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => AtariDumpReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => AtariDumpWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
