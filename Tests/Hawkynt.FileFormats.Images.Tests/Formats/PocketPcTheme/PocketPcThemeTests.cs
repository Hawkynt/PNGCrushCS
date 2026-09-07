using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Png;

namespace FileFormat.PocketPcTheme.Tests;

/// <summary>The picture inside a Pocket PC theme.</summary>
/// <remarks>
/// XnView's reader for this name checks the cabinet signature and then scans the bytes for a
/// picture's opening bytes without unpacking anything. Every reader case below was put to its
/// converter on a fixture built the same way: it read the GIF, the PNG and the JFIF, and refused
/// both the cabinet with nothing in it and the one whose JPEG opens with an Exif segment.
/// <para/>
/// Writer cases inspect the CAB and Windows CE installation structures rather than accepting a
/// self-round-trip as proof: offsets, 8.3 numbered members, the <c>MSCE</c> control file and type-0
/// CFDATA blocks all have to account for the exact logical payload.
/// </remarks>
[TestFixture]
public sealed class PocketPcThemeTests {

  private const int _WIDTH = 5;
  private const int _HEIGHT = 4;
  private const int _MAX_CAB_BLOCK = 32768;
  private const uint _CE_FILE_ALWAYS_OVERWRITE = 0x40000000;

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

  [Test]
  [Category("Unit")]
  public void FromBytes_SomethingThatIsNotACabinetIsRefused()
    => Assert.Throws<InvalidDataException>(() => PocketPcThemeReader.FromBytes(_Png()));

  [Test]
  [Category("Unit")]
  public void FromBytes_ACabinetStoringNothingWholeIsRefused()
    => Assert.Throws<InvalidDataException>(() => PocketPcThemeReader.FromBytes(_Cabinet([])));

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
  public void ToBytes_InvalidModelIsRefused() {
    Assert.Multiple(() => {
      Assert.Throws<ArgumentOutOfRangeException>(() => PocketPcThemeWriter.ToBytes(new() { Width = 0, Height = 1, PixelData = [0, 0, 0] }));
      Assert.Throws<ArgumentOutOfRangeException>(() => PocketPcThemeWriter.ToBytes(new() { Width = 1, Height = 0, PixelData = [0, 0, 0] }));
      Assert.Throws<ArgumentException>(() => PocketPcThemeWriter.ToBytes(new() { Width = 1, Height = 1, PixelData = [0, 0] }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesAWindowsCeThemeCabinet() {
    var bytes = PocketPcThemeWriter.ToBytes(PocketPcThemeFile.FromRawImage(_Picture()));

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 4).ToArray(), Is.EqualTo(PocketPcThemeFile.Signature.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4)), Is.EqualTo((uint)bytes.Length));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4)), Is.EqualTo(44u));
      Assert.That(bytes[24], Is.EqualTo(3));
      Assert.That(bytes[25], Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26, 2)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28, 2)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(30, 2)), Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(42, 2)), Is.Zero);
    });

    var files = _ReadFiles(bytes);
    var payload = _ReadStoredFolder(bytes);
    var control = payload.AsSpan(checked((int)files[0].Offset), checked((int)files[0].Size)).ToArray();
    var setup = Encoding.UTF8.GetString(payload, checked((int)files[3].Offset), checked((int)files[3].Size));
    var ceFiles = _ReadCeFiles(control);

    Assert.Multiple(() => {
      Assert.That(files[0].Name, Is.EqualTo("PNGCRUSH.000"));
      Assert.That(files[1].Name, Is.EqualTo("0STWATER.002"));
      Assert.That(files[2].Name, Is.EqualTo("TDYWATER.001"));
      Assert.That(files[3].Name, Is.EqualTo("_setup.xml"));
      Assert.That(files[0].Offset, Is.Zero);
      Assert.That(files[1].Offset, Is.EqualTo(files[0].Size));
      Assert.That(files[2].Offset, Is.EqualTo(files[1].Offset + files[1].Size));
      Assert.That(files[3].Offset, Is.EqualTo(files[2].Offset + files[2].Size));
      Assert.That(files[1].Size, Is.EqualTo(files[2].Size));
      Assert.That(payload.Length, Is.EqualTo(checked((int)(files[0].Size + files[1].Size + files[2].Size + files[3].Size))));
      Assert.That(payload.AsSpan(checked((int)files[1].Offset), 4).ToArray(), Is.EqualTo(PocketPcThemeFile.PngSignature.ToArray()));
      Assert.That(payload.AsSpan(checked((int)files[2].Offset), 4).ToArray(), Is.EqualTo(PocketPcThemeFile.PngSignature.ToArray()));
      Assert.That(control.AsSpan(0, 4).ToArray(), Is.EqualTo("MSCE"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(control.AsSpan(8, 4)), Is.EqualTo((uint)control.Length));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(control.AsSpan(48, 2)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(control.AsSpan(50, 2)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(control.AsSpan(52, 2)), Is.EqualTo(2));
      Assert.That(_ReadCeString(control), Is.EqualTo("%CE2%"));
      Assert.That(ceFiles[0], Is.EqualTo((1, 1, 1, _CE_FILE_ALWAYS_OVERWRITE, "tdywater.png")));
      Assert.That(ceFiles[1], Is.EqualTo((2, 1, 2, _CE_FILE_ALWAYS_OVERWRITE, "stwater.png")));
      Assert.That(setup, Does.Contain("<wap-provisioningdoc>"));
      Assert.That(setup, Does.Contain("value=\"TDYWATER.001\""));
      Assert.That(setup, Does.Contain("value=\"0STWATER.002\""));
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

  private static string _ReadCeString(byte[] control) {
    var at = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(control.AsSpan(60, 4)));
    var id = BinaryPrimitives.ReadUInt16LittleEndian(control.AsSpan(at, 2));
    var length = BinaryPrimitives.ReadUInt16LittleEndian(control.AsSpan(at + 2, 2));
    if (id != 1 || length == 0 || at + 4 + length > control.Length || control[at + 3 + length] != 0)
      throw new InvalidDataException("Invalid CE STRINGS section.");

    return Encoding.ASCII.GetString(control, at + 4, length - 1);
  }

  private static (int Id, int Directory, int RepeatedId, uint Flags, string Name)[] _ReadCeFiles(byte[] control) {
    var count = BinaryPrimitives.ReadUInt16LittleEndian(control.AsSpan(52, 2));
    var at = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(control.AsSpan(68, 4)));
    var result = new (int Id, int Directory, int RepeatedId, uint Flags, string Name)[count];

    for (var i = 0; i < count; ++i) {
      if (at < 0 || at > control.Length - 12)
        throw new InvalidDataException("CE FILES entry lies outside the control file.");

      var id = BinaryPrimitives.ReadUInt16LittleEndian(control.AsSpan(at, 2));
      var directory = BinaryPrimitives.ReadUInt16LittleEndian(control.AsSpan(at + 2, 2));
      var repeatedId = BinaryPrimitives.ReadUInt16LittleEndian(control.AsSpan(at + 4, 2));
      var flags = BinaryPrimitives.ReadUInt32LittleEndian(control.AsSpan(at + 6, 4));
      var length = BinaryPrimitives.ReadUInt16LittleEndian(control.AsSpan(at + 10, 2));
      if (length == 0 || at + 12 + length > control.Length || control[at + 11 + length] != 0)
        throw new InvalidDataException("CE FILES name is invalid.");

      result[i] = (id, directory, repeatedId, flags, Encoding.ASCII.GetString(control, at + 12, length - 1));
      at += 12 + length;
    }

    return result;
  }
}
