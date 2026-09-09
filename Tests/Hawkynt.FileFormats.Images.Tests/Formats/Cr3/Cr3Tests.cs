using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Cr3;
using Hawkynt.FileFormats.Images;
using NUnit.Framework;

namespace FileFormat.Cr3.Tests;

/// <summary>Canon CR3 files, read for the pictures beside their sensor data.</summary>
/// <remarks>
/// No real CR3 was available, so the fixture was built to the layout ExifTool
/// reads and ExifTool is what judges it: given a file written here it reports the
/// type as CR3, states the codec version out of the Canon box, and extracts the
/// preview and the thumbnail byte for byte as they went in.
///
/// <para>The sensor data itself is coded with CRX, which is not implemented. The
/// registry writer therefore carries an arbitrary picture as the ordinary JPEG
/// preview and does not synthesise a sensor track or camera-authored metadata.</para>
/// </remarks>
[TestFixture]
public sealed class Cr3Tests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Cr3", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  private static Cr3File _Read() => Cr3Reader.FromBytes(_Fixture("canon_style.cr3"));

  private static RawImage _Gradient(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var offset = (y * width + x) * 3;
      pixels[offset] = (byte)(x * 255 / Math.Max(1, width - 1));
      pixels[offset + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      pixels[offset + 2] = (byte)((x + y) * 255 / Math.Max(1, width + height - 2));
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  public void TheCodecVersionAndBothPicturesComeOutOfTheBoxesTheyLiveIn() {
    var file = _Read();
    Assert.Multiple(() => {
      Assert.That(file.CodecVersion, Is.EqualTo("CanonCR3_001/00.09.00/00.00.00"));
      Assert.That(file.PreviewJpeg, Is.Not.Null);
      Assert.That(file.ThumbnailJpeg, Is.Not.Null);
      Assert.That(file.PreviewWidth, Is.EqualTo(320));
      Assert.That(file.PreviewHeight, Is.EqualTo(240));
      Assert.That(file.ThumbnailWidth, Is.EqualTo(160));
      Assert.That(file.ThumbnailHeight, Is.EqualTo(120));
    });

    // Both are ordinary JPEGs, start-of-image marker and all.
    Assert.That(file.PreviewJpeg![0], Is.EqualTo(0xFF));
    Assert.That(file.PreviewJpeg[1], Is.EqualTo(0xD8));
    Assert.That(file.ThumbnailJpeg![0], Is.EqualTo(0xFF));
    Assert.That(file.ThumbnailJpeg[1], Is.EqualTo(0xD8));
  }

  /// <summary>The picture handed back is the larger of the two, not the first one met.</summary>
  [Test]
  public void ThePictureIsTheFullSizePreview() {
    var image = Cr3File.ToRawImage(_Read());
    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(320));
      Assert.That(image.Height, Is.EqualTo(240));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  public void AFileWrittenHereReadsBackWithBothPicturesUnchanged() {
    var file = _Read();
    var again = Cr3Reader.FromBytes(Cr3Writer.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(again.CodecVersion, Is.EqualTo(file.CodecVersion));
      Assert.That(again.PreviewJpeg, Is.EqualTo(file.PreviewJpeg));
      Assert.That(again.ThumbnailJpeg, Is.EqualTo(file.ThumbnailJpeg));
      Assert.That(again.PreviewWidth, Is.EqualTo(file.PreviewWidth));
      Assert.That(again.ThumbnailHeight, Is.EqualTo(file.ThumbnailHeight));
    });
  }

  [Test]
  public void TheRegistryWritesAnArbitraryPictureAsAPreviewOnlyCr3() {
    var source = _Gradient(32, 24);
    var bytes = FormatRegistry.Write(source, ImageFormat.Cr3);

    Assert.That(bytes, Is.Not.Null, "CR3 must be registered as writable");
    Assert.That(FormatRegistry.DetectFromBytes(bytes!), Is.EqualTo(ImageFormat.Cr3));

    var file = Cr3Reader.FromBytes(bytes!);
    var decoded = Cr3File.ToRawImage(file);
    Assert.Multiple(() => {
      Assert.That(file.PreviewJpeg, Is.Not.Null);
      Assert.That(file.ThumbnailJpeg, Is.Null);
      Assert.That(file.PreviewWidth, Is.EqualTo(source.Width));
      Assert.That(file.PreviewHeight, Is.EqualTo(source.Height));
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  public void DimensionsThatDoNotFitTheCr3PreviewHeaderAreRefused() {
    var source = new RawImage {
      Width = ushort.MaxValue + 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[(ushort.MaxValue + 1) * 3],
    };

    var failure = Assert.Throws<ArgumentOutOfRangeException>(() => Cr3File.FromRawImage(source));
    Assert.That(failure!.ParamName, Is.EqualTo("Width"));
  }

  [Test]
  public void TheLowLevelWriterDoesNotSilentlyTruncatePreviewDimensions() {
    var source = _Read();
    var file = new Cr3File {
      PreviewJpeg = source.PreviewJpeg,
      PreviewWidth = ushort.MaxValue + 1,
      PreviewHeight = 1,
    };

    var failure = Assert.Throws<ArgumentOutOfRangeException>(() => Cr3Writer.ToBytes(file));
    Assert.That(failure!.ParamName, Is.EqualTo(nameof(Cr3File.PreviewWidth)));
  }

  [Test]
  public void AnIsoBaseMediaFileThatIsNotCanonsIsRefused() {
    var bytes = _Fixture("canon_style.cr3");
    // Turn the brand into an MP4's.
    bytes[8] = (byte)'i';
    bytes[9] = (byte)'s';
    bytes[10] = (byte)'o';
    bytes[11] = (byte)'m';
    Assert.Throws<InvalidDataException>(() => Cr3Reader.FromBytes(bytes));
  }

  /// <summary>
  /// A CR3 whose only picture is its sensor data says so, because CRX is not decoded here.
  /// </summary>
  [Test]
  public void ACr3CarryingOnlySensorDataIsRefusedByName() {
    var file = _Read();
    var stripped = new Cr3File { CodecVersion = file.CodecVersion, ThumbnailJpeg = file.ThumbnailJpeg, ThumbnailWidth = 160, ThumbnailHeight = 120 };
    var bytes = Cr3Writer.ToBytes(stripped);

    // Blank the thumbnail's four-character code so nothing is left to find.
    var at = System.Text.Encoding.ASCII.GetString(bytes).IndexOf("THMB", StringComparison.Ordinal);
    Assert.That(at, Is.GreaterThan(0));
    bytes[at] = (byte)'X';

    var failure = Assert.Throws<NotSupportedException>(() => Cr3Reader.FromBytes(bytes));
    Assert.That(failure!.Message, Does.Contain("CRX"));
  }
}
