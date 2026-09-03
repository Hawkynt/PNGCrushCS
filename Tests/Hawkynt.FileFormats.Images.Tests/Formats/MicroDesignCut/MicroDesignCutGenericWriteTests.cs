using System;
using FileFormat.Core;

namespace FileFormat.MicroDesignCut.Tests;

[TestFixture]
public sealed class MicroDesignCutGenericWriteTests {

  [Test]
  [Category("Integration")]
  public void FromRawImage_ThroughInterface_UsesLowerHeightAliasAndRoundTrips() {
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
      Assert.That(file.HeightCode, Is.EqualTo(3));
      Assert.That(file.WidthCode, Is.EqualTo(6));
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00 }));
      Assert.That(restored.HeightCode, Is.EqualTo(file.HeightCode));
      Assert.That(restored.WidthCode, Is.EqualTo(file.WidthCode));
      Assert.That(restored.RasterData, Is.EqualTo(file.RasterData));
    });
  }

  [Test]
  [Category("Unit")]
  public void CanonicalHeightCode_ChoosesLowerAliasAcrossRepresentableRange() {
    Assert.Multiple(() => {
      Assert.That(MicroDesignCutFile.GetCanonicalHeightCode(1), Is.EqualTo(0));
      Assert.That(MicroDesignCutFile.GetCanonicalHeightCode(2), Is.EqualTo(1));
      Assert.That(MicroDesignCutFile.GetCanonicalHeightCode(3), Is.EqualTo(3));
      Assert.That(MicroDesignCutFile.GetCanonicalHeightCode(MicroDesignCutFile.GetHeight(ushort.MaxValue)), Is.EqualTo(ushort.MaxValue));
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);
}
