using System;
using System.Linq;
using System.Text;
using FileFormat.Ani;
using FileFormat.Ico;
using FileFormat.Png;

namespace FileFormat.Ani.Tests;

[TestFixture]
public sealed class AniWriterTests {

  private static IcoFile _CreateTestIcoFrame(int width, int height) {
    var pngFile = new PngFile {
      Width = width, Height = height, BitDepth = 8, ColorType = PngColorType.RGBA,
      PixelData = Enumerable.Range(0, height).Select(_ => new byte[width * 4]).ToArray()
    };
    var pngBytes = PngWriter.ToBytes(pngFile);
    return new IcoFile {
      Images = [new IcoImage { Width = width, Height = height, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes }]
    };
  }

  private static AniFile _CreateTestAniFile(int frameCount = 1) {
    var frames = Enumerable.Range(0, frameCount).Select(_ => _CreateTestIcoFrame(16, 16)).ToArray();
    return new AniFile {
      Header = new AniHeader(
        CbSize: 36,
        NumFrames: frameCount,
        NumSteps: frameCount,
        Width: 16,
        Height: 16,
        BitCount: 32,
        NumPlanes: 1,
        DisplayRate: 10,
        Flags: 2
      ),
      Frames = frames
    };
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithRiffSignature() {
    var ani = _CreateTestAniFile();
    var bytes = AniWriter.ToBytes(ani);

    var signature = Encoding.ASCII.GetString(bytes, 0, 4);
    Assert.That(signature, Is.EqualTo("RIFF"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasAconFormType() {
    var ani = _CreateTestAniFile();
    var bytes = AniWriter.ToBytes(ani);

    var formType = Encoding.ASCII.GetString(bytes, 8, 4);
    Assert.That(formType, Is.EqualTo("ACON"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsAnihChunk() {
    var ani = _CreateTestAniFile();
    var bytes = AniWriter.ToBytes(ani);

    var text = Encoding.ASCII.GetString(bytes);
    Assert.That(text, Does.Contain("anih"));
  }
}
