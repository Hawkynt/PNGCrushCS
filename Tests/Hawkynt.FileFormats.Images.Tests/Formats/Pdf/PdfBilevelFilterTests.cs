using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using FileFormat.Pdf.Filters;

namespace FileFormat.Pdf.Tests;

/// <summary>The <c>CCITTFaxDecode</c> and <c>JBIG2Decode</c> filters, held to oracle decodes of the same streams.</summary>
/// <remarks>
/// A case is a coded stream, its decode parameters and the picture it has to decode to:
/// <c>name.bin</c>, <c>name.globals.bin</c> for a JBIG2 stream that has a <c>JBIG2Globals</c>
/// stream, <c>name.params</c> (<c>Key=value</c> lines: <c>Filter</c>, <c>Width</c>, <c>Height</c>
/// and the decode parameters) and <c>name.pbm</c>. PBM writes 1 for black and the filter output has
/// 0 for black, so the two are compared bit-inverted.
/// <para/>
/// The committed fixtures were drawn and coded here. The CCITT ones are one drawing coded by
/// libtiff's <c>tiffcp</c> every way TIFF can carry it, and their <c>.pbm</c> is poppler's
/// <c>pdfimages</c> run on a one-image PDF carrying the stream unchanged. The JBIG2 ones come from
/// <see cref="Jbig2FixtureWriter"/> and their <c>.pbm</c> is the picture that was coded, which each
/// was checked against poppler 26.11 (<c>pdfimages</c>) and Ghostscript's jbig2dec:
/// <list type="bullet">
/// <item>generic templates 0-3 with and without typical prediction and moved adaptive pixels,
/// halftone (arithmetic and MMR), text, text with refinement, Huffman text: both decode exactly the
/// coded picture.</item>
/// <item>generic MMR: Ghostscript exact; poppler's JBIG2 MMR decoder is 2 pixels off, while its
/// CCITTFaxDecode of the very same bits is exact.</item>
/// <item>striped page of unknown height: Ghostscript exact; poppler 195 pixels off along the right
/// edge of the AND-combined region.</item>
/// <item>intermediate region refined into the page: poppler exact but for those same 195 pixels;
/// Ghostscript does not implement intermediate regions.</item>
/// <item>refinement of the page itself, with and without typical prediction: no oracle agrees
/// with another (poppler 3 and 329 pixels off, Ghostscript 768 and 776), so these two are held to
/// the coded picture and to T.88 6.3.5.6 alone.</item>
/// </list>
/// </remarks>
[TestFixture]
public sealed class PdfBilevelFilterTests {

  /// <summary>The fixtures beside this source file, or found by walking up from the test assembly when the build mapped the source paths away.</summary>
  private static string _FixtureDirectory([CallerFilePath] string source = "") {
    var beside = Path.Combine(Path.GetDirectoryName(source) ?? "", "Fixtures", "Filters");
    if (Directory.Exists(beside))
      return beside;

    var relative = Path.Combine("Tests", "Hawkynt.FileFormats.Images.Tests", "Formats", "Pdf", "Fixtures", "Filters");
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent) {
      var candidate = Path.Combine(directory.FullName, relative);
      if (Directory.Exists(candidate))
        return candidate;
    }

    throw new DirectoryNotFoundException("The PDF filter fixtures were not found.");
  }

  private static IEnumerable<string> _Fixtures(string prefix)
    => Directory.EnumerateFiles(_FixtureDirectory(), prefix + "*.params").Select(p => Path.GetFileNameWithoutExtension(p)).Order();

  public static IEnumerable<TestCaseData> CcittFixtures() => _Fixtures("ccitt-").Select(n => new TestCaseData(n).SetName($"Ccitt_DecodesAsPoppler({n})"));

  public static IEnumerable<TestCaseData> Jbig2Fixtures() => _Fixtures("jbig2-").Select(n => new TestCaseData(n).SetName($"Jbig2_DecodesToTheCodedPicture({n})"));

  [TestCaseSource(nameof(CcittFixtures))]
  [Category("Unit")]
  public void Ccitt_DecodesAsPoppler(string name) => _AssertMatchesExpectation(Path.Combine(_FixtureDirectory(), name));

  [TestCaseSource(nameof(Jbig2Fixtures))]
  [Category("Unit")]
  public void Jbig2_DecodesToTheCodedPicture(string name) => _AssertMatchesExpectation(Path.Combine(_FixtureDirectory(), name));

  [Test]
  [Category("Unit")]
  public void FixturesArePresent() {
    Assert.Multiple(() => {
      Assert.That(CcittFixtures().Count(), Is.GreaterThanOrEqualTo(7));
      Assert.That(Jbig2Fixtures().Count(), Is.GreaterThanOrEqualTo(15));
    });
  }

  [Test]
  [Category("Unit")]
  public void Ccitt_BlackIs1_InvertsEveryBit() {
    var data = File.ReadAllBytes(Path.Combine(_FixtureDirectory(), "ccitt-g4.bin"));
    var normal = PdfCcittFaxDecoder.Decode(data, -1, false, false, 203, 61, true, false);
    var inverted = PdfCcittFaxDecoder.Decode(data, -1, false, false, 203, 61, true, true);
    Assert.That(inverted, Is.EqualTo(normal.Select(b => (byte)~b).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void Ccitt_AgreesWithTheTiffGroup4Decoder() {
    // The format's own Group 4 decoder is independent code; on a stream both read they must agree.
    var data = File.ReadAllBytes(Path.Combine(_FixtureDirectory(), "ccitt-g4.bin"));
    var pdf = PdfCcittFaxDecoder.Decode(data, -1, false, false, 203, 61, true, true);
    var tiff = FileFormat.Ccitt.CcittG4Decoder.Decode(data, 203, 61);
    Assert.That(_Masked(pdf, 203, 61), Is.EqualTo(_Masked(tiff, 203, 61)));
  }

  [Test]
  [Category("Unit")]
  public void Ccitt_RowsGiven_ShortDataIsPaddedWhite() {
    var data = File.ReadAllBytes(Path.Combine(_FixtureDirectory(), "ccitt-g4.bin"));
    var decoded = PdfCcittFaxDecoder.Decode(data[..40], -1, false, false, 203, 61, true, false);
    Assert.Multiple(() => {
      Assert.That(decoded, Has.Length.EqualTo(26 * 61));
      Assert.That(decoded[^26..].All(b => b == 0xFF), Is.True, "the last row is white");
    });
  }

  [Test]
  [Category("Unit")]
  public void Ccitt_NoRows_DecodesUntilTheDataEnds() {
    var data = File.ReadAllBytes(Path.Combine(_FixtureDirectory(), "ccitt-g3-1d.bin"));
    var decoded = PdfCcittFaxDecoder.Decode(data, 0, true, false, 203, 0, true, false);
    var expected = PdfCcittFaxDecoder.Decode(data, 0, true, false, 203, 61, true, false);
    Assert.That(decoded, Has.Length.GreaterThanOrEqualTo(expected.Length));
    Assert.That(decoded.AsSpan(0, expected.Length).ToArray(), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void Ccitt_Garbage_DoesNotThrowOrHang() {
    var random = new Random(891);
    var data = new byte[500];
    random.NextBytes(data);
    foreach (var k in new[] { -1, 0, 4 })
      Assert.DoesNotThrow(() => PdfCcittFaxDecoder.Decode(data, k, k >= 0, false, 300, 0, true, false));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2_Garbage_ThrowsInvalidDataOrNotSupported() {
    var random = new Random(891);
    for (var i = 0; i < 50; ++i) {
      var data = new byte[200];
      random.NextBytes(data);
      try {
        PdfJbig2Decoder.Decode(data, null, 64, 64);
      } catch (InvalidDataException) {
      } catch (NotSupportedException) {
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void Jbig2_UnknownSegmentType_IsRefusedByName() {
    // A segment of type 54, which T.88 does not define, and nothing else.
    byte[] segment = [0, 0, 0, 0, 54, 0, 1, 0, 0, 0, 0];
    var failure = Assert.Throws<NotSupportedException>(() => PdfJbig2Decoder.Decode(segment, null, 8, 8));
    Assert.That(failure!.Message, Does.Contain("54"));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2_Empty_IsAllWhite() {
    var decoded = PdfJbig2Decoder.Decode([], null, 10, 3);
    Assert.That(decoded, Is.EqualTo(Enumerable.Repeat((byte)0xFF, 6).ToArray()));
  }

  /// <summary>Compares against every case in the directory named by <c>PDF_BILEVEL_CORPUS</c>.</summary>
  /// <remarks>
  /// For streams that cannot be committed: images pulled out of real PDFs (pdf.js's test corpus,
  /// for one) with the same layout as the fixtures here. Cases poppler itself refuses carry no
  /// <c>.pbm</c> and only have to decode without an unexpected exception.
  /// </remarks>
  [Test]
  [Explicit("Needs a corpus directory in PDF_BILEVEL_CORPUS.")]
  [Category("Integration")]
  public void Corpus_MatchesPoppler() {
    var directory = Environment.GetEnvironmentVariable("PDF_BILEVEL_CORPUS");
    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
      Assert.Ignore("PDF_BILEVEL_CORPUS is not set to a directory.");

    var failures = new List<string>();
    var report = new StringBuilder();
    foreach (var parameters in Directory.EnumerateFiles(directory, "*.params").Order()) {
      var name = Path.ChangeExtension(parameters, null);
      try {
        var (decoded, expected, width, height) = _DecodeCase(name);
        if (expected == null) {
          report.AppendLine($"{Path.GetFileName(name)}: decoded, poppler has no expectation");
          continue;
        }

        var differing = _DifferingPixels(decoded, expected, width, height);
        if (differing != 0)
          File.WriteAllBytes(name + ".ours.pbm", [.. Encoding.ASCII.GetBytes($"P4\n{width} {height}\n"), .. decoded.Take((width + 7) / 8 * height).Select(b => (byte)~b)]);
        report.AppendLine($"{Path.GetFileName(name)}: {width}x{height}, {differing} pixels differ");
        if (differing != 0)
          failures.Add($"{Path.GetFileName(name)}: {differing} pixels differ");
      } catch (Exception failure) when (failure is NotSupportedException) {
        report.AppendLine($"{Path.GetFileName(name)}: refused: {failure.Message}");
      } catch (Exception failure) {
        failures.Add($"{Path.GetFileName(name)}: {failure.GetType().Name}: {failure.Message}");
      }
    }

    TestContext.Out.WriteLine(report.ToString());
    Assert.That(failures, Is.Empty);
  }

  private static void _AssertMatchesExpectation(string name) {
    var (decoded, expected, width, height) = _DecodeCase(name);
    Assert.That(expected, Is.Not.Null, "the fixture has no expectation");
    Assert.That(_DifferingPixels(decoded, expected!, width, height), Is.Zero);
  }

  private static (byte[] Decoded, byte[]? Expected, int Width, int Height) _DecodeCase(string name) {
    var parameters = File.ReadAllLines(name + ".params")
      .Where(l => l.Contains('='))
      .ToDictionary(l => l[..l.IndexOf('=')], l => l[(l.IndexOf('=') + 1)..]);
    var data = File.ReadAllBytes(name + ".bin");
    var width = int.Parse(parameters["Width"], CultureInfo.InvariantCulture);
    var height = int.Parse(parameters["Height"], CultureInfo.InvariantCulture);

    int Int(string key, int fallback) => parameters.TryGetValue(key, out var v) ? (int)double.Parse(v, CultureInfo.InvariantCulture) : fallback;
    bool Bool(string key, bool fallback) => parameters.TryGetValue(key, out var v) ? v == "true" : fallback;

    var decoded = parameters["Filter"] switch {
      "CCITTFaxDecode" => PdfCcittFaxDecoder.Decode(
        data, Int("K", 0), Bool("EndOfLine", false), Bool("EncodedByteAlign", false), Int("Columns", 1728),
        Int("Rows", 0), Bool("EndOfBlock", true), Bool("BlackIs1", false)),
      "JBIG2Decode" => PdfJbig2Decoder.Decode(data, File.Exists(name + ".globals.bin") ? File.ReadAllBytes(name + ".globals.bin") : null, width, height),
      var filter => throw new InvalidDataException($"No filter {filter}."),
    };

    var expected = File.Exists(name + ".pbm") ? _ReadPbmAsFilterOutput(name + ".pbm", width, height) : null;
    return (decoded, expected, width, height);
  }

  /// <summary>A P4 PBM's bits, inverted to the filter's 0-for-black.</summary>
  private static byte[] _ReadPbmAsFilterOutput(string path, int width, int height) {
    var bytes = File.ReadAllBytes(path);
    var position = 0;
    var fields = new List<string>();
    while (fields.Count < 3) {
      while (char.IsWhiteSpace((char)bytes[position]))
        ++position;
      if (bytes[position] == '#') {
        while (bytes[position] != '\n')
          ++position;
        continue;
      }

      var start = position;
      while (!char.IsWhiteSpace((char)bytes[position]))
        ++position;
      fields.Add(Encoding.ASCII.GetString(bytes, start, position - start));
    }

    Assert.Multiple(() => {
      Assert.That(fields[0], Is.EqualTo("P4"));
      Assert.That(int.Parse(fields[1], CultureInfo.InvariantCulture), Is.EqualTo(width));
      Assert.That(int.Parse(fields[2], CultureInfo.InvariantCulture), Is.EqualTo(height));
    });

    return bytes.AsSpan(position + 1, (width + 7) / 8 * height).ToArray().Select(b => (byte)~b).ToArray();
  }

  private static int _DifferingPixels(byte[] decoded, byte[] expected, int width, int height) {
    var rowSize = (width + 7) / 8;
    var differing = 0;
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = y * rowSize + (x >> 3);
        var mask = 0x80 >> (x & 7);
        var ours = at < decoded.Length && (decoded[at] & mask) != 0;
        var theirs = (expected[at] & mask) != 0;
        if (ours != theirs)
          ++differing;
      }

    return differing;
  }

  /// <summary>The rows with the padding bits past the last column cleared, which nothing promises the value of.</summary>
  private static byte[] _Masked(byte[] rows, int width, int height) {
    var rowSize = (width + 7) / 8;
    var masked = rows.AsSpan(0, rowSize * height).ToArray();
    if (width % 8 != 0)
      for (var y = 0; y < height; ++y)
        masked[y * rowSize + rowSize - 1] &= (byte)(0xFF << (8 - width % 8));
    return masked;
  }

  /// <summary>Writes the JBIG2 fixtures into the directory named by <c>JBIG2_FIXTURE_OUT</c>.</summary>
  /// <remarks>
  /// Run once to remake them; each stream then goes through poppler for its <c>.pbm</c>, and the
  /// <c>.source.pbm</c> written beside it is what was coded, which poppler's decode has to equal
  /// before a fixture is worth keeping.
  /// </remarks>
  [Test]
  [Explicit("Writes fixtures; needs JBIG2_FIXTURE_OUT.")]
  [Category("Integration")]
  public void WriteJbig2Fixtures() {
    var directory = Environment.GetEnvironmentVariable("JBIG2_FIXTURE_OUT");
    if (string.IsNullOrEmpty(directory))
      Assert.Ignore("JBIG2_FIXTURE_OUT is not set.");

    Directory.CreateDirectory(directory);
    foreach (var fixture in Jbig2FixtureWriter.All()) {
      var basePath = Path.Combine(directory, fixture.Name);
      File.WriteAllBytes(basePath + ".bin", fixture.Data);
      if (fixture.Globals != null)
        File.WriteAllBytes(basePath + ".globals.bin", fixture.Globals);

      File.WriteAllText(basePath + ".params", $"Filter=JBIG2Decode\nWidth={fixture.Width}\nHeight={fixture.Height}\n");
      var rowSize = (fixture.Width + 7) / 8;
      var header = Encoding.ASCII.GetBytes($"P4\n{fixture.Width} {fixture.Height}\n");
      var pbm = new byte[header.Length + rowSize * fixture.Height];
      header.CopyTo(pbm, 0);
      for (var y = 0; y < fixture.Height; ++y)
        for (var x = 0; x < fixture.Width; ++x)
          if (fixture.Source[y][x] != 0)
            pbm[header.Length + y * rowSize + (x >> 3)] |= (byte)(0x80 >> (x & 7));
      File.WriteAllBytes(basePath + ".source.pbm", pbm);
    }
  }
}

/// <summary>A minimal JBIG2 encoder, enough to make one stream per feature the decoder has to read.</summary>
/// <remarks>
/// Test-side code only. The MQ coder is written from T.88 E.2 and MMR comes from the CCITT Group 4
/// encoder of this package; the context labels follow T.88 6.2.5.3, 6.3.5.3 and Annex A, and how far
/// poppler and Ghostscript agree with each stream is listed on <see cref="PdfBilevelFilterTests"/>. Huffman
/// codes come from the Annex B standard tables.
/// </remarks>
internal static class Jbig2FixtureWriter {

  internal sealed record Fixture(string Name, int Width, int Height, byte[] Data, byte[]? Globals, byte[][] Source);

  internal static IEnumerable<Fixture> All() {
    yield return _Generic("jbig2-generic-t0", 0, [(3, -1), (-3, -1), (2, -2), (-2, -2)], false);
    yield return _Generic("jbig2-generic-t0-at-tpgdon", 0, [(4, -2), (-5, -1), (1, -3), (-6, 0)], true);
    yield return _Generic("jbig2-generic-t1-tpgdon", 1, [(3, -1)], true);
    yield return _Generic("jbig2-generic-t2", 2, [(2, -1)], false);
    yield return _Generic("jbig2-generic-t3-tpgdon", 3, [(2, -1)], true);
    yield return _GenericMmr();
    yield return _Text(false);
    yield return _Text(true);
    yield return _TextHuffman();
    yield return _Halftone(false);
    yield return _Halftone(true);
    yield return _Refinement(false);
    yield return _Refinement(true);
    yield return _Composite(striped: true, withIntermediate: false);
    yield return _Composite(striped: false, withIntermediate: true);
  }

  #region pictures

  private const int _Width = 96, _Height = 64;

  /// <summary>Shapes with long runs, repeated rows for typical prediction and some noise.</summary>
  private static byte[][] _Drawing(int width = _Width, int height = _Height) {
    var picture = _Blank(width, height);
    var random = new Random(891);
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var dx = x - width / 3;
        var dy = y - height / 2;
        var ring = dx * dx + dy * dy;
        var on = ring < 20 * 20 && ring > 12 * 12
          || x > width / 2 && x < width - 8 && y > 8 && y < 20
          || (x + y) % 9 == 0 && x > width / 2 && y > 30
          || y >= height - 12 && y < height - 6 && x % 4 != 3
          || random.Next(97) == 0;
        picture[y][x] = on ? (byte)1 : (byte)0;
      }

    return picture;
  }

  private static byte[][] _Blank(int width, int height) {
    var picture = new byte[height][];
    for (var y = 0; y < height; ++y)
      picture[y] = new byte[width];
    return picture;
  }

  /// <summary>A handful of small glyph-like shapes.</summary>
  private static List<byte[][]> _Glyphs() {
    var glyphs = new List<byte[][]>();
    var random = new Random(4711);
    int[] widths = [5, 7, 6, 4, 8, 7];
    int[] heights = [9, 9, 7, 9, 6, 11];
    for (var g = 0; g < widths.Length; ++g) {
      var glyph = _Blank(widths[g], heights[g]);
      for (var y = 0; y < heights[g]; ++y)
        for (var x = 0; x < widths[g]; ++x)
          glyph[y][x] = (byte)(x == 0 || y == heights[g] - 1 || random.Next(3) == 0 ? 1 : 0);
      glyphs.Add(glyph);
    }

    return glyphs;
  }

  private static void _Blit(byte[][] target, byte[][] source, int x, int y, int combination = 0) {
    for (var row = 0; row < source.Length; ++row)
      for (var column = 0; column < source[row].Length; ++column) {
        var ty = y + row;
        var tx = x + column;
        if ((uint)ty >= (uint)target.Length || (uint)tx >= (uint)target[ty].Length)
          continue;

        var bit = source[row][column];
        target[ty][tx] = combination switch {
          0 => (byte)(target[ty][tx] | bit),
          1 => (byte)(target[ty][tx] & bit),
          2 => (byte)(target[ty][tx] ^ bit),
          3 => (byte)((target[ty][tx] ^ bit) ^ 1),
          _ => bit,
        };
      }
  }

  private static byte[] _Packed(byte[][] picture) {
    var width = picture.Length > 0 ? picture[0].Length : 0;
    var rowSize = (width + 7) / 8;
    var packed = new byte[rowSize * picture.Length];
    for (var y = 0; y < picture.Length; ++y)
      for (var x = 0; x < width; ++x)
        if (picture[y][x] != 0)
          packed[y * rowSize + (x >> 3)] |= (byte)(0x80 >> (x & 7));
    return packed;
  }

  #endregion

  #region segments

  private sealed class _Stream(int firstNumber = 0) {
    private readonly MemoryStream _bytes = new();
    private int _next = firstNumber;

    public int Segment(int type, byte[] data, params int[] referredTo) {
      var number = this._next++;
      _Int(this._bytes, number);
      this._bytes.WriteByte((byte)type);
      this._bytes.WriteByte((byte)(referredTo.Length << 5));
      foreach (var referred in referredTo)
        this._bytes.WriteByte((byte)referred);
      this._bytes.WriteByte(1);
      _Int(this._bytes, data.Length);
      this._bytes.Write(data);
      return number;
    }

    public byte[] ToArray() => this._bytes.ToArray();
  }

  private static void _Int(MemoryStream stream, int value) {
    stream.WriteByte((byte)(value >> 24));
    stream.WriteByte((byte)(value >> 16));
    stream.WriteByte((byte)(value >> 8));
    stream.WriteByte((byte)value);
  }

  private static void _Short(MemoryStream stream, int value) {
    stream.WriteByte((byte)(value >> 8));
    stream.WriteByte((byte)value);
  }

  private static byte[] _PageInformation(int width, int height, int flags = 0, int striping = 0) {
    var data = new MemoryStream();
    _Int(data, width);
    _Int(data, height);
    _Int(data, 0);
    _Int(data, 0);
    data.WriteByte((byte)flags);
    _Short(data, striping);
    return data.ToArray();
  }

  private static void _RegionInformation(MemoryStream data, int width, int height, int x, int y, int combination) {
    _Int(data, width);
    _Int(data, height);
    _Int(data, x);
    _Int(data, y);
    data.WriteByte((byte)combination);
  }

  #endregion

  #region arithmetic coding

  /// <summary>The MQ encoder of T.88 E.2 (INITENC, ENCODE, RENORME, BYTEOUT, FLUSH), written from the text.</summary>
  private sealed class _MqEncoder {
    private static readonly (int Qe, int Nmps, int Nlps, bool Switch)[] _Table = [
      (0x5601, 1, 1, true), (0x3401, 2, 6, false), (0x1801, 3, 9, false), (0x0AC1, 4, 12, false),
      (0x0521, 5, 29, false), (0x0221, 38, 33, false), (0x5601, 7, 6, true), (0x5401, 8, 14, false),
      (0x4801, 9, 14, false), (0x3801, 10, 14, false), (0x3001, 11, 17, false), (0x2401, 12, 18, false),
      (0x1C01, 13, 20, false), (0x1601, 29, 21, false), (0x5601, 15, 14, true), (0x5401, 16, 14, false),
      (0x5101, 17, 15, false), (0x4801, 18, 16, false), (0x3801, 19, 17, false), (0x3401, 20, 18, false),
      (0x3001, 21, 19, false), (0x2801, 22, 19, false), (0x2401, 23, 20, false), (0x2201, 24, 21, false),
      (0x1C01, 25, 22, false), (0x1801, 26, 23, false), (0x1601, 27, 24, false), (0x1401, 28, 25, false),
      (0x1201, 29, 26, false), (0x1101, 30, 27, false), (0x0AC1, 31, 28, false), (0x09C1, 32, 29, false),
      (0x08A1, 33, 30, false), (0x0521, 34, 31, false), (0x0441, 35, 32, false), (0x02A1, 36, 33, false),
      (0x0221, 37, 34, false), (0x0141, 38, 35, false), (0x0111, 39, 36, false), (0x0085, 40, 37, false),
      (0x0049, 41, 38, false), (0x0025, 42, 39, false), (0x0015, 43, 40, false), (0x0009, 44, 41, false),
      (0x0005, 45, 42, false), (0x0001, 45, 43, false), (0x5601, 46, 46, false),
    ];

    // The byte before the first is a placeholder BYTEOUT may carry into; it is dropped at the end.
    private readonly List<byte> _output = [0];
    private uint _a = 0x8000, _c;
    private int _ct = 12;

    public void Encode(byte[] indices, byte[] mpss, int context, int bit) {
      var (qe, nmps, nlps, switches) = _Table[indices[context]];
      this._a -= (uint)qe;
      if (bit == mpss[context]) {
        if ((this._a & 0x8000) != 0) {
          this._c += (uint)qe;
          return;
        }

        if (this._a < qe)
          this._a = (uint)qe;
        else
          this._c += (uint)qe;
        indices[context] = (byte)nmps;
      } else {
        if (this._a < qe)
          this._c += (uint)qe;
        else
          this._a = (uint)qe;
        if (switches)
          mpss[context] ^= 1;
        indices[context] = (byte)nlps;
      }

      do {
        this._a <<= 1;
        this._c <<= 1;
        if (--this._ct == 0)
          this._ByteOut();
      } while ((this._a & 0x8000) == 0);
    }

    private void _ByteOut() {
      if (this._output[^1] == 0xFF) {
        this._Stuffed();
        return;
      }

      if (this._c < 0x8000000) {
        this._output.Add((byte)(this._c >> 19));
        this._c &= 0x7FFFF;
        this._ct = 8;
        return;
      }

      ++this._output[^1];
      if (this._output[^1] == 0xFF) {
        this._c &= 0x7FFFFFF;
        this._Stuffed();
        return;
      }

      this._output.Add((byte)(this._c >> 19));
      this._c &= 0x7FFFF;
      this._ct = 8;
    }

    private void _Stuffed() {
      this._output.Add((byte)(this._c >> 20));
      this._c &= 0xFFFFF;
      this._ct = 7;
    }

    public byte[] Finish() {
      var temp = this._c + this._a;
      this._c |= 0xFFFF;
      if (this._c >= temp)
        this._c -= 0x8000;

      this._c <<= this._ct;
      this._ByteOut();
      this._c <<= this._ct;
      this._ByteOut();
      if (this._output[^1] != 0xFF)
        this._output.Add(0xFF);
      this._output.Add(0xAC);
      return this._output.Skip(1).ToArray();
    }
  }

  private sealed class _Coder {
    private readonly _MqEncoder _encoder = new();
    private readonly Dictionary<string, (byte[] Indices, byte[] Mps)> _contexts = [];

    public void Bit(string id, int context, int bit) {
      if (!this._contexts.TryGetValue(id, out var model))
        this._contexts[id] = model = (new byte[1 << 16], new byte[1 << 16]);
      this._encoder.Encode(model.Indices, model.Mps, context, bit);
    }

    /// <summary>The integer encoding procedure (A.2); null is the out-of-band value.</summary>
    public void Integer(string id, int? value) {
      var prev = 1;
      void Write(long v, int length) {
        for (var i = length - 1; i >= 0; --i) {
          var bit = (int)((v >> i) & 1);
          this.Bit(id, prev, bit);
          prev = prev < 256 ? (prev << 1) | bit : (((prev << 1) | bit) & 511) | 256;
        }
      }

      if (value == null) {
        Write(1, 1);
        Write(0, 1);
        Write(0, 2);
        return;
      }

      var v = (long)value.Value;
      Write(v < 0 ? 1 : 0, 1);
      v = Math.Abs(v);
      if (v < 4) {
        Write(0, 1);
        Write(v, 2);
      } else if (v < 20) {
        Write(0b10, 2);
        Write(v - 4, 4);
      } else if (v < 84) {
        Write(0b110, 3);
        Write(v - 20, 6);
      } else if (v < 340) {
        Write(0b1110, 4);
        Write(v - 84, 8);
      } else if (v < 4436) {
        Write(0b11110, 5);
        Write(v - 340, 12);
      } else {
        Write(0b11111, 5);
        Write(v - 4436, 32);
      }
    }

    /// <summary>The symbol ID encoding procedure (A.3).</summary>
    public void Iaid(int codeLength, int value) {
      var prev = 1;
      for (var i = codeLength - 1; i >= 0; --i) {
        var bit = (value >> i) & 1;
        this.Bit("IAID", prev, bit);
        prev = (prev << 1) | bit;
      }
    }

    public byte[] Finish() => this._encoder.Finish();
  }

  private static readonly (int X, int Y)[][] _Templates = [
    [(-1, -2), (0, -2), (1, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), (-4, 0), (-3, 0), (-2, 0), (-1, 0)],
    [(-1, -2), (0, -2), (1, -2), (2, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), (-3, 0), (-2, 0), (-1, 0)],
    [(-1, -2), (0, -2), (1, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), (-2, 0), (-1, 0)],
    [(-3, -1), (-2, -1), (-1, -1), (0, -1), (1, -1), (-4, 0), (-3, 0), (-2, 0), (-1, 0)],
  ];

  private static readonly int[] _PseudoPixelContexts = [0x9B25, 0x0795, 0x00E5, 0x0195];

  /// <summary>Codes a bitmap with the generic region procedure (6.2.5), the labels in raster order of the template pixels.</summary>
  private static void _EncodeGeneric(_Coder coder, byte[][] bitmap, int template, (int X, int Y)[] at, bool prediction, byte[][]? skip = null) {
    var points = _Templates[template].Concat(at).OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
    var height = bitmap.Length;
    var width = height > 0 ? bitmap[0].Length : 0;
    var ltp = 0;
    for (var y = 0; y < height; ++y) {
      if (prediction) {
        var typical = y > 0 && bitmap[y].AsSpan().SequenceEqual(bitmap[y - 1]) ? 1 : 0;
        coder.Bit("GB", _PseudoPixelContexts[template], typical ^ ltp);
        ltp = typical;
        if (typical != 0)
          continue;
      }

      for (var x = 0; x < width; ++x) {
        if (skip != null && skip[y][x] != 0)
          continue;

        var label = 0;
        for (var k = 0; k < points.Length; ++k) {
          var px = x + points[k].X;
          var py = y + points[k].Y;
          var bit = py >= 0 && px >= 0 && px < width && py < height ? bitmap[py][px] : 0;

          // Pixels not yet coded on this row are zero to the decoder too.
          if (py == y && px >= x)
            bit = 0;
          label |= bit << (points.Length - 1 - k);
        }

        coder.Bit("GB", label, bitmap[y][x]);
      }
    }
  }

  private static readonly ((int X, int Y)[] Coding, (int X, int Y)[] Reference)[] _RefinementTemplates = [
    ([(0, -1), (1, -1), (-1, 0)], [(0, -1), (1, -1), (-1, 0), (0, 0), (1, 0), (-1, 1), (0, 1), (1, 1)]),
    ([(-1, -1), (0, -1), (1, -1), (-1, 0)], [(0, -1), (-1, 0), (0, 0), (1, 0), (0, 1), (1, 1)]),
  ];

  private static readonly int[] _RefinementPseudoPixelContexts = [0x0020, 0x0008];

  /// <summary>Codes a bitmap with the generic refinement procedure (6.3.5) against a reference.</summary>
  private static void _EncodeRefinement(_Coder coder, byte[][] bitmap, byte[][] reference, int template, (int X, int Y)[] at, int dx, int dy, bool prediction) {
    var (coding, referencePoints) = _RefinementTemplates[template];
    if (template == 0) {
      coding = [.. coding, at[0]];
      referencePoints = [.. referencePoints, at[1]];
    }

    var height = bitmap.Length;
    var width = height > 0 ? bitmap[0].Length : 0;
    int Ref(int y, int x) => (uint)y < (uint)reference.Length && (uint)x < (uint)reference[0].Length ? reference[y][x] : 0;
    bool Uniform(int y, int x) {
      var first = Ref(y - 1, x - 1);
      for (var oy = -1; oy <= 1; ++oy)
        for (var ox = -1; ox <= 1; ++ox)
          if (Ref(y + oy, x + ox) != first)
            return false;
      return true;
    }

    var ltp = 0;
    for (var y = 0; y < height; ++y) {
      if (prediction) {
        // A row is typical when every pixel with a uniform neighbourhood matches it.
        var typical = 1;
        for (var x = 0; x < width && typical != 0; ++x)
          if (Uniform(y - dy, x - dx) && bitmap[y][x] != Ref(y - dy - 1, x - dx - 1))
            typical = 0;

        coder.Bit("GR", _RefinementPseudoPixelContexts[template], typical ^ ltp);
        ltp = typical;
      }

      for (var x = 0; x < width; ++x) {
        if (ltp != 0 && Uniform(y - dy, x - dx))
          continue;

        var label = 0;
        foreach (var (px, py) in coding) {
          var cy = y + py;
          var cx = x + px;
          var bit = cy >= 0 && cx >= 0 && cx < width && cy <= y ? bitmap[cy][cx] : 0;
          if (cy == y && cx >= x)
            bit = 0;
          label = (label << 1) | bit;
        }

        foreach (var (px, py) in referencePoints)
          label = (label << 1) | Ref(y + py - dy, x + px - dx);

        coder.Bit("GR", label, bitmap[y][x]);
      }
    }
  }

  private static int _Log2(int count) {
    var bits = 0;
    while (count > 1 << bits)
      ++bits;
    return bits;
  }

  #endregion

  #region Huffman coding

  private sealed class _BitWriter {
    private readonly MemoryStream _bytes = new();
    private int _current, _count;

    public void Write(long value, int length) {
      for (var i = length - 1; i >= 0; --i) {
        this._current = (this._current << 1) | (int)((value >> i) & 1);
        if (++this._count == 8)
          this._Flush();
      }
    }

    public void Align() {
      if (this._count > 0) {
        this._current <<= 8 - this._count;
        this._Flush();
      }
    }

    private void _Flush() {
      this._bytes.WriteByte((byte)this._current);
      this._current = this._count = 0;
    }

    public byte[] ToArray() {
      this.Align();
      return this._bytes.ToArray();
    }
  }

  /// <summary>Lines (low, prefix length, range length, prefix code) of the Annex B tables used here; a null low is out-of-band.</summary>
  private static readonly Dictionary<int, (long? Low, int PrefixLength, int RangeLength, int Code)[]> _Tables = new() {
    [1] = [(0, 1, 4, 0x0), (16, 2, 8, 0x2), (272, 3, 16, 0x6), (65808, 3, 32, 0x7)],
    [2] = [(0, 1, 0, 0x0), (1, 2, 0, 0x2), (2, 3, 0, 0x6), (3, 4, 3, 0xE), (11, 5, 6, 0x1E), (75, 6, 32, 0x3E), (null, 6, 0, 0x3F)],
    [4] = [(1, 1, 0, 0x0), (2, 2, 0, 0x2), (3, 3, 0, 0x6), (4, 4, 3, 0xE), (12, 5, 6, 0x1E), (76, 5, 32, 0x1F)],
    [6] = [(-2048, 5, 10, 0x1C), (-1024, 4, 9, 0x8), (-512, 4, 8, 0x9), (-256, 4, 7, 0xA), (-128, 5, 6, 0x1D), (-64, 5, 5, 0x1E), (-32, 4, 5, 0xB), (0, 2, 7, 0x0), (128, 3, 7, 0x2), (256, 3, 8, 0x3), (512, 4, 9, 0xC), (1024, 4, 10, 0xD)],
    [8] = [(-15, 8, 3, 0xFC), (-7, 9, 1, 0x1FC), (-5, 8, 1, 0xFD), (-3, 9, 0, 0x1FD), (-2, 7, 0, 0x7C), (-1, 4, 0, 0xA), (0, 2, 1, 0x0), (2, 5, 0, 0x1A), (3, 6, 0, 0x3A), (4, 3, 4, 0x4), (20, 6, 1, 0x3B), (22, 4, 4, 0xB), (38, 4, 5, 0xC), (70, 5, 6, 0x1B), (134, 5, 7, 0x1C), (262, 6, 7, 0x3C), (390, 7, 8, 0x7D), (646, 6, 10, 0x3D), (null, 2, 0, 0x1)],
    [11] = [(1, 1, 0, 0x0), (2, 2, 1, 0x2), (4, 4, 0, 0xC), (5, 4, 1, 0xD), (7, 5, 1, 0x1C), (9, 5, 2, 0x1D), (13, 6, 2, 0x3C), (17, 7, 2, 0x7A), (21, 7, 3, 0x7B), (29, 7, 4, 0x7C), (45, 7, 5, 0x7D), (77, 7, 6, 0x7E), (141, 7, 32, 0x7F)],
  };

  private static void _Huffman(_BitWriter writer, int table, int? value) {
    foreach (var (low, prefixLength, rangeLength, code) in _Tables[table]) {
      if (low == null) {
        if (value != null)
          continue;
        writer.Write(code, prefixLength);
        return;
      }

      if (value == null || value < low || rangeLength < 32 && value >= low + (1L << rangeLength))
        continue;

      writer.Write(code, prefixLength);
      writer.Write(value.Value - low.Value, rangeLength);
      return;
    }

    throw new ArgumentOutOfRangeException(nameof(value), $"Table B.{table} has no line for {value}.");
  }

  #endregion

  #region the fixtures

  private static readonly (int X, int Y)[] _NominalAt = [(3, -1), (-3, -1), (2, -2), (-2, -2)];

  private static void _WriteAt(MemoryStream data, (int X, int Y)[] at) {
    foreach (var (x, y) in at) {
      data.WriteByte((byte)(sbyte)x);
      data.WriteByte((byte)(sbyte)y);
    }
  }

  private static Fixture _Generic(string name, int template, (int X, int Y)[] at, bool prediction) {
    var picture = _Drawing();
    var coder = new _Coder();
    _EncodeGeneric(coder, picture, template, at, prediction);

    var data = new MemoryStream();
    _RegionInformation(data, _Width, _Height, 0, 0, 0);
    data.WriteByte((byte)((template << 1) | (prediction ? 8 : 0)));
    _WriteAt(data, at);
    data.Write(coder.Finish());
    var stream = new _Stream();
    stream.Segment(48, _PageInformation(_Width, _Height));
    stream.Segment(38, data.ToArray());
    stream.Segment(49, []);
    return new(name, _Width, _Height, stream.ToArray(), null, picture);
  }

  private static Fixture _GenericMmr() {
    var picture = _Drawing();
    var data = new MemoryStream();
    _RegionInformation(data, _Width, _Height, 0, 0, 0);
    data.WriteByte(1);
    data.Write(FileFormat.Ccitt.CcittG4Encoder.Encode(_Packed(picture), _Width, _Height));
    var stream = new _Stream();
    stream.Segment(48, _PageInformation(_Width, _Height));
    stream.Segment(39, data.ToArray());
    return new("jbig2-generic-mmr", _Width, _Height, stream.ToArray(), null, picture);
  }

  /// <summary>A symbol dictionary in the globals and a text region using it, with or without refinement.</summary>
  private static Fixture _Text(bool refinement) {
    var glyphs = _Glyphs();
    var coder = new _Coder();

    // The dictionary: one height class per glyph height, widths ascending inside each.
    var order = Enumerable.Range(0, glyphs.Count).OrderBy(i => glyphs[i].Length).ThenBy(i => glyphs[i][0].Length).ToList();
    var currentHeight = 0;
    var index = 0;
    while (index < order.Count) {
      var height = glyphs[order[index]].Length;
      coder.Integer("IADH", height - currentHeight);
      currentHeight = height;
      var currentWidth = 0;
      while (index < order.Count && glyphs[order[index]].Length == height) {
        var glyph = glyphs[order[index]];
        coder.Integer("IADW", glyph[0].Length - currentWidth);
        currentWidth = glyph[0].Length;
        _EncodeGeneric(coder, glyph, 0, _NominalAt, false);
        ++index;
      }

      coder.Integer("IADW", null);
    }

    // Export all: a run of none left out, then every one.
    coder.Integer("IAEX", 0);
    coder.Integer("IAEX", glyphs.Count);

    var dictionary = new MemoryStream();
    _Short(dictionary, 0);
    _WriteAt(dictionary, _NominalAt);
    _Int(dictionary, glyphs.Count);
    _Int(dictionary, glyphs.Count);
    dictionary.Write(coder.Finish());

    // The symbols as the dictionary exports them, in the order it decoded them.
    var exported = order.Select(i => glyphs[i]).ToList();
    var codeLength = _Log2(exported.Count);

    // The text: three lines, bottom-left reference corner, strip size 2 so IAIT is used.
    var picture = _Blank(_Width, _Height);
    var text = new _Coder();
    const int stripSize = 2;
    text.Integer("IADT", 0);
    var stripT = 0;
    var firstS = 0;
    var random = new Random(17);
    var instances = 0;
    var refinementAt = new (int X, int Y)[] { (-1, -1), (-1, -1) };
    for (var line = 0; line < 3; ++line) {
      var t = 14 + line * 20 + line % 2;
      var strip = t / stripSize;
      text.Integer("IADT", strip - stripT);
      stripT = strip;
      var s = 2 + line * 3;
      text.Integer("IAFS", s - firstS);
      firstS = s;
      var currentS = s;
      for (var word = 0; word < 9; ++word) {
        if (word > 0) {
          var gap = 1 + random.Next(4);
          text.Integer("IADS", gap);
          currentS += gap;
        }

        var id = random.Next(exported.Count);
        text.Integer("IAIT", t % stripSize);
        text.Iaid(codeLength, id);
        var symbol = exported[id];
        if (refinement) {
          var refine = word % 3 == 1;
          text.Integer("IARI", refine ? 1 : 0);
          if (refine) {
            // The same size, with a pixel or two changed.
            var refined = symbol.Select(r => (byte[])r.Clone()).ToArray();
            refined[1][1] ^= 1;
            refined[^2][^1] ^= 1;
            text.Integer("IARDW", 0);
            text.Integer("IARDH", 0);
            text.Integer("IARDX", 0);
            text.Integer("IARDY", 0);
            _EncodeRefinement(text, refined, symbol, 0, refinementAt, 0, 0, false);
            symbol = refined;
          }
        }

        // Bottom-left corner: the symbol's bottom row sits on t, its left column on s.
        _Blit(picture, symbol, currentS, t - symbol.Length + 1);
        currentS += symbol[0].Length - 1;
        ++instances;
      }

      text.Integer("IADS", null);
    }

    var region = new MemoryStream();
    _RegionInformation(region, _Width, _Height, 0, 0, 0);
    // REFCORNER 0 is BOTTOMLEFT (7.4.3.1.1).
    _Short(region, (refinement ? 2 : 0) | (1 << 2));
    if (refinement)
      _WriteAt(region, refinementAt);

    _Int(region, instances);
    region.Write(text.Finish());

    var globals = new _Stream();
    var dictionaryNumber = globals.Segment(0, dictionary.ToArray());
    var stream = new _Stream(1);
    stream.Segment(48, _PageInformation(_Width, _Height));
    stream.Segment(6, region.ToArray(), dictionaryNumber);
    stream.Segment(49, []);
    return new(refinement ? "jbig2-text-refinement" : "jbig2-text", _Width, _Height, stream.ToArray(), globals.ToArray(), picture);
  }

  /// <summary>A Huffman-coded symbol dictionary and text region, with standard tables throughout.</summary>
  private static Fixture _TextHuffman() {
    var glyphs = _Glyphs();
    var order = Enumerable.Range(0, glyphs.Count).OrderBy(i => glyphs[i].Length).ThenBy(i => glyphs[i][0].Length).ToList();

    // 7.4.2.1.1: Huffman, height deltas by table B.4, widths by B.2, bitmap sizes and exports by B.1.
    var writer = new _BitWriter();
    var currentHeight = 0;
    var index = 0;
    while (index < order.Count) {
      var height = glyphs[order[index]].Length;
      _Huffman(writer, 4, height - currentHeight);
      currentHeight = height;
      var currentWidth = 0;
      var classGlyphs = new List<byte[][]>();
      while (index < order.Count && glyphs[order[index]].Length == height) {
        var glyph = glyphs[order[index]];
        _Huffman(writer, 2, glyph[0].Length - currentWidth);
        currentWidth = glyph[0].Length;
        classGlyphs.Add(glyph);
        ++index;
      }

      _Huffman(writer, 2, null);

      // The height class as one uncompressed collective bitmap.
      _Huffman(writer, 1, 0);
      writer.Align();
      for (var y = 0; y < height; ++y) {
        foreach (var glyph in classGlyphs)
          foreach (var pixel in glyph[y])
            writer.Write(pixel, 1);
        writer.Align();
      }
    }

    _Huffman(writer, 1, 0);
    _Huffman(writer, 1, glyphs.Count);

    var dictionary = new MemoryStream();
    _Short(dictionary, 1);
    _Int(dictionary, glyphs.Count);
    _Int(dictionary, glyphs.Count);
    dictionary.Write(writer.ToArray());

    var exported = order.Select(i => glyphs[i]).ToList();

    // 7.4.3.1.7: every symbol ID gets a 3-bit code, which are 000, 001, ... in order. The run-code
    // table itself only needs RUNCODE3, so that alone gets a length, of 1.
    var text = new _BitWriter();
    for (var i = 0; i <= 34; ++i)
      text.Write(i == 3 ? 1 : 0, 4);
    for (var i = 0; i < exported.Count; ++i)
      text.Write(0, 1);
    text.Align();

    var picture = _Blank(_Width, _Height);
    _Huffman(text, 11, 1);
    var stripT = -1;
    var firstS = 0;
    var random = new Random(23);
    var instances = 0;
    for (var line = 0; line < 2; ++line) {
      var t = 20 + line * 25;
      _Huffman(text, 11, t - stripT);
      stripT = t;
      var s = 3 + line * 5;
      _Huffman(text, 6, s - firstS);
      firstS = s;
      var currentS = s;
      for (var word = 0; word < 8; ++word) {
        if (word > 0) {
          var gap = random.Next(5);
          _Huffman(text, 8, gap);
          currentS += gap;
        }

        var id = random.Next(exported.Count);
        text.Write(id, 3);
        var symbol = exported[id];

        // REFCORNER 1, TOPLEFT: the symbol's top row sits on t.
        _Blit(picture, symbol, currentS, t);
        currentS += symbol[0].Length - 1;
        ++instances;
      }

      _Huffman(text, 8, null);
    }

    var region = new MemoryStream();
    _RegionInformation(region, _Width, _Height, 0, 0, 0);
    _Short(region, 1 | (1 << 4));
    _Short(region, 0);
    _Int(region, instances);
    region.Write(text.ToArray());

    var stream = new _Stream();
    stream.Segment(48, _PageInformation(_Width, _Height));
    var dictionaryNumber = stream.Segment(0, dictionary.ToArray());
    stream.Segment(6, region.ToArray(), dictionaryNumber);
    return new("jbig2-text-huffman", _Width, _Height, stream.ToArray(), null, picture);
  }

  /// <summary>A pattern dictionary of five grey levels and a halftone region laid on an upright grid.</summary>
  private static Fixture _Halftone(bool mmr) {
    const int patternSize = 4;
    const int patternCount = 5;
    var patterns = new List<byte[][]>();
    for (var level = 0; level < patternCount; ++level) {
      var pattern = _Blank(patternSize, patternSize);
      var filled = 0;
      for (var y = 0; y < patternSize; ++y)
        for (var x = 0; x < patternSize; ++x)
          if (filled++ < level * 4)
            pattern[(y * 3 + x) % patternSize][x] = 1;
      patterns.Add(pattern);
    }

    var collective = _Blank(patternSize * patternCount, patternSize);
    for (var i = 0; i < patternCount; ++i)
      _Blit(collective, patterns[i], i * patternSize, 0, 4);

    var dictionary = new MemoryStream();
    dictionary.WriteByte((byte)(mmr ? 1 : 0));
    dictionary.WriteByte(patternSize);
    dictionary.WriteByte(patternSize);
    _Int(dictionary, patternCount - 1);
    if (mmr)
      dictionary.Write(FileFormat.Ccitt.CcittG4Encoder.Encode(_Packed(collective), collective[0].Length, patternSize));
    else {
      var coder = new _Coder();
      _EncodeGeneric(coder, collective, 0, [(-patternSize, 0), (-3, -1), (2, -2), (-2, -2)], false);
      dictionary.Write(coder.Finish());
    }

    // The grid: one cell per pattern, grey level from a ramp and a disc.
    const int gridWidth = _Width / patternSize, gridHeight = _Height / patternSize;
    var levels = new int[gridHeight, gridWidth];
    for (var mg = 0; mg < gridHeight; ++mg)
      for (var ng = 0; ng < gridWidth; ++ng) {
        var dx = ng - gridWidth / 2;
        var dy = mg - gridHeight / 2;
        levels[mg, ng] = dx * dx + dy * dy < 36 ? patternCount - 1 : ng * patternCount / gridWidth;
      }

    var bitsPerValue = _Log2(patternCount);
    var picture = _Blank(_Width, _Height);
    for (var mg = 0; mg < gridHeight; ++mg)
      for (var ng = 0; ng < gridWidth; ++ng)
        _Blit(picture, patterns[levels[mg, ng]], ng * patternSize, mg * patternSize);

    // Annex C: the levels Gray-coded, then sent one bit plane at a time, most significant first.
    var planes = new byte[bitsPerValue][][];
    for (var j = 0; j < bitsPerValue; ++j)
      planes[j] = _Blank(gridWidth, gridHeight);
    for (var mg = 0; mg < gridHeight; ++mg)
      for (var ng = 0; ng < gridWidth; ++ng) {
        var gray = levels[mg, ng] ^ (levels[mg, ng] >> 1);
        for (var j = 0; j < bitsPerValue; ++j)
          planes[j][mg][ng] = (byte)((gray >> j) & 1);
      }

    var region = new MemoryStream();
    _RegionInformation(region, _Width, _Height, 0, 0, 0);
    region.WriteByte((byte)(mmr ? 1 : 0));
    _Int(region, gridWidth);
    _Int(region, gridHeight);
    _Int(region, 0);
    _Int(region, 0);
    _Short(region, patternSize << 8);
    _Short(region, 0);
    if (mmr)
      for (var j = bitsPerValue - 1; j >= 0; --j)
        region.Write(FileFormat.Ccitt.CcittG4Encoder.Encode(_Packed(planes[j]), gridWidth, gridHeight));
    else {
      var coder = new _Coder();
      for (var j = bitsPerValue - 1; j >= 0; --j)
        _EncodeGeneric(coder, planes[j], 0, _NominalAt, false);
      region.Write(coder.Finish());
    }

    var stream = new _Stream();
    stream.Segment(48, _PageInformation(_Width, _Height));
    var dictionaryNumber = stream.Segment(16, dictionary.ToArray());
    stream.Segment(22, region.ToArray(), dictionaryNumber);
    return new(mmr ? "jbig2-halftone-mmr" : "jbig2-halftone", _Width, _Height, stream.ToArray(), null, picture);
  }

  /// <summary>A generic region drawn, then refined in place on the page with typical prediction.</summary>
  private static Fixture _Refinement(bool prediction) {
    var first = _Drawing();
    var second = first.Select(r => (byte[])r.Clone()).ToArray();
    for (var y = 20; y < 40; ++y)
      for (var x = 10; x < 50; ++x)
        if ((x * 7 + y * 3) % 11 == 0)
          second[y][x] ^= 1;

    var stream = new _Stream();
    stream.Segment(48, _PageInformation(_Width, _Height, flags: 1 << 6));
    var generic = new MemoryStream();
    _RegionInformation(generic, _Width, _Height, 0, 0, 0);
    generic.WriteByte(0);
    _WriteAt(generic, _NominalAt);
    var coder = new _Coder();
    _EncodeGeneric(coder, first, 0, _NominalAt, false);
    generic.Write(coder.Finish());
    stream.Segment(38, generic.ToArray());

    // Refine a window of the page, REPLACE so the refined pixels stand.
    const int left = 3, top = 16, width = 53, height = 30;
    var reference = first.Skip(top).Take(height).Select(r => r.Skip(left).Take(width).ToArray()).ToArray();
    var target = second.Skip(top).Take(height).Select(r => r.Skip(left).Take(width).ToArray()).ToArray();
    var refinement = new MemoryStream();
    _RegionInformation(refinement, width, height, left, top, 4);
    refinement.WriteByte((byte)(prediction ? 2 : 0));
    var at = new (int X, int Y)[] { (-1, -1), (-1, -1) };
    _WriteAt(refinement, at);
    var refiner = new _Coder();
    _EncodeRefinement(refiner, target, reference, 0, at, 0, 0, prediction);
    refinement.Write(refiner.Finish());
    stream.Segment(42, refinement.ToArray());
    return new(prediction ? "jbig2-refinement-tpgron" : "jbig2-refinement", _Width, _Height, stream.ToArray(), null, second);
  }

  /// <summary>
  /// A page with default pixel black and regions combined by XOR and AND under a
  /// combination-operator override; either striped with its height left unknown, or carrying an
  /// intermediate region refined into the page by XNOR.
  /// </summary>
  private static Fixture _Composite(bool striped, bool withIntermediate) {
    var page = _Blank(_Width, _Height);
    foreach (var row in page)
      Array.Fill(row, (byte)1);

    var stream = new _Stream();
    // Default pixel 1, override allowed, height unknown, striped with stripes of at most 32 rows.
    stream.Segment(48, striped
      ? _PageInformation(_Width, -1, flags: (1 << 2) | (1 << 6), striping: 0x8000 | 32)
      : _PageInformation(_Width, _Height, flags: (1 << 2) | (1 << 6)));

    void Generic(byte[][] bitmap, int x, int y, int combination) {
      var data = new MemoryStream();
      _RegionInformation(data, bitmap[0].Length, bitmap.Length, x, y, combination);
      data.WriteByte(1 << 1);
      _WriteAt(data, [(3, -1)]);
      var coder = new _Coder();
      _EncodeGeneric(coder, bitmap, 1, [(3, -1)], false);
      data.Write(coder.Finish());
      stream.Segment(38, data.ToArray());
    }

    var drawing = _Drawing();
    var top = drawing.Take(32).ToArray();
    Generic(top, 0, 0, 2);
    _Blit(page, top, 0, 0, 2);
    if (striped)
      stream.Segment(50, [0, 0, 0, 31]);

    var bottom = drawing.Skip(32).Select(r => r.Take(64).ToArray()).ToArray();
    Generic(bottom, 16, 32, 1);
    _Blit(page, bottom, 16, 32, 1);

    if (withIntermediate) {
      // An intermediate region, then a refinement of it placed with XNOR.
      var patch = _Glyphs()[5];
      var intermediate = new MemoryStream();
      _RegionInformation(intermediate, patch[0].Length, patch.Length, 30, 40, 0);
      intermediate.WriteByte(0);
      _WriteAt(intermediate, _NominalAt);
      var patchCoder = new _Coder();
      _EncodeGeneric(patchCoder, patch, 0, _NominalAt, false);
      intermediate.Write(patchCoder.Finish());
      var intermediateNumber = stream.Segment(36, intermediate.ToArray());

      var refined = patch.Select(r => (byte[])r.Clone()).ToArray();
      refined[2][3] ^= 1;
      refined[5][1] ^= 1;
      var refinement = new MemoryStream();
      _RegionInformation(refinement, patch[0].Length, patch.Length, 30, 40, 3);
      refinement.WriteByte(1);
      var refiner = new _Coder();
      _EncodeRefinement(refiner, refined, patch, 1, [], 0, 0, false);
      refinement.Write(refiner.Finish());
      stream.Segment(42, refinement.ToArray(), intermediateNumber);
      _Blit(page, refined, 30, 40, 3);

    }

    if (striped)
      stream.Segment(50, [0, 0, 0, (byte)(_Height - 1)]);
    stream.Segment(49, []);
    return new(striped ? "jbig2-striped" : "jbig2-intermediate", _Width, _Height, stream.ToArray(), null, page);
  }

  #endregion
}
