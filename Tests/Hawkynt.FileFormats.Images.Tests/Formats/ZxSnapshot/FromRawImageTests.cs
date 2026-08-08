using System;
using System.IO;
using FileFormat.Core;
using FileFormat.ZxSnapshot;

namespace FileFormat.ZxSnapshot.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Picture(int width = 17, int height = 9) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 7);
      pixels[i * 3 + 1] = (byte)(i * 13);
      pixels[i * 3 + 2] = (byte)(i * 31);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TheScreenComesBackAtItsSize() {
    var decoded = ZxSnapshotFile.ToRawImage(ZxSnapshotReader.FromBytes(ZxSnapshotWriter.ToBytes(ZxSnapshotFile.FromRawImage(_Picture(256, 192)))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((256, 192)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IsExactlyOneOfTheLengthsASnapshotComesIn() {
    // Nothing in a snapshot says what it is; the length is the only thing that does, so any other
    // would be refused on the way back in.
    Assert.That(ZxSnapshotWriter.ToBytes(ZxSnapshotFile.FromRawImage(_Picture(256, 192))), Has.Length.EqualTo(ZxSnapshotFile.ShortFileSize));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ScalesAPictureOfAnyOtherSize() {
    var decoded = ZxSnapshotFile.ToRawImage(ZxSnapshotFile.FromRawImage(_Picture(100, 100)));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((256, 192)));
  }
}
