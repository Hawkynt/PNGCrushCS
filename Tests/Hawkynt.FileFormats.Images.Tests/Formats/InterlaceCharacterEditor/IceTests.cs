using System;
using System.IO;
using FileFormat.Core;
using FileFormat.InterlaceCharacterEditor;

namespace FileFormat.InterlaceCharacterEditor.Tests;

[TestFixture]
public sealed class IceTests {

  private static readonly IceMode[] _AllModes =
    [IceMode.SuperIrg, IceMode.SuperIrg2, IceMode.Cin, IceMode.Min, IceMode.Pcin];

  private static RawImage _Sample() {
    const int width = IceLayout.DisplayWidth;
    const int height = IceLayout.DisplayHeight;
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      data[o] = (byte)(x * 255 / (width - 1));
      data[o + 1] = (byte)(y * 255 / (height - 1));
      data[o + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  /// <summary>A picture of four flat quadrants, which every mode can reproduce closely.</summary>
  private static RawImage _Quadrants() {
    const int width = IceLayout.DisplayWidth;
    const int height = IceLayout.DisplayHeight;
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      data[o + 2] = (byte)(x < width / 2 ? 220 : 20);
      data[o + 1] = (byte)(y < height / 2 ? 220 : 20);
      data[o] = 20;
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  [TestCaseSource(nameof(_AllModes))]
  [Category("Unit")]
  public void ToBytes_ProducesTheSizeReadersExpect(IceMode mode) {
    var expected = mode switch {
      IceMode.SuperIrg => 18310,
      IceMode.SuperIrg2 => 18314,
      IceMode.Cin or IceMode.Min => 17350,
      _ => 17354,
    };

    Assert.That(IceWriter.ToBytes(IceFile.FromRawImage(_Sample(), mode)), Has.Length.EqualTo(expected));
  }

  [TestCaseSource(nameof(_AllModes))]
  [Category("Unit")]
  public void FromRawImage_GivesEveryCellItsOwnGlyph(IceMode mode) {
    var file = IceFile.FromRawImage(_Sample(), mode);

    // A band is three character rows of forty cells, and the codes run 0..119 across it.
    for (var row = 0; row < IceLayout.Rows; ++row)
    for (var col = 0; col < IceLayout.Columns; ++col) {
      var expected = (row % IceLayout.RowsPerBank) * IceLayout.Columns + col;
      Assert.That(file.Characters1[row * IceLayout.Columns + col], Is.EqualTo(expected));
    }
  }

  [TestCaseSource(nameof(_AllModes))]
  [Category("Unit")]
  public void RoundTrip_PreservesEverySection(IceMode mode) {
    var file = IceFile.FromRawImage(_Sample(), mode);
    var restored = IceReader.FromSpan(IceWriter.ToBytes(file), mode);

    Assert.Multiple(() => {
      Assert.That(restored.Mode, Is.EqualTo(mode));
      Assert.That(restored.Header, Is.EqualTo(file.Header));
      Assert.That(restored.FontData, Is.EqualTo(file.FontData));
      Assert.That(restored.Characters1, Is.EqualTo(file.Characters1));
      Assert.That(restored.Characters2, Is.EqualTo(file.Characters2));
    });
  }

  [TestCaseSource(nameof(_AllModes))]
  [Category("Unit")]
  public void Decoding_WhatWeEncoded_LooksLikeThePicture(IceMode mode) {
    // Encoder and decoder are separate code paths over the same font and character maps; a wrong
    // bit order or cell layout in either shows up here as a picture that no longer resembles the
    // input, even though the file itself would still be structurally valid.
    var source = _Quadrants();
    var decoded = IceFile.ToRawImage(IceFile.FromRawImage(source, mode));

    long error = 0;
    var pixels = IceLayout.DisplayWidth * IceLayout.DisplayHeight;
    for (var i = 0; i < pixels; ++i) {
      var slot = decoded.PixelData[i] * 3;
      int dr = decoded.Palette![slot] - source.PixelData[i * 4];
      int dg = decoded.Palette[slot + 1] - source.PixelData[i * 4 + 1];
      int db = decoded.Palette[slot + 2] - source.PixelData[i * 4 + 2];
      error += Math.Abs(dr) + Math.Abs(dg) + Math.Abs(db);
    }

    Assert.That(error / (pixels * 3.0), Is.LessThan(48), $"{mode} reconstructs the picture too poorly");
  }

  [TestCaseSource(nameof(_AllModes))]
  [Category("Unit")]
  public void ToRawImage_ProducesTheDisplayedSize(IceMode mode) {
    var raw = IceFile.ToRawImage(IceFile.FromRawImage(_Sample(), mode));

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(IceLayout.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(IceLayout.DisplayHeight));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.PaletteCount, Is.GreaterThan(1));
    });
  }

  [TestCase(".irg", IceMode.SuperIrg)]
  [TestCase(".ir2", IceMode.SuperIrg2)]
  [TestCase(".ICN", IceMode.Cin)]
  [TestCase(".imn", IceMode.Min)]
  [TestCase(".ipc", IceMode.Pcin)]
  [Category("Unit")]
  public void ModeFromExtension_NamesThePictureFormat(string extension, IceMode expected)
    => Assert.That(IceReader.ModeFromExtension(extension), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void SharedCharacterMapModes_WriteOnlyOneMap() {
    var file = IceFile.FromRawImage(_Sample(), IceMode.Min);

    Assert.Multiple(() => {
      Assert.That(IceLayout.SharesCharacterMap(IceMode.Min), Is.True);
      Assert.That(file.Characters2, Is.SameAs(file.Characters1));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAWronglySizedFile()
    => Assert.Throws<InvalidDataException>(() => IceReader.FromBytes(new byte[1024]));

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsOtherSizes() {
    var raw = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgba32, PixelData = new byte[320 * 200 * 4] };

    Assert.Throws<ArgumentException>(() => IceFile.FromRawImage(raw));
  }
}
