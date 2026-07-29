using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MadStudio;

namespace FileFormat.MadStudio.Tests;

[TestFixture]
public sealed class MadStudioTests {

  private static readonly MadStudioMode[] _AllModes = Enum.GetValues<MadStudioMode>();

  /// <summary>
  /// A 32x32 checkerboard of two greys — the only kind of picture every one of these modes can
  /// reproduce closely, and enough to catch a wrong cell geometry.
  /// </summary>
  /// <remarks>
  /// Character modes cannot fill a cell with an arbitrary register: doing so needs a glyph whose
  /// every pixel carries the same value, and the character ROM only supplies two of those — the
  /// space and its inverse. So a mid-grey has no solid representation, and a target using more
  /// than two levels would score badly for reasons that are the format's rather than the
  /// encoder's. Grey is used rather than colour because the hardware palette is built from a
  /// hue/luminance model whose colours are far less saturated than a vivid RGB value. The block
  /// size is a multiple of every mode's cell, so the same target is exactly expressible in all of
  /// them.
  /// </remarks>
  private static RawImage _Blocks() {
    const int width = MadStudioLayout.DisplayWidth;
    const int height = MadStudioLayout.DisplayHeight;
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      var level = (byte)((x / 32 + y / 32) % 2 == 0 ? 240 : 15);
      data[o] = data[o + 1] = data[o + 2] = level;
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  [TestCase(MadStudioMode.Antic2, 962)]
  [TestCase(MadStudioMode.Antic4, 967)]
  [TestCase(MadStudioMode.Antic5, 487)]
  [TestCase(MadStudioMode.Graphics1, 485)]
  [TestCase(MadStudioMode.Graphics2, 245)]
  [Category("Unit")]
  public void FileSizeFor_MatchesWhatReadersExpect(MadStudioMode mode, int expected)
    => Assert.That(MadStudioLayout.FileSizeFor(mode), Is.EqualTo(expected));

  [TestCaseSource(nameof(_AllModes))]
  [Category("Unit")]
  public void EveryMode_FillsTheSameScreen(MadStudioMode mode) {
    Assert.Multiple(() => {
      Assert.That(MadStudioLayout.ColumnsFor(mode) * MadStudioLayout.CellWidthFor(mode), Is.EqualTo(MadStudioLayout.DisplayWidth));
      Assert.That(MadStudioLayout.RowsFor(mode) * MadStudioLayout.CellHeightFor(mode), Is.EqualTo(MadStudioLayout.DisplayHeight));
    });
  }

  [TestCaseSource(nameof(_AllModes))]
  [Category("Unit")]
  public void RoundTrip_PreservesTheGridAndColors(MadStudioMode mode) {
    var file = MadStudioFile.FromRawImage(_Blocks(), mode);
    var restored = MadStudioReader.FromSpan(MadStudioWriter.ToBytes(file), mode);

    Assert.Multiple(() => {
      Assert.That(restored.Mode, Is.EqualTo(mode));
      Assert.That(restored.Characters, Is.EqualTo(file.Characters));
      Assert.That(restored.Colors, Is.EqualTo(file.Colors));
    });
  }

  [TestCaseSource(nameof(_AllModes))]
  [Category("Unit")]
  public void FromSpan_RecognisesTheModeFromTheSizeAlone(MadStudioMode mode) {
    var bytes = MadStudioWriter.ToBytes(MadStudioFile.FromRawImage(_Blocks(), mode));

    Assert.That(MadStudioReader.FromBytes(bytes).Mode, Is.EqualTo(mode));
  }

  [TestCaseSource(nameof(_AllModes))]
  [Category("Unit")]
  public void ChooseCharacters_RecoversAScreenItWasGiven(MadStudioMode mode) {
    // Encoder and renderer are separate paths over the same glyph and register rules, and a wrong
    // bit order or cell geometry in either would leave the file structurally valid while drawing
    // the wrong thing. Rendering a known screen and asking the encoder to reproduce it pins the
    // two together: whatever codes it picks, the picture it draws has to come back identical.
    //
    // A plain error bound would not work here. Filling a cell with a colour needs a glyph whose
    // every pixel carries the same value, and the character ROM supplies only the space — so most
    // colours have no solid form and any ordinary target scores badly for reasons that belong to
    // the format rather than the encoder.
    var colors = new byte[] { 0x00, 0x0E, 0x28, 0x86, 0xB8 };
    var original = new MadStudioFile {
      Mode = mode,
      Characters = _Grid(mode),
      Colors = mode == MadStudioMode.Antic2 ? [] : colors,
    };

    var rendered = MadStudioFile.ToRawImage(original);
    var bgra = PixelConverter.Convert(rendered, PixelFormat.Bgra32);
    var recovered = original with {
      Characters = MadStudioEncoder.ChooseCharacters(
        mode, bgra.PixelData, Atari8BitGraphics.CreatePalette(), _ColorsOf(original), MadStudioFile.Font),
    };

    Assert.That(_Rgb(MadStudioFile.ToRawImage(recovered)), Is.EqualTo(_Rgb(rendered)));
  }

  /// <summary>A grid using a spread of character codes, including the inverse-video half.</summary>
  private static byte[] _Grid(MadStudioMode mode) {
    var grid = new byte[MadStudioLayout.CharacterMapSizeFor(mode)];
    for (var i = 0; i < grid.Length; ++i)
      grid[i] = (byte)(i * 37 % 256);

    return grid;
  }

  private static byte[] _ColorsOf(MadStudioFile file)
    => file.Mode == MadStudioMode.Antic2 ? [0, 14] : file.Colors;

  /// <summary>Flattens an indexed image to RGB so two renderings compare by colour, not by index.</summary>
  private static byte[] _Rgb(RawImage image) {
    var result = new byte[image.PixelData.Length * 3];
    for (var i = 0; i < image.PixelData.Length; ++i)
      Array.Copy(image.Palette!, image.PixelData[i] * 3, result, i * 3, 3);

    return result;
  }

  [Test]
  [Category("Unit")]
  public void Antic2_StoresNoColors() {
    var file = MadStudioFile.FromRawImage(_Blocks(), MadStudioMode.Antic2);

    Assert.Multiple(() => {
      Assert.That(file.Colors, Is.Empty);
      Assert.That(MadStudioFile.ToRawImage(file).PaletteCount, Is.EqualTo(2));
    });
  }

  [TestCase(MadStudioMode.Graphics1)]
  [TestCase(MadStudioMode.Graphics2)]
  [Category("Unit")]
  public void GraphicsModes_PutTheirColorsAfterTheGrid(MadStudioMode mode) {
    var file = MadStudioFile.FromRawImage(_Blocks(), mode);
    var bytes = MadStudioWriter.ToBytes(file);
    var offset = MadStudioLayout.CharacterMapSizeFor(mode);

    Assert.Multiple(() => {
      Assert.That(MadStudioLayout.ColorsFollowCharacters(mode), Is.True);
      Assert.That(bytes[offset..(offset + MadStudioLayout.ColorCount)], Is.EqualTo(file.Colors));
    });
  }

  [TestCase(".an2", MadStudioMode.Antic2)]
  [TestCase(".an4", MadStudioMode.Antic4)]
  [TestCase(".AN5", MadStudioMode.Antic5)]
  [TestCase(".gr1", MadStudioMode.Graphics1)]
  [TestCase(".gr2", MadStudioMode.Graphics2)]
  [Category("Unit")]
  public void ModeFromExtension_NamesTheCharacterMode(string extension, MadStudioMode expected)
    => Assert.That(MadStudioLayout.ModeFromExtension(extension), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnUnknownSize()
    => Assert.Throws<InvalidDataException>(() => MadStudioReader.FromBytes(new byte[1000]));

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsOtherSizes() {
    var raw = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgba32, PixelData = new byte[320 * 200 * 4] };

    Assert.Throws<ArgumentException>(() => MadStudioFile.FromRawImage(raw));
  }
}
