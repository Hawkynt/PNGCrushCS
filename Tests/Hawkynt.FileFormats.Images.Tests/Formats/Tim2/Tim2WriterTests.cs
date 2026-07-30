using System;
using System.Collections.Generic;
using FileFormat.Tim2;

namespace FileFormat.Tim2.Tests;

[TestFixture]
public sealed class Tim2WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithTIM2() {
    var file = new Tim2File {
      Version = 4,
      Alignment = 0,
      Pictures = [
        new Tim2Picture {
          Width = 4,
          Height = 2,
          Format = Tim2Format.Rgb32,
          MipmapCount = 1,
          PixelData = new byte[4 * 2 * 4]
        }
      ]
    };

    var bytes = Tim2Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'T'));
    Assert.That(bytes[1], Is.EqualTo((byte)'I'));
    Assert.That(bytes[2], Is.EqualTo((byte)'M'));
    Assert.That(bytes[3], Is.EqualTo((byte)'2'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectPictureCount() {
    var pictures = new List<Tim2Picture>();
    for (var i = 0; i < 3; ++i)
      pictures.Add(new Tim2Picture {
        Width = 2,
        Height = 2,
        Format = Tim2Format.Rgb32,
        MipmapCount = 1,
        PixelData = new byte[2 * 2 * 4]
      });

    var file = new Tim2File {
      Version = 4,
      Alignment = 0,
      Pictures = pictures.AsReadOnly()
    };

    var bytes = Tim2Writer.ToBytes(file);
    var pictureCount = BitConverter.ToUInt16(bytes, 6);

    Assert.That(pictureCount, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PictureHeaderWritten() {
    var pixelData = new byte[4 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new Tim2File {
      Version = 4,
      Alignment = 0,
      Pictures = [
        new Tim2Picture {
          Width = 4,
          Height = 2,
          Format = Tim2Format.Rgb32,
          MipmapCount = 1,
          PixelData = pixelData
        }
      ]
    };

    var bytes = Tim2Writer.ToBytes(file);

    var picHeaderOffset = Tim2Header.StructSize;
    var width = BitConverter.ToUInt16(bytes, picHeaderOffset + 20);
    var height = BitConverter.ToUInt16(bytes, picHeaderOffset + 22);
    var format = bytes[picHeaderOffset + 16];

    Assert.That(width, Is.EqualTo(4));
    Assert.That(height, Is.EqualTo(2));
    Assert.That(format, Is.EqualTo((byte)Tim2Format.Rgb32));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var pixelData = new byte[2 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var file = new Tim2File {
      Version = 4,
      Alignment = 0,
      Pictures = [
        new Tim2Picture {
          Width = 2,
          Height = 2,
          Format = Tim2Format.Rgb32,
          MipmapCount = 1,
          PixelData = pixelData
        }
      ]
    };

    var bytes = Tim2Writer.ToBytes(file);

    var dataOffset = Tim2Header.StructSize + Tim2PictureHeader.StructSize;
    var extractedData = new byte[pixelData.Length];
    Array.Copy(bytes, dataOffset, extractedData, 0, pixelData.Length);

    Assert.That(extractedData, Is.EqualTo(pixelData));
  }
}
