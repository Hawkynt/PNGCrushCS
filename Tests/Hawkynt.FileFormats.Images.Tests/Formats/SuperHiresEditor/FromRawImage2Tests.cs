using FileFormat.Core;

namespace FileFormat.SuperHiresEditor.Tests;

[TestFixture]
public sealed class FromRawImage2Tests {

  /// <summary>A picture the bitmap can hold exactly: two of the machine's colours per 8x8 cell.</summary>
  private static RawImage TwoColoursPerCell(int width, int height) {
    var palette = Commodore64Graphics.CreatePalette();
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = y / 8 * (width / 8 + 1) + x / 8;
      var index = ((x + y) & 3) < 2 ? cell * 7 % 16 : (cell * 11 + 5) % 16;
      var at = (y * width + x) * 3;
      rgb[at] = palette[index * 3];
      rgb[at + 1] = palette[index * 3 + 1];
      rgb[at + 2] = palette[index * 3 + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoColoursPerCell_IsExact() {
    var source = TwoColoursPerCell(SuperHiresEditor2File.Width, SuperHiresEditor2File.Height);

    var bytes = SuperHiresEditor2Writer.ToBytes(_Encode<SuperHiresEditor2File>(source));
    var decoded = SuperHiresEditor2File.ToRawImage(SuperHiresEditor2Reader.FromBytes(bytes));

    Assert.That(decoded.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<SuperHiresEditor2File>(TwoColoursPerCell(53, 91));

    Assert.That(
      SuperHiresEditor2Writer.ToBytes(file),
      Has.Length.EqualTo(SuperHiresEditor2File.PlainFileSize));
  }

  /// <summary>
  /// A packed file stores its sprites column by column and a plain one the way the hardware wants
  /// them; the picture is written plain, so it must say so.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_DescribesThePlainLayout() {
    var file = _Encode<SuperHiresEditor2File>(
      TwoColoursPerCell(SuperHiresEditor2File.Width, SuperHiresEditor2File.Height));

    Assert.Multiple(() => {
      Assert.That(file.ColumnSprites, Is.False);
      Assert.That(file.ScreenStride, Is.EqualTo(Commodore64Graphics.Columns));
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
