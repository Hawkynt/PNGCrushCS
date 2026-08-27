using System;
using System.Linq;
using FileFormat.Core;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class WebPAnimationWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesFramesThatTheReaderCanRecoverAndComposite() {
    const int width = 4;
    const int height = 2;
    var first = _Frame(width, height, 0xFF0000FF, 75);
    var second = _Frame(width, height, 0xFF00FF00, 125);

    var file = new WebPFile {
      Features = new WebPFeatures(width, height, HasAlpha: false, IsLossless: true, IsAnimated: true),
      ImageData = [],
      IsLossless = true,
      Frames = [first, second],
      Animation = new WebPAnimationInfo { BackgroundColorBgra = 0x11223344, LoopCount = 7 }
    };

    var encoded = WebPWriter.ToBytes(file);
    var decoded = WebPReader.FromBytes(encoded);

    Assert.Multiple(() => {
      Assert.That(decoded.Frames, Has.Count.EqualTo(2));
      Assert.That(decoded.Animation, Is.Not.Null);
      Assert.That(decoded.Animation!.BackgroundColorBgra, Is.EqualTo(0x11223344u));
      Assert.That(decoded.Animation.LoopCount, Is.EqualTo(7));
      Assert.That(decoded.Frames[0].DurationMilliseconds, Is.EqualTo(75));
      Assert.That(decoded.Frames[1].DurationMilliseconds, Is.EqualTo(125));
      Assert.That(WebPFile.ToRawImage(decoded, 0).ToRgba32().Take(4).ToArray(), Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
      Assert.That(WebPFile.ToRawImage(decoded, 1).ToRgba32().Take(4).ToArray(), Is.EqualTo(new byte[] { 0, 255, 0, 255 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesSubRectangleCoordinatesBlendAndDisposal() {
    var frame = _Frame(2, 2, 0x80FF0000, 33) with {
      X = 2,
      Y = 4,
      BlendMethod = WebPFrameBlendMethod.AlphaBlend,
      DisposalMethod = WebPFrameDisposalMethod.Background
    };

    var file = new WebPFile {
      Features = new WebPFeatures(8, 8, HasAlpha: true, IsLossless: true, IsAnimated: true),
      ImageData = [],
      IsLossless = true,
      Frames = [frame],
      Animation = new WebPAnimationInfo { BackgroundColorBgra = 0, LoopCount = 0 }
    };

    var decoded = WebPReader.FromBytes(WebPWriter.ToBytes(file));
    var actual = decoded.Frames.Single();

    Assert.Multiple(() => {
      Assert.That(actual.X, Is.EqualTo(2));
      Assert.That(actual.Y, Is.EqualTo(4));
      Assert.That(actual.Width, Is.EqualTo(2));
      Assert.That(actual.Height, Is.EqualTo(2));
      Assert.That(actual.DurationMilliseconds, Is.EqualTo(33));
      Assert.That(actual.BlendMethod, Is.EqualTo(WebPFrameBlendMethod.AlphaBlend));
      Assert.That(actual.DisposalMethod, Is.EqualTo(WebPFrameDisposalMethod.Background));
      Assert.That(actual.HasAlpha, Is.True);
    });
  }

  private static WebPFrame _Frame(int width, int height, uint rgba, int duration) {
    var a = (rgba >> 24) & 0xFF;
    var r = (rgba >> 16) & 0xFF;
    var g = (rgba >> 8) & 0xFF;
    var b = rgba & 0xFF;
    var argb = new uint[width * height];
    Array.Fill(argb, (a << 24) | (r << 16) | (g << 8) | b);

    return new WebPFrame {
      X = 0,
      Y = 0,
      Width = width,
      Height = height,
      DurationMilliseconds = duration,
      DisposalMethod = WebPFrameDisposalMethod.None,
      BlendMethod = WebPFrameBlendMethod.None,
      ImageData = Vp8LEncoder.Encode(argb, width, height, hasAlpha: a != 0xFF),
      IsLossless = true,
      HasAlpha = a != 0xFF
    };
  }
}
