using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MadDesigner;

namespace FileFormat.MadDesigner.Tests;

[TestFixture]
public sealed class MadDesignerTests {

  [Test]
  public void Reader_RejectsAnyOtherLength() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => MadDesignerReader.FromBytes(new byte[MadDesignerFile.FileSize - 1]));
      Assert.Throws<InvalidDataException>(() => MadDesignerReader.FromBytes(new byte[MadDesignerFile.FileSize + 1]));
    });
  }

  [Test]
  public void Bits_RunMostSignificantFirst() {
    var data = new byte[MadDesignerFile.FileSize];
    data[0] = 0b1000_0001;

    var image = MadDesignerFile.ToRawImage(MadDesignerReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(image.PixelData[0], Is.EqualTo(1), "leftmost pixel");
      Assert.That(image.PixelData[7], Is.EqualTo(1), "rightmost pixel of the byte");
      Assert.That(image.PixelData[1], Is.Zero);
    });
  }

  [Test]
  public void Palette_IsTheTwoColorsTheProgramDrawsWith() {
    var image = MadDesignerFile.ToRawImage(MadDesignerReader.FromBytes(new byte[MadDesignerFile.FileSize]));
    // Materialised before the lambda: a span local cannot be captured.
    var background = Atari8BitGraphics.Palette.Slice(MadDesignerFile.BackgroundColor * 3, 3).ToArray();
    var ink = Atari8BitGraphics.Palette.Slice(MadDesignerFile.InkColor * 3, 3).ToArray();

    Assert.Multiple(() => {
      Assert.That(image.PaletteCount, Is.EqualTo(2));
      Assert.That(image.Palette![..3], Is.EqualTo(background));
      Assert.That(image.Palette![3..6], Is.EqualTo(ink));
    });
  }

  [Test]
  public void Dimensions_AreFixed() {
    var image = MadDesignerFile.ToRawImage(MadDesignerReader.FromBytes(new byte[MadDesignerFile.FileSize]));

    Assert.That((image.Width, image.Height), Is.EqualTo((512, 256)));
  }

  [Test]
  public void RoundTrip_PreservesTheBitmap() {
    var data = new byte[MadDesignerFile.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 31);

    var file = MadDesignerReader.FromBytes(data);
    var reread = MadDesignerReader.FromBytes(MadDesignerWriter.ToBytes(file));

    Assert.That(reread.BitmapData, Is.EqualTo(data));
  }

  [Test]
  public void Encoding_ThenDecoding_ReproducesATwoColorPicture() {
    // The two colours are fixed and not stored, so a picture already drawn in them survives exactly.
    var palette = MadDesignerFile.PaletteRgb();
    var source = new byte[MadDesignerFile.Width * MadDesignerFile.Height * 3];
    for (var y = 0; y < MadDesignerFile.Height; ++y)
    for (var x = 0; x < MadDesignerFile.Width; ++x) {
      var entry = ((x / 3) + (y / 5)) % 2;
      palette.AsSpan(entry * 3, 3).CopyTo(source.AsSpan((y * MadDesignerFile.Width + x) * 3));
    }

    var image = new RawImage {
      Width = MadDesignerFile.Width, Height = MadDesignerFile.Height,
      Format = PixelFormat.Rgb24, PixelData = source,
    };
    var decoded = PixelConverter.Convert(MadDesignerFile.ToRawImage(MadDesignerFile.FromRawImage(image)), PixelFormat.Rgb24);

    Assert.That(decoded.PixelData, Is.EqualTo(source));
  }
}
