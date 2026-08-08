using FileFormat.Core;

namespace FileFormat.SeuckSprites.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A sheet the file can hold exactly: the space around the sprites in the one colour the decoder
  /// always puts there, and within each sprite only black, white, that same background and one
  /// colour of its own — every pixel doubled, since a multicolour pixel is two screen pixels wide.
  /// </summary>
  private static RawImage LegalSheet() {
    const int width = SeuckSpritesFile.Width;
    const int height = SeuckSpritesFile.Height;
    var palette = Commodore64Graphics.CreatePalette();
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      int row = y % SeuckSpritesFile.CellHeight, column = x % SeuckSpritesFile.CellWidth;
      var index = SeuckSpritesFile.BackgroundColor;

      if (row < SeuckSpritesFile.SpriteHeight && column < SeuckSpritesFile.SpriteWidth) {
        var sprite = x / SeuckSpritesFile.CellWidth + (y / SeuckSpritesFile.CellHeight) * SeuckSpritesFile.Columns;
        if (sprite < SeuckSpritesFile.SpriteCount) {
          // The sprite's own colour, kept clear of the three it shares with every other sprite.
          var own = 2 + sprite % 9;
          index = ((row + column / 2) & 3) switch {
            0 => 0,
            1 => own,
            2 => 1,
            _ => SeuckSpritesFile.BackgroundColor,
          };
        }
      }

      var at = (y * width + x) * 3;
      rgb[at] = palette[index * 3];
      rgb[at + 1] = palette[index * 3 + 1];
      rgb[at + 2] = palette[index * 3 + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ASheetOfLegalSprites_IsExact() {
    var source = LegalSheet();

    var bytes = SeuckSpritesWriter.ToBytes(_Encode<SeuckSpritesFile>(source));
    var decoded = SeuckSpritesFile.ToRawImage(SeuckSpritesReader.FromBytes(bytes));

    Assert.That(decoded.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var rgb = new byte[101 * 37 * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i * 37);

    var file = _Encode<SeuckSpritesFile>(
      new() { Width = 101, Height = 37, Format = PixelFormat.Rgb24, PixelData = rgb });

    Assert.That(SeuckSpritesWriter.ToBytes(file), Has.Length.EqualTo(SeuckSpritesFile.FileSize));
  }

  /// <summary>
  /// The one colour a sprite chooses for itself lives in the last byte of its record, and the
  /// encoder must find the colour the sheet actually shows rather than settle for the three every
  /// sprite already has.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_FindsEachSpriteOwnColour() {
    var data = _Encode<SeuckSpritesFile>(LegalSheet()).Data;

    Assert.Multiple(() => {
      for (var sprite = 0; sprite < SeuckSpritesFile.SpriteCount; ++sprite) {
        var offset = SeuckSpritesFile.SpritesOffset + sprite * SeuckSpritesFile.SpriteLength;
        Assert.That(data[offset + SeuckSpritesFile.SpriteLength - 1], Is.EqualTo(2 + sprite % 9));
      }
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
