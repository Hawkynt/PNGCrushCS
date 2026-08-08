using System;
using FileFormat.Core;

namespace FileFormat.Graph2FontMch.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Four of the machine's colours, which is what one cell can draw.</summary>
  private static readonly byte[] _Registers = [0x00, 0x28, 0x86, 0x0E];

  /// <summary>
  /// A picture whose colours change every scanline and every two columns, which is exactly what the
  /// format holds — a character pixel is two screen pixels wide and the registers are rewritten per
  /// scanline.
  /// </summary>
  private static RawImage _Doubled(int width, int height) {
    var gtia = Atari8BitGraphics.Palette;
    var data = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var entry = _Registers[((x >> 1) + y * 3) & 3] * 3;
      var at = (y * width + x) * 3;
      data[at] = gtia[entry];
      data[at + 1] = gtia[entry + 1];
      data[at + 2] = gtia[entry + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FourColoursACell_ReproducesExactly() {
    var source = _Doubled(Graph2FontMchFile.Width, Graph2FontMchFile.Height);
    var file = Graph2FontMchFile.FromRawImage(source);
    var decoded = Graph2FontMchFile.ToRawImage(
      Graph2FontMchReader.FromBytes(Graph2FontMchWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(
        (decoded.Width, decoded.Height),
        Is.EqualTo((Graph2FontMchFile.Width, Graph2FontMchFile.Height)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnyOtherSize() {
    // The screen is one size and no other, so a picture of another is brought to it.
    var file = Graph2FontMchFile.FromRawImage(_Doubled(37, 11));

    Assert.Multiple(() => {
      Assert.That(file.Columns, Is.EqualTo(Graph2FontMchFile.WrittenColumns));
      Assert.That(
        file.Data,
        Has.Length.EqualTo(
          Graph2FontMchFile.WrittenColumns * Graph2FontMchFile.BytesPerCell * Graph2FontMchFile.CellRows
          + Graph2FontMchFile.RegisterCount * Graph2FontMchFile.Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_LeavesTheSplitInverseFlagClearInEveryCell() {
    // One cell anywhere with that bit set switches the whole screen to reading its inverse from a
    // different bit half way down every cell, which would move the colours of cells that never
    // asked for it.
    var file = Graph2FontMchFile.FromRawImage(
      _Doubled(Graph2FontMchFile.Width, Graph2FontMchFile.Height));

    var cells = Graph2FontMchFile.WrittenColumns * Graph2FontMchFile.CellRows;

    Assert.Multiple(() => {
      for (var cell = 0; cell < cells; ++cell)
        Assert.That(
          file.Data[cell * Graph2FontMchFile.BytesPerCell] & 64, Is.Zero, $"cell {cell}");
    });
  }
}
