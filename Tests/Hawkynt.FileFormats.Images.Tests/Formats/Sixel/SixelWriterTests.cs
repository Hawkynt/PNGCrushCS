using System;
using System.Text;
using FileFormat.Sixel;

namespace FileFormat.Sixel.Tests;

[TestFixture]
public sealed class SixelWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithDcs() {
    var file = _BuildSimpleFile();
    var bytes = SixelWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.StartWith("\x1BP"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EndsWithSt() {
    var file = _BuildSimpleFile();
    var bytes = SixelWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.EndWith("\x1B\\"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ColorDefsWritten() {
    var file = _BuildSimpleFile();
    var bytes = SixelWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("#0;2;"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataEncoded() {
    var file = new SixelFile {
      Width = 1,
      Height = 6,
      PixelData = new byte[6],
      Palette = [255, 0, 0],
      PaletteColorCount = 1,
      AspectRatio = 0,
      BackgroundMode = 0
    };

    var bytes = SixelWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text.Contains('q'), Is.True);
  }

  private static SixelFile _BuildSimpleFile() => new() {
    Width = 2,
    Height = 6,
    PixelData = new byte[12],
    Palette = [255, 0, 0, 0, 255, 0],
    PaletteColorCount = 2,
    AspectRatio = 0,
    BackgroundMode = 0
  };
}
