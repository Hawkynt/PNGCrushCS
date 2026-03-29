using System;
using NUnit.Framework;
using FileFormat.AtariArtist;

namespace FileFormat.AtariArtist.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AtariArtistFile {
      PixelData = new byte[AtariArtistFile.ExpectedFileSize],
    };
    var bytes = AtariArtistWriter.ToBytes(original);
    var roundTripped = AtariArtistReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => AtariArtistReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => AtariArtistWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
