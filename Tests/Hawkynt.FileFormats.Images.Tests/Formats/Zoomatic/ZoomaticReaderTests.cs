using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Zoomatic;

namespace FileFormat.Zoomatic.Tests;

[TestFixture]
public sealed class ZoomaticReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZoomaticReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZoomaticReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zom"));
    Assert.Throws<FileNotFoundException>(() => ZoomaticReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZoomaticReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => ZoomaticReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesLoadAddress() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBitmapData() {
    var screen = _BuildScreen(0x03);
    screen[0] = 0xAB;
    screen[7999] = 0xCD;

    var result = ZoomaticReader.FromBytes(_Pack(screen, 0x4000));

    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesScreenData() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.ScreenData.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesColorData() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.ColorData.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBackgroundColor() {
    var data = _BuildValidFile(0x4000, 0x07);
    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.BackgroundColor, Is.EqualTo(0x07));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = _BuildValidFile(0x6000, 0x00);
    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidFile_ParsesCorrectly() {
    var data = _BuildValidFile(0x4000, 0x05);
    using var ms = new MemoryStream(data);
    var result = ZoomaticReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenData.Length, Is.EqualTo(1000));
    Assert.That(result.ColorData.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BlankScreen_BackgroundColorDefaultsToZero() {
    var result = ZoomaticReader.FromBytes(_Pack(new byte[10001], 0x2000));

    Assert.That(result.BackgroundColor, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_StreamEndingBeforeTheScreenIsFull_ThrowsInvalidDataException() {
    var truncated = _Pack(new byte[10001], 0x2000)[..8];

    Assert.Throws<InvalidDataException>(() => ZoomaticReader.FromBytes(truncated));
  }

  private static byte[] _BuildValidFile(ushort loadAddress, byte backgroundColor)
    => _Pack(_BuildScreen(backgroundColor), loadAddress);

  /// <summary>The depacked screen: bitmap, video matrix, colour RAM, then the background register.</summary>
  private static byte[] _BuildScreen(byte backgroundColor) {
    var screen = new byte[10001];
    for (var i = 0; i < 8000; ++i)
      screen[i] = (byte)(i % 256);

    for (var i = 0; i < 1000; ++i)
      screen[8000 + i] = (byte)(i % 16);

    for (var i = 0; i < 1000; ++i)
      screen[9000 + i] = (byte)((i + 3) % 16);

    screen[10000] = backgroundColor;
    return screen;
  }

  /// <summary>
  /// Packs a screen the way a Zoomatic file carries one, written out here rather than called from
  /// the library so that the reader is held to the format and not to its own writer.
  /// </summary>
  /// <remarks>
  /// The depacker starts at the last byte of the file, which states the escape, and walks down
  /// towards the front while filling the screen from its last byte downwards. Everything before the
  /// load address is therefore laid out back to front.
  /// </remarks>
  private static byte[] _Pack(byte[] screen, ushort loadAddress) {
    const byte escape = 0xFE;
    var body = new List<byte>();

    for (var at = screen.Length - 1; at >= 0;) {
      var value = screen[at];
      var run = 1;
      while (run < 256 && at - run >= 0 && screen[at - run] == value)
        ++run;

      if (run > 3 || value == escape) {
        body.Add(escape);
        body.Add((byte)(run & 0xFF));
        body.Add(value);
      } else {
        for (var i = 0; i < run; ++i)
          body.Add(value);
      }

      at -= run;
    }

    body.Reverse();

    var file = new byte[2 + body.Count + 1];
    file[0] = (byte)(loadAddress & 0xFF);
    file[1] = (byte)(loadAddress >> 8);
    body.CopyTo(file, 2);
    file[^1] = escape;
    return file;
  }
}
