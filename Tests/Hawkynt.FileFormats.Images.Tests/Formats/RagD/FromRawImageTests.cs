using FileFormat.Core;

namespace FileFormat.RagD.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A picture of at most 256 colours, which is all eight bitplanes can address.</summary>
  private static RawImage UpToTwoHundredAndFiftySixColours(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var i = 0; i < width * height; ++i) {
      var index = i % 256;
      rgb[i * 3] = (byte)index;
      rgb[i * 3 + 1] = (byte)(255 - index);
      rgb[i * 3 + 2] = (byte)(index * 3 & 0xFF);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoHundredAndFiftySixColours_IsExact() {
    var source = UpToTwoHundredAndFiftySixColours(64, 40);

    var bytes = RagDWriter.ToBytes(_Encode<RagDFile>(source));
    var decoded = RagDFile.ToRawImage(RagDReader.FromBytes(bytes));

    Assert.That(decoded.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
  }

  /// <summary>
  /// A row is a whole number of words, so a width the header cannot state is sampled up to the next
  /// one it can rather than refused.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_RoundsAWidthUpToAWholeNumberOfWords() {
    var file = _Encode<RagDFile>(UpToTwoHundredAndFiftySixColours(101, 37));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(112));
      Assert.That(file.Height, Is.EqualTo(37));
      Assert.That(RagDReader.FromBytes(RagDWriter.ToBytes(file)).Width, Is.EqualTo(112));
    });
  }

  /// <summary>
  /// Eight bitplanes and one byte a pixel take exactly the same room and the header does not tell
  /// them apart — only the file name does, and a file read without one is taken as bitplanes. So
  /// bitplanes is what has to be written.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesBitplanesAgainstAFalconPalette() {
    var file = _Encode<RagDFile>(UpToTwoHundredAndFiftySixColours(64, 40));

    Assert.Multiple(() => {
      Assert.That(file.IsChunky, Is.False);
      Assert.That(file.Planes, Is.EqualTo(RagDFile.WrittenPlanes));
      Assert.That(file.PaletteLength, Is.EqualTo(RagDFile.FalconPaletteLength));
      Assert.That(RagDReader.FromBytes(RagDWriter.ToBytes(file)).IsChunky, Is.False);
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
