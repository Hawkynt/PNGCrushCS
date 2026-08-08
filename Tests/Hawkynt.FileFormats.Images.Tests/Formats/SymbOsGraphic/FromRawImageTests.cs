using FileFormat.Core;

namespace FileFormat.SymbOsGraphic.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A picture drawn only in the sixteen colours a wide chunk can name.</summary>
  private static RawImage SixteenColours(int width, int height) {
    var palette = SymbOsGraphicFile.SixteenColorPalette;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = (x + y * 3) % 16;
      var at = (y * width + x) * 3;
      rgb[at] = palette[index * 3];
      rgb[at + 1] = palette[index * 3 + 1];
      rgb[at + 2] = palette[index * 3 + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// An odd width is the case worth pinning: two pixels share a byte, so the last one of a row has
  /// half a byte to itself and the row still has to be a whole number of them.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_AnOddWidth_IsExact() {
    var source = SixteenColours(37, 23);

    var bytes = SymbOsGraphicWriter.ToBytes(_Encode<SymbOsGraphicFile>(source));
    var decoded = SymbOsGraphicFile.ToRawImage(SymbOsGraphicReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(37));
      Assert.That(decoded.Height, Is.EqualTo(23));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>
  /// Nothing here has a screen to sample to — the file states its own size — so a picture of any
  /// size keeps it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsThePictureSize() {
    var file = _Encode<SymbOsGraphicFile>(SixteenColours(101, 7));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(101));
      Assert.That(file.Height, Is.EqualTo(7));
      Assert.That(SymbOsGraphicReader.FromBytes(SymbOsGraphicWriter.ToBytes(file)).Width, Is.EqualTo(101));
    });
  }

  /// <summary>
  /// A row of chunks ends with a marker and the last row does not, so a graphic written as one chunk
  /// must carry no marker at all — one would leave the reader a row with nothing to check against.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesOneWideChunkAndNoRowMarker() {
    var file = _Encode<SymbOsGraphicFile>(SixteenColours(37, 23));

    Assert.Multiple(() => {
      Assert.That(file.Chunks, Has.Count.EqualTo(1));
      Assert.That(file.Data[0], Is.EqualTo(SymbOsGraphicFile.WideHeader));
      Assert.That(file.Data[1], Is.EqualTo(SymbOsGraphicFile.WideHeaderKind));
      Assert.That(file.Data, Has.Length.EqualTo(SymbOsGraphicFile.WideHeaderSize + 23 * 19));
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
