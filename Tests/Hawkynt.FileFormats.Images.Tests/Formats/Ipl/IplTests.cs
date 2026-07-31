using System;
using System.IO;
using FileFormat.Ipl;
using FileFormat.Core;

namespace FileFormat.Ipl.Tests;

[TestFixture]
public class IplReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => IplReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => IplReader.FromBytes(new byte[15]));

  /// <summary>
  /// A real IPL header — tag, version, "data", then the 32-bit fields — rather than the two 16-bit
  /// numbers at offsets 0 and 4 that the placeholder reader used to invent.
  /// </summary>
  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = _BuildIpl(320, 240, "iiii");

    var result = IplReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(240));
    });
  }

  /// <summary>The tag says which byte order the header is in, and both are ordinary IPL files.</summary>
  [Test]
  public void FromBytes_BigEndianTag_ReadsTheSameDimensions() {
    var result = IplReader.FromBytes(_BuildIpl(320, 240, "mmmm"));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(240));
    });
  }

  [Test]
  public void FromBytes_UnknownTag_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => IplReader.FromBytes(_BuildIpl(4, 4, "zzzz")));

  /// <summary>Channels are stored as separate planes, so a red pixel is not three-in-a-row.</summary>
  [Test]
  public void FromBytes_ReadsChannelsAsPlanes() {
    var data = _BuildIpl(2, 1, "iiii");
    var plane = 2 * 1;
    data[44] = 255;              // red plane, pixel 0
    data[44 + plane] = 0;        // green plane
    data[44 + (plane * 2)] = 0;  // blue plane

    var result = IplReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(255), "red");
      Assert.That(result.PixelData[1], Is.EqualTo(0), "green");
      Assert.That(result.PixelData[2], Is.EqualTo(0), "blue");
    });
  }

  private static byte[] _BuildIpl(int width, int height, string tag) {
    var plane = width * height;
    var data = new byte[44 + (plane * 3) + 8];
    var big = tag == "mmmm";

    System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(data, 0);
    System.Text.Encoding.ASCII.GetBytes("100f").CopyTo(data, 8);
    System.Text.Encoding.ASCII.GetBytes("data").CopyTo(data, 12);
    _Write(data, 20, width, big);
    _Write(data, 24, height, big);
    _Write(data, 28, 3, big);
    System.Text.Encoding.ASCII.GetBytes("fini").CopyTo(data, 44 + (plane * 3));
    return data;
  }

  private static void _Write(byte[] data, int offset, int value, bool bigEndian) {
    if (bigEndian)
      System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset), value);
    else
      System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplReader.FromStream(null!));
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new IplFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = IplWriter.ToBytes(file);
    var file2 = IplReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new IplFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3],
    };
    var raw = IplFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    var file2 = IplFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

