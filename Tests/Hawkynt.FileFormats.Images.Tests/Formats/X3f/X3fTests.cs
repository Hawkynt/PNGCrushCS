using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.X3f;

namespace FileFormat.X3f.Tests;

[TestFixture]
public sealed class X3fTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 3);
      pixels[i * 3 + 1] = (byte)(i * 5);
      pixels[i * 3 + 2] = (byte)(i * 7);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private sealed record Section(int Format, int Width, int Height, int Stride, byte[] Body);

  /// <summary>A container stating a sensor of the given size and holding the given sections.</summary>
  private static byte[] _File(int statedWidth, int statedHeight, params Section[] sections) {
    using var ms = new MemoryStream();
    var header = new byte[X3fFile.HeaderSize];
    X3fFile.Magic.CopyTo(header);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 0x00020002);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(X3fFile.ColumnsField), (uint)statedWidth);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(X3fFile.RowsField), (uint)statedHeight);
    ms.Write(header);

    var placed = new (int Offset, int Length)[sections.Length];
    for (var i = 0; i < sections.Length; ++i) {
      var section = sections[i];
      var start = (int)ms.Position;

      var head = new byte[X3fFile.ImageSectionHeaderSize];
      System.Text.Encoding.ASCII.GetBytes("SECi").CopyTo(head, 0);
      BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8), 2);
      BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), (uint)section.Format);
      BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(16), (uint)section.Width);
      BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(20), (uint)section.Height);
      BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(24), (uint)section.Stride);
      ms.Write(head);
      ms.Write(section.Body);

      placed[i] = (start, (int)ms.Position - start);
    }

    var directory = (int)ms.Position;
    ms.Write(X3fFile.DirectoryMagic);
    ms.Write(BitConverter.GetBytes(0x00020000u));
    ms.Write(BitConverter.GetBytes((uint)sections.Length));
    foreach (var (offset, length) in placed) {
      ms.Write(BitConverter.GetBytes((uint)offset));
      ms.Write(BitConverter.GetBytes((uint)length));
      ms.Write(System.Text.Encoding.ASCII.GetBytes("IMA2"));
    }

    ms.Write(BitConverter.GetBytes((uint)directory));
    return ms.ToArray();
  }

  private static Section _Jpeg(int width, int height)
    => new(X3fFile.FormatJpeg, width, height, 0, JpegWriter.ToBytes(JpegFile.FromRawImage(_Picture(width, height))));

  private static Section _Rgb(int width, int height) {
    var stride = width * 3;
    return new(X3fFile.FormatRgb24, width, height, stride, new byte[stride * height]);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => X3fReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => X3fReader.FromBytes(new byte[512]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ADirectoryPointerThatIsNotOne_ThrowsInvalidDataException() {
    var data = _File(64, 48, _Jpeg(64, 48));
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(data.Length - 4), 250);

    Assert.Throws<InvalidDataException>(() => X3fReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AFullSizeJpegSectionIsThePicture() {
    var decoded = X3fFile.ToRawImage(X3fReader.FromBytes(_File(64, 48, _Jpeg(64, 48))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(64));
      Assert.That(decoded.Height, Is.EqualTo(48));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheLargestReadableSectionWins() {
    var decoded = X3fFile.ToRawImage(X3fReader.FromBytes(_File(64, 48, _Rgb(16, 12), _Jpeg(64, 48))));

    Assert.That(decoded.Width, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_APreviewIsNotAnswerdAsThePicture() {
    // A file whose only readable section is a fraction of the size it claims is a Foveon raw with a
    // preview beside it, and the preview is not what was asked for.
    Assert.Throws<InvalidDataException>(() => X3fReader.FromBytes(_File(2268, 1512, _Rgb(189, 126))));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ACodingThisDoesNotUndo_ThrowsInvalidDataException() {
    // Format six is the Foveon Huffman raw.
    Assert.Throws<InvalidDataException>(() => X3fReader.FromBytes(_File(2304, 1531, new Section(6, 2304, 1531, 0, new byte[64]))));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UncompressedSamplesAreReadAtTheirStatedStride() {
    // The stride is padded past the width, so rows taken end to end would shear the picture.
    var width = 9;
    var height = 4;
    var stride = width * 3 + 1;
    var body = new byte[stride * height];
    for (var y = 0; y < height; ++y)
      body[y * stride] = (byte)(y + 1);

    var file = X3fReader.FromBytes(_File(width, height, new Section(X3fFile.FormatRgb24, width, height, stride, body)));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      for (var y = 0; y < height; ++y)
        Assert.That(file.PixelData[y * width * 3], Is.EqualTo((byte)(y + 1)), $"row {y} starts where the stride says");
    });
  }
}
