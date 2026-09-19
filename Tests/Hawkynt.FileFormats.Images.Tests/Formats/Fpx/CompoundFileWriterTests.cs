using System;
using System.Linq;
using FileFormat.Fpx;

namespace FileFormat.Fpx.Tests;

/// <summary>
/// Holds the Compound File Binary container writer to the sizes where its structure changes shape.
/// </summary>
/// <remarks>
/// One writer carries four formats: FlashPix, and the legacy binary forms of Word, Excel and
/// PowerPoint all hand their streams to <see cref="CompoundFileWriter"/>. Until now every test that
/// touched it went through one of those four and used a picture, so the container was only ever
/// exercised at the two or three stream sizes those pictures happen to produce, and the places
/// where a compound file changes shape were never visited at all.
/// <para/>
/// Those places are sizes, and there are four of them. A stream shorter than the header's 4096-byte
/// cutoff is not stored in the file's own sectors but in a second, 64-byte allocation inside the
/// root entry's stream; at the cutoff exactly it moves to ordinary sectors, and the two sides of
/// that boundary use different tables, different sector sizes and a different starting-sector
/// meaning in the directory entry. Past 128 sectors the FAT itself needs a second sector, and past
/// 109 FAT sectors the header runs out of room to list them and the DIFAT chain starts. Each of
/// those is a distinct arrangement of the same file and is covered here, from both sides where it
/// is a boundary.
/// <para/>
/// What this file cannot prove is that the arrangement is the one [MS-CFB] describes: it reads back
/// with the reader that sits beside the writer, and the two could share one misreading. That is what
/// <see cref="Hawkynt.FileFormats.Images.Tests.CompoundFileStructuredStorageTests"/> is for.
/// </remarks>
[TestFixture]
public sealed class CompoundFileWriterTests {

  private const int _MINI_STREAM_CUTOFF = 4096;
  private const int _MINI_SECTOR_SIZE = 64;
  private const int _SECTOR_SIZE = 512;

  [Test]
  [Category("Unit")]
  public void Build_AlwaysStartsWithTheCompoundFileSignature() {
    var writer = new CompoundFileWriter(Guid.Empty);
    writer.AddStream(0, "Only", [1, 2, 3]);

    Assert.That(CompoundFile.HasSignature(writer.Build()), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void Build_StreamWithNoBytes_IsKeptWithoutAChain() {
    var written = Container(("Empty", []));
    var read = new CompoundFile(written);
    var entry = read.Streams().Single(pair => pair.Key == "/Empty").Value;

    Assert.Multiple(() => {
      Assert.That(entry.Size, Is.Zero);
      Assert.That(read.Read(entry), Is.Empty);
    });
  }

  /// <summary>
  /// The mini sector is 64 bytes, so a stream of one byte and a stream of 64 occupy the same amount
  /// of it and a stream of 65 needs a second one it has to be chained to.
  /// </summary>
  [TestCase(1)]
  [TestCase(_MINI_SECTOR_SIZE - 1)]
  [TestCase(_MINI_SECTOR_SIZE)]
  [TestCase(_MINI_SECTOR_SIZE + 1)]
  [Category("Unit")]
  public void Build_StreamInsideTheMiniStream_RoundTripsAtEveryMiniSectorBoundary(int length) {
    var payload = Pattern(length);

    _AssertRoundTrips(Container(("Small", payload)), ("Small", payload));
  }

  /// <summary>
  /// 4095 and 4096 are the same picture to a caller and two different files: below the header's
  /// cutoff the bytes live in the root entry's mini stream and the directory's starting sector is a
  /// mini-sector number, at the cutoff they live in the file's own 512-byte sectors and it is a
  /// sector number. Getting the comparison the wrong way round puts a stream in one allocation and
  /// reads it out of the other.
  /// </summary>
  [TestCase(_MINI_STREAM_CUTOFF - 1)]
  [TestCase(_MINI_STREAM_CUTOFF)]
  [TestCase(_MINI_STREAM_CUTOFF + 1)]
  [Category("Unit")]
  public void Build_StreamEitherSideOfTheMiniStreamCutoff_RoundTrips(int length) {
    var payload = Pattern(length);

    _AssertRoundTrips(Container(("Boundary", payload)), ("Boundary", payload));
  }

  /// <summary>
  /// A stream of exactly one sector and one of a sector plus a byte differ by whether the chain has
  /// to be threaded at all.
  /// </summary>
  [TestCase(_MINI_STREAM_CUTOFF)]
  [TestCase(_MINI_STREAM_CUTOFF + _SECTOR_SIZE)]
  [TestCase(_MINI_STREAM_CUTOFF + _SECTOR_SIZE + 1)]
  [Category("Unit")]
  public void Build_StreamOfWholeAndPartialSectors_RoundTrips(int length) {
    var payload = Pattern(length);

    _AssertRoundTrips(Container(("Sectors", payload)), ("Sectors", payload));
  }

  /// <summary>
  /// One FAT sector holds 128 entries, so a stream past 64 KiB is the first that needs the
  /// allocation table to span more than one sector of its own.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Build_StreamSpanningSeveralFatSectors_RoundTrips() {
    var payload = Pattern(600 * _SECTOR_SIZE);

    _AssertRoundTrips(Container(("Wide", payload)), ("Wide", payload));
  }

  /// <summary>
  /// The header lists 109 FAT sectors and no more. Past that the list continues in DIFAT sectors
  /// chained through the file, which is a part of the format nothing in this repository had ever
  /// written before this test asked for a stream large enough to need it.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void Build_StreamLargeEnoughToNeedDifatSectors_RoundTrips() {
    // 109 FAT sectors cover 13,952 sectors; 8 MiB needs 16,384 of them.
    var payload = Pattern(8 * 1024 * 1024);

    _AssertRoundTrips(Container(("Huge", payload)), ("Huge", payload));
  }

  /// <summary>
  /// A directory sector holds four entries including the root, so five streams is the first count
  /// that makes the directory a chain rather than a single sector.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Build_MoreStreamsThanOneDirectorySector_RoundTripsEveryOne() {
    var streams = Enumerable.Range(0, 40).Select(i => ($"Stream{i:D2}", Pattern(i * 7 + 1))).ToArray();

    _AssertRoundTrips(Container(streams), streams);
  }

  /// <summary>
  /// The mini stream is itself a chain of ordinary sectors, so enough short streams to fill more
  /// than 512 bytes of it exercises a path a single short stream never reaches.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Build_ManyShortStreams_ChainsTheMiniStreamAcrossSectors() {
    var streams = Enumerable.Range(0, 32).Select(i => ($"Mini{i:D2}", Pattern(_MINI_SECTOR_SIZE))).ToArray();

    _AssertRoundTrips(Container(streams), streams);
  }

  /// <summary>Short and long streams in one file use both allocations at once.</summary>
  [Test]
  [Category("Unit")]
  public void Build_MixtureOfShortAndLongStreams_RoundTripsBoth() {
    var streams = new (string Name, byte[] Data)[] {
      ("Short", Pattern(100)),
      ("Long", Pattern(20_000)),
      ("Empty", []),
      ("AlsoShort", Pattern(4095)),
      ("AlsoLong", Pattern(4096)),
    };

    _AssertRoundTrips(Container(streams), streams);
  }

  [Test]
  [Category("Unit")]
  public void Build_StreamsUnderStorages_AreReachableByTheirPath() {
    var writer = new CompoundFileWriter(new("00020906-0000-0000-C000-000000000046"));
    var outer = writer.AddStorage(0, "Outer", Guid.Empty);
    var inner = writer.AddStorage(outer, "Inner", Guid.Empty);
    writer.AddStream(0, "AtRoot", Pattern(10));
    writer.AddStream(outer, "InOuter", Pattern(5000));
    writer.AddStream(inner, "InInner", Pattern(20));

    var read = new CompoundFile(writer.Build());
    var paths = read.Streams().Select(pair => pair.Key).ToArray();

    Assert.That(paths, Is.SupersetOf(new[] { "/AtRoot", "/Outer/InOuter", "/Outer/Inner/InInner" }));
  }

  /// <summary>
  /// A sibling tree is ordered by name length first and only then alphabetically, which is not any
  /// ordering a string comparer gives by default. Names that disagree between the two orderings are
  /// what tells the difference.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Build_NamesThatSortDifferentlyByLengthAndAlphabet_AreAllStillFound() {
    var streams = new (string Name, byte[] Data)[] {
      ("zz", Pattern(3)),
      ("a", Pattern(4)),
      ("aaaa", Pattern(5)),
      ("B", Pattern(6)),
      ("bbb", Pattern(7)),
      ("Zzz", Pattern(8)),
    };

    _AssertRoundTrips(Container(streams), streams);
  }

  /// <summary>31 UTF-16 code units is the longest name a directory entry can hold.</summary>
  [Test]
  [Category("Unit")]
  public void AddStream_NameOfThirtyOneCharacters_IsAccepted() {
    var name = new string('n', 31);
    var payload = Pattern(9);

    _AssertRoundTrips(Container((name, payload)), (name, payload));
  }

  [Test]
  [Category("Unit")]
  public void AddStream_NameOfThirtyTwoCharacters_IsRejected() {
    var writer = new CompoundFileWriter(Guid.Empty);

    Assert.Throws<ArgumentException>(() => writer.AddStream(0, new('n', 32), [1]));
  }

  [TestCase("")]
  [TestCase(null)]
  [Category("Unit")]
  public void AddStream_WithoutAName_IsRejected(string? name) {
    var writer = new CompoundFileWriter(Guid.Empty);

    Assert.Throws<ArgumentException>(() => writer.AddStream(0, name!, [1]));
  }

  [Test]
  [Category("Unit")]
  public void AddStream_WithoutData_IsRejected() {
    var writer = new CompoundFileWriter(Guid.Empty);

    Assert.Throws<ArgumentNullException>(() => writer.AddStream(0, "Nothing", null!));
  }

  [TestCase(1)]
  [TestCase(-1)]
  [TestCase(int.MaxValue)]
  [Category("Unit")]
  public void AddStream_UnderAParentThatDoesNotExist_IsRejected(int parent) {
    var writer = new CompoundFileWriter(Guid.Empty);

    Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddStream(parent, "Orphan", [1]));
  }

  /// <summary>A stream is not a directory, so nothing can be filed under one.</summary>
  [Test]
  [Category("Unit")]
  public void AddStream_UnderAStream_IsRejected() {
    var writer = new CompoundFileWriter(Guid.Empty);
    var stream = writer.AddStream(0, "NotAStorage", [1]);

    Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddStream(stream, "Under", [1]));
  }

  [Test]
  [Category("Unit")]
  public void AddStorage_UnderAStream_IsRejected() {
    var writer = new CompoundFileWriter(Guid.Empty);
    var stream = writer.AddStream(0, "NotAStorage", [1]);

    Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddStorage(stream, "Under", Guid.Empty));
  }

  /// <summary>A flat container holding exactly the streams named.</summary>
  internal static byte[] Container(params (string Name, byte[] Data)[] streams) {
    var writer = new CompoundFileWriter(Guid.Empty);
    foreach (var (name, data) in streams)
      writer.AddStream(0, name, data);

    return writer.Build();
  }

  /// <summary>Bytes that differ everywhere, so a chain followed wrongly cannot come back looking right.</summary>
  internal static byte[] Pattern(int length) {
    var result = new byte[length];
    for (var i = 0; i < length; ++i)
      result[i] = (byte)(i * 31 + (i >> 8) * 17 + 1);

    return result;
  }

  /// <summary>
  /// Two entries of one storage that the format cannot tell apart are refused rather than written.
  /// </summary>
  /// <remarks>
  /// A storage holds its children in a tree ordered by length and then without case, and a reader
  /// finds a stream by walking that ordering rather than by reading every entry. So a second entry
  /// that compares equal to the first is written, is reachable by nothing, and produces a file that
  /// is quietly short of a stream rather than one that fails to parse. Refusing it at the point of
  /// the mistake is the only place the caller can still be told which name it was.
  /// </remarks>
  [TestCase("Contents", "Contents", TestName = "TwoEntriesOfOneStorageWithTheSameName_AreRefused")]
  [TestCase("Contents", "CONTENTS", TestName = "TwoEntriesOfOneStorageDifferingOnlyInCase_AreRefused")]
  [TestCase("Contents", "coNTenTS", TestName = "TwoEntriesOfOneStorageDifferingOnlyInMixedCase_AreRefused")]
  [Category("ExceptionalCase")]
  public void CollidingSiblingNames_AreRefused(string first, string second) {
    var writer = new CompoundFileWriter(Guid.Empty);
    writer.AddStream(0, first, [1, 2, 3]);

    Assert.That(
      () => writer.AddStream(0, second, [4, 5, 6]),
      Throws.ArgumentException.With.Message.Contains(second));
  }

  [Test]
  [Category("EdgeCase")]
  public void TheSameNameUnderTwoDifferentStorages_IsAccepted() {
    // The rule is per storage, not per file: two storages may each hold their own "Contents", and
    // every real compound file does exactly that.
    var writer = new CompoundFileWriter(Guid.Empty);
    var left = writer.AddStorage(0, "Left", Guid.Empty);
    var right = writer.AddStorage(0, "Right", Guid.Empty);

    writer.AddStream(left, "Contents", [1, 2, 3]);
    writer.AddStream(right, "Contents", [4, 5, 6]);

    var container = writer.Build();
    var read = new CompoundFile(container);
    var found = read.Streams().ToDictionary(pair => pair.Key, pair => pair.Value);

    Assert.Multiple(() => {
      Assert.That(read.Read(found["/Left/Contents"]), Is.EqualTo(new byte[] { 1, 2, 3 }));
      Assert.That(read.Read(found["/Right/Contents"]), Is.EqualTo(new byte[] { 4, 5, 6 }));
    });
  }

  /// <summary>A name carrying one of the four characters MS-CFB 2.6.1 reserves is refused.</summary>
  [TestCase("Ole/Stream")]
  [TestCase(@"Ole\Stream")]
  [TestCase("Ole:Stream")]
  [TestCase("Ole!Stream")]
  [Category("ExceptionalCase")]
  public void AReservedCharacterInAName_IsRefused(string name) {
    var writer = new CompoundFileWriter(Guid.Empty);

    Assert.That(() => writer.AddStream(0, name, [1]), Throws.ArgumentException);
  }

  [Test]
  [Category("Boundary")]
  public void ANameOfEveryOtherPunctuation_IsAccepted() {
    // Only four characters are reserved. A leading 0x05 is conventional for summary-information
    // streams and must keep working, and the rest of punctuation is not the container's business.
    var writer = new CompoundFileWriter(Guid.Empty);
    writer.AddStream(0, "\u0005SummaryInformation", [1]);
    writer.AddStream(0, "Name-With.Punctuation_And Space", [2]);

    Assert.That(() => writer.Build(), Throws.Nothing);
  }

  private static void _AssertRoundTrips(byte[] container, params (string Name, byte[] Data)[] expected) {
    var read = new CompoundFile(container);
    var found = read.Streams().ToDictionary(pair => pair.Key, pair => pair.Value);

    Assert.Multiple(() => {
      foreach (var (name, data) in expected) {
        var path = "/" + name;
        Assert.That(found.ContainsKey(path), Is.True, $"'{path}' is not in the container that was just written");
        if (!found.TryGetValue(path, out var entry))
          continue;

        Assert.That(entry.Size, Is.EqualTo(data.Length), $"{path} declared length");
        Assert.That(read.Read(entry), Is.EqualTo(data), $"{path} contents");
      }
    });
  }
}
