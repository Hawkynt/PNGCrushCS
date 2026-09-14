using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Excel;
using FileFormat.Fpx;

namespace FileFormat.Excel.Tests;

[TestFixture]
public sealed class ExcelBinaryFileTests {

  [TestCase(".xls")]
  [TestCase(".xlt")]
  [Category("Integration")]
  public void Writer_CreatesCompoundBiffWorkbookAndRoundTrips(string extension) {
    var source = _Picture(19, 11);
    var bytes = ExcelWriter.ToBytes(ExcelFile.FromRawImage(source, extension));

    Assert.That(CompoundFile.HasSignature(bytes), Is.True);
    var compound = new CompoundFile(bytes);
    var workbookEntry = compound.Streams().Single(pair => pair.Key.Equals("/Workbook", StringComparison.OrdinalIgnoreCase));
    var workbook = compound.Read(workbookEntry.Value);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(workbook), Is.EqualTo(0x0809), "globals BOF");
      Assert.That(_ContainsRecord(workbook, 0x0085), Is.True, "BoundSheet8");
      Assert.That(_ContainsRecord(workbook, 0x00E9), Is.True, "BkHim");
    });

    _AssertSame(source, ExcelFile.ToRawImage(ExcelReader.FromSpan(bytes)));
  }

  [Test]
  [Category("Integration")]
  public void Writer_SplitsLargeBackgroundAcrossContinueRecords() {
    var source = _Picture(80, 80);
    var bytes = ExcelWriter.ToBytes(ExcelFile.FromRawImage(source, ".xls"));
    var compound = new CompoundFile(bytes);
    var workbookEntry = compound.Streams().Single(pair => pair.Key.Equals("/Workbook", StringComparison.OrdinalIgnoreCase));
    var workbook = compound.Read(workbookEntry.Value);

    Assert.That(_ContainsRecord(workbook, 0x003C), Is.True, "Continue");
    _AssertSame(source, ExcelFile.ToRawImage(ExcelReader.FromSpan(bytes)));
  }

  [Test]
  [Category("Unit")]
  public void Reader_RejectsCompoundFileWithoutWorkbookStream() {
    var compound = new CompoundFileWriter(Guid.Empty);
    compound.AddStream(0, "Not Excel", [1, 2, 3]);

    var error = Assert.Throws<InvalidDataException>(() => ExcelReader.FromSpan(compound.Build()));
    Assert.That(error!.Message, Does.Contain("Workbook"));
  }

  private static bool _ContainsRecord(ReadOnlySpan<byte> workbook, ushort wanted) {
    for (var at = 0; at + 4 <= workbook.Length;) {
      var type = BinaryPrimitives.ReadUInt16LittleEndian(workbook[at..]);
      var length = BinaryPrimitives.ReadUInt16LittleEndian(workbook[(at + 2)..]);
      var next = at + 4 + length;
      if (next > workbook.Length)
        return false;
      if (type == wanted)
        return true;
      at = next;
    }
    return false;
  }

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[checked(width * height * 3)];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 29 + y * 17);
      pixels[at + 1] = (byte)(x * 7 + y * 41);
      pixels[at + 2] = (byte)(x * 31 + y * 13);
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static void _AssertSame(RawImage expected, RawImage actual) {
    Assert.Multiple(() => {
      Assert.That((actual.Width, actual.Height), Is.EqualTo((expected.Width, expected.Height)));
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }
}
