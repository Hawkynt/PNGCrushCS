using FileFormat.Core;

namespace FileFormat.SuperHiresFli.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture the narrow form can hold exactly. Its colour map carries a pair of colours for every
  /// eight pixels of every scanline, so the limit is two of the machine's colours per group of
  /// eight; a picture obeying the stricter eight-by-eight rule obeys this one too.
  /// </summary>
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
  public void RoundTrip_TwoColoursPerEightPixels_IsExact() {
    var source = TwoColoursPerCell(SuperHiresFliFile.NarrowWidth, SuperHiresFliFile.Height);

    var bytes = SuperHiresFliWriter.ToBytes(_Encode<SuperHiresFliFile>(source));
    var decoded = SuperHiresFliFile.ToRawImage(SuperHiresFliReader.FromBytes(bytes));

    Assert.That(decoded.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<SuperHiresFliFile>(TwoColoursPerCell(75, 43));

    Assert.Multiple(() => {
      Assert.That(file.Data, Has.Length.EqualTo(SuperHiresFliFile.UnpackedSize));
      Assert.That(
        SuperHiresFliFile.ToRawImage(file).Width, Is.EqualTo(SuperHiresFliFile.NarrowWidth));
    });
  }

  /// <summary>
  /// The wide form is recognised by its length and nothing else, so a packed file that happened to
  /// come out at exactly that length would be read as a different picture entirely.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_NeverProducesTheWideFormLength() {
    var flat = new byte[SuperHiresFliFile.NarrowWidth * SuperHiresFliFile.Height * 3];
    var bytes = SuperHiresFliWriter.ToBytes(_Encode<SuperHiresFliFile>(
      new() {
        Width = SuperHiresFliFile.NarrowWidth,
        Height = SuperHiresFliFile.Height,
        Format = PixelFormat.Rgb24,
        PixelData = flat,
      }));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.Not.EqualTo(SuperHiresFliFile.WideFileSize));
      Assert.That(SuperHiresFliReader.FromBytes(bytes).HasSprites, Is.False);
    });
  }

  /// <summary>
  /// The two sprite planes override the colour map wherever they are set, so an encoder that leaves
  /// them alone has to leave them empty rather than merely unwritten.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_LeavesBothSpritePlanesClear() {
    var data = _Encode<SuperHiresFliFile>(
      TwoColoursPerCell(SuperHiresFliFile.NarrowWidth, SuperHiresFliFile.Height)).Data;

    Assert.Multiple(() => {
      Assert.That(data[..SuperHiresFliFile.NarrowSecondSpriteOffset], Is.All.Zero);
      Assert.That(
        data[SuperHiresFliFile.NarrowSecondSpriteOffset..SuperHiresFliFile.NarrowBitmapOffset],
        Is.All.Zero);
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
