using System;
using FileFormat.Core;

namespace Crush.Viewer.Tests;

/// <summary>The guard that stops a failed capture from being written out as a screenshot.</summary>
[TestFixture]
public sealed class WindowCaptureTests {

  private static RawImage _Bgra(int width, int height, Func<int, int, (byte B, byte G, byte R)> colour) {
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var (b, g, r) = colour(x, y);
        var i = (y * width + x) * 4;
        pixels[i] = b;
        pixels[i + 1] = g;
        pixels[i + 2] = r;
        pixels[i + 3] = 255;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }

  [Test]
  public void IsBlank_OneFlatColour_IsBlank() {
    Assert.That(WindowCapture.IsBlank(_Bgra(8, 8, (_, _) => (17, 17, 17))), Is.True);
  }

  [Test]
  public void IsBlank_AllBlack_IsBlank() {
    // The failure this exists for: an X capture of a screen the window never reached is all black,
    // and writing it would leave a screenshot job that looks green and produced a black rectangle.
    Assert.That(WindowCapture.IsBlank(_Bgra(16, 16, (_, _) => (0, 0, 0))), Is.True);
  }

  [Test]
  public void IsBlank_ASinglePixelOfADifferentColour_IsNotBlank() {
    var image = _Bgra(8, 8, (_, _) => (17, 17, 17));
    image.PixelData[(5 * 8 + 3) * 4 + 1] = 18;

    Assert.That(WindowCapture.IsBlank(image), Is.False);
  }

  [Test]
  public void IsBlank_ADifferenceInAlphaAlone_IsStillBlank() {
    // Alpha is not part of the comparison on purpose: a screen blit carries none and the Windows
    // path fills it in wholesale, so a difference there says nothing about what was drawn.
    var image = _Bgra(8, 8, (_, _) => (17, 17, 17));
    image.PixelData[(2 * 8 + 2) * 4 + 3] = 0;

    Assert.That(WindowCapture.IsBlank(image), Is.True);
  }

  [Test]
  public void IsBlank_AGradient_IsNotBlank() {
    Assert.That(WindowCapture.IsBlank(_Bgra(8, 8, (x, y) => ((byte)(x * 8), (byte)(y * 8), 128))), Is.False);
  }

  [Test]
  public void IsBlank_APictureTooSmallToJudge_IsBlank() {
    var image = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Bgra32, PixelData = [1, 2, 3, 4] };

    Assert.That(WindowCapture.IsBlank(image), Is.True);
  }

  [Test]
  [Platform("Win")]
  public void IsSupported_OnWindows_IsTrue() {
    Assert.That(WindowCapture.IsSupported, Is.True);
  }

  [Test]
  [Platform("Linux")]
  public void IsSupported_OnAWaylandSession_IsRefusedWithAReasonAndAWayOut() {
    // A Wayland client cannot read back the compositor's screen, and GTK prefers Wayland even when
    // DISPLAY names a working X server — so an X capture there quietly produces a black picture.
    // Refusing has to survive: this is the one failure mode that otherwise looks like a success.
    var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
    var backend = Environment.GetEnvironmentVariable("GDK_BACKEND");
    try {
      Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-0");
      Environment.SetEnvironmentVariable("GDK_BACKEND", null);
      Assert.That(WindowCapture.IsSupported, Is.False);
      Assert.That(WindowCapture.UnsupportedReason, Does.Contain("Wayland"));
      Assert.That(WindowCapture.UnsupportedReason, Does.Contain("GDK_BACKEND=x11"), "the refusal does not say how to get a capture");

      // Forcing GTK onto X11 puts the window somewhere a capture can read it again.
      Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");
      Assert.That(WindowCapture.IsSupported, Is.True);
    } finally {
      Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", wayland);
      Environment.SetEnvironmentVariable("GDK_BACKEND", backend);
    }
  }
}
