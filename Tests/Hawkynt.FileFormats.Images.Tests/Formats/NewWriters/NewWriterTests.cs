using System;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests.NewWriters;

/// <summary>
/// The five formats that gained a writer, each checked against what its own reader makes of it.
/// </summary>
/// <remarks>
/// A round trip through our own pair proves only that the two agree, so each of these also asserts
/// something the format itself fixes — a length, a signature, a field a reader validates — that a
/// matched pair of mistakes could not satisfy by accident.
/// </remarks>
[TestFixture]
public sealed class NewWriterTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 255 / Math.Max(1, width - 1));
      pixels[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      pixels[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Botticelli_IsAFullScreenWithBothColourAreas() {
    var file = FileFormat.Botticelli.BotticelliFile.FromRawImage(_Picture(320, 200));
    var bytes = FileFormat.Botticelli.BotticelliWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(10050));
      Assert.That(bytes.Skip(2).Take(1000).Any(b => b != 0), Is.True, "the luminance area");
      Assert.That(bytes.Skip(1026).Take(1000).Any(b => b != 0), Is.True, "the hue area");
      Assert.That(
        Encoding.ASCII.GetString(bytes, 1020, 4), Is.Not.EqualTo("MULT"),
        "the high-resolution screen carries no multicolour marker");
    });
  }

  [Test]
  [Category("Unit")]
  public void Brus_CarriesTheBytesItsReaderChecks() {
    var file = FileFormat.Brus.BrusFile.FromRawImage(_Picture(320, 200));
    var bytes = FileFormat.Brus.BrusWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 2, 4), Is.EqualTo("BRUS"));
      Assert.That(bytes[6], Is.EqualTo(4));
      Assert.That(bytes[10], Is.EqualTo(1));
      Assert.That(bytes[11], Is.EqualTo(2));
      Assert.That(bytes[12], Is.EqualTo(40), "forty columns of eight pixels");
      Assert.That(bytes[13] | (bytes[14] << 8), Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Integration")]
  public void Brus_RoundTripsThroughItsOwnPacker() {
    var file = FileFormat.Brus.BrusFile.FromRawImage(_Picture(320, 200));
    var restored = FileFormat.Brus.BrusReader.FromBytes(FileFormat.Brus.BrusWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Columns, Is.EqualTo(file.Columns));
      Assert.That(restored.Height, Is.EqualTo(file.Height));
      Assert.That(restored.Bitmap, Is.EqualTo(file.Bitmap));
      Assert.That(restored.Colors, Is.EqualTo(file.Colors));
    });
  }

  [Test]
  [Category("Unit")]
  public void Grafix_StatesABitmapLengthThatFollowsFromTheSizeAndDepth() {
    var file = FileFormat.Grafix.GrafixFile.FromRawImage(_Picture(320, 200));
    var bytes = FileFormat.Grafix.GrafixWriter.ToBytes(file);

    var stated = (bytes[1574] << 24) | (bytes[1575] << 16) | (bytes[1576] << 8) | bytes[1577];
    var stride = ((320 + 15) >> 4) * 4 * 2;

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("GRXP"));
      Assert.That(bytes[4], Is.EqualTo(1));
      Assert.That(bytes[5], Is.EqualTo(1));
      Assert.That(bytes[34] << 8 | bytes[35], Is.EqualTo(16), "sixteen colours over four planes");
      Assert.That(stated, Is.EqualTo(stride * 200));
      Assert.That(bytes, Has.Length.EqualTo(1586 + stride * 200));
    });
  }

  [Test]
  [Category("Integration")]
  public void Grafix_RoundTripsItsBitmapAndPalette() {
    var file = FileFormat.Grafix.GrafixFile.FromRawImage(_Picture(320, 200));
    var restored = FileFormat.Grafix.GrafixReader.FromBytes(FileFormat.Grafix.GrafixWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.Planes, Is.EqualTo(4));
      Assert.That(restored.Bitmap, Is.EqualTo(file.Bitmap));
    });
  }

  [Test]
  [Category("Integration")]
  public void XlPaint_RoundTripsBothScreensAndItsRegisters() {
    var file = FileFormat.XlPaint.XlPaintFile.FromRawImage(_Picture(320, 192));
    var restored = FileFormat.XlPaint.XlPaintReader.FromBytes(FileFormat.XlPaint.XlPaintWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Height, Is.EqualTo(192));
      Assert.That(restored.Registers, Is.EqualTo(file.Registers));
      Assert.That(restored.ScreenData, Is.EqualTo(file.ScreenData));
    });
  }

  [Test]
  [Category("Integration")]
  public void XlPaint_ReachesMoreColoursThanOneScreenHolds() {
    var file = FileFormat.XlPaint.XlPaintFile.FromRawImage(_Picture(320, 192));
    var image = FileFormat.XlPaint.XlPaintFile.ToRawImage(file);

    var distinct = new System.Collections.Generic.HashSet<(byte, byte, byte)>();
    for (var i = 0; i < image.PixelData.Length; i += 3)
      distinct.Add((image.PixelData[i], image.PixelData[i + 1], image.PixelData[i + 2]));

    Assert.That(
      distinct, Has.Count.GreaterThan(4),
      "four registers averaged in pairs reach ten colours, which is why there are two screens");
  }

  [Test]
  [Category("Integration")]
  public void PerfectPix_WritesItsTwoFieldsBesideTheHead() {
    var directory = Directory.CreateTempSubdirectory("perfectpix");
    try {
      var target = new FileInfo(Path.Combine(directory.FullName, "sample.pph"));
      Assert.That(FormatRegistry.Write(_Picture(320, 200), ImageFormat.PerfectPix, target), Is.True);

      var odd = new FileInfo(Path.Combine(directory.FullName, "sample.odd"));
      var even = new FileInfo(Path.Combine(directory.FullName, "sample.eve"));

      Assert.Multiple(() => {
        Assert.That(target.Length, Is.EqualTo(22), "the head is a size, a mode and sixteen colours");
        Assert.That(odd.Exists, Is.True);
        Assert.That(even.Exists, Is.True);
        Assert.That(odd.Length, Is.EqualTo(200 * (320 >> 2)));
        Assert.That(even.Length, Is.EqualTo(odd.Length));
      });

      // And the reader must find them again, which is the whole point of writing three files.
      var read = FileFormat.PerfectPix.PerfectPixReader.FromFile(target);
      Assert.That(read.Width, Is.EqualTo(320));
      Assert.That(read.Height, Is.EqualTo(200));
      Assert.That(read.OddField, Has.Length.EqualTo(odd.Length));
    } finally {
      directory.Delete(true);
    }
  }

  [Test]
  [Category("Unit")]
  public void PerfectPix_NamesOnlyColoursTheFirmwareHas() {
    var file = FileFormat.PerfectPix.PerfectPixFile.FromRawImage(_Picture(320, 200));
    var head = FileFormat.PerfectPix.PerfectPixWriter.ToBytes(file);

    Assert.That(head[0], Is.EqualTo(4), "the sixteen-colour mode whose fields stay in register");
    for (var i = 0; i < 16; ++i)
      Assert.That(head[6 + i], Is.LessThanOrEqualTo(26), $"colour {i}");
  }
}
