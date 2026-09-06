using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffDctv;

namespace FileFormat.IffDctv.Tests;

[TestFixture]
public sealed class IffDctvReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDctvReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDctvReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dctv"));
    Assert.Throws<FileNotFoundException>(() => IffDctvReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDctvReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => IffDctvReader.FromBytes(new byte[1]));
  }

  /// <remarks>
  /// The reader used to accept any twelve bytes and hand back whatever followed as grey. Anything
  /// that is not an ILBM has to be turned away, or every unrecognised file in the world reads as a
  /// DCTV picture.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void FromBytes_NotAnIlbm_ThrowsInvalidDataException() {
    var data = new byte[64];
    "FORM"u8.CopyTo(data);
    "DEEP"u8.CopyTo(data.AsSpan(8));

    Assert.Throws<InvalidDataException>(() => IffDctvReader.FromBytes(data));
  }

  /// <remarks>
  /// A DCTV file is an ordinary ILBM. The synchronisation sequence in the top line is the only thing
  /// that separates one from a picture that is merely noisy, so an ILBM without it is not a DCTV.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void FromBytes_IlbmWithoutSignature_ThrowsInvalidDataException() {
    var picture = IffDctvFile.FromRawImage(_Sample(320, 8));
    var bytes = IffDctvWriter.ToBytes(picture);

    var broken = picture with { Samples = (byte[])picture.Samples.Clone() };
    Array.Clear(broken.Samples, 0, broken.Width);
    var brokenBytes = IffDctvWriter.ToBytes(broken);

    Assert.That(() => IffDctvReader.FromBytes(bytes), Throws.Nothing);
    Assert.Throws<InvalidDataException>(() => IffDctvReader.FromBytes(brokenBytes));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ReadsWhatTheWriterProduced() {
    var bytes = IffDctvWriter.ToBytes(IffDctvFile.FromRawImage(_Sample(320, 16)));

    using var ms = new MemoryStream(bytes);
    var result = IffDctvReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.ContentHeight, Is.EqualTo(18));
      Assert.That(result.Interlaced, Is.True);
      Assert.That(result.Height, Is.EqualTo(16));
    });
  }

  internal static RawImage _Sample(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var offset = (y * width + x) * 3;
        pixels[offset] = (byte)(x * 255 / (width - 1));
        pixels[offset + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
        pixels[offset + 2] = 96;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }
}
