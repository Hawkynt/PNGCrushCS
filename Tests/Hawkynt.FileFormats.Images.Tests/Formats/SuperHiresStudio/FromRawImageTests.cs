using FileFormat.Core;

namespace FileFormat.SuperHiresStudio.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A picture the screen can hold exactly: two of the machine's colours per 8x8 cell.</summary>
  private static RawImage TwoColoursPerCell(int width, int height) {
    var palette = Commodore64Graphics.CreatePalette();
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = y / 8 * (width / 8 + 1) + x / 8;

      // Two colours a cell, and a diagonal within it so neither the bitmap nor the matrix is
      // uniform enough to hide a mistake in the other.
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
    var source = TwoColoursPerCell(SuperHiresStudioFile.Width, SuperHiresStudioFile.Height);

    var bytes = SuperHiresStudioWriter.ToBytes(_Encode<SuperHiresStudioFile>(source));
    var decoded = SuperHiresStudioFile.ToRawImage(SuperHiresStudioReader.FromBytes(bytes));

    Assert.That(decoded.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<SuperHiresStudioFile>(TwoColoursPerCell(101, 67));

    Assert.That(SuperHiresStudioWriter.ToBytes(file), Has.Length.EqualTo(SuperHiresStudioFile.FileSize));
  }

  /// <summary>
  /// The sprite window is what the format exists for, and this encoder deliberately leaves it clear
  /// — so the picture inside it must come from the bitmap like everywhere else.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_LeavesTheSpriteWindowClear() {
    var data = _Encode<SuperHiresStudioFile>(
      TwoColoursPerCell(SuperHiresStudioFile.Width, SuperHiresStudioFile.Height)).Data;

    Assert.That(
      data[SuperHiresStudioFile.BackgroundSpritesOffset..SuperHiresStudioFile.VideoMatrixOffset],
      Is.All.Zero);
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
