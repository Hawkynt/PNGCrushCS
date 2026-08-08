using System;
using System.IO;
using System.Text;
using FileFormat.CorelGallery;

namespace FileFormat.CorelGallery.Tests;

[TestFixture]
public sealed class CorelGalleryTests {

  /// <summary>An 8-bit Windows bitmap with no file header, the shape these carry as a preview.</summary>
  internal static byte[] Dib(int width, int height) {
    var stride = (width + 3) & ~3;
    var data = new byte[40 + 256 * 4 + stride * height];
    BitConverter.GetBytes(40).CopyTo(data, 0);
    BitConverter.GetBytes(width).CopyTo(data, 4);
    BitConverter.GetBytes(height).CopyTo(data, 8);
    BitConverter.GetBytes((short)1).CopyTo(data, 12);
    BitConverter.GetBytes((short)8).CopyTo(data, 14);
    BitConverter.GetBytes(stride * height).CopyTo(data, 20);
    BitConverter.GetBytes(256).CopyTo(data, 32);
    for (var i = 0; i < 256; ++i) {
      data[40 + i * 4] = (byte)i;
      data[40 + i * 4 + 1] = (byte)i;
      data[40 + i * 4 + 2] = (byte)i;
    }

    for (var i = 0; i < stride * height; ++i)
      data[40 + 256 * 4 + i] = (byte)(i * 3);

    return data;
  }

  private static byte[] _Clipart(int width, int height) {
    var head = new byte[CorelGalleryFile.PreviewOffset];
    Encoding.ASCII.GetBytes("@CorelBMF\n\rCorel Corporation\n\r").CopyTo(head, 0);
    var dib = Dib(width, height);
    var file = new byte[head.Length + dib.Length + 64];
    head.CopyTo(file, 0);
    dib.CopyTo(file, head.Length);
    return file;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CorelGalleryReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => CorelGalleryReader.FromBytes(new byte[512]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsThePreviewAtTheOffsetTheFormatPutsIt() {
    var preview = CorelGalleryFile.ToRawImage(CorelGalleryReader.FromBytes(_Clipart(96, 96)));

    Assert.Multiple(() => {
      Assert.That(preview.Width, Is.EqualTo(96));
      Assert.That(preview.Height, Is.EqualTo(96));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_APreviewLargerThanTheFile_ThrowsInvalidDataException() {
    var data = _Clipart(96, 96);
    // Say the picture is far taller than the bytes after the header can hold.
    BitConverter.GetBytes(4000).CopyTo(data, CorelGalleryFile.PreviewOffset + 8);

    Assert.Throws<InvalidDataException>(() => CorelGalleryReader.FromBytes(data));
  }
}
