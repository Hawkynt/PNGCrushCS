using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.SecondNatureSlideShow;

namespace FileFormat.SecondNatureSlideShow.Tests;

[TestFixture]
public sealed class SecondNatureSlideShowTests {

  private const int _Width = 24, _Height = 16;

  private static byte[] _Jpeg() {
    var pixels = new byte[_Width * _Height * 3];
    for (var i = 0; i < _Width * _Height; ++i)
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = (byte)(i * 3);

    return JpegWriter.ToBytes(JpegFile.FromRawImage(new() {
      Width = _Width, Height = _Height, Format = PixelFormat.Rgb24, PixelData = pixels,
    }));
  }

  private static byte[] _Collection(int slides, string title = "A Collection") {
    var jpeg = _Jpeg();
    var slideLength = SecondNatureSlideShowFile.SlideHeaderSize + jpeg.Length;

    var directory = SecondNatureSlideShowFile.DirectoryOffset;
    var first = directory + slides * SecondNatureSlideShowFile.DirectoryEntrySize;
    var file = new byte[first + slides * slideLength];

    Encoding.ASCII.GetBytes(SecondNatureSlideShowFile.Signature).CopyTo(file, 0);
    Encoding.ASCII.GetBytes(title).CopyTo(file, 0x50);

    var at = first;
    for (var i = 0; i < slides; ++i) {
      BitConverter.GetBytes(at).CopyTo(file, directory + i * SecondNatureSlideShowFile.DirectoryEntrySize);
      BitConverter.GetBytes(slideLength).CopyTo(file, directory + i * SecondNatureSlideShowFile.DirectoryEntrySize + 4);

      BitConverter.GetBytes((ushort)_Width).CopyTo(file, at + SecondNatureSlideShowFile.SlideSizeOffset);
      BitConverter.GetBytes((ushort)_Height).CopyTo(file, at + SecondNatureSlideShowFile.SlideSizeOffset + 2);
      BitConverter.GetBytes((ushort)_Width).CopyTo(file, at + SecondNatureSlideShowFile.SlideSizeRepeatOffset);
      BitConverter.GetBytes((ushort)_Height).CopyTo(file, at + SecondNatureSlideShowFile.SlideSizeRepeatOffset + 2);
      jpeg.CopyTo(file, at + SecondNatureSlideShowFile.SlideHeaderSize);
      at += slideLength;
    }

    return file;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SecondNatureSlideShowReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SecondNatureSlideShowReader.FromBytes(new byte[4096]));

  [Test]
  [Category("Unit")]
  public void FromBytes_TheDirectorySaysHowManySlidesThereAre() {
    var collection = SecondNatureSlideShowReader.FromBytes(_Collection(4, "The James J. Tissot Collection"));

    Assert.Multiple(() => {
      Assert.That(SecondNatureSlideShowFile.ImageCount(collection), Is.EqualTo(4));
      Assert.That(collection.Title, Is.EqualTo("The James J. Tissot Collection"));
      Assert.That(collection.Slides[0].Width, Is.EqualTo(_Width));
      Assert.That(collection.Slides[0].Height, Is.EqualTo(_Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SlidesThatDoNotEndOnTheLastByte_ThrowsInvalidDataException() {
    var data = _Collection(2);
    Array.Resize(ref data, data.Length + 1);

    Assert.Throws<InvalidDataException>(() => SecondNatureSlideShowReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ASlideStartingWhereTheOneBeforeDidNotEnd_ThrowsInvalidDataException() {
    var data = _Collection(2);
    var at = SecondNatureSlideShowFile.DirectoryOffset + SecondNatureSlideShowFile.DirectoryEntrySize;
    BitConverter.GetBytes(BitConverter.ToInt32(data, at) + 2).CopyTo(data, at);

    Assert.Throws<InvalidDataException>(() => SecondNatureSlideShowReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ADirectoryThatDoesNotDivideEvenly_ThrowsInvalidDataException() {
    var data = _Collection(2);
    BitConverter.GetBytes(BitConverter.ToInt32(data, SecondNatureSlideShowFile.DirectoryOffset) + 3)
      .CopyTo(data, SecondNatureSlideShowFile.DirectoryOffset);

    Assert.Throws<InvalidDataException>(() => SecondNatureSlideShowReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_ThePictureIsTheSizeItsRecordStated() {
    var collection = SecondNatureSlideShowReader.FromBytes(_Collection(3));
    var picture = SecondNatureSlideShowFile.ToRawImage(collection, 2);

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(_Width));
      Assert.That(picture.Height, Is.EqualTo(_Height));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_ARecordDisagreeingWithItsJpeg_ThrowsInvalidDataException() {
    var data = _Collection(1);
    var slide = SecondNatureSlideShowFile.DirectoryOffset + SecondNatureSlideShowFile.DirectoryEntrySize;
    BitConverter.GetBytes((ushort)(_Width + 1)).CopyTo(data, slide + SecondNatureSlideShowFile.SlideSizeOffset);
    BitConverter.GetBytes((ushort)(_Width + 1)).CopyTo(data, slide + SecondNatureSlideShowFile.SlideSizeRepeatOffset);

    var collection = SecondNatureSlideShowReader.FromBytes(data);
    Assert.Throws<InvalidDataException>(() => SecondNatureSlideShowFile.ToRawImage(collection, 0));
  }
}
