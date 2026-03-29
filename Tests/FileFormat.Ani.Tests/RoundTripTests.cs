using System;
using System.Linq;
using FileFormat.Ani;
using FileFormat.Ico;
using FileFormat.Png;

namespace FileFormat.Ani.Tests;

[TestFixture]
public sealed class RoundTripTests {

  private static IcoFile _CreateTestIcoFrame(int width, int height) {
    var pngFile = new PngFile {
      Width = width, Height = height, BitDepth = 8, ColorType = PngColorType.RGBA,
      PixelData = Enumerable.Range(0, height).Select(y => {
        var row = new byte[width * 4];
        for (var x = 0; x < width; ++x) {
          row[x * 4] = (byte)((x + y) * 7);
          row[x * 4 + 1] = (byte)((x + y) * 11);
          row[x * 4 + 2] = (byte)((x + y) * 13);
          row[x * 4 + 3] = 255;
        }
        return row;
      }).ToArray()
    };
    var pngBytes = PngWriter.ToBytes(pngFile);
    return new IcoFile {
      Images = [new IcoImage { Width = width, Height = height, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes }]
    };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleFrame() {
    var frame = _CreateTestIcoFrame(16, 16);
    var original = new AniFile {
      Header = new AniHeader(CbSize: 36, NumFrames: 1, NumSteps: 1, Width: 16, Height: 16, BitCount: 32, NumPlanes: 1, DisplayRate: 10, Flags: 2),
      Frames = [frame]
    };

    var bytes = AniWriter.ToBytes(original);
    var restored = AniReader.FromBytes(bytes);

    Assert.That(restored.Header.NumFrames, Is.EqualTo(1));
    Assert.That(restored.Header.NumSteps, Is.EqualTo(1));
    Assert.That(restored.Frames.Count, Is.EqualTo(1));
    Assert.That(restored.Frames[0].Images.Count, Is.EqualTo(1));
    Assert.That(restored.Frames[0].Images[0].Width, Is.EqualTo(16));
    Assert.That(restored.Frames[0].Images[0].Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleFrames() {
    var frames = new[] { _CreateTestIcoFrame(16, 16), _CreateTestIcoFrame(16, 16), _CreateTestIcoFrame(16, 16) };
    var original = new AniFile {
      Header = new AniHeader(CbSize: 36, NumFrames: 3, NumSteps: 3, Width: 16, Height: 16, BitCount: 32, NumPlanes: 1, DisplayRate: 5, Flags: 2),
      Frames = frames
    };

    var bytes = AniWriter.ToBytes(original);
    var restored = AniReader.FromBytes(bytes);

    Assert.That(restored.Header.NumFrames, Is.EqualTo(3));
    Assert.That(restored.Frames.Count, Is.EqualTo(3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithRates() {
    var frame = _CreateTestIcoFrame(16, 16);
    var original = new AniFile {
      Header = new AniHeader(CbSize: 36, NumFrames: 1, NumSteps: 1, Width: 16, Height: 16, BitCount: 32, NumPlanes: 1, DisplayRate: 10, Flags: 2),
      Frames = [frame],
      Rates = [15]
    };

    var bytes = AniWriter.ToBytes(original);
    var restored = AniReader.FromBytes(bytes);

    Assert.That(restored.Rates, Is.Not.Null);
    Assert.That(restored.Rates, Has.Length.EqualTo(1));
    Assert.That(restored.Rates![0], Is.EqualTo(15));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithSequence() {
    var frames = new[] { _CreateTestIcoFrame(16, 16), _CreateTestIcoFrame(16, 16) };
    var original = new AniFile {
      Header = new AniHeader(CbSize: 36, NumFrames: 2, NumSteps: 3, Width: 16, Height: 16, BitCount: 32, NumPlanes: 1, DisplayRate: 10, Flags: 3),
      Frames = frames,
      Sequence = [0, 1, 0]
    };

    var bytes = AniWriter.ToBytes(original);
    var restored = AniReader.FromBytes(bytes);

    Assert.That(restored.Sequence, Is.Not.Null);
    Assert.That(restored.Sequence, Has.Length.EqualTo(3));
    Assert.That(restored.Sequence![0], Is.EqualTo(0));
    Assert.That(restored.Sequence[1], Is.EqualTo(1));
    Assert.That(restored.Sequence[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_HeaderFields() {
    var frame = _CreateTestIcoFrame(32, 32);
    var original = new AniFile {
      Header = new AniHeader(CbSize: 36, NumFrames: 1, NumSteps: 1, Width: 32, Height: 32, BitCount: 24, NumPlanes: 1, DisplayRate: 7, Flags: 2),
      Frames = [frame]
    };

    var bytes = AniWriter.ToBytes(original);
    var restored = AniReader.FromBytes(bytes);

    Assert.That(restored.Header.NumFrames, Is.EqualTo(1));
    Assert.That(restored.Header.NumSteps, Is.EqualTo(1));
    Assert.That(restored.Header.Width, Is.EqualTo(32));
    Assert.That(restored.Header.Height, Is.EqualTo(32));
    Assert.That(restored.Header.BitCount, Is.EqualTo(24));
    Assert.That(restored.Header.DisplayRate, Is.EqualTo(7));
    Assert.That(restored.Header.HasSequence, Is.False);
  }
}
