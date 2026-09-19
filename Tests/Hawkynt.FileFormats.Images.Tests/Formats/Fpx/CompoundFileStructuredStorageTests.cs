using System;
using System.Linq;
using FileFormat.Core;
using FileFormat.Excel;
using FileFormat.Fpx;
using FileFormat.Fpx.Tests;
using FileFormat.PowerPoint;
using FileFormat.Word;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Hands the containers this package writes to the structured-storage implementation Windows ships,
/// and makes it say what is in them.
/// </summary>
/// <remarks>
/// Every other test of a compound file here reads it back with <see cref="CompoundFile"/>, the
/// reader that sits in the same folder as the writer. That pairing cannot see a defect the two
/// share, and a container is exactly where such a defect hides: a chain threaded one entry short, a
/// sibling tree that is ordered by the wrong comparison, a starting sector counted from the wrong
/// origin — a reader written from the same understanding follows all of them back out again and
/// agrees.
/// <para/>
/// <c>ole32.dll</c> did not come from here. It is Microsoft's own implementation of [MS-CFB], it is
/// what the applications these files are written for sit on top of, and it is on every Windows
/// machine, so this needs no download and no install to have an outside opinion. It parses the
/// header, the DIFAT, the FAT, the MiniFAT and the red/black directory tree, and it refuses what
/// does not hold together.
/// <para/>
/// The claim these tests support, and the only one, is that the container is a compound file. That
/// a legacy Word, Excel or PowerPoint file this package writes actually opens in the application it
/// targets is a claim about what is <em>inside</em> those streams — the FIB, the BIFF record
/// stream, the persisted object graph — and structured storage has no opinion about any of it.
/// Nothing in this repository has asked an office application yet, which is why those formats say
/// <c>none</c> in the Oracle column and not a tool name.
/// </remarks>
[TestFixture]
public sealed class CompoundFileStructuredStorageTests {

  /// <summary>
  /// Leaves the machine without structured storage before anything here runs.
  /// </summary>
  /// <remarks>
  /// The oracle can skip from inside itself, but <see cref="Assert.Ignore(string)"/> is a failure
  /// rather than a skip once an <see cref="Assert.Multiple(TestDelegate)"/> block has been entered —
  /// NUnit will not let a multiple assertion be abandoned halfway. Deciding here, before any test
  /// body starts, keeps that shape out of every one of them.
  /// </remarks>
  [SetUp]
  public void SkipWithoutStructuredStorage() {
    if (!StructuredStorageOracle.IsAvailable)
      Assert.Ignore("no structured storage on this machine to ask");
  }

  private static readonly Guid _Word8ClassId = new("00020906-0000-0000-C000-000000000046");
  private static readonly Guid _Excel8ClassId = new("00020820-0000-0000-C000-000000000046");
  private static readonly Guid _PowerPoint8ClassId = new("64818D10-4F9B-11CF-86EA-00AA00B929E8");

  /// <summary>
  /// The sizes where the container changes shape, each handed to the outside implementation rather
  /// than only to the reader beside the writer.
  /// </summary>
  [TestCase(0, TestName = "StructuredStorage_ReadsBackAStreamOf_0_Bytes")]
  [TestCase(1, TestName = "StructuredStorage_ReadsBackAStreamOf_1_Byte")]
  [TestCase(64, TestName = "StructuredStorage_ReadsBackAStreamOf_64_Bytes_OneMiniSector")]
  [TestCase(65, TestName = "StructuredStorage_ReadsBackAStreamOf_65_Bytes_TwoMiniSectors")]
  [TestCase(4095, TestName = "StructuredStorage_ReadsBackAStreamOf_4095_Bytes_BelowTheCutoff")]
  [TestCase(4096, TestName = "StructuredStorage_ReadsBackAStreamOf_4096_Bytes_AtTheCutoff")]
  [TestCase(4097, TestName = "StructuredStorage_ReadsBackAStreamOf_4097_Bytes_AboveTheCutoff")]
  [TestCase(307200, TestName = "StructuredStorage_ReadsBackAStreamOf_307200_Bytes_ManyFatSectors")]
  [Category("Integration")]
  public void StructuredStorage_ReadsBackEveryStreamSizeExactly(int length) {
    var payload = CompoundFileWriterTests.Pattern(length);
    var container = CompoundFileWriterTests.Container(("Payload", payload));

    Assert.That(StructuredStorageOracle.ReadStreamOrIgnore(container, "Payload"), Is.EqualTo(payload));
  }

  /// <summary>
  /// Past 109 FAT sectors the header cannot list them all and the DIFAT chain begins. This is the
  /// only test that makes anything outside this package walk that chain.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void StructuredStorage_ReadsBackAStreamLargeEnoughToNeedDifatSectors() {
    var payload = CompoundFileWriterTests.Pattern(8 * 1024 * 1024);
    var container = CompoundFileWriterTests.Container(("Huge", payload));

    Assert.That(StructuredStorageOracle.ReadStreamOrIgnore(container, "Huge"), Is.EqualTo(payload));
  }

  /// <summary>
  /// A directory is a red/black tree of siblings, and a tree that is built wrong still finds every
  /// entry by walking all of it — which is what this package's own reader does. Structured storage
  /// searches it, so a name it cannot reach is a name it does not have.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void StructuredStorage_FindsEveryNameInADirectoryLargeEnoughToBeATree() {
    var streams = Enumerable.Range(0, 40).Select(i => ($"Stream{i:D2}", CompoundFileWriterTests.Pattern(i * 7 + 1))).ToArray();
    var container = CompoundFileWriterTests.Container(streams);

    var found = StructuredStorageOracle.RootElementsOrIgnore(container);

    Assert.Multiple(() => {
      Assert.That(found.Select(element => element.Name).OrderBy(name => name, StringComparer.Ordinal),
        Is.EqualTo(streams.Select(pair => pair.Item1).OrderBy(name => name, StringComparer.Ordinal)));
      foreach (var (name, data) in streams)
        Assert.That(StructuredStorageOracle.ReadStreamOrIgnore(container, name), Is.EqualTo(data), name);
    });
  }

  /// <summary>
  /// Names sort by length before they sort alphabetically, which is the ordering a sibling tree is
  /// searched with and not the one any ordinary comparer gives.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void StructuredStorage_FindsNamesWhoseLengthAndAlphabeticalOrderDisagree() {
    var streams = new (string Name, byte[] Data)[] {
      ("zz", CompoundFileWriterTests.Pattern(3)),
      ("a", CompoundFileWriterTests.Pattern(4)),
      ("aaaa", CompoundFileWriterTests.Pattern(5)),
      ("B", CompoundFileWriterTests.Pattern(6)),
      ("bbb", CompoundFileWriterTests.Pattern(7)),
      ("Zzz", CompoundFileWriterTests.Pattern(8)),
    };
    var container = CompoundFileWriterTests.Container(streams);

    Assert.Multiple(() => {
      foreach (var (name, data) in streams)
        Assert.That(StructuredStorageOracle.ReadStreamOrIgnore(container, name), Is.EqualTo(data), name);
    });
  }

  [Test]
  [Category("Integration")]
  public void StructuredStorage_OpensWhatTheFlashPixWriterProduces() {
    var written = FpxWriter.ToBytes(new() { Width = 65, Height = 65, PixelData = _Pixels(65, 65) });

    Assert.That(StructuredStorageOracle.RootElementsOrIgnore(written), Is.Not.Empty);
  }

  [TestCase(".doc")]
  [TestCase(".dot")]
  [Category("Integration")]
  public void StructuredStorage_OpensWhatTheLegacyWordWriterProduces(string extension) {
    var written = WordWriter.ToBytes(WordFile.FromRawImage(_Picture(19, 11), extension));

    _AssertStreamsAgreeWithOurReader(written, "WordDocument", "1Table", "Data");
  }

  [TestCase(".xls")]
  [TestCase(".xlt")]
  [Category("Integration")]
  public void StructuredStorage_OpensWhatTheLegacyExcelWriterProduces(string extension) {
    var written = ExcelWriter.ToBytes(ExcelFile.FromRawImage(_Picture(80, 80), extension));

    _AssertStreamsAgreeWithOurReader(written, "Workbook");
  }

  [TestCase(".ppt")]
  [TestCase(".pps")]
  [TestCase(".pot")]
  [Category("Integration")]
  public void StructuredStorage_OpensWhatTheLegacyPowerPointWriterProduces(string extension) {
    var written = _WriteLegacyPowerPoint(_Picture(19, 11), extension);

    _AssertStreamsAgreeWithOurReader(written, "PowerPoint Document", "Current User", "Pictures");
  }

  /// <summary>
  /// The root CLSID is how a shell decides which program owns the file, and it is the one field of
  /// the container an office application reads before it reads anything of its own.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void StructuredStorage_ReportsTheClassEachLegacyOfficeWriterDeclares() {
    var word = WordWriter.ToBytes(WordFile.FromRawImage(_Picture(9, 5), ".doc"));
    var excel = ExcelWriter.ToBytes(ExcelFile.FromRawImage(_Picture(9, 5), ".xls"));
    var powerPoint = _WriteLegacyPowerPoint(_Picture(9, 5), ".ppt");

    Assert.Multiple(() => {
      Assert.That(StructuredStorageOracle.RootClassIdOrIgnore(word), Is.EqualTo(_Word8ClassId));
      Assert.That(StructuredStorageOracle.RootClassIdOrIgnore(excel), Is.EqualTo(_Excel8ClassId));
      Assert.That(StructuredStorageOracle.RootClassIdOrIgnore(powerPoint), Is.EqualTo(_PowerPoint8ClassId));
    });
  }

  /// <summary>
  /// Asks the outside implementation for the named streams and holds every byte it returns against
  /// what this package's own reader makes of the same file.
  /// </summary>
  private static void _AssertStreamsAgreeWithOurReader(byte[] written, params string[] expected) {
    var ours = new CompoundFile(written);
    var found = StructuredStorageOracle.RootElementsOrIgnore(written);

    Assert.Multiple(() => {
      Assert.That(found.Select(element => element.Name).OrderBy(name => name, StringComparer.Ordinal),
        Is.EqualTo(expected.OrderBy(name => name, StringComparer.Ordinal)));

      foreach (var name in expected) {
        var entry = ours.Streams().Single(pair => pair.Key.Equals("/" + name, StringComparison.OrdinalIgnoreCase)).Value;
        Assert.That(StructuredStorageOracle.ReadStreamOrIgnore(written, name), Is.EqualTo(ours.Read(entry)), name);
      }
    });
  }

  /// <summary>
  /// The complete binary presentation is reached through the image contract, which is where the
  /// legacy extensions are dispatched; <see cref="PowerPointWriter"/> writes the older carrier.
  /// </summary>
  private static byte[] _WriteLegacyPowerPoint(RawImage picture, string extension)
    => _ViaContract<PowerPointFile>(PowerPointFile.FromRawImage(picture, extension));

  private static byte[] _ViaContract<T>(T file)
    where T : IImageFormatWriter<T>
    => T.ToBytes(file);

  private static RawImage _Picture(int width, int height)
    => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = _Pixels(width, height) };

  private static byte[] _Pixels(int width, int height) {
    var result = new byte[checked(width * height * 3)];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      result[at] = (byte)(x * 3 + y);
      result[at + 1] = (byte)(x + y * 5);
      result[at + 2] = (byte)(x * 7 + y * 11);
    }

    return result;
  }
}
