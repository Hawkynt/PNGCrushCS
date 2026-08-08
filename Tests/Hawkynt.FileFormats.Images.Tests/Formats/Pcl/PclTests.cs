using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Pcl;

namespace FileFormat.Pcl.Tests;

/// <summary>
/// No print job was available to check against, so every fixture is built byte by byte from the
/// escape sequences and compression rules of HP's PCL 5 Technical Reference and the PCL
/// Implementor's Guide.
/// </summary>
[TestFixture]
public sealed class PclTests {

  /// <summary>How the escape a command opens with is written in these fixtures.</summary>
  private const string _E = "\u001B";

  /// <summary>Assembles a job out of command text and raw data, nested however deeply.</summary>
  private static byte[] _Job(params object[] parts) {
    var bytes = new List<byte>();
    _Append(bytes, parts);

    return bytes.ToArray();
  }

  private static void _Append(List<byte> bytes, IEnumerable<object> parts) {
    foreach (var part in parts)
      switch (part) {
        case string text:
          bytes.AddRange(Encoding.Latin1.GetBytes(text));
          break;

        case byte[] raw:
          bytes.AddRange(raw);
          break;

        case IEnumerable<object> nested:
          _Append(bytes, nested);
          break;

        default:
          throw new ArgumentException("A job is command text and raw bytes.", nameof(parts));
      }
  }

  /// <summary>One row transferred with whatever the current compression method is.</summary>
  private static object[] _Row(params byte[] data) => [$"{_E}*b{data.Length}W", data];

  private static PclFile _Read(byte[] job) => PclReader.FromBytes(job);

  private static RawImage _Draw(byte[] job) => PclFile.ToRawImage(_Read(job));

  /// <summary>The pixels of one row, as palette indices.</summary>
  private static byte[] _RowOf(RawImage image, int y) => image.PixelData[(y * image.Width)..((y + 1) * image.Width)];

  /// <summary>A monochrome job: reset, resolution, start, whatever is given, end, reset.</summary>
  private static byte[] _Simple(params object[] middle)
    => _Job($"{_E}E{_E}*t150R{_E}*r0A", middle, $"{_E}*rC{_E}E");

  private static byte[] _Ones(int count) => Enumerable.Repeat((byte)1, count).ToArray();

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PclReader.FromBytes(null!));

  /// <summary>A job that prints only text carries no picture, and saying so beats a blank page.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AJobWithNoRasterInItIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Job($"{_E}E{_E}&l0OHello, printer.{_E}E")));

  [Test]
  [Category("Unit")]
  public void FromBytes_UnencodedRowsAreEightPixelsAByte() {
    var image = _Draw(_Simple(_Row(0b1010_0000, 0x00), _Row(0x00, 0b0000_0001)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(16));
      Assert.That(image.Height, Is.EqualTo(2));
      Assert.That(_RowOf(image, 0)[..4], Is.EqualTo(new byte[] { 1, 0, 1, 0 }), "the most significant bit is the leftmost pixel");
      Assert.That(_RowOf(image, 1)[15], Is.EqualTo(1));
    });
  }

  /// <summary>A set bit is a dot on the paper, so index one is the black of a two-entry palette.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_ASetBitIsInk() {
    var image = _Draw(_Simple(_Row(0xFF)));

    Assert.Multiple(() => {
      Assert.That(image.PaletteCount, Is.EqualTo(2));
      Assert.That(image.Palette![..3], Is.EqualTo(new byte[] { 255, 255, 255 }));
      Assert.That(image.Palette![3..6], Is.EqualTo(new byte[] { 0, 0, 0 }));
      Assert.That(_RowOf(image, 0), Is.EqualTo(_Ones(8)));
    });
  }

  /// <summary>
  /// The job states the raster's width and height, and the printer pads a row that arrives short.
  /// A reader that took only the row it was given would make a picture eight pixels wide.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_TheStatedWidthAndHeightAreThePictureSize() {
    var image = _Draw(_Job($"{_E}E{_E}*r24S{_E}*r3T{_E}*r0A", _Row(0xFF), $"{_E}*rC{_E}E"));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(24));
      Assert.That(image.Height, Is.EqualTo(3));
      Assert.That(_RowOf(image, 0)[8], Is.EqualTo(0), "what the row did not carry is blank");
    });
  }

  /// <summary>Run-length pairs count repetitions less one, so a count of three is four bytes.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_RunLengthCountsRepetitionsLessOne() {
    var image = _Draw(_Simple($"{_E}*b1M", _Row(3, 0xFF)));

    Assert.That(image.Width, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ARunLengthRowWithAnOddNumberOfBytesIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Simple($"{_E}*b1M", _Row(3, 0xFF, 2))));

  /// <summary>
  /// Method two is the TIFF rule: a control byte from nought to a hundred and twenty-seven takes
  /// that many plus one bytes as they are, and a negative one repeats the byte after it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_TheTiffRulePacksLiteralsAndRepeatsDifferently() {
    var image = _Draw(_Simple($"{_E}*b2M", _Row(1, 0xAA, 0xBB, 0xFD, 0xFF)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(48), "two bytes taken as they are, then a repeat of four");
      Assert.That(_RowOf(image, 0)[16..48], Is.EqualTo(_Ones(32)));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ATiffRowAskingForMoreLiteralsThanItHasIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Simple($"{_E}*b2M", _Row(7, 0xAA))));

  /// <summary>
  /// A delta row says only what changed. Its command byte holds the number of replacement bytes
  /// less one in the top three bits and the offset in the bottom five.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ADeltaRowReplacesPartOfTheRowBefore() {
    var image = _Draw(_Simple(
      _Row(0x00, 0x00, 0x00, 0x00),
      $"{_E}*b3M",
      _Row(0b001_00010, 0xFF, 0xFF)
    ));

    Assert.Multiple(() => {
      Assert.That(image.Height, Is.EqualTo(2));
      Assert.That(_RowOf(image, 1)[..16], Is.EqualTo(new byte[16]), "the first two bytes were not replaced");
      Assert.That(_RowOf(image, 1)[16..32], Is.EqualTo(_Ones(16)), "and the next two were");
    });
  }

  /// <summary>
  /// An offset of thirty-one means the offset carries on into the bytes after the command: every
  /// 255 adds 255 and the first byte below it adds itself and ends the run.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AnOffsetOfThirtyOneCarriesOnIntoTheNextByte() {
    var image = _Draw(_Simple(
      _Row(new byte[40]),
      $"{_E}*b3M",
      _Row(0b000_11111, 5, 0xFF)
    ));

    Assert.Multiple(() => {
      Assert.That(_RowOf(image, 1)[36 * 8], Is.EqualTo(1), "thirty-one and five is byte thirty-six");
      Assert.That(_RowOf(image, 1)[35 * 8], Is.EqualTo(0));
    });
  }

  /// <summary>A delta row that writes past the row it is changing came from a wider picture.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ADeltaRowRunningPastTheRowIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Simple(
      _Row(0x00, 0x00),
      $"{_E}*b3M",
      _Row(0b111_00001, 1, 2, 3, 4, 5, 6, 7, 8)
    )));

  /// <summary>Method five sends a whole block, each row prefixed by its own method and a count.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AnAdaptiveBlockCarriesItsRowsMethodByMethod() {
    byte[] block = [
      0, 0, 2, 0xF0, 0x0F,
      4, 0, 2,
      5, 0, 1
    ];

    var image = _Draw(_Simple($"{_E}*b5M", $"{_E}*b{block.Length}W", block));

    Assert.Multiple(() => {
      Assert.That(image.Height, Is.EqualTo(4), "one row, two empty, one repeat");
      Assert.That(_RowOf(image, 1), Is.EqualTo(new byte[16]), "the empty rows print nothing");
      Assert.That(_RowOf(image, 3), Is.EqualTo(_RowOf(image, 0)), "and the last is the first again");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AnAdaptiveBlockCutShortOfItsControlBytesIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Simple($"{_E}*b5M", _Row(0, 0))));

  [Test]
  [Category("Unit")]
  public void FromBytes_ACompressionMethodThisDoesNotDecodeIsRefusedRatherThanGuessedAt()
    => Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => _Read(_Simple($"{_E}*b4M", _Row(0, 0, 0, 4))));
      Assert.Throws<InvalidDataException>(() => _Read(_Simple($"{_E}*b9M", _Row(0, 0))));
      Assert.Throws<InvalidDataException>(() => _Read(_Simple($"{_E}*b8M", _Row(0, 0))));
      Assert.Throws<InvalidDataException>(() => _Read(_Simple($"{_E}*b12M", _Row(0, 0))));
    });

  /// <summary>
  /// Configuring the image data builds a palette out of commands this reader does not read, so a
  /// picture drawn from it would be the right shape in invented colours.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AJobThatConfiguresItsOwnImageDataIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Job(
      $"{_E}E{_E}*v6W", new byte[] { 0, 1, 3, 8, 8, 8 }, $"{_E}*r0A", _Row(0xFF), $"{_E}*rC{_E}E"
    )));

  [Test]
  [Category("Unit")]
  public void FromBytes_ARowTransferOutsideARasterIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Job($"{_E}E", _Row(0xFF), $"{_E}E")));

  /// <summary>
  /// A transfer states how many bytes follow it. One that states more than the job has left has
  /// been cut, and reading what is there would show part of a page as the whole of it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ATransferStatingMoreBytesThanTheJobHasIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Job($"{_E}E{_E}*r0A{_E}*b64W", new byte[] { 1, 2, 3 })));

  /// <summary>
  /// A downloaded font is bytes that follow a command, not commands. A reader that stepped through
  /// them would find escapes inside the glyph data and take the font for a page.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ADownloadedFontIsSteppedOverRatherThanReadAsCommands() {
    var glyphs = _Job($"{_E}*r0A", _Row(0xFF, 0xFF), $"{_E}*rC");
    var image = _Draw(_Job($"{_E}E{_E})s{glyphs.Length}W", glyphs, $"{_E}*r0A", _Row(0b1111_0000), $"{_E}*rC{_E}E"));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(8), "the font's bytes were not read as a picture");
      Assert.That(_RowOf(image, 0), Is.EqualTo(new byte[] { 1, 1, 1, 1, 0, 0, 0, 0 }));
    });
  }

  /// <summary>
  /// A lower-case terminator carries the same two characters on to the next command, so
  /// <c>ESC*b1m2W</c> is a compression method and a transfer in one sequence.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ACombinedSequenceIsTwoCommands() {
    var image = _Draw(_Job($"{_E}E{_E}*r0A{_E}*b1m2W", new byte[] { 3, 0xFF }, $"{_E}*rC{_E}E"));

    Assert.That(image.Width, Is.EqualTo(32), "the run-length method was set by the same sequence");
  }

  /// <summary>Moving down the page without printing leaves blank rows behind.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ARasterYOffsetLeavesBlankRows() {
    var image = _Draw(_Simple(_Row(0xFF), $"{_E}*b2Y", _Row(0xFF)));

    Assert.Multiple(() => {
      Assert.That(image.Height, Is.EqualTo(4));
      Assert.That(_RowOf(image, 1), Is.EqualTo(new byte[8]));
      Assert.That(_RowOf(image, 3), Is.EqualTo(_Ones(8)));
    });
  }

  /// <summary>
  /// Simple colour mode three sends three planes a row, and the index they make is the device RGB
  /// palette in which one is red and seven is white.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_SimpleColourSendsAPlaneAtATime() {
    var image = _Draw(_Job(
      $"{_E}E{_E}*r3U{_E}*r0A",
      $"{_E}*b1V", new byte[] { 0b1100_0000 },
      $"{_E}*b1V", new byte[] { 0b1010_0000 },
      $"{_E}*b1W", new byte[] { 0b1001_0000 },
      $"{_E}*rC{_E}E"
    ));

    Assert.Multiple(() => {
      Assert.That(image.PaletteCount, Is.EqualTo(8));
      Assert.That(_RowOf(image, 0)[..4], Is.EqualTo(new byte[] { 7, 1, 2, 4 }), "plane one is the least significant bit");
      Assert.That(image.Palette![3..6], Is.EqualTo(new byte[] { 255, 0, 0 }), "index one is red");
      Assert.That(image.Palette![6..9], Is.EqualTo(new byte[] { 0, 255, 0 }), "index two is green");
      Assert.That(image.Palette![12..15], Is.EqualTo(new byte[] { 0, 0, 255 }), "index four is blue");
    });
  }

  /// <summary>
  /// The four-plane KCMY mode is described second-hand and not in HP's own colour manual, so it is
  /// refused rather than assumed to work like the three-plane ones.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AColourModeHpDoesNotDocumentIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Job($"{_E}E{_E}*r-4U{_E}*r0A", _Row(0xFF), $"{_E}*rC{_E}E")));

  [Test]
  [Category("Unit")]
  public void FromBytes_ARowSentInFewerPlanesThanTheColourModeUsesIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Job($"{_E}E{_E}*r3U{_E}*r0A", _Row(0xFF), $"{_E}*rC{_E}E")));
}
