using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Png;

namespace FileFormat.PocketPcTheme.Tests;

/// <summary>
/// The picture inside a Pocket PC theme.
/// </summary>
/// <remarks>
/// XnView's reader for this name checks the cabinet signature and then scans the bytes for a
/// picture's opening bytes without unpacking anything. Every reader case below was put to its
/// converter on a fixture built the same way: it read the GIF, the PNG and the JFIF, and refused
/// both the cabinet with nothing in it and the one whose JPEG opens with an Exif segment.
/// <para/>
/// Writer cases additionally inspect the CAB structures themselves rather than accepting a
/// round-trip as proof: offsets, file records and type-0 CFDATA blocks must describe the exact
/// logical payload that the reader reconstructs.
/// </remarks>
[TestFixture]
public sealed class PocketPcThemeTests {

  private const int _WIDTH = 5;
  private const int _HEIGHT = 4;
  private const int _MAX_CAB_BLOCK = 32768;

  private static RawImage _Picture() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        var at = (y * _WIDTH + x) * 3;
        pixels[at] = (byte)(x * 40 + 3);
        pixels[at + 1] = (byte)(y * 50 + 7);
        pixels[at + 2] = (byte)(x * y * 11 + 1);
      }

    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _NoisePicture(int width, int height) {
    var pixels = new byte[width * height * 3];
    var state = 0xC001D00Du;
    for (var i = 0; i < pixels.Length; ++i) {
      state ^= state << 13;
      state ^= state >> 17;
      state ^= state << 5;
      pixels[i] = (byte)state;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static byte[] _Png() => PngWriter.ToBytes(PngFile.FromRawImage(_Picture()));
  private static byte[] _Jpeg() => JpegWriter.ToBytes(JpegFile.FromRawImage(_Picture()));

  /// <summary>A cabinet-looking fixture whose header is followed by the bytes given.</summary>
  private static byte[] _Cabinet(byte[] stored, int gap = 32) {
    using var memory = new MemoryStream();
    memory.Write(PocketPcThemeFile.Signature);
    memory.Write(new byte[gap], 0, gap);
    memory.Write(stored, 0, stored.Length);
    return memory.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PocketPcThemeReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tsk"));
    Assert.Throws<FileNotFoundException>(() => PocketPcThemeReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PocketPcThemeReader.FromBytes([0x4D, 0x53]));

  /// <summary>A picture on its own is not a theme, however readable it is.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_SomethingThatIsNotACabinetIsRefused()
    => Assert.Throws<InvalidDataException>(() => PocketPcThemeReader.FromBytes(_Png()));

  /// <summary>A cabinet whose files are all packed has nothing this can reach, and says so.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ACabinetStoringNothingWholeIsRefused()
    => Assert.Throws<InvalidDataException>(() => PocketPcThemeReader.FromBytes(_Cabinet([])));

  /// <summary>
  /// The JPEG test is on four bytes. An Exif file opens <c>FF D8 FF E1</c> and is not one of the
  /// three signatures this looks for — XnView refuses it under this name too.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AJpegThatIsNotJfifIsNotFound() {
    var exif = _Jpeg();
    exif[3] = 0xE1;

    Assert.Throws<InvalidDataException>(() => PocketPcThemeReader.FromBytes(_Cabinet(exif)));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ThePictureIsTheFirstOneStoredWhole([Values(0, 1, 32, 512)] int gap) {
    var read = PocketPcThemeReader.FromBytes(_Cabinet(_Png(), gap));

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_AJfifIsFoundTheSameWayAPngIs() {
    var read = PocketPcThemeReader.FromBytes(_Cabinet(_Jpeg()));

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_EveryPixelComesBackAsItWasPutIn() {
    var expected = PixelConverter.Convert(PngFile.ToRawImage(PngReader.FromBytes(_Png())), PixelFormat.Rgb24);

    var image = PocketPcThemeFile.ToRawImage(PocketPcThemeReader.FromBytes(_Cabinet(_Png())));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData, Is.EqualTo(expected.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesARealStoredCabinetWithThemeFiles() {
    var bytes = PocketPcThemeWriter.ToBytes(PocketPcThemeFile.FromRawImage(_Picture()));

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 4).ToArray(), Is.EqualTo(PocketPcThemeFile.Signature.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4)), Is.EqualTo((uint)bytes.Length));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4)), Is.EqualTo(44u));
      Assert.That(bytes[24], Is.EqualTo(3));
      Assert.That(bytes[25], Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26, 2)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28, 2)), Is.EqualTo(3));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(30, 2)), Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(42, 2)), Is.Zero);
    });

    var files = _ReadFiles(bytes);
    var payload = _ReadStoredFolder(bytes);

    Assert.Multiple(() => {
      Assert.That(files[0].Name, Is.EqualTo("tdywater.png"));
      Assert.That(files[1].Name, Is.EqualTo("stwater.png"));
      Assert.That(files[2].Name, Is.EqualTo("_setup.xml"));
      Assert.That(files[0].Offset, Is.Zero);
      Assert.That(files[1].Offset, Is.EqualTo(files[0].Size));
      Assert.That(files[2].Offset, Is.EqualTo(files[0].Size + files[1].Size));
      Assert.That(files[0].Size, Is.EqualTo(files[1].Size));
      Assert.That(payload.Length, Is.EqualTo((long)files[0].Size + files[1].Size + files[2].Size));
      Assert.That(payload.AsSpan((int)files[0].Offset, 4).ToArray(), Is.EqualTo(PocketPcThemeFile.PngSignature.ToArray()));
      Assert.That(payload.AsSpan((int)files[1].Offset, 4).ToArray(), Is.EqualTo(PocketPcThemeFile.PngSignature.ToArray()));
      Assert.That(
        Encoding.UTF8.GetString(payload, (int)files[2].Offset, (int)files[2].Size),
        Does.Contain("<wap-provisioningdoc>"));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToBytes_RoundTripsLosslesslyThroughTheReader() {
    var expected = _Picture();

    var bytes = PocketPcThemeWriter.ToBytes(PocketPcThemeFile.FromRawImage(expected));
    var actual = PocketPcThemeFile.ToRawImage(PocketPcThemeReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(actual.Width, Is.EqualTo(expected.Width));
      Assert.That(actual.Height, Is.EqualTo(expected.Height));
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }

  /// <summary>
  /// CAB inserts an eight-byte CFDATA header between 32 KiB stored blocks. A large PNG therefore
  /// cannot be decoded from the physical cabinet bytes as one contiguous stream; the reader must
  /// rebuild the folder first. This also exercises the writer's block splitting.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void ToBytes_LargePictureSpansCabDataBlocksAndStillRoundTrips() {
    var expected = _NoisePicture(256, 256);

    var bytes = PocketPcThemeWriter.ToBytes(PocketPcThemeFile.FromRawImage(expected));
    var blockCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(40, 2));
    var actual = PocketPcThemeFile.ToRawImage(PocketPcThemeReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(blockCount, Is.GreaterThan(1));
      Assert.That(actual.Width, Is.EqualTo(expected.Width));
      Assert.That(actual.Height, Is.EqualTo(expected.Height));
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }

  private static (string Name, uint Size, uint Offset)[] _ReadFiles(byte[] cabinet) {
    var count = BinaryPrimitives.ReadUInt16LittleEndian(cabinet.AsSpan(28, 2));
    var at = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(cabinet.AsSpan(16, 4)));
    var result = new (string Name, uint Size, uint Offset)[count];

    for (var i = 0; i < count; ++i) {
      if (at < 0 || at > cabinet.Length - 16)
        throw new InvalidDataException("CFFILE lies outside the cabinet.");

      var size = BinaryPrimitives.ReadUInt32LittleEndian(cabinet.AsSpan(at, 4));
      var offset = BinaryPrimitives.ReadUInt32LittleEndian(cabinet.AsSpan(at + 4, 4));
      var folder = BinaryPrimitives.ReadUInt16LittleEndian(cabinet.AsSpan(at + 8, 2));
      if (folder != 0)
        throw new InvalidDataException("Test cabinet unexpectedly uses another folder.");

      var nameStart = at + 16;
      var nameEnd = Array.IndexOf(cabinet, (byte)0, nameStart);
      if (nameEnd < nameStart)
        throw new InvalidDataException("CFFILE name is not terminated.");

      result[i] = (Encoding.ASCII.GetString(cabinet, nameStart, nameEnd - nameStart), size, offset);
      at = nameEnd + 1;
    }

    return result;
  }

  private static byte[] _ReadStoredFolder(byte[] cabinet) {
    var at = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(cabinet.AsSpan(36, 4)));
    var blockCount = BinaryPrimitives.ReadUInt16LittleEndian(cabinet.AsSpan(40, 2));
    using var result = new MemoryStream();

    for (var i = 0; i < blockCount; ++i) {
      if (at < 0 || at > cabinet.Length - 8)
        throw new InvalidDataException("CFDATA lies outside the cabinet.");

      var checksum = BinaryPrimitives.ReadUInt32LittleEndian(cabinet.AsSpan(at, 4));
      var stored = BinaryPrimitives.ReadUInt16LittleEndian(cabinet.AsSpan(at + 4, 2));
      var unpacked = BinaryPrimitives.ReadUInt16LittleEndian(cabinet.AsSpan(at + 6, 2));
      if (checksum != 0 || stored != unpacked || stored > _MAX_CAB_BLOCK || stored > cabinet.Length - at - 8)
        throw new InvalidDataException("CFDATA is not a valid uncompressed block written by this format.");

      result.Write(cabinet.AsSpan(at + 8, stored));
      at += 8 + stored;
    }

    if (at != cabinet.Length)
      throw new InvalidDataException("Cabinet contains bytes outside the declared CFDATA blocks.");

    return result.ToArray();
  }
}
