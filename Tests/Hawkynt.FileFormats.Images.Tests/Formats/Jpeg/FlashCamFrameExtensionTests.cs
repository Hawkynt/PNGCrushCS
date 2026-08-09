using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Jpeg.Tests;

/// <summary>
/// A FlashCam frame (<c>.ncy</c>) is a JPEG.
/// </summary>
/// <remarks>
/// XnView's format table gives the FlashCam frame the same loader address as JPEG itself — the same
/// address it gives <c>.jps</c>, <c>.fsy</c> and <c>.mph</c>, all of which are already read here as
/// JPEGs — and its converter, told to read a plain JFIF as a FlashCam frame, returns the picture.
/// </remarks>
[TestFixture]
public sealed class FlashCamFrameExtensionTests {

  private static string[] _ExtensionsOf<T>() where T : IImageFormatMetadata<T> => T.FileExtensions;

  [Test]
  [Category("Unit")]
  public void FileExtensions_CarryTheFlashCamFrame()
    => Assert.That(_ExtensionsOf<JpegFile>(), Does.Contain(".ncy"));

  /// <summary>The reader says no to something that is not a JPEG, which is what makes the claim worth making.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AFileThatIsNotAJpegIsRefused() {
    var foreign = new byte[4096];
    for (var i = 0; i < foreign.Length; ++i)
      foreign[i] = (byte)(i * 3);

    Assert.Throws<InvalidDataException>(() => JpegReader.FromBytes(foreign));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_APlainJfifIsRead() {
    var pixels = new byte[16 * 12 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 251);

    var jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(new() {
      Width = 16, Height = 12, Format = PixelFormat.Rgb24, PixelData = pixels
    }));

    var image = JpegFile.ToRawImage(JpegReader.FromBytes(jpeg));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(16));
      Assert.That(image.Height, Is.EqualTo(12));
    });
  }
}
