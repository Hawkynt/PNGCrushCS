using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.JigsawPicture;

namespace FileFormat.JigsawPicture.Tests;

[TestFixture]
public sealed class JigsawPictureReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JigsawPictureReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jig"));
    Assert.Throws<FileNotFoundException>(() => JigsawPictureReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JigsawPictureReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => JigsawPictureReader.FromBytes(new byte[20]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ABitmapUnderItsOwnNameIsRefused() {
    var data = JigsawFixture.Picture();
    data[0] = (byte)'B';
    data[1] = (byte)'M';

    Assert.Throws<InvalidDataException>(() => JigsawPictureReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TwoBytesAreNotEnoughToAccept() {
    // AOL's ART files also open with "JG", so the rest of the two headers is what decides. Here the
    // reserved words of the file header hold something, which no bitmap does.
    var data = JigsawFixture.Picture();
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6), 1);

    Assert.Throws<InvalidDataException>(() => JigsawPictureReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AStatedSizeLargerThanTheFileIsRefused() {
    var data = JigsawFixture.Picture();
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2), (uint)data.Length + 1);

    Assert.Throws<InvalidDataException>(() => JigsawPictureReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelsBeforeTheHeadersEndAreRefused() {
    var data = JigsawFixture.Picture();
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(10), 20);

    Assert.Throws<InvalidDataException>(() => JigsawPictureReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ThePictureIsTheOneTheBitmapReaderGives() {
    var read = JigsawPictureReader.FromBytes(JigsawFixture.Picture());

    Assert.That(read.Image.Width, Is.EqualTo(2));
    Assert.That(read.Image.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AgreesWithTheSameBytesReadAsABitmap() {
    // The only difference between the two files is the signature, so anything but agreement means
    // the reader is doing something of its own.
    var jigsaw = JigsawFixture.Picture();
    var bitmap = (byte[])jigsaw.Clone();
    bitmap[0] = (byte)'B';
    bitmap[1] = (byte)'M';

    var fromJigsaw = JigsawPictureFile.ToRawImage(JigsawPictureReader.FromBytes(jigsaw));
    var fromBitmap = FileFormat.Bmp.BmpFile.ToRawImage(FileFormat.Bmp.BmpReader.FromBytes(bitmap));

    Assert.That(fromJigsaw.Width, Is.EqualTo(fromBitmap.Width));
    Assert.That(fromJigsaw.Height, Is.EqualTo(fromBitmap.Height));
    Assert.That(fromJigsaw.PixelData, Is.EqualTo(fromBitmap.PixelData));
  }
}

/// <summary>A Windows bitmap with its signature replaced, which is the whole of the format.</summary>
internal static class JigsawFixture {

  internal static byte[] Picture() {
    const int width = 2;
    const int height = 2;
    const int pixelsAt = 14 + 40;
    var stride = (width * 3 + 3) / 4 * 4;
    var data = new byte[pixelsAt + stride * height];

    data[0] = (byte)'J';
    data[1] = (byte)'G';
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2), (uint)data.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(10), pixelsAt);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), 40);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), 24);

    for (var i = pixelsAt; i < data.Length; ++i)
      data[i] = (byte)(i * 37 % 251);

    return data;
  }
}
