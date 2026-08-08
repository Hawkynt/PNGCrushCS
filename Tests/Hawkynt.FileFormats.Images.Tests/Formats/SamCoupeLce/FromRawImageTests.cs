using FileFormat.Core;
using FileFormat.SamCoupeMode4;

namespace FileFormat.SamCoupeLce.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture the pair of screens can hold exactly: every stored pixel two screen pixels wide, and
  /// at most sixteen of the hardware's colours on the scanlines of either parity — the two screens
  /// keep separate palettes, so the picture as a whole may hold thirty-two.
  /// </summary>
  private static RawImage TwoFieldsOfSixteen() {
    const int width = SamCoupeLceFile.Width;
    const int height = SamCoupeLceFile.Height;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; x += 2) {
      // The even scanlines take colour bytes 1 to 16 and the odd ones 17 to 32, so no colour of one
      // field is a colour of the other.
      var color = SamCoupePalette.ToRgb((byte)((y & 1) * 16 + (x / 2 + y / 2) % 16 + 1));

      for (var repeat = 0; repeat < 2; ++repeat) {
        var at = (y * width + x + repeat) * 3;
        rgb[at] = (byte)(color >> 16);
        rgb[at + 1] = (byte)(color >> 8);
        rgb[at + 2] = (byte)color;
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenColoursPerField_IsExact() {
    var source = TwoFieldsOfSixteen();

    var bytes = SamCoupeLceWriter.ToBytes(_Encode<SamCoupeLceFile>(source));
    var decoded = SamCoupeLceFile.ToRawImage(SamCoupeLceReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var rgb = new byte[101 * 67 * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i * 29);

    var file = _Encode<SamCoupeLceFile>(
      new() { Width = 101, Height = 67, Format = PixelFormat.Rgb24, PixelData = rgb });

    Assert.That(SamCoupeLceWriter.ToBytes(file), Has.Length.EqualTo(SamCoupeLceFile.ScreenSize * 2));
  }

  /// <summary>
  /// The second screen begins wherever the first one's interrupt list ends, and that is the only
  /// thing in the file that says how long either of them is.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_StartsTheSecondScreenWhereTheFirstListEnds() {
    var file = _Encode<SamCoupeLceFile>(TwoFieldsOfSixteen());

    Assert.Multiple(() => {
      Assert.That(file.SecondScreenOffset, Is.EqualTo(SamCoupeLceFile.ScreenSize));
      Assert.That(
        file.Data[SamCoupeLceFile.InterruptOffset],
        Is.EqualTo(SamCoupeLceFile.InterruptTerminator));
      Assert.That(
        SamCoupeLceReader.FromBytes(SamCoupeLceWriter.ToBytes(file)).SecondScreenOffset,
        Is.EqualTo(SamCoupeLceFile.ScreenSize));
    });
  }

  /// <summary>
  /// Each screen owns one parity of the display, so reducing the picture once and sharing the
  /// result would throw away half of the thirty-two colours the pair can hold.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_GivesEachScreenItsOwnPalette() {
    var data = _Encode<SamCoupeLceFile>(TwoFieldsOfSixteen()).Data;

    var first = data[SamCoupeLceFile.PaletteOffset..(SamCoupeLceFile.PaletteOffset + SamCoupeLceFile.PaletteSize)];
    var second = data[(SamCoupeLceFile.ScreenSize + SamCoupeLceFile.PaletteOffset)..
      (SamCoupeLceFile.ScreenSize + SamCoupeLceFile.PaletteOffset + SamCoupeLceFile.PaletteSize)];

    Assert.That(second, Is.Not.EqualTo(first));
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
