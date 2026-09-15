using System;
using FileFormat.Core;

namespace FileFormat.MultiLaceEditor.Tests;

[TestFixture]
public sealed class MultiLaceEditorFromRawImageTests {

  /// <summary>
  /// Bands of the editor's own colours down the screen, black over the rows the fields cannot reach.
  /// </summary>
  /// <remarks>
  /// Bands rather than a pattern, because the two fields are half a colour-clock out of step with
  /// each other: field one pairs the pixels at 0 and 1, field two the ones at 1 and 2, so a picture
  /// that changes colour anywhere across a row cannot be held by both at once. That is the format
  /// and not the encoder — the displacement is there to resolve edges the pair can place between the
  /// two fields rather than in either.
  /// <para/>
  /// The bottom eight rows are black because a field is 256 character cells and seven rows of forty
  /// is 280, so the last 24 have nothing behind them.
  /// </remarks>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var index = y >= 48 ? 0 : MultiLaceEditorFile.ColorIndices[y / 8 % MultiLaceEditorFile.ColorCount];
      var colour = Commodore64Graphics.HexColors[index];

      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        rgb[at] = (byte)(colour >> 16);
        rgb[at + 1] = (byte)(colour >> 8);
        rgb[at + 2] = (byte)colour;
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source(320, 56);
    var decoded = MultiLaceEditorFile.ToRawImage(MultiLaceEditorFile.FromRawImage(source));
    var mine = _Rgb(decoded);
    var theirs = _Rgb(source);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(56));

      // From the second column on: the displaced field has nothing to show in the first, so half of
      // what the eye averages there is black whatever the picture says.
      for (var y = 0; y < 56; ++y)
      for (var x = 1; x < 320; ++x) {
        var at = (y * 320 + x) * 3;
        Assert.That(mine[at], Is.EqualTo(theirs[at]), $"{x},{y}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = MultiLaceEditorFile.ToRawImage(MultiLaceEditorFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(56));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => MultiLaceEditorFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = MultiLaceEditorFile.FromRawImage(_Source(320, 56));
    var restored = MultiLaceEditorReader.FromBytes(MultiLaceEditorWriter.ToBytes(file));

    Assert.That(
      _Rgb(MultiLaceEditorFile.ToRawImage(restored)), Is.EqualTo(_Rgb(MultiLaceEditorFile.ToRawImage(file))));
  }
}
