using FileFormat.Core;

namespace FileFormat.UifliEditor.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture the screen can hold exactly. The video matrix changes every other scanline rather
  /// than every one, so a block is eight pixels by two, and within it the two frames give two of the
  /// machine's colours and their average.
  /// </summary>
  private static RawImage ThreeShadesPerBlock(int width, int height) {
    var palette = Commodore64Graphics.CreatePalette();
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var block = y / 2 * (width / 8 + 1) + x / 8;
      var first = block * 7 % 16;
      var second = (block * 11 + 5) % 16;

      var at = (y * width + x) * 3;
      for (var channel = 0; channel < 3; ++channel) {
        var high = palette[first * 3 + channel];
        var low = palette[second * 3 + channel];

        rgb[at + channel] = ((x + y) & 3) switch {
          0 or 1 => high,
          2 => low,
          _ => (byte)((high + low) / 2),
        };
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThreeShadesPerBlock_IsExact() {
    var source = ThreeShadesPerBlock(UifliEditorFile.Width, UifliEditorFile.Height);

    var bytes = UifliEditorWriter.ToBytes(_Encode<UifliEditorFile>(source));
    var decoded = UifliEditorFile.ToRawImage(UifliEditorReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<UifliEditorFile>(ThreeShadesPerBlock(85, 51));

    Assert.That(file.Data, Has.Length.EqualTo(UifliEditorFile.UnpackedSize));
  }

  /// <summary>
  /// The sprites draw in one colour for the whole picture and cover four bitmap pixels apiece, so
  /// leaving them alone means leaving them empty in both frames.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_LeavesTheSpritesOfBothFramesClear() {
    var data = _Encode<UifliEditorFile>(
      ThreeShadesPerBlock(UifliEditorFile.Width, UifliEditorFile.Height)).Data;

    Assert.Multiple(() => {
      Assert.That(data[UifliEditorFile.FirstSpriteOffset..UifliEditorFile.FirstBitmapOffset], Is.All.Zero);
      Assert.That(data[UifliEditorFile.SecondSpriteOffset..UifliEditorFile.SecondBitmapOffset], Is.All.Zero);
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
