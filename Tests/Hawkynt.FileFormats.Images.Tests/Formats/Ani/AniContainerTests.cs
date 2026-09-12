using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Ani;
using FileFormat.Cur;
using FileFormat.Ico;

namespace FileFormat.Ani.Tests;

/// <summary>
/// The RIFF container an animated cursor is: its chunks, its metadata, and the fact that its
/// frames are cursors.
/// </summary>
[TestFixture]
public sealed class AniContainerTests {

  private const int _InfoHeaderSize = 40;

  private static byte[] _IconDib(int width, int height) {
    var colourBytes = width * 4 * height;
    var maskBytes = (width + 31) / 32 * 4 * height;
    var dib = new byte[_InfoHeaderSize + colourBytes + maskBytes];
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), _InfoHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height * 2);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
    return dib;
  }

  /// <summary>A cursor file of one entry with a hotspot that is not at the origin.</summary>
  private static byte[] _Cursor(int width, int height, ushort hotspotX, ushort hotspotY)
    => IcoWriter.Assemble(IcoFileType.Cursor, [
      new IconBundleEntry(0, width, height, 32, hotspotX, hotspotY, IcoImageFormat.Bmp, _IconDib(width, height))
    ]);

  private static byte[] _Icon(int width, int height)
    => IcoWriter.Assemble(IcoFileType.Icon, [
      new IconBundleEntry(0, width, height, 32, 0, 0, IcoImageFormat.Bmp, _IconDib(width, height))
    ]);

  /// <summary>Writes one RIFF chunk, padded to an even length as RIFF requires.</summary>
  private static void _Chunk(Stream to, string id, byte[] body) {
    to.Write(Encoding.ASCII.GetBytes(id));
    Span<byte> size = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)body.Length);
    to.Write(size);
    to.Write(body);
    if ((body.Length & 1) != 0)
      to.WriteByte(0);
  }

  private static byte[] _Anih(int frames, int steps, int displayRate, int flags) {
    var anih = new byte[36];
    BinaryPrimitives.WriteInt32LittleEndian(anih.AsSpan(0), 36);
    BinaryPrimitives.WriteInt32LittleEndian(anih.AsSpan(4), frames);
    BinaryPrimitives.WriteInt32LittleEndian(anih.AsSpan(8), steps);
    BinaryPrimitives.WriteInt32LittleEndian(anih.AsSpan(24), 1);
    BinaryPrimitives.WriteInt32LittleEndian(anih.AsSpan(28), displayRate);
    BinaryPrimitives.WriteInt32LittleEndian(anih.AsSpan(32), flags);
    return anih;
  }

  /// <summary>
  /// An ANI built by hand, so the tests are not reading back only what this library writes.
  /// </summary>
  private static byte[] _BuildAni(
    byte[][] frames,
    int displayRate = 6,
    int flags = 1,
    int[]? rates = null,
    int[]? sequence = null,
    string? title = null,
    string? artist = null
  ) {
    using var body = new MemoryStream();
    _Chunk(body, "anih", _Anih(frames.Length, sequence?.Length ?? frames.Length, displayRate, flags));

    if (title != null || artist != null) {
      using var info = new MemoryStream();
      info.Write("INFO"u8);
      if (title != null)
        _Chunk(info, "INAM", Encoding.ASCII.GetBytes(title + "\0"));
      if (artist != null)
        _Chunk(info, "IART", Encoding.ASCII.GetBytes(artist + "\0"));
      _Chunk(body, "LIST", info.ToArray());
    }

    if (rates != null) {
      var rateBody = new byte[rates.Length * 4];
      for (var i = 0; i < rates.Length; ++i)
        BinaryPrimitives.WriteInt32LittleEndian(rateBody.AsSpan(i * 4), rates[i]);
      _Chunk(body, "rate", rateBody);
    }

    if (sequence != null) {
      var seqBody = new byte[sequence.Length * 4];
      for (var i = 0; i < sequence.Length; ++i)
        BinaryPrimitives.WriteInt32LittleEndian(seqBody.AsSpan(i * 4), sequence[i]);
      _Chunk(body, "seq ", seqBody);
    }

    using var fram = new MemoryStream();
    fram.Write("fram"u8);
    foreach (var frame in frames)
      _Chunk(fram, "icon", frame);
    _Chunk(body, "LIST", fram.ToArray());

    var inner = body.ToArray();
    using var file = new MemoryStream();
    file.Write("RIFF"u8);
    Span<byte> size = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)(4 + inner.Length));
    file.Write(size);
    file.Write("ACON"u8);
    file.Write(inner);
    return file.ToArray();
  }

  // ── The frames are cursors ───────────────────────────────────────────────

  [Test]
  [Category("Boundary")]
  public void FromBytes_FramesAreCursors_WhichIsWhatAnAnimatedCursorIsMadeOf() {
    // Every real animated cursor's frames are type 2. Parsing each frame as an icon rejected the
    // type field and threw, so no file but this library's own output could be opened at all.
    var ani = _BuildAni([_Cursor(32, 32, 4, 5), _Cursor(32, 32, 4, 5)]);

    var file = AniReader.FromBytes(ani);

    Assert.That(file.Frames, Has.Count.EqualTo(2));
    Assert.That(file.Frames[0].Images, Has.Count.EqualTo(1));
    Assert.That(file.Frames[0].Images[0].Width, Is.EqualTo(32));
    Assert.That(file.Frames[0].Images[0].Height, Is.EqualTo(32));
  }

  [Test]
  [Category("Boundary")]
  public void FromBytes_FramesMayAlsoBeIcons() {
    var ani = _BuildAni([_Icon(16, 16)]);

    var file = AniReader.FromBytes(ani);

    Assert.That(file.Frames, Has.Count.EqualTo(1));
    Assert.That(file.Frames[0].Images[0].Width, Is.EqualTo(16));
  }

  [Test]
  [Category("Boundary")]
  public void FromBytes_KeepsEachFramesBytesExactly() {
    var first = _Cursor(32, 32, 4, 5);
    var second = _Cursor(16, 16, 1, 2);
    var ani = _BuildAni([first, second]);

    var file = AniReader.FromBytes(ani);

    Assert.That(file.FrameData, Has.Count.EqualTo(2));
    Assert.That(file.FrameData[0], Is.EqualTo(first).AsCollection);
    Assert.That(file.FrameData[1], Is.EqualTo(second).AsCollection);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheHotspotOfEveryFrame() {
    // The hotspot lives in two directory bytes of the frame's own file. Reassembling a frame from
    // the parsed view loses it, and a cursor whose hotspot is lost points at its top left corner.
    var ani = _BuildAni([_Cursor(32, 32, 11, 13), _Cursor(32, 32, 17, 19)]);

    var written = AniWriter.ToBytes(AniReader.FromBytes(ani));
    var reread = AniReader.FromBytes(written);

    Assert.That(reread.FrameData, Has.Count.EqualTo(2));
    var hotspots = reread.FrameData.Select(f => CurReader.FromBytes(f).Images[0]).ToArray();
    Assert.That(hotspots[0].HotspotX, Is.EqualTo(11));
    Assert.That(hotspots[0].HotspotY, Is.EqualTo(13));
    Assert.That(hotspots[1].HotspotX, Is.EqualTo(17));
    Assert.That(hotspots[1].HotspotY, Is.EqualTo(19));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AFrameThatWasReadComesOutByteIdentical() {
    var frame = _Cursor(24, 24, 3, 3);
    var ani = _BuildAni([frame]);

    var reread = AniReader.FromBytes(AniWriter.ToBytes(AniReader.FromBytes(ani)));

    Assert.That(reread.FrameData[0], Is.EqualTo(frame).AsCollection);
  }

  [Test]
  [Category("Integration")]
  public void ToBytes_AFileBuiltRatherThanReadHasItsFramesAssembled() {
    // Nothing carries the original bytes here, so the parsed frames are the only source there is.
    var built = new AniFile {
      Header = new AniHeader(36, NumFrames: 1, NumSteps: 1, Width: 16, Height: 16, BitCount: 32, NumPlanes: 1, DisplayRate: 6, Flags: 1),
      Frames = [new IcoFile { Images = [new IcoImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Bmp, Data = _IconDib(16, 16) }] }]
    };

    var reread = AniReader.FromBytes(AniWriter.ToBytes(built));

    Assert.That(reread.Frames, Has.Count.EqualTo(1));
    Assert.That(reread.Frames[0].Images[0].Width, Is.EqualTo(16));
  }

  // ── anih ─────────────────────────────────────────────────────────────────

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsEveryAnihField() {
    var ani = _BuildAni([_Cursor(32, 32, 0, 0)], displayRate: 12, flags: 3, sequence: [0]);

    var header = AniReader.FromBytes(ani).Header;

    Assert.That(header.CbSize, Is.EqualTo(36));
    Assert.That(header.NumFrames, Is.EqualTo(1));
    Assert.That(header.NumSteps, Is.EqualTo(1));
    Assert.That(header.NumPlanes, Is.EqualTo(1));
    Assert.That(header.DisplayRate, Is.EqualTo(12));
    Assert.That(header.Flags, Is.EqualTo(3));
    Assert.That(header.HasIconFrames, Is.True);
    Assert.That(header.HasSequence, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SetsAfIconBecauseTheFramesAreWholeCursorFiles() {
    var ani = _BuildAni([_Cursor(32, 32, 0, 0)], flags: 0);

    var header = AniReader.FromBytes(AniWriter.ToBytes(AniReader.FromBytes(ani))).Header;

    Assert.That(header.HasIconFrames, Is.True, "AF_ICON, 0x0001");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SetsAfSequenceOnlyWhenASequenceIsActuallyWritten() {
    var without = new AniFile {
      Header = new AniHeader(36, 1, 1, 16, 16, 32, 1, 6, Flags: 3),
      Frames = [new IcoFile { Images = [new IcoImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Bmp, Data = _IconDib(16, 16) }] }]
    };

    var header = AniReader.FromBytes(AniWriter.ToBytes(without)).Header;

    Assert.That(header.HasSequence, Is.False, "the header claimed one but there is no seq chunk");
    Assert.That(header.HasIconFrames, Is.True);
  }

  // ── rate and seq ─────────────────────────────────────────────────────────

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsThePerStepRates() {
    var ani = _BuildAni([_Cursor(16, 16, 0, 0), _Cursor(16, 16, 0, 0)], rates: [10, 20]);

    var rates = AniReader.FromBytes(ani).Rates;

    Assert.That(rates, Is.EqualTo(new[] { 10, 20 }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheStepToFrameMap() {
    var ani = _BuildAni([_Cursor(16, 16, 0, 0), _Cursor(16, 16, 0, 0)], sequence: [0, 1, 0, 1, 0]);

    var file = AniReader.FromBytes(ani);

    Assert.That(file.Sequence, Is.EqualTo(new[] { 0, 1, 0, 1, 0 }));
    Assert.That(file.Frames, Has.Count.EqualTo(2), "five steps replaying two frames");
  }

  [Test]
  [Category("Boundary")]
  public void FromBytes_ReadsTheWholeRateChunkEvenWhenTheHeadersStepCountDisagrees() {
    // The chunk is read for what it holds rather than for as many entries as the header allows.
    // Clamping to the header's count silently dropped the rates of a file whose count was wrong,
    // and dropped all of them when the count was nought.
    var ani = _BuildAni([_Cursor(16, 16, 0, 0)]);
    var patched = _PatchNumSteps(ani, 0);

    var file = AniReader.FromBytes(_WithRateChunk(patched, [7, 8, 9]));

    Assert.That(file.Header.NumSteps, Is.EqualTo(0));
    Assert.That(file.Rates, Is.EqualTo(new[] { 7, 8, 9 }));
  }

  [Test]
  [Category("Boundary")]
  public void FromBytes_NoRateOrSeqChunk_LeavesBothUnset() {
    var file = AniReader.FromBytes(_BuildAni([_Cursor(16, 16, 0, 0)]));

    Assert.That(file.Rates, Is.Null);
    Assert.That(file.Sequence, Is.Null);
  }

  // ── LIST INFO ────────────────────────────────────────────────────────────

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheTitleAndAuthorFromTheInfoList() {
    var ani = _BuildAni([_Cursor(16, 16, 0, 0)], title: "Spinning globe", artist: "Somebody");

    var file = AniReader.FromBytes(ani);

    Assert.That(file.Title, Is.EqualTo("Spinning globe"));
    Assert.That(file.Artist, Is.EqualTo("Somebody"));
  }

  [Test]
  [Category("Boundary")]
  public void FromBytes_NoInfoList_LeavesTitleAndAuthorUnset() {
    var file = AniReader.FromBytes(_BuildAni([_Cursor(16, 16, 0, 0)]));

    Assert.That(file.Title, Is.Null);
    Assert.That(file.Artist, Is.Null);
  }

  [Test]
  [Category("Boundary")]
  public void FromBytes_InfoListWithOnlyATitle_LeavesTheAuthorUnset() {
    var file = AniReader.FromBytes(_BuildAni([_Cursor(16, 16, 0, 0)], title: "Only a title"));

    Assert.That(file.Title, Is.EqualTo("Only a title"));
    Assert.That(file.Artist, Is.Null);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheTitleAndAuthor() {
    var ani = _BuildAni([_Cursor(16, 16, 0, 0)], title: "Demo", artist: "Workbench");

    var reread = AniReader.FromBytes(AniWriter.ToBytes(AniReader.FromBytes(ani)));

    Assert.That(reread.Title, Is.EqualTo("Demo"));
    Assert.That(reread.Artist, Is.EqualTo("Workbench"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesNoInfoListWhenThereIsNothingToPutInIt() {
    var built = new AniFile {
      Header = new AniHeader(36, 1, 1, 16, 16, 32, 1, 6, 1),
      Frames = [new IcoFile { Images = [new IcoImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Bmp, Data = _IconDib(16, 16) }] }]
    };

    var bytes = AniWriter.ToBytes(built);

    Assert.That(_Contains(bytes, "INFO"u8), Is.False);
  }

  // ── Chunk walking ────────────────────────────────────────────────────────

  [Test]
  [Category("Boundary")]
  public void FromBytes_SkipsChunksItDoesNotKnow() {
    var ani = _BuildAni([_Cursor(16, 16, 0, 0)]);
    var withExtra = _InsertChunkAfterHeader(ani, "junk", [1, 2, 3, 4, 5]);

    var file = AniReader.FromBytes(withExtra);

    Assert.That(file.Frames, Has.Count.EqualTo(1));
  }

  [Test]
  [Category("Boundary")]
  public void FromBytes_WalksPastAnOddLengthChunksPadByte() {
    var ani = _BuildAni([_Cursor(16, 16, 0, 0)]);
    // A five-byte body is padded with one NUL. A walker that does not skip the pad lands one byte
    // short of the next chunk id and then reads nothing else in the file.
    var withExtra = _InsertChunkAfterHeader(ani, "junk", [1, 2, 3, 4, 5]);

    var file = AniReader.FromBytes(withExtra);

    Assert.That(file.Frames, Has.Count.EqualTo(1), "the fram list after the padded chunk was still found");
  }

  [Test]
  [Category("Exceptional")]
  public void FromBytes_NoAnihChunk_Throws() {
    var ani = _BuildAni([_Cursor(16, 16, 0, 0)]);
    // Rename anih so the chunk is still there and still walked, but no longer recognised.
    var at = _IndexOf(ani, "anih"u8);
    ani[at] = (byte)'x';

    Assert.Throws<InvalidDataException>(() => AniReader.FromBytes(ani));
  }

  [Test]
  [Category("Exceptional")]
  public void FromBytes_AnihShorterThanTheHeader_Throws() {
    using var body = new MemoryStream();
    _Chunk(body, "anih", new byte[20]);
    var inner = body.ToArray();

    using var file = new MemoryStream();
    file.Write("RIFF"u8);
    Span<byte> size = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)(4 + inner.Length));
    file.Write(size);
    file.Write("ACON"u8);
    file.Write(inner);

    Assert.Throws<InvalidDataException>(() => AniReader.FromBytes(file.ToArray()));
  }

  [Test]
  [Category("Exceptional")]
  public void FromBytes_NotAcon_Throws() {
    var ani = _BuildAni([_Cursor(16, 16, 0, 0)]);
    "WAVE"u8.CopyTo(ani.AsSpan(8));

    Assert.Throws<InvalidDataException>(() => AniReader.FromBytes(ani));
  }

  // ── helpers ──────────────────────────────────────────────────────────────

  private static bool _Contains(byte[] haystack, ReadOnlySpan<byte> needle) => _TryIndexOf(haystack, needle) >= 0;

  private static int _IndexOf(byte[] haystack, ReadOnlySpan<byte> needle) {
    var at = _TryIndexOf(haystack, needle);
    Assert.That(at, Is.GreaterThanOrEqualTo(0), "the fixture should contain what the test is looking for");
    return at;
  }

  private static int _TryIndexOf(byte[] haystack, ReadOnlySpan<byte> needle) {
    for (var i = 0; i + needle.Length <= haystack.Length; ++i)
      if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
        return i;
    return -1;
  }

  /// <summary>Puts a chunk straight after the twelve-byte RIFF header and fixes the file size.</summary>
  private static byte[] _InsertChunkAfterHeader(byte[] ani, string id, byte[] body) {
    using var chunk = new MemoryStream();
    _Chunk(chunk, id, body);
    var inserted = chunk.ToArray();

    var result = new byte[ani.Length + inserted.Length];
    ani.AsSpan(0, 12).CopyTo(result);
    inserted.CopyTo(result, 12);
    ani.AsSpan(12).CopyTo(result.AsSpan(12 + inserted.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(result.Length - 8));
    return result;
  }

  private static byte[] _PatchNumSteps(byte[] ani, int steps) {
    var at = _IndexOf(ani, "anih"u8);
    BinaryPrimitives.WriteInt32LittleEndian(ani.AsSpan(at + 8 + 8), steps);
    return ani;
  }

  /// <summary>Appends a rate chunk after the RIFF header and fixes the file size.</summary>
  private static byte[] _WithRateChunk(byte[] ani, int[] rates) {
    var body = new byte[rates.Length * 4];
    for (var i = 0; i < rates.Length; ++i)
      BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(i * 4), rates[i]);
    return _InsertChunkAfterHeader(ani, "rate", body);
  }
}
