using System;
using FileFormat.Core;

namespace FileFormat.MicroDesignCut.Tests;

/// <summary>
/// Two height codes decode to almost every pixel height, so the one the parameterless conversion
/// picks is a decision the format's public behaviour rests on. These tests pin it, and pin that a
/// file written from pixels alone reads back with the same codes.
/// </summary>
[TestFixture]
public sealed class MicroDesignCutGenericWriteTests {

  [Test]
  [Category("Integration")]
  public void FromRawImage_ThroughInterface_UsesTheChosenHeightCodeAndRoundTrips() {
    var pixels = new byte[8 * 3 * 3];
    Array.Fill(pixels, (byte)255);
    var source = new RawImage {
      Width = 8,
      Height = 3,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var file = _Encode<MicroDesignCutFile>(source);
    var restored = MicroDesignCutReader.FromBytes(MicroDesignCutWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(file.HeightCode, Is.EqualTo(4));
      Assert.That(file.WidthCode, Is.EqualTo(6));
      // A width of exactly eight still stores a second, wholly unused byte per row.
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00 }));
      Assert.That(restored.HeightCode, Is.EqualTo(file.HeightCode));
      Assert.That(restored.WidthCode, Is.EqualTo(file.WidthCode));
      Assert.That(restored.RasterData, Is.EqualTo(file.RasterData));
    });
  }

  [Test]
  [Category("Unit")]
  public void HeightCodeFor_DecodesBackToTheHeightItWasAskedFor() {
    Assert.Multiple(() => {
      for (var height = 1; height <= 64; ++height)
        Assert.That(
          MicroDesignCutFile.GetHeight(MicroDesignCutFile.HeightCodeFor(height)),
          Is.EqualTo(height),
          $"height {height}");

      const int LARGEST = (ushort.MaxValue + 2) / 2;
      Assert.That(
        MicroDesignCutFile.GetHeight(MicroDesignCutFile.HeightCodeFor(LARGEST)),
        Is.EqualTo(LARGEST));
    });
  }

  [Test]
  [Category("Unit")]
  public void HeightCodeFor_RefusesHeightsTheCodeCannotHold() {
    Assert.Multiple(() => {
      Assert.Throws<ArgumentOutOfRangeException>(() => MicroDesignCutFile.HeightCodeFor(0));
      Assert.Throws<ArgumentOutOfRangeException>(() => MicroDesignCutFile.HeightCodeFor((ushort.MaxValue + 2) / 2 + 1));
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);
}
