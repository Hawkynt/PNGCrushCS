using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.PhotoSuiteProject;
using FileFormat.Png;

namespace FileFormat.PhotoSuiteProject.Tests;

/// <summary>
/// The picture inside an MGI PhotoSuite project.
/// </summary>
/// <remarks>
/// Nothing published names the streams a <c>.pzp</c> holds, and XnView does not use their names
/// either: it walks the compound document from offset 512 for the eight bytes a PNG opens with and
/// decodes the first one. What stands outside this file is that a fixture built this way is read by
/// XnView's converter at the PNG's size.
/// </remarks>
[TestFixture]
public sealed class PhotoSuiteProjectTests {

  private const int _WIDTH = 5;
  private const int _HEIGHT = 4;
  private const uint _END_OF_CHAIN = 0xFFFFFFFE;

  private static byte[] _Png(int width = _WIDTH, int height = _HEIGHT) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)(x * 40 + 3);
        pixels[at + 1] = (byte)(y * 50 + 7);
        pixels[at + 2] = (byte)(x * y * 11 + 1);
      }

    return PngWriter.ToBytes(PngFile.FromRawImage(new() {
      Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels
    }));
  }

  private static byte[] _Build(int gap = 0, bool withPng = true) {
    var png = withPng ? _Png() : [];
    var data = new byte[PhotoSuiteProjectFile.ScanStart + gap + png.Length + 8];
    PhotoSuiteProjectFile.Signature.CopyTo(data);
    png.CopyTo(data, PhotoSuiteProjectFile.ScanStart + gap);
    return data;
  }

  private static RawImage _NoiseImage(int width, int height) {
    var pixels = new byte[width * height * 3];
    var state = 0xA5C31E27u;
    for (var i = 0; i < pixels.Length; ++i) {
      state ^= state << 13;
      state ^= state >> 17;
      state ^= state << 5;
      pixels[i] = (byte)(state >> 24);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PhotoSuiteProjectReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pzp"));
    Assert.Throws<FileNotFoundException>(() => PhotoSuiteProjectReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PhotoSuiteProjectReader.FromBytes(new byte[64]));

  /// <summary>A PNG on its own is not a project, however readable it is.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_SomethingThatIsNotACompoundDocumentIsRefused() {
    var png = _Png(64, 64);

    Assert.Throws<InvalidDataException>(() => PhotoSuiteProjectReader.FromBytes(png));
  }

  /// <summary>A compound document of nothing but text is refused rather than drawn empty.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ACompoundDocumentWithoutAPictureIsRefused()
    => Assert.Throws<InvalidDataException>(() => PhotoSuiteProjectReader.FromBytes(_Build(withPng: false)));

  [Test]
  [Category("Integration")]
  public void FromBytes_ThePictureIsThePngInside([Values(0, 4, 512)] int gap) {
    var read = PhotoSuiteProjectReader.FromBytes(_Build(gap));

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_EveryPixelComesBackAsItWasPutIn() {
    var expected = PixelConverter.Convert(PngFile.ToRawImage(PngReader.FromBytes(_Png())), PixelFormat.Rgb24);

    var image = PhotoSuiteProjectFile.ToRawImage(PhotoSuiteProjectReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData, Is.EqualTo(expected.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void Writer_SmallPictureUsesMiniStreamAndRoundTripsExactly() {
    var expected = new RawImage {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Rgb24,
      PixelData = PhotoSuiteProjectFile.ToRawImage(PhotoSuiteProjectReader.FromBytes(_Build())).PixelData,
    };

    var data = PhotoSuiteProjectWriter.ToBytes(PhotoSuiteProjectFile.FromRawImage(expected));
    var actual = PhotoSuiteProjectFile.ToRawImage(PhotoSuiteProjectReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(data.AsSpan(0, 8).ToArray(), Is.EqualTo(PhotoSuiteProjectFile.Signature.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(26, 2)), Is.EqualTo(3));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(30, 2)), Is.EqualTo(9));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(32, 2)), Is.EqualTo(6));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(56, 4)), Is.EqualTo(4096u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(60, 4)), Is.Not.EqualTo(_END_OF_CHAIN));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(64, 4)), Is.GreaterThan(0u));
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void Writer_LargePictureUsesNormalFatAndRoundTripsExactly() {
    var expected = _NoiseImage(96, 96);

    var data = PhotoSuiteProjectWriter.ToBytes(PhotoSuiteProjectFile.FromRawImage(expected));
    var actual = PhotoSuiteProjectFile.ToRawImage(PhotoSuiteProjectReader.FromBytes(data));
    var directorySector = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(48, 4));
    var directoryOffset = checked((int)(directorySector + 1) * 512);
    var imageEntry = data.AsSpan(directoryOffset + 128, 128);
    var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(imageEntry.Slice(64, 2));
    var name = Encoding.Unicode.GetString(imageEntry[..(nameLength - 2)]);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(60, 4)), Is.EqualTo(_END_OF_CHAIN));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(64, 4)), Is.Zero);
      Assert.That(name, Is.EqualTo("Image"));
      Assert.That(imageEntry[66], Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(imageEntry.Slice(120, 8)), Is.GreaterThanOrEqualTo(4096ul));
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void Writer_MultiFatProjectStillRoundTripsExactly() {
    var expected = _NoiseImage(192, 192);

    var data = PhotoSuiteProjectWriter.ToBytes(PhotoSuiteProjectFile.FromRawImage(expected));
    var actual = PhotoSuiteProjectFile.ToRawImage(PhotoSuiteProjectReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(44, 4)), Is.GreaterThan(1u));
      Assert.That(data.Length % 512, Is.Zero);
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }
}
