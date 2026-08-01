using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.GraphSaurus.Tests;

/// <summary>A Graph Saurus screen: a BSAVE header, then whichever mode the length names.</summary>
[TestFixture]
public sealed class GraphSaurusTests {

  private const int Screen5Size = 7 + 212 * 128;
  private const int Screen8Size = 7 + 212 * 256;

  private static RawImage _Picture() {
    var pixels = new byte[256 * 212 * 3];
    for (var y = 0; y < 212; ++y)
    for (var x = 0; x < 256; ++x) {
      var at = (y * 256 + x) * 3;
      pixels[at] = (byte)x;
      pixels[at + 1] = (byte)(y * 255 / 211);
      pixels[at + 2] = (byte)((x + y) & 0xFF);
    }

    return new() { Width = 256, Height = 212, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_CarriesTheBsaveHeaderThatMakesItAFile() {
    var bytes = GraphSaurusWriter.ToBytes(GraphSaurusFile.FromRawImage(_Picture()));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(Screen5Size), "four bits a pixel, which is Screen 5");
      Assert.That(bytes[0], Is.EqualTo(0xFE), "the BSAVE marker");
      Assert.That(MsxGraphics.ReadBsaveEndAddress(bytes), Is.EqualTo(212 * 128 - 1));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesTheModeFromTheLength() {
    var five = new byte[Screen5Size];
    five[0] = 0xFE;
    var eight = new byte[Screen8Size];
    eight[0] = 0xFE;

    Assert.Multiple(() => {
      Assert.That(GraphSaurusReader.FromBytes(five).IsScreen8, Is.False);
      Assert.That(GraphSaurusReader.FromBytes(eight).IsScreen8, Is.True);
    });
  }

  [TestCase(54272)]
  [TestCase(Screen5Size - 1)]
  [TestCase(0)]
  [Category("Unit")]
  public void Read_RefusesALengthThatNamesNoScreen(int length)
    => Assert.Throws<InvalidDataException>(() => GraphSaurusReader.FromBytes(new byte[length]));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileWithNoBsaveHeader() {
    var bytes = new byte[Screen5Size];
    bytes[1] = 1;

    Assert.Throws<InvalidDataException>(() => GraphSaurusReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void Screen8_ReadsAsGreenRedBlueRatherThanRedGreenBlue() {
    var bytes = new byte[Screen8Size];
    bytes[0] = 0xFE;
    // Green in the top three bits, red in the next three, blue in the bottom two.
    bytes[7] = 0xE0;

    var image = GraphSaurusFile.ToRawImage(GraphSaurusReader.FromBytes(bytes));
    var index = image.PixelData[0];
    Assert.Multiple(() => {
      Assert.That(image.Palette![index * 3], Is.EqualTo(0), "no red");
      Assert.That(image.Palette[index * 3 + 1], Is.EqualTo(255), "all green");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheBitmap() {
    var original = GraphSaurusFile.FromRawImage(_Picture());
    var restored = GraphSaurusReader.FromBytes(GraphSaurusWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.IsScreen8, Is.False);
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void WrittenToFile_PutsThePaletteBesideThePicture() {
    var directory = Directory.CreateTempSubdirectory("graphsaurus");
    try {
      var target = new FileInfo(Path.Combine(directory.FullName, "sample.sr5"));
      Assert.That(FormatRegistry.Write(_Picture(), ImageFormat.GraphSaurus, target), Is.True);

      var palette = new FileInfo(Path.Combine(directory.FullName, "sample.pl5"));
      Assert.That(palette.Exists, Is.True, "a Screen 5 picture is sixteen indices and nothing else");
      Assert.That(palette.Length, Is.EqualTo(32));

      // And reading it back through the same path must find that palette again.
      var read = GraphSaurusReader.FromFile(target);
      Assert.That(read.Palette, Is.Not.Null);
    } finally {
      directory.Delete(true);
    }
  }
}
