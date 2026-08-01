using System;
using FileFormat.Core;
using FileFormat.SinclairBasic;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// The one format here whose encoder emits a program rather than pixels.
/// </summary>
/// <remarks>
/// The ZX81 could not store a picture, so a picture was a BASIC listing whose PRINT statements put
/// the block characters where they belonged. PRINT AT reaches rows 0 to 21 and no further — the
/// machine keeps the last two lines for what is being typed — which is why these programs carry a
/// scroll routine to fill the bottom one.
/// </remarks>
[TestFixture]
public sealed class SinclairBasicWriterTests {

  private static RawImage _Pattern() {
    var rgb = new byte[Zx81Graphics.Width * Zx81Graphics.Height * 3];
    for (var y = 0; y < Zx81Graphics.Height; ++y)
    for (var x = 0; x < Zx81Graphics.Width; ++x) {
      // Nothing below the rows a PRINT statement can reach, which is what this can express.
      var lit = y < (SinclairBasicWriter.LastPrintableRow + 1) * 8 && ((x / 4 + y / 4) % 2 == 0);
      var at = (y * Zx81Graphics.Width + x) * 3;
      rgb[at] = rgb[at + 1] = rgb[at + 2] = (byte)(lit ? 255 : 0);
    }

    return new() {
      Width = Zx81Graphics.Width, Height = Zx81Graphics.Height,
      Format = PixelFormat.Rgb24, PixelData = rgb,
    };
  }

  [Test]
  [Category("Unit")]
  public void WrittenProgram_ReadsBackAsTheSameScreen() {
    var written = SinclairBasicFile.FromRawImage(_Pattern());

    var bytes = SinclairBasicWriter.ToBytes(written);
    var back = SinclairBasicReader.FromBytes(bytes);

    Assert.That(back.Screen, Is.EqualTo(written.Screen));
  }

  /// <summary>A picture reaching the input area is refused rather than written without it.</summary>
  [Test]
  [Category("Unit")]
  public void PictureBelowTheLastPrintableRow_IsRefused() {
    var screen = new byte[Zx81Graphics.ScreenSize];
    screen[^1] = 1;

    Assert.Throws<NotSupportedException>(() => SinclairBasicWriter.ToBytes(new() { Screen = screen }));
  }
}
