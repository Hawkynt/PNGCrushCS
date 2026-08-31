using System;
using System.IO;
using FileFormat.AtariDoodle;
using FileFormat.Core;

namespace FileFormat.AtariDoodle.Tests;

[TestFixture]
public sealed class AtariDoodleConformanceTests {

  [Test]
  public void Reader_RequiresExactPublishedFileSize() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => AtariDoodleReader.FromBytes(new byte[AtariDoodleFile.ScreenDataSize - 1]));
      Assert.Throws<InvalidDataException>(() => AtariDoodleReader.FromBytes(new byte[AtariDoodleFile.ScreenDataSize + 1]));
    });
  }

  [Test]
  public void Reader_PreservesScreenMemoryByteForByte() {
    var bytes = new byte[AtariDoodleFile.ScreenDataSize];
    for (var i = 0; i < bytes.Length; ++i)
      bytes[i] = (byte)(i * 37 + 11);

    var file = AtariDoodleReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(640));
      Assert.That(file.Height, Is.EqualTo(400));
      Assert.That(file.ScreenData, Is.EqualTo(bytes));
      Assert.That(AtariDoodleWriter.ToBytes(file), Is.EqualTo(bytes));
    });
  }

  [Test]
  public void ToRawImage_UsesAtariHighResolutionBitPolarity() {
    var screen = new byte[AtariDoodleFile.ScreenDataSize];
    screen[0] = 0x80;

    var image = AtariDoodleFile.ToRawImage(new AtariDoodleFile { ScreenData = screen });

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(image.PixelData[0], Is.EqualTo(1), "set high-resolution bit is black ink");
      Assert.That(image.PixelData[1], Is.EqualTo(0), "clear high-resolution bit is white paper");
      Assert.That(image.Palette, Is.EqualTo(new byte[] { 255, 255, 255, 0, 0, 0 }));
      Assert.That(image.PaletteCount, Is.EqualTo(2));
    });
  }

  [Test]
  public void FromRawImage_BlackSetsBitAndWhiteClearsIt() {
    var pixels = new byte[AtariDoodleFile.FixedWidth * AtariDoodleFile.FixedHeight * 3];
    Array.Fill(pixels, (byte)255);
    pixels[0] = pixels[1] = pixels[2] = 0;

    var file = AtariDoodleFile.FromRawImage(new RawImage {
      Width = AtariDoodleFile.FixedWidth,
      Height = AtariDoodleFile.FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    });

    Assert.Multiple(() => {
      Assert.That(file.ScreenData[0] & 0x80, Is.EqualTo(0x80));
      Assert.That(file.ScreenData[0] & 0x40, Is.Zero);
    });
  }

  [Test]
  public void FromRawImage_RejectsGeometryThatRawFormatCannotRepresentUnambiguously() {
    var image = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<ArgumentException>(() => AtariDoodleFile.FromRawImage(image));
  }

  [Test]
  public void Writer_RejectsWrongScreenLength() {
    var file = new AtariDoodleFile { ScreenData = new byte[AtariDoodleFile.ScreenDataSize - 1] };
    Assert.Throws<ArgumentException>(() => AtariDoodleWriter.ToBytes(file));
  }

  [Test]
  public void StreamReader_ConsumesFromCurrentPositionOnly() {
    var prefix = new byte[17];
    var payload = new byte[AtariDoodleFile.ScreenDataSize];
    payload[0] = 0xA5;
    using var stream = new MemoryStream(new byte[prefix.Length + payload.Length]);
    stream.Position = prefix.Length;
    stream.Write(payload);
    stream.Position = prefix.Length;

    var file = AtariDoodleReader.FromStream(stream);

    Assert.That(file.ScreenData[0], Is.EqualTo(0xA5));
  }
}
