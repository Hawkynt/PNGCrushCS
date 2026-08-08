using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Ioca;

namespace FileFormat.Ioca.Tests;

[TestFixture]
public sealed class IocaReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IocaReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ica"));
    Assert.Throws<FileNotFoundException>(() => IocaReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IocaReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => IocaReader.FromBytes(new byte[4]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AFourByteSizeIsNotAnIocaImage() {
    // What this used to accept: any file at all, with its first four bytes taken as a width and a
    // height. A real IOCA image is a chain of MO:DCA structured fields — two bytes of length, the
    // introducer 0xD3, a three-byte type — and it states its size in an Image Data Descriptor. No
    // file has the four-byte header this once invented, and the writer beside it wrote the same
    // invention, so the two agreed and nothing else could read either.
    var data = new byte[] { 0x00, 0x08, 0x00, 0x02, 0xFF, 0xAA, 0x00, 0x00, 0x00, 0x00 };

    Assert.Throws<InvalidDataException>(() => IocaReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AStructuredFieldChainThatStatesNoSizeIsRefused() {
    // A well-formed chain that carries no Image Data Descriptor states no size, and a size is not
    // guessed from anywhere else.
    var data = new byte[] { 0x00, 0x08, 0xD3, 0xA8, 0xA8, 0x00, 0x00, 0x00, 0x00, 0x00 };

    Assert.Throws<InvalidDataException>(() => IocaReader.FromBytes(data));
  }
}
