using System;
using FileFormat.Core;

namespace FileFormat.Mdp.Tests;

/// <summary>
/// MDP is reachable through <see cref="IImageFromRawImage{TSelf}"/> only because the three
/// page-layout bytes a picture cannot state are defaulted. What those defaults are is therefore part
/// of the format's public behaviour, not an implementation detail, and a round trip has to carry
/// them back unchanged.
/// </summary>
[TestFixture]
public sealed class MdpGenericWriteTests {

  [Test]
  [Category("Integration")]
  public void FromRawImage_ThroughInterface_UsesTheDocumentedPageDefaultsAndRoundTrips() {
    var pixels = new byte[8 * 4 * 3];
    Array.Fill(pixels, (byte)255);
    var source = new RawImage {
      Width = 8,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var file = _Encode<MdpFile>(source);
    var restored = MdpReader.FromBytes(MdpWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(file.Resolution, Is.EqualTo(MdpFile.DefaultResolution));
      // Wider than it is tall, so the landscape member of the pair.
      Assert.That(file.PageFormat, Is.EqualTo(MdpPageFormat.A4Landscape));
      Assert.That(file.PageRamBlocks, Is.EqualTo(1));
      Assert.That(file.SerialNumber, Is.EqualTo("0000000"));
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }));
      Assert.That(restored.Width, Is.EqualTo(file.Width));
      Assert.That(restored.Height, Is.EqualTo(file.Height));
      Assert.That(restored.Resolution, Is.EqualTo(file.Resolution));
      Assert.That(restored.PageFormat, Is.EqualTo(file.PageFormat));
      Assert.That(restored.PageRamBlocks, Is.EqualTo(file.PageRamBlocks));
      Assert.That(restored.SerialNumber, Is.EqualTo(file.SerialNumber));
      Assert.That(restored.RasterData, Is.EqualTo(file.RasterData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TallerThanItIsWide_TakesThePortraitPageFormat() {
    var pixels = new byte[8 * 16 * 3];
    Array.Fill(pixels, (byte)255);
    var source = new RawImage {
      Width = 8,
      Height = 16,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    Assert.That(MdpFile.FromRawImage(source).PageFormat, Is.EqualTo(MdpPageFormat.A4Portrait));
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);
}
