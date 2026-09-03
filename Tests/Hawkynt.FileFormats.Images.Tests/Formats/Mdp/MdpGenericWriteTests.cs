using FileFormat.Core;

namespace FileFormat.Mdp.Tests;

[TestFixture]
public sealed class MdpGenericWriteTests {

  [Test]
  [Category("Integration")]
  public void FromRawImage_ThroughInterface_UsesCanonicalPageMetadataAndRoundTrips() {
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
      Assert.That(file.PageFormat, Is.EqualTo(MdpFile.DefaultPageFormat));
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

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);
}
