using System;
using NUnit.Framework;
using FileFormat.AtariCompressed;

namespace FileFormat.AtariCompressed.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AtariCompressedFile {
      Width = 320,
      Height = 192,
      PixelData = new byte[7680],
    };
    var bytes = AtariCompressedWriter.ToBytes(original);
    var roundTripped = AtariCompressedReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => AtariCompressedReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => AtariCompressedWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
