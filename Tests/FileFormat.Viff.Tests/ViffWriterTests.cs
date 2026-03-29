using System;
using FileFormat.Viff;

namespace FileFormat.Viff.Tests;

[TestFixture]
public sealed class ViffWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ViffWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSize1024() {
    var file = new ViffFile {
      Width = 2,
      Height = 2,
      Bands = 1,
      StorageType = ViffStorageType.Byte,
      PixelData = new byte[4]
    };

    var bytes = ViffWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(ViffHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicByte() {
    var file = new ViffFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      StorageType = ViffStorageType.Byte,
      PixelData = new byte[1]
    };

    var bytes = ViffWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(ViffHeader.Magic));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSizeCorrect() {
    var width = 8;
    var height = 4;
    var bands = 3;
    var pixelBytes = width * height * bands;
    var file = new ViffFile {
      Width = width,
      Height = height,
      Bands = bands,
      StorageType = ViffStorageType.Byte,
      PixelData = new byte[pixelBytes]
    };

    var bytes = ViffWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(ViffHeader.StructSize + pixelBytes));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_16bit_TotalSizeCorrect() {
    var width = 4;
    var height = 2;
    var bands = 1;
    var pixelBytes = width * height * bands * 2; // 2 bytes per element for Short
    var file = new ViffFile {
      Width = width,
      Height = height,
      Bands = bands,
      StorageType = ViffStorageType.Short,
      PixelData = new byte[pixelBytes]
    };

    var bytes = ViffWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(ViffHeader.StructSize + pixelBytes));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CommentWritten() {
    var file = new ViffFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      StorageType = ViffStorageType.Byte,
      Comment = "Hello VIFF",
      PixelData = new byte[1]
    };

    var bytes = ViffWriter.ToBytes(file);
    var comment = System.Text.Encoding.ASCII.GetString(bytes, 8, 10);

    Assert.That(comment, Is.EqualTo("Hello VIFF"));
  }
}
