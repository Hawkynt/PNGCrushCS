using FileFormat.Core;

namespace FileFormat.ShfXlEdit.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture the editor's layout can hold exactly: two of the machine's colours per group of
  /// eight pixels on one scanline, which is as fine as the colour map goes.
  /// </summary>
  private static RawImage TwoColoursPerGroup(int width, int height) {
    var palette = Commodore64Graphics.CreatePalette();
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var group = y * (width / 8 + 1) + x / 8;
      var index = ((x + y) & 3) < 2 ? group * 7 % 16 : (group * 11 + 5) % 16;
      var at = (y * width + x) * 3;
      rgb[at] = palette[index * 3];
      rgb[at + 1] = palette[index * 3 + 1];
      rgb[at + 2] = palette[index * 3 + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoColoursPerEightPixels_IsExact() {
    var source = TwoColoursPerGroup(ShfXlEditFile.Width, ShfXlEditFile.Height);

    var bytes = ShfXlEditWriter.ToBytes(_Encode<ShfXlEditFile>(source));
    var decoded = ShfXlEditFile.ToRawImage(ShfXlEditReader.FromBytes(bytes));

    Assert.That(decoded.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<ShfXlEditFile>(TwoColoursPerGroup(59, 33));

    Assert.Multiple(() => {
      Assert.That(file.Data, Has.Length.EqualTo(ShfXlEditFile.UnpackedSize));
      Assert.That(ShfXlEditFile.ToRawImage(file).Width, Is.EqualTo(ShfXlEditFile.Width));
    });
  }

  /// <summary>
  /// The form that is a copy of video memory is recognised by its length and nothing else, so a
  /// packed file that came out at exactly that length would be taken apart with the wrong map.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_PacksBackwardsAndNeverProducesTheVideoMemoryLength() {
    var bytes = ShfXlEditWriter.ToBytes(
      _Encode<ShfXlEditFile>(TwoColoursPerGroup(ShfXlEditFile.Width, ShfXlEditFile.Height)));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.Not.EqualTo(ShfXlEditFile.RawFileSize));
      Assert.That(ShfXlEditReader.FromBytes(bytes).IsRaw, Is.False);
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
