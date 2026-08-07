using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.GraphSaurus7.Tests;

/// <summary>
/// A Graph Saurus Screen 7 picture: a BSAVE header, then 512 pixels a line at four bits each.
/// </summary>
/// <remarks>
/// The one Graph Saurus mode that had no reader. Screen 5, Screen 6, Screen 8 and the interlaced
/// Screen 7 were all here; plain Screen 7 was not, and the interlaced one is two frames of it.
/// </remarks>
[TestFixture]
public sealed class GraphSaurus7Tests {

  private static byte[] _ValidFile() {
    var data = new byte[GraphSaurus7File.MinimumFileSize];
    data[0] = 0xFE;

    var last = GraphSaurus7File.BitmapSize - 1;
    data[3] = (byte)last;
    data[4] = (byte)(last >> 8);

    // Left pixel in the high nibble, right in the low, so the two are told apart.
    for (var at = 0; at < GraphSaurus7File.BitmapSize; ++at)
      data[GraphSaurus7File.BitmapOffset + at] = 0x3A;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GraphSaurus7Reader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(
      () => GraphSaurus7Reader.FromBytes(new byte[GraphSaurus7File.MinimumFileSize - 1]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheBsaveMarker_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(
      () => GraphSaurus7Reader.FromBytes(new byte[GraphSaurus7File.MinimumFileSize]));

  [Test]
  [Category("Unit")]
  public void MinimumFileSize_IsTheHeaderAndTwoHundredAndTwelveRows() {
    // A Screen 5 picture is the same shape at half the width and is 27143; reading one as this
    // would draw it at twice the size. The reference decoder turns a short file down rather than
    // reading a part-height picture from the header, so this length is exact rather than a floor.
    Assert.That(GraphSaurus7File.MinimumFileSize, Is.EqualTo(54279));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_DrawsEachStoredRowTwice() {
    // 512 by 212 stored, drawn 512 by 424: the pixels are half as tall as they are wide.
    var picture = GraphSaurus7File.ToRawImage(GraphSaurus7Reader.FromBytes(_ValidFile()));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(512));
      Assert.That(picture.Height, Is.EqualTo(424));
      Assert.That(picture.PixelData[0], Is.EqualTo(picture.PixelData[512]), "row 0 and row 1 are one stored row");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_TakesTheHighNibbleAsTheLeftPixel() {
    var picture = GraphSaurus7File.ToRawImage(GraphSaurus7Reader.FromBytes(_ValidFile()));

    Assert.Multiple(() => {
      Assert.That(picture.PixelData[0], Is.EqualTo(0x3), "high nibble is drawn first");
      Assert.That(picture.PixelData[1], Is.EqualTo(0xA), "low nibble second");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_WithoutACompanion_UsesTheColoursTheMachineStartsWith() {
    var picture = GraphSaurus7File.ToRawImage(GraphSaurus7Reader.FromBytes(_ValidFile()));
    var expected = MsxGraphics.PaletteToRgb(MsxGraphics.DefaultPalette, GraphSaurus7File.ColorCount);

    Assert.That(picture.Palette, Is.EqualTo(expected));
  }

  [Test]
  [Category("Integration")]
  public void FromFile_ReadsThePaletteLyingBesideThePicture() {
    // Sixteen two-byte entries and no header of its own — the reference decoder reads a companion
    // from its first byte, so one carrying a BSAVE header comes out shifted by seven and wrong.
    var directory = Directory.CreateTempSubdirectory("saurus7");
    try {
      var picture = Path.Combine(directory.FullName, "probe.sr7");
      File.WriteAllBytes(picture, _ValidFile());

      var palette = new byte[GraphSaurus7File.ColorCount * MsxGraphics.PaletteEntrySize];
      palette[3 * MsxGraphics.PaletteEntrySize] = 0x70;   // entry 3: red at full, blue at none
      File.WriteAllBytes(Path.ChangeExtension(picture, ".pl7"), palette);

      var image = GraphSaurus7File.ToRawImage(GraphSaurus7Reader.FromFile(new(picture)));

      Assert.Multiple(() => {
        Assert.That(image.Palette![9], Is.EqualTo(255), "entry 3 red");
        Assert.That(image.Palette[10], Is.EqualTo(0), "entry 3 green");
        Assert.That(image.Palette[11], Is.EqualTo(0), "entry 3 blue");
      });
    } finally {
      directory.Delete(recursive: true);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThePictureComesBack() {
    var original = GraphSaurus7Reader.FromBytes(_ValidFile());

    var restored = GraphSaurus7Reader.FromBytes(GraphSaurus7Writer.ToBytes(original));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_KeepsThePaletteItChose() {
    var pixels = new byte[512 * 424 * 3];
    for (var y = 0; y < 424; ++y)
    for (var x = 0; x < 512; ++x) {
      var at = (y * 512 + x) * 3;
      pixels[at] = (byte)(x >> 1);
      pixels[at + 1] = (byte)(y * 255 / 423);
      pixels[at + 2] = (byte)((x + y) & 0xFF);
    }

    var file = GraphSaurus7File.FromRawImage(new() {
      Width = 512, Height = 424, Format = PixelFormat.Rgb24, PixelData = pixels,
    });

    Assert.Multiple(() => {
      Assert.That(GraphSaurus7Writer.ToBytes(file), Has.Length.EqualTo(GraphSaurus7File.MinimumFileSize));
      Assert.That(file.Palette, Has.Length.EqualTo(GraphSaurus7File.ColorCount * MsxGraphics.PaletteEntrySize),
        "the colours belong in a companion file, so the writer has to hand them back");
    });
  }
}
