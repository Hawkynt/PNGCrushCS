using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Paradox;

namespace FileFormat.Paradox.Tests;

[TestFixture]
public sealed class ParadoxTests {

  private static ParadoxFile _Sample() {
    var pixels = new byte[Atari8BitGraphics.Gr7Width * ParadoxFile.FieldRows];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % ParadoxFile.ColorCount);

    return new() {
      FirstField = Atari8BitGraphics.PackGr7(pixels, ParadoxFile.FieldRows),
      SecondField = Atari8BitGraphics.PackGr7(pixels, ParadoxFile.FieldRows),
      FirstFieldColors = [0x28, 0x4A, 0x6C, 0x00],
      SecondFieldColors = [0x38, 0x5A, 0x7C, 0x00],
    };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is8008() {
    // Two 4000-byte half-height fields plus two four-byte colour sets.
    Assert.That(ParadoxFile.FileSize, Is.EqualTo(8008));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesBothFieldsAndBothColorSets() {
    var original = _Sample();
    var restored = ParadoxReader.FromBytes(ParadoxWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.FirstField, Is.EqualTo(original.FirstField));
      Assert.That(restored.SecondField, Is.EqualTo(original.SecondField));
      Assert.That(restored.FirstFieldColors, Is.EqualTo(original.FirstFieldColors));
      Assert.That(restored.SecondFieldColors, Is.EqualTo(original.SecondFieldColors));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => ParadoxReader.FromBytes(new byte[ParadoxFile.FileSize - 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_InterleavesTheFieldsAndOffersEightColours() {
    var raw = ParadoxFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(ParadoxFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(ParadoxFile.DisplayHeight));
      // Two four-colour fields give eight palette entries between them.
      Assert.That(raw.PaletteCount, Is.EqualTo(ParadoxFile.ColorCount * 2));
      // Odd rows draw from the second field, so their indices sit in the upper half.
      Assert.That(raw.PixelData[ParadoxFile.DisplayWidth], Is.GreaterThanOrEqualTo(ParadoxFile.ColorCount));
      Assert.That(raw.PixelData[0], Is.LessThan(ParadoxFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var w = ParadoxFile.DisplayWidth;
    var h = ParadoxFile.DisplayHeight;
    var data = new byte[w * h * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 1] = (byte)(i % 199);
      data[i + 3] = 255;
    }

    var raw = new RawImage { Width = w, Height = h, Format = PixelFormat.Rgba32, PixelData = data };

    Assert.That(ParadoxWriter.ToBytes(ParadoxFile.FromRawImage(raw)), Has.Length.EqualTo(ParadoxFile.FileSize));
  }
}
