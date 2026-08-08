using FileFormat.Core;

namespace FileFormat.TobiasRichterSlideshow.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture the screen can hold exactly: at most sixteen colours on any one scanline, each of
  /// them one the ST can make — three bits a channel, scaled so that seven is white.
  /// </summary>
  private static RawImage SixteenColoursPerScanline(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var entry = x * 16 / width;
      var at = (y * width + x) * 3;
      rgb[at] = _Channel((entry + y) % 8);
      rgb[at + 1] = _Channel(entry % 8);
      rgb[at + 2] = _Channel((entry * 3 + y * 5) % 8);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>One of the eight intensities a three-bit channel comes back as.</summary>
  private static byte _Channel(int value) => ChannelScaling.Expand3(value);

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenColoursPerScanline_IsExact() {
    var source = SixteenColoursPerScanline(
      TobiasRichterSlideshowFile.Width, TobiasRichterSlideshowFile.Height);

    var bytes = TobiasRichterSlideshowWriter.ToBytes(_Encode<TobiasRichterSlideshowFile>(source));
    var decoded = TobiasRichterSlideshowFile.ToRawImage(TobiasRichterSlideshowReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<TobiasRichterSlideshowFile>(SixteenColoursPerScanline(101, 67));

    Assert.That(
      TobiasRichterSlideshowWriter.ToBytes(file),
      Has.Length.EqualTo(TobiasRichterSlideshowFile.FileSize));
  }

  /// <summary>
  /// Which of the two palette forms a file is in is settled from all of its palettes at once, so a
  /// single line written in the wider one would shift every channel of every other line.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsEveryPaletteInThePlainForm() {
    var data = _Encode<TobiasRichterSlideshowFile>(SixteenColoursPerScanline(
      TobiasRichterSlideshowFile.Width, TobiasRichterSlideshowFile.Height)).Data;

    Assert.That(
      AtariStGraphics.IsStePalette(
        data,
        TobiasRichterSlideshowFile.PaletteOffset,
        TobiasRichterSlideshowFile.PaletteLineCount * TobiasRichterSlideshowFile.ColorCount),
      Is.False);
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
