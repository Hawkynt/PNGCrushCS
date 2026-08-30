using System.IO;
using System.Text;
using FileFormat.Sixel;

namespace FileFormat.Sixel.Tests;

[TestFixture]
public sealed class SixelConformanceTests {

  [Test]
  [Category("Unit")]
  public void Decode_RasterAttributes_PreserveBlankExtent() {
    var body = "\"1;1;4;7#2;2;100;0;0@";

    var pixels = SixelCodec.Decode(body, out var width, out var height, out _, out _);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(4));
      Assert.That(height, Is.EqualTo(7));
      Assert.That(pixels, Has.Length.EqualTo(28));
      Assert.That(pixels[0], Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_P2One_OverprintsWithoutErasingEarlierColorPlane() {
    const string body = "#1;2;100;0;0~?$#2;2;0;100;0?~";

    var pixels = SixelCodec.Decode(body, out var width, out var height, out _, out _, backgroundMode: 1);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(2));
      Assert.That(height, Is.EqualTo(6));
      for (var y = 0; y < 6; ++y) {
        Assert.That(pixels[y * 2], Is.EqualTo(1), $"red plane lost at row {y}");
        Assert.That(pixels[y * 2 + 1], Is.EqualTo(2), $"green plane missing at row {y}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_P2Zero_ZeroBitsRestoreBackground() {
    const string body = "#1;2;100;0;0~?$#2;2;0;100;0?~";

    var pixels = SixelCodec.Decode(body, out _, out _, out _, out _, backgroundMode: 0);

    for (var y = 0; y < 6; ++y) {
      Assert.That(pixels[y * 2], Is.Zero, $"zero bit did not restore background at row {y}");
      Assert.That(pixels[y * 2 + 1], Is.EqualTo(2), $"green plane missing at row {y}");
    }
  }

  [Test]
  [Category("Unit")]
  public void Decode_DecHlsHue120_IsRed() {
    const string body = "#2;1;120;50;100~";

    SixelCodec.Decode(body, out _, out _, out var palette, out _);

    Assert.That(palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(palette![6], Is.EqualTo(255));
      Assert.That(palette[7], Is.Zero);
      Assert.That(palette[8], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_AcceptsEightBitDcsAndSt() {
    var body = Encoding.ASCII.GetBytes("0;1;0q\"1;1;1;6#2;2;100;0;0~");
    var data = new byte[body.Length + 2];
    data[0] = 0x90;
    body.CopyTo(data, 1);
    data[^1] = 0x9C;

    var file = SixelReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(1));
      Assert.That(file.Height, Is.EqualTo(6));
      Assert.That(file.BackgroundMode, Is.EqualTo(1));
      Assert.That(file.PixelData, Is.All.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_MissingStringTerminator_IsRejected() {
    var data = Encoding.ASCII.GetBytes("\x1BP0;1;0q#0~");

    Assert.Throws<InvalidDataException>(() => SixelReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void Writer_UsesTransparentOverprintModeAndExactRasterAttributes() {
    var file = new SixelFile {
      Width = 2,
      Height = 6,
      PixelData = [0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1],
      Palette = [255, 0, 0, 0, 255, 0],
      PaletteColorCount = 2,
      AspectRatio = 0,
      BackgroundMode = 0,
    };

    var text = Encoding.ASCII.GetString(SixelWriter.ToBytes(file));

    Assert.That(text, Does.StartWith("\x1BP0;1;0q\"1;1;2;6"));
  }
}
