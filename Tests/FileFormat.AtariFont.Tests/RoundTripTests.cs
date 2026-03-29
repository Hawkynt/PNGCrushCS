using System;
using NUnit.Framework;
using FileFormat.AtariFont;

namespace FileFormat.AtariFont.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AtariFontFile {
      FontData = new byte[1024],
    };
    var bytes = AtariFontWriter.ToBytes(original);
    var roundTripped = AtariFontReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.FontData, Is.EqualTo(original.FontData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => AtariFontReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => AtariFontWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
