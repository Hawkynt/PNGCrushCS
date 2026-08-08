using FileFormat.Core;
using FileFormat.SamCoupeMode4;

namespace FileFormat.SamCoupeScreen.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture mode 1 can hold exactly: two colours per 8x8 cell, both of them either dark or light,
  /// since a cell's ink and paper share one bright flag and the palette is ordered by brightness.
  /// </summary>
  /// <remarks>
  /// The eight dark colours are the ones with only low bits set, whose channels reach 0x49; the
  /// eight light ones have all three high bits set and reach at least 0x92. So no dark one is
  /// brighter than any light one, and which half each falls in is not a matter of where the
  /// reduction happened to put it.
  /// </remarks>
  private static RawImage TwoColoursPerCellOfOneHalf(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = y / 8 * (width / 8 + 1) + x / 8;
      var light = cell % 2 * 112;
      var entry = light + (((x + y) & 3) < 2 ? cell * 3 % 8 : (cell * 5 + 1) % 8);
      var color = SamCoupePalette.ToRgb((byte)entry);

      var at = (y * width + x) * 3;
      rgb[at] = (byte)(color >> 16);
      rgb[at + 1] = (byte)(color >> 8);
      rgb[at + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoColoursPerCell_IsExact() {
    var source = TwoColoursPerCellOfOneHalf(256, SamCoupeScreenFile.ScreenHeight);

    var bytes = SamCoupeScreenWriter.ToBytes(_Encode<SamCoupeScreenFile>(source));
    var decoded = SamCoupeScreenFile.ToRawImage(SamCoupeScreenReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<SamCoupeScreenFile>(TwoColoursPerCellOfOneHalf(101, 67));

    Assert.That(
      SamCoupeScreenWriter.ToBytes(file), Has.Length.EqualTo(SamCoupeScreenFile.Mode1FileSize));
  }

  /// <summary>
  /// Where the interrupt list starts is what fixes the mode, and where it ends must be the end of
  /// the file — so a written screen has to say mode 1 in both places at once, and its rows have to
  /// go in the Spectrum's shuffled order rather than in the one they are drawn in.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesModeOneWithAnEmptyInterruptList() {
    var file = _Encode<SamCoupeScreenFile>(
      TwoColoursPerCellOfOneHalf(256, SamCoupeScreenFile.ScreenHeight));

    Assert.Multiple(() => {
      Assert.That(file.Mode, Is.EqualTo(SamCoupeScreenMode.Mode1));
      Assert.That(
        file.Data[SamCoupeScreenFile.InterruptOffsetFor(SamCoupeScreenMode.Mode1)],
        Is.EqualTo(SamCoupeScreenFile.InterruptTerminator));
      Assert.That(file.Data, Has.Length.EqualTo(SamCoupeScreenFile.Mode1FileSize));

      // Read back without its name, the length alone must still settle it as mode 1.
      Assert.That(
        SamCoupeScreenReader.FromBytes(SamCoupeScreenWriter.ToBytes(file)).Mode,
        Is.EqualTo(SamCoupeScreenMode.Mode1));
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
