using System;
using FileFormat.Core;

namespace FileFormat.MultiLaceEditor.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Every 4x8 cell is one flat colour of the machine's own sixteen, which any multicolour
  /// cell can hold — so nothing is approximated and the blend of the two frames is exact.</summary>
  private static RawImage _SolidCells() {
    const int width = MultiLaceEditorFile.FixedWidth, height = MultiLaceEditorFile.FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = (y / 8) * (width / 4) + x / 4;
      var color = Commodore64Graphics.HexColors[cell % 16];
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(color >> 16);
      rgb[at + 1] = (byte)(color >> 8);
      rgb[at + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SolidCells_ReproducesExactly() {
    var source = _SolidCells();
    var file = MultiLaceEditorFile.FromRawImage(source);
    var restored = MultiLaceEditorReader.FromBytes(MultiLaceEditorWriter.ToBytes(file));
    var decoded = MultiLaceEditorFile.ToRawImage(restored);

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesTheSameScreenIntoBothFrames() {
    // What reaches the eye is the average of the two frames; equal frames average to themselves,
    // which is what makes a still picture come back unchanged.
    var file = MultiLaceEditorFile.FromRawImage(_SolidCells());

    const int frame = MultiLaceEditorFile.BitmapSize + MultiLaceEditorFile.ScreenRamSize;
    Assert.That(file.RawData[frame..(frame * 2)], Is.EqualTo(file.RawData[..frame]));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ScalesAPictureOfAnyOtherSize() {
    static RawImage Raw(int width, int height)
      => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = new byte[width * height * 3] };

    var small = MultiLaceEditorFile.ToRawImage(MultiLaceEditorFile.FromRawImage(Raw(64, 64)));
    var large = MultiLaceEditorFile.ToRawImage(MultiLaceEditorFile.FromRawImage(Raw(800, 600)));

    Assert.That((small.Width, small.Height), Is.EqualTo((large.Width, large.Height)));
  }
}
