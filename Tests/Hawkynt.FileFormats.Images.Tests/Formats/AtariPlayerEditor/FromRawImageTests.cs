using System;
using FileFormat.Core;

namespace FileFormat.AtariPlayerEditor.Tests;

[TestFixture]
public sealed class AtariPlayerEditorFileFromRawImageTests {

  /// <summary>
  /// A sheet of frames whose pixels are black, one colour, the other, or the two ORed — which is the
  /// whole of what two overlapping players can show — with the frame's last two pixels the border.
  /// </summary>
  private static RawImage _Source(int frames, int height) {
    const byte first = 0x28, second = 0x84;
    var width = frames * AtariPlayerEditorFile.NarrowFrameWidth;
    var rgb = new byte[width * height * 3];
    var palette = Atari8BitGraphics.Palette;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var column = x % AtariPlayerEditorFile.NarrowFrameWidth / 2;
      var bits = column >= AtariPlayerEditorFile.PlayerPixels ? 0 : (column + y) & 3;
      var shown = (((bits & 1) != 0 ? first : 0) | ((bits & 2) != 0 ? second : 0)) & 254;

      var at = (y * width + x) * 3;
      rgb[at] = palette[shown * 3];
      rgb[at + 1] = palette[shown * 3 + 1];
      rgb[at + 2] = palette[shown * 3 + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source(3, 21);
    var decoded = AtariPlayerEditorFile.ToRawImage(AtariPlayerEditorFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(3 * AtariPlayerEditorFile.NarrowFrameWidth));
      Assert.That(decoded.Height, Is.EqualTo(21));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureTallerThanAPlayerIsScaledRatherThanRefused() {
    // Forty-eight rows is the whole of a player, and sixteen frames the whole of the sheet.
    var decoded = AtariPlayerEditorFile.ToRawImage(AtariPlayerEditorFile.FromRawImage(_Source(4, 120)));

    Assert.Multiple(() => {
      Assert.That(decoded.Height, Is.EqualTo(AtariPlayerEditorFile.MaxHeight));
      Assert.That(decoded.Width, Is.EqualTo(4 * AtariPlayerEditorFile.NarrowFrameWidth));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => AtariPlayerEditorFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void TheSheetIsAlwaysTheSameLengthAndTheTwoPlayersSitOnTopOfEachOther() {
    // The editor wrote its whole workspace out, so a sheet of one frame is as long as one of sixteen;
    // and the gap is zero because overlapping is what buys the third colour.
    var file = AtariPlayerEditorFile.FromRawImage(_Source(3, 21));
    var bytes = AtariPlayerEditorWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(AtariPlayerEditorFile.FileSize));
      Assert.That(bytes[..4], Is.EqualTo(AtariPlayerEditorFile.Signature.ToArray()));
      Assert.That(bytes[4], Is.EqualTo(3));
      Assert.That(bytes[5], Is.EqualTo(21));
      Assert.That(bytes[6], Is.EqualTo(0));
      Assert.That(file.FrameWidth, Is.EqualTo(AtariPlayerEditorFile.NarrowFrameWidth));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = AtariPlayerEditorFile.FromRawImage(_Source(3, 21));
    var restored = AtariPlayerEditorReader.FromBytes(AtariPlayerEditorWriter.ToBytes(file));

    Assert.That(
      _Rgb(AtariPlayerEditorFile.ToRawImage(restored)),
      Is.EqualTo(_Rgb(AtariPlayerEditorFile.ToRawImage(file))));
  }
}
