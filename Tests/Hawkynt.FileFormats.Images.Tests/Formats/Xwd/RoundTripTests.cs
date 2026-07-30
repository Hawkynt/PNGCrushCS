using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Xwd;

namespace FileFormat.Xwd.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_8bpp_Indexed() {
    var width = 4;
    var height = 3;
    var bytesPerLine = width;
    var numColors = 4;

    var colormap = new byte[numColors * 12];
    for (var i = 0; i < numColors; ++i) {
      var offset = i * 12;
      BinaryPrimitives.WriteUInt32BigEndian(colormap.AsSpan(offset), (uint)i);
      BinaryPrimitives.WriteUInt16BigEndian(colormap.AsSpan(offset + 4), (ushort)(i * 0x4000));
      BinaryPrimitives.WriteUInt16BigEndian(colormap.AsSpan(offset + 6), (ushort)(i * 0x2000));
      BinaryPrimitives.WriteUInt16BigEndian(colormap.AsSpan(offset + 8), (ushort)(i * 0x1000));
      colormap[offset + 10] = 7; // flags
      colormap[offset + 11] = 0; // padding
    }

    var pixelData = new byte[bytesPerLine * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % numColors);

    var original = new XwdFile {
      Width = width,
      Height = height,
      BitsPerPixel = 8,
      BytesPerLine = bytesPerLine,
      PixmapFormat = XwdPixmapFormat.ZPixmap,
      PixmapDepth = 8,
      VisualClass = XwdVisualClass.PseudoColor,
      ByteOrder = 1,
      BitmapUnit = 32,
      BitmapBitOrder = 1,
      BitmapPad = 32,
      BitsPerRgb = 8,
      ColormapEntries = (uint)numColors,
      WindowName = "indexed_test",
      PixelData = pixelData,
      Colormap = colormap
    };

    var bytes = XwdWriter.ToBytes(original);
    var restored = XwdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(original.BitsPerPixel));
    Assert.That(restored.BytesPerLine, Is.EqualTo(original.BytesPerLine));
    Assert.That(restored.VisualClass, Is.EqualTo(original.VisualClass));
    Assert.That(restored.WindowName, Is.EqualTo(original.WindowName));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Colormap, Is.EqualTo(original.Colormap));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_24bpp_TrueColor() {
    var width = 8;
    var height = 4;
    var bytesPerLine = width * 3;

    var pixelData = new byte[bytesPerLine * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new XwdFile {
      Width = width,
      Height = height,
      BitsPerPixel = 24,
      BytesPerLine = bytesPerLine,
      PixmapFormat = XwdPixmapFormat.ZPixmap,
      PixmapDepth = 24,
      VisualClass = XwdVisualClass.TrueColor,
      ByteOrder = 1,
      BitmapUnit = 32,
      BitmapBitOrder = 1,
      BitmapPad = 32,
      RedMask = 0x00FF0000,
      GreenMask = 0x0000FF00,
      BlueMask = 0x000000FF,
      BitsPerRgb = 8,
      WindowName = "truecolor_test",
      PixelData = pixelData
    };

    var bytes = XwdWriter.ToBytes(original);
    var restored = XwdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(24));
    Assert.That(restored.RedMask, Is.EqualTo(0x00FF0000));
    Assert.That(restored.GreenMask, Is.EqualTo(0x0000FF00));
    Assert.That(restored.BlueMask, Is.EqualTo(0x000000FF));
    Assert.That(restored.WindowName, Is.EqualTo("truecolor_test"));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Colormap, Is.Null);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_32bpp() {
    var width = 4;
    var height = 2;
    var bytesPerLine = width * 4;

    var pixelData = new byte[bytesPerLine * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new XwdFile {
      Width = width,
      Height = height,
      BitsPerPixel = 32,
      BytesPerLine = bytesPerLine,
      PixmapFormat = XwdPixmapFormat.ZPixmap,
      PixmapDepth = 24,
      VisualClass = XwdVisualClass.TrueColor,
      ByteOrder = 1,
      BitmapUnit = 32,
      BitmapBitOrder = 1,
      BitmapPad = 32,
      RedMask = 0x00FF0000,
      GreenMask = 0x0000FF00,
      BlueMask = 0x000000FF,
      BitsPerRgb = 8,
      WindowName = "rgba",
      WindowX = -10,
      WindowY = 42,
      WindowBorderWidth = 2,
      PixelData = pixelData
    };

    var bytes = XwdWriter.ToBytes(original);
    var restored = XwdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(32));
    Assert.That(restored.WindowX, Is.EqualTo(-10));
    Assert.That(restored.WindowY, Is.EqualTo(42));
    Assert.That(restored.WindowBorderWidth, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_1bpp() {
    var width = 16;
    var height = 2;
    var bytesPerLine = 2; // 16 bits = 2 bytes per line

    var pixelData = new byte[bytesPerLine * height];
    pixelData[0] = 0xAA;
    pixelData[1] = 0x55;
    pixelData[2] = 0xFF;
    pixelData[3] = 0x00;

    var original = new XwdFile {
      Width = width,
      Height = height,
      BitsPerPixel = 1,
      BytesPerLine = bytesPerLine,
      PixmapFormat = XwdPixmapFormat.XYBitmap,
      PixmapDepth = 1,
      VisualClass = XwdVisualClass.StaticGray,
      ByteOrder = 1,
      BitmapUnit = 8,
      BitmapBitOrder = 1,
      BitmapPad = 8,
      BitsPerRgb = 1,
      WindowName = "mono",
      PixelData = pixelData
    };

    var bytes = XwdWriter.ToBytes(original);
    var restored = XwdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(1));
    Assert.That(restored.PixmapFormat, Is.EqualTo(XwdPixmapFormat.XYBitmap));
    Assert.That(restored.VisualClass, Is.EqualTo(XwdVisualClass.StaticGray));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
