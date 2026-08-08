using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Cgm.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      data[i * 3] = (byte)(i * 7);
      data[i * 3 + 1] = (byte)(i * 13);
      data[i * 3 + 2] = (byte)(i * 29);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_ReproducesEveryPixel() {
    var source = _Gradient(37, 11);
    var decoded = CgmFile.ToRawImage(CgmReader.FromBytes(CgmWriter.ToBytes(CgmFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = CgmFile.ToRawImage(CgmReader.FromBytes(CgmWriter.ToBytes(CgmFile.FromRawImage(_Gradient(200, 3)))));
    var tall = CgmFile.ToRawImage(CgmReader.FromBytes(CgmWriter.ToBytes(CgmFile.FromRawImage(_Gradient(3, 200)))));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  /// <summary>
  /// A coordinate is a signed sixteen-bit integer at the default precision, so a picture wider than
  /// that has no extent the file could state. That is the one refusal the format itself requires.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_ASizeTheExtentCannotState_IsRefusedByName() {
    var huge = new RawImage { Width = 40000, Height = 2, Format = PixelFormat.Rgb24, PixelData = new byte[40000 * 2 * 3] };

    var failure = Assert.Throws<ArgumentOutOfRangeException>(() => CgmFile.FromRawImage(huge));
    Assert.That(failure!.Message, Does.Contain("sixteen-bit"));
  }

  /// <summary>
  /// In the binary encoding each row of cells starts on a word boundary, so a row of an odd number
  /// of bytes carries a pad the parameter list does not otherwise account for. Reading straight on
  /// through slides every row after the first by a byte, which a picture whose width is a multiple
  /// of two pixels would never show.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_AnOddRowOfBytes_KeepsItsRowsAligned() {
    // Five pixels is fifteen bytes a row, so every row but the last is followed by a pad.
    var source = _Gradient(5, 3);
    var decoded = CgmFile.ToRawImage(CgmReader.FromBytes(CgmWriter.ToBytes(CgmFile.FromRawImage(source))));

    Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
  }

  /// <summary>
  /// The picture goes in as a cell array — the standard's own raster element — and as nothing else.
  /// A metafile of paths traced from a bitmap would carry geometry the picture never had.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_HoldsOneCellArrayAndNoOtherDrawing() {
    var file = CgmFile.FromRawImage(_Gradient(37, 11));
    var drawn = file.Commands.Where(command => command.ElementClass == 4).ToList();

    Assert.Multiple(() => {
      Assert.That(drawn, Has.Count.EqualTo(1));
      Assert.That(drawn[0].ElementId, Is.EqualTo(9), "the cell array");
      Assert.That(file.Commands[0].ElementClass, Is.Zero);
      Assert.That(file.Commands[0].ElementId, Is.EqualTo(1), "BEGIN METAFILE");
      Assert.That(file.Commands[^1].ElementId, Is.EqualTo(2), "and END METAFILE");
    });
  }

  /// <summary>
  /// The corners the standard places the grid by: a row runs from P towards R and the rows advance
  /// from R towards Q, so the first cell stored is the one at P. The picture's first row is its top
  /// and a metafile's y axis points up, which puts P at the larger y.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_PlacesTheGridByThreeCornersInTheStandardsOrder() {
    var file = CgmFile.FromRawImage(_Gradient(37, 11));
    var cells = file.Commands.First(command => command is { ElementClass: 4, ElementId: 9 }).Parameters;

    short Coordinate(int index) => BinaryPrimitives.ReadInt16BigEndian(cells.AsSpan(index * 2));

    Assert.Multiple(() => {
      Assert.That((Coordinate(0), Coordinate(1)), Is.EqualTo(((short)0, (short)11)), "P is the top left");
      Assert.That((Coordinate(2), Coordinate(3)), Is.EqualTo(((short)37, (short)0)), "Q is diagonally opposite");
      Assert.That((Coordinate(4), Coordinate(5)), Is.EqualTo(((short)37, (short)11)), "R makes a row run left to right");
      Assert.That(Coordinate(6), Is.EqualTo((short)37), "cells across");
      Assert.That(Coordinate(7), Is.EqualTo((short)11), "cells down");
      Assert.That(Coordinate(8), Is.Zero, "and the colours are at the file's own precision");
    });
  }
}
