using System;
using System.IO;
using FileFormat.Fl32;
using FileFormat.Core;

namespace FileFormat.Fl32.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gray_PreservesPixelData() {
    var pixels = new float[4 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = i * 0.1f;

    var original = new Fl32File { Width = 4, Height = 3, Channels = 1, PixelData = pixels };
    var bytes = Fl32Writer.ToBytes(original);
    var restored = Fl32Reader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.Height, Is.EqualTo(3));
      Assert.That(restored.Channels, Is.EqualTo(1));
      Assert.That(restored.PixelData, Has.Length.EqualTo(12));
    });
    for (var i = 0; i < pixels.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixels[i]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_PreservesPixelData() {
    var pixels = new float[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (i + 1) * 0.05f;

    var original = new Fl32File { Width = 2, Height = 2, Channels = 3, PixelData = pixels };
    var bytes = Fl32Writer.ToBytes(original);
    var restored = Fl32Reader.FromBytes(bytes);

    Assert.That(restored.Channels, Is.EqualTo(3));
    for (var i = 0; i < pixels.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixels[i]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba_PreservesPixelData() {
    var pixels = new float[2 * 2 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = i * 0.02f;

    var original = new Fl32File { Width = 2, Height = 2, Channels = 4, PixelData = pixels };
    var bytes = Fl32Writer.ToBytes(original);
    var restored = Fl32Reader.FromBytes(bytes);

    Assert.That(restored.Channels, Is.EqualTo(4));
    for (var i = 0; i < pixels.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixels[i]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new Fl32File {
      Width = 3, Height = 2, Channels = 1,
      PixelData = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f]
    };

    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fl32");
    try {
      File.WriteAllBytes(tmp, Fl32Writer.ToBytes(original));
      var restored = Fl32Reader.FromFile(new FileInfo(tmp));
      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(3));
        Assert.That(restored.Height, Is.EqualTo(2));
        Assert.That(restored.PixelData, Has.Length.EqualTo(6));
      });
    } finally {
      try { File.Delete(tmp); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray() {
    var original = new Fl32File {
      Width = 2, Height = 2, Channels = 1,
      PixelData = [0.0f, 0.33f, 0.66f, 1.0f]
    };

    var raw = Fl32File.ToRawImage(original);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray16));

    var restored = Fl32File.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.Channels, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb() {
    var pixels = new float[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = i / (float)pixels.Length;

    var original = new Fl32File { Width = 2, Height = 2, Channels = 3, PixelData = pixels };
    var raw = Fl32File.ToRawImage(original);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb48));

    var restored = Fl32File.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.Channels, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgba() {
    var pixels = new float[2 * 2 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = i / (float)pixels.Length;

    var original = new Fl32File { Width = 2, Height = 2, Channels = 4, PixelData = pixels };
    var raw = Fl32File.ToRawImage(original);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba64));

    var restored = Fl32File.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.Channels, Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FloatPrecision_PreservedExactly() {
    var pixels = new[] { float.MinValue, float.MaxValue, float.Epsilon, 0f, 3.14159265f, -1.0f };
    var original = new Fl32File { Width = 3, Height = 2, Channels = 1, PixelData = pixels };

    var bytes = Fl32Writer.ToBytes(original);
    var restored = Fl32Reader.FromBytes(bytes);

    for (var i = 0; i < pixels.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixels[i]));
  }
}
