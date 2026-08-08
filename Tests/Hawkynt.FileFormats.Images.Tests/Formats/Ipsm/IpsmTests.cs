using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Ipsm;

namespace FileFormat.Ipsm.Tests;

[TestFixture]
public sealed class IpsmTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i)
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = (byte)(i * 5);

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IpsmReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => IpsmReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_StatedLengthMustBeTheFilesLength() {
    var data = IpsmWriter.ToBytes(IpsmFile.FromRawImage(_Picture(16, 8)));
    Array.Resize(ref data, data.Length + 1);

    Assert.Throws<InvalidDataException>(() => IpsmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoBitmapChunk_ThrowsInvalidDataException() {
    var data = IpsmWriter.ToBytes(IpsmFile.FromRawImage(_Picture(16, 8)));
    // Rename BTMP so the directory is well formed and carries no picture.
    data[IpsmFile.HeaderSize + IpsmFile.DirectoryEntrySize] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => IpsmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TheDirectoryAccountsForTheWholeFile() {
    var bytes = IpsmWriter.ToBytes(IpsmFile.FromRawImage(_Picture(16, 8)));
    var offset = BitConverter.ToInt32(bytes, IpsmFile.HeaderSize + IpsmFile.DirectoryEntrySize + 4);
    var length = BitConverter.ToInt32(bytes, IpsmFile.HeaderSize + IpsmFile.DirectoryEntrySize + 8);

    Assert.Multiple(() => {
      Assert.That(BitConverter.ToInt32(bytes, 4), Is.EqualTo(bytes.Length), "the stated length is the file's");
      Assert.That(offset + length, Is.EqualTo(bytes.Length), "the picture runs to the end of the file");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThePictureComesBackAtItsSize() {
    var decoded = IpsmFile.ToRawImage(IpsmReader.FromBytes(IpsmWriter.ToBytes(IpsmFile.FromRawImage(_Picture(32, 16)))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(32));
      Assert.That(decoded.Height, Is.EqualTo(16));
    });
  }
}
