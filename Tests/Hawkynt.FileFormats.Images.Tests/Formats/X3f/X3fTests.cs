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

  [Test]
  [Category("Unit")]
  public void ToBytes_RoundTripsUncompressedRgb24Exactly() {
    var picture = _Picture(5, 3);

    var written = X3fWriter.ToBytes(X3fFile.FromRawImage(picture));
    var decoded = X3fFile.ToRawImage(X3fReader.FromBytes(written));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(picture.Width));
      Assert.That(decoded.Height, Is.EqualTo(picture.Height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesVersion22ProcessedImageAndTerminalDirectory() {
    const int width = 5;
    const int height = 3;
    const int stride = 16;
    const int sectionLength = X3fFile.ImageSectionHeaderSize + stride * height;
    const int directoryOffset = X3fFile.HeaderSize + sectionLength;

    var written = X3fWriter.ToBytes(X3fFile.FromRawImage(_Picture(width, height)));
    // Copied out rather than sliced in place: a ref struct local cannot be captured by the lambda
    // Assert.Multiple takes.
    var section = written.AsSpan(X3fFile.HeaderSize, sectionLength).ToArray();
    var directory = written.AsSpan(directoryOffset).ToArray();

    Assert.Multiple(() => {
      Assert.That(written.AsSpan(0, 4).ToArray(), Is.EqualTo(X3fFile.Magic.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(4)), Is.EqualTo(0x00020002u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(X3fFile.ColumnsField)), Is.EqualTo((uint)width));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(X3fFile.RowsField)), Is.EqualTo((uint)height));

      Assert.That(section[..4], Is.EqualTo(new byte[] { (byte)'S', (byte)'E', (byte)'C', (byte)'i' }));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(4)), Is.EqualTo(0x00020000u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(8)), Is.EqualTo(2u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(12)), Is.EqualTo((uint)X3fFile.FormatRgb24));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(16)), Is.EqualTo((uint)width));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(20)), Is.EqualTo((uint)height));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(24)), Is.EqualTo((uint)stride));

      Assert.That(directory[..4], Is.EqualTo(X3fFile.DirectoryMagic.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(4)), Is.EqualTo(0x00020000u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(8)), Is.EqualTo(1u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(12)), Is.EqualTo((uint)X3fFile.HeaderSize));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(16)), Is.EqualTo((uint)sectionLength));
      Assert.That(directory.AsSpan(20, 4).ToArray(), Is.EqualTo(new byte[] { (byte)'I', (byte)'M', (byte)'A', (byte)'G' }));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(written.Length - 4)), Is.EqualTo((uint)directoryOffset));
    });

    for (var y = 0; y < height; ++y)
      Assert.That(section[X3fFile.ImageSectionHeaderSize + y * stride + width * 3], Is.Zero, $"row {y} padding is zero");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataLengthDoesNotMatchDimensions_ThrowsArgumentException() {
    var file = new X3fFile { Width = 2, Height = 2, PixelData = new byte[11] };

    Assert.Throws<ArgumentException>(() => X3fWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ConvertsToRgb24() {
    var rgba = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [1, 2, 3, 4, 5, 6, 7, 8],
    };

    var file = X3fFile.FromRawImage(rgba);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Height, Is.EqualTo(1));
      Assert.That(file.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 5, 6, 7 }));
    });
  }
}
