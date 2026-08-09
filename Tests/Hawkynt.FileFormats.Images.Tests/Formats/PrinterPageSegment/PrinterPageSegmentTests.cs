using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PrinterPageSegment.Tests;

/// <summary>
/// IBM printer page segments: MO:DCA structured fields carrying an IM1 image.
/// </summary>
/// <remarks>
/// Every case below was built as a file first and handed to XnView's own converter, and what is
/// asserted is what its <c>-out pnm</c> returned for it. The pictures are deliberately asymmetric
/// both ways round so that a mirror, a flip or a transpose could not pass; the two-cell case checks
/// the one thing a mosaic format can get wrong and nothing else can, which is where the second piece
/// lands.
/// </remarks>
[TestFixture]
public sealed class PrinterPageSegmentTests {

  private const int _IMAGE_INPUT_DESCRIPTOR = 0xD3A67B;
  private const int _IMAGE_CELL_POSITION = 0xD3AC7B;
  private const int _IMAGE_PICTURE_DATA = 0xD3EE7B;
  private const int _END_OF_IMAGE = 0xD3A97B;
  private const int _BEGIN_PAGE_SEGMENT = 0xD3A85F;

  /// <summary>The introducer, a length covering the eight bytes behind it, a type, a flag, a sequence.</summary>
  private static byte[] _Field(int type, params byte[] payload) {
    var field = new List<byte> {
      0x5A,
      (byte)((payload.Length + 8) >> 8),
      (byte)(payload.Length + 8),
      (byte)(type >> 16),
      (byte)(type >> 8),
      (byte)type,
      0, 0, 0,
    };
    field.AddRange(payload);
    return field.ToArray();
  }

  private static byte[] _Descriptor(int width, int height, int cellWidth, int behind = 0) {
    var payload = new byte[36];
    payload[18] = (byte)(width >> 8);
    payload[19] = (byte)width;
    payload[20] = (byte)(height >> 8);
    payload[21] = (byte)height;
    payload[28] = (byte)(cellWidth >> 8);
    payload[29] = (byte)cellWidth;
    payload[30] = (byte)(behind >> 8);
    payload[31] = (byte)behind;
    return _Field(_IMAGE_INPUT_DESCRIPTOR, payload);
  }

  private static byte[] _Cell(int x, int y, int cellWidth, int fillWidth = 0xFFFF, int fillHeight = 0xFFFF)
    => _Field(_IMAGE_CELL_POSITION, [
      (byte)(x >> 8), (byte)x,
      (byte)(y >> 8), (byte)y,
      (byte)(cellWidth >> 8), (byte)cellWidth,
      0, 0,
      (byte)(fillWidth >> 8), (byte)fillWidth,
      (byte)(fillHeight >> 8), (byte)fillHeight,
    ]);

  private static byte[] _Join(params byte[][] parts) {
    var all = new List<byte>();
    foreach (var part in parts)
      all.AddRange(part);

    return all.ToArray();
  }

  [Test]
  [Category("Integration")]
  public void Read_ReturnsTheRowsTheConverterReturnsForTheSameFile() {
    // Twenty-four by five, and no two rows alike.
    byte[] rows = [0xFF, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x18, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x03];
    var file = PrinterPageSegmentReader.FromBytes(_Join(
      _Field(_BEGIN_PAGE_SEGMENT),
      _Descriptor(24, 5, 24),
      _Cell(0, 0, 24),
      _Field(_IMAGE_PICTURE_DATA, rows),
      _Field(_END_OF_IMAGE)));

    var image = PrinterPageSegmentFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(24));
      Assert.That(image.Height, Is.EqualTo(5));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed1));
      Assert.That(image.PixelData, Is.EqualTo(rows));
      Assert.That(image.Palette, Is.EqualTo(new byte[] { 255, 255, 255, 0, 0, 0 }), "a set bit is ink");
    });
  }

  [Test]
  [Category("Integration")]
  public void Read_PutsTheSecondCellWhereItsPositionSaysAndNotOverTheFirst() {
    // Thirty-two wide out of two sixteen-wide cells. A reader ignoring the position would draw the
    // second on top of the first and lose half the picture.
    byte[] left = [0xF0, 0x00, 0x0F, 0x00, 0x00, 0xFF, 0xAA, 0x55];
    byte[] right = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
    var file = PrinterPageSegmentReader.FromBytes(_Join(
      _Descriptor(32, 4, 16),
      _Cell(0, 0, 16), _Field(_IMAGE_PICTURE_DATA, left),
      _Cell(16, 0, 16), _Field(_IMAGE_PICTURE_DATA, right),
      _Field(_END_OF_IMAGE)));

    Assert.That(file.PixelData, Is.EqualTo(new byte[] {
      0xF0, 0x00, 0x01, 0x02,
      0x0F, 0x00, 0x03, 0x04,
      0x00, 0xFF, 0x05, 0x06,
      0xAA, 0x55, 0x07, 0x08,
    }));
  }

  [Test]
  [Category("Integration")]
  public void Read_ClearsOneRowFewerThanTheFillRectangleAsks() {
    // The fill asks for sixty-four rows from row two of a four-row picture and clears exactly one.
    // The off-by-one is the converter's; a reader that "corrected" it would rub out the bottom row.
    var file = PrinterPageSegmentReader.FromBytes(_Join(
      _Descriptor(16, 4, 16),
      _Cell(0, 0, 16), _Field(_IMAGE_PICTURE_DATA, [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]),
      _Cell(0, 2, 16, 64, 64),
      _Field(_END_OF_IMAGE)));

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF }));
  }

  [Test]
  [Category("Integration")]
  public void Read_LeavesTheRectangleAloneWhenEitherSideIsTheSkipValue() {
    var file = PrinterPageSegmentReader.FromBytes(_Join(
      _Descriptor(16, 4, 16),
      _Cell(0, 0, 16), _Field(_IMAGE_PICTURE_DATA, [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]),
      _Cell(0, 2, 16, 0xFFFF, 64),
      _Field(_END_OF_IMAGE)));

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }));
  }

  [Test]
  [Category("Integration")]
  public void Read_TakesTheCellAsWideAsThePictureWhenTheDescriptorLeavesItEmpty() {
    var file = PrinterPageSegmentReader.FromBytes(_Join(
      _Descriptor(16, 4, 0),
      _Cell(0, 0, 16), _Field(_IMAGE_PICTURE_DATA, [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]),
      _Field(_END_OF_IMAGE)));

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }));
  }

  [Test]
  [Category("Integration")]
  public void Read_NeedsNoCellPositionAtAll() {
    byte[] rows = [0xFF, 0x00, 0x80, 0x01];
    var file = PrinterPageSegmentReader.FromBytes(_Join(
      _Descriptor(16, 2, 16),
      _Field(_IMAGE_PICTURE_DATA, rows),
      _Field(_END_OF_IMAGE)));

    Assert.That(file.PixelData, Is.EqualTo(rows));
  }

  [Test]
  [Category("Integration")]
  public void Read_FinishesOnTheLastRowWhenDataIsStillInHandAndNoMarkerFollows() {
    byte[] rows = [0xFF, 0x00, 0x80, 0x01];
    var file = PrinterPageSegmentReader.FromBytes(_Join(
      _Descriptor(16, 2, 16),
      _Field(_IMAGE_PICTURE_DATA, [0xFF, 0x00, 0x80, 0x01, 0xAA, 0xBB])));

    Assert.That(file.PixelData, Is.EqualTo(rows));
  }

  /// <summary>
  /// The same file one row shorter is refused, which is not a distinction anyone would invent.
  /// </summary>
  /// <remarks>
  /// Running out of data exactly at the bottom row does not end the read — the loader goes looking
  /// for the next field and falls off the end of the file. Built both ways and handed over, the file
  /// with the spare row is read and this one is not.
  /// </remarks>
  [Test]
  [Category("Integration")]
  public void Read_RefusesASegmentWhoseDataStopsExactlyAtTheBottomRowWithNoMarker()
    => Assert.Throws<InvalidDataException>(() => PrinterPageSegmentReader.FromBytes(_Join(
      _Descriptor(16, 2, 16),
      _Field(_IMAGE_PICTURE_DATA, [0xFF, 0x00, 0x80, 0x01]))));

  /// <summary>
  /// An IOCA page segment is refused, which is why the IOCA reader here never closed this name.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void Read_RefusesTheOtherImageArchitectureUnderTheSameFileFamily()
    => Assert.Throws<InvalidDataException>(() => PrinterPageSegmentReader.FromBytes(_Join(
      _Field(0xD3A88C, 0x00, 0x00),
      _Descriptor(16, 2, 16),
      _Field(_IMAGE_PICTURE_DATA, [0xFF, 0xFF, 0x00, 0x00]),
      _Field(_END_OF_IMAGE))));

  [Test]
  [Category("Integration")]
  public void Read_RefusesAPictureThatStartsBeforeAnythingStatesItsSize()
    => Assert.Throws<InvalidDataException>(() => PrinterPageSegmentReader.FromBytes(_Join(
      _Cell(0, 0, 16),
      _Field(_IMAGE_PICTURE_DATA, [0xFF, 0xFF]),
      _Field(_END_OF_IMAGE))));

  [Test]
  [Category("Integration")]
  public void Read_RefusesAWidthThatIsNotAWholeNumberOfBytes()
    => Assert.Throws<InvalidDataException>(() => PrinterPageSegmentReader.FromBytes(_Join(
      _Descriptor(12, 2, 8),
      _Cell(0, 0, 8),
      _Field(_IMAGE_PICTURE_DATA, [0xFF, 0x00]),
      _Field(_END_OF_IMAGE))));

  [Test]
  [Category("Integration")]
  public void Read_RefusesADescriptorWhoseCellWidthIsEmptyButNotAlone()
    => Assert.Throws<InvalidDataException>(() => PrinterPageSegmentReader.FromBytes(_Join(
      _Descriptor(16, 4, 0, behind: 5),
      _Field(_IMAGE_PICTURE_DATA, [0xFF, 0xFF]),
      _Field(_END_OF_IMAGE))));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileNotMadeOfStructuredFields()
    => Assert.Throws<InvalidDataException>(() => PrinterPageSegmentReader.FromBytes(new byte[64]));
}
