using FileFormat.Core;

namespace FileFormat.AndrewToolkit.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _GrayRamp(int width, int height) {
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gray8_ReturnsEveryToneUnchanged() {
    var source = _GrayRamp(12, 5);

    var restored = AndrewToolkitFile.ToRawImage(AndrewToolkitReader.FromBytes(AndrewToolkitWriter.ToBytes(AndrewToolkitFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(12));
      Assert.That(restored.Height, Is.EqualTo(5));
      Assert.That(restored.EnsureFormat(PixelFormat.Gray8).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The size lives in a text header the reader parses back, so the two have to agree.</summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_HeaderStatesTheSizeTheReaderFindsAgain() {
    var bytes = AndrewToolkitWriter.ToBytes(AndrewToolkitFile.FromRawImage(_GrayRamp(37, 3)));

    var restored = AndrewToolkitReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(37));
      Assert.That(restored.Height, Is.EqualTo(3));
      Assert.That(restored.RawData, Has.Length.EqualTo(37 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ColourSource_BecomesLuminanceRatherThanBeingRefused() {
    var source = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = [255, 255, 255] };

    Assert.That(AndrewToolkitFile.FromRawImage(source).RawData, Is.EqualTo(new byte[] { 255 }));
  }
}
