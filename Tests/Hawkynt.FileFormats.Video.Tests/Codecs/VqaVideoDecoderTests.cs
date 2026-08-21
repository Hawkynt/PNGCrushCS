using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The VQA decoder's codebook-and-index-table decode, its codebook rate control, and the version and
/// colour-depth refusals, on pictures built here byte by byte.
/// </summary>
/// <remarks>
/// Three real files — two Red Alert, one original Command &amp; Conquer, 320x156, 85 and 160 pictures,
/// 245 in all — were decoded here and by ffmpeg and compared sample for sample against ffmpeg's own
/// <c>rgb24</c> output: every picture is identical. What that comparison cannot reach on demand is
/// exercised here instead: a solid-fill block against a codebook-copy block, the index table's
/// two-half split read directly, and — the finding this decoder rests on — that a codebook finished
/// accumulating from its eighth <c>CBPZ</c> piece during picture N becomes current starting with
/// picture N+1, not picture N itself.
/// </remarks>
[TestFixture]
public sealed class VqaVideoDecoderTests {

  private const int _WIDTH = 8;
  private const int _HEIGHT = 2;
  private const int _BLOCK_WIDTH = 4;
  private const int _BLOCK_HEIGHT = 2;

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheVqaCodeIsTaken()
    => Assert.That(VqaVideoDecoder.Accepts(_Stream()), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cvid") };

    Assert.That(VqaVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream();

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Westwood VQA Video"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<VqaVideoDecoder>());
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void VersionOneRefuses() {
    var stream = _Stream(version: 1);

    var failure = Assert.Throws<NotSupportedException>(() => VqaVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("version 1"));
  }

  [Test]
  [Category("Unit")]
  public void TheHighColourFlagRefuses() {
    var stream = _Stream(highColour: true);

    Assert.Throws<NotSupportedException>(() => VqaVideoDecoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void APictureThatIsNotAWholeNumberOfBlocksRefuses() {
    var stream = _Stream(width: 9);

    Assert.Throws<NotSupportedException>(() => VqaVideoDecoder.Create(stream));
  }

  // ============================================================================================
  // Blocks
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASolidFillBlockPaintsOneColourAcrossTheWholeBlock() {
    var decoder = VqaVideoDecoder.Create(_Stream());
    var table = _Table(topValues: [7, 7], lowValues: [0x0f, 0x0f]); // one row of two blocks, both filled

    Assert.That(decoder.TryDecode(new(0, _Picture(codebook: [], palette: null, table)), out var picture), Is.True);

    Assert.That(picture.PixelData, Is.EqualTo(Enumerable.Repeat((byte)7, _WIDTH * _HEIGHT)));
  }

  [Test]
  [Category("Unit")]
  public void ACodebookEntryIsCopiedBlockForBlock() {
    var decoder = VqaVideoDecoder.Create(_Stream());
    // Entry 0: 4x2 bytes, distinct per pixel so row-major placement is checkable.
    byte[] codebook = [10, 11, 12, 13, 20, 21, 22, 23];
    var table = _Table(topValues: [0, 0x0f], lowValues: [0, 0x0f]); // block 0 = codebook entry 0; block 1 = solid fill (not checked here)

    Assert.That(decoder.TryDecode(new(0, _Picture(codebook, palette: null, table)), out var picture), Is.True);

    Assert.That(picture.PixelData[..4], Is.EqualTo(new byte[] { 10, 11, 12, 13 }));
    Assert.That(picture.PixelData[_WIDTH..(_WIDTH + 4)], Is.EqualTo(new byte[] { 20, 21, 22, 23 }));
  }

  // ============================================================================================
  // Codebook rate control
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APartialCodebookThatCompletesOnPictureNBecomesCurrentOnPictureNPlusOne() {
    var decoder = VqaVideoDecoder.Create(_Stream());
    var oldCodebook = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 };
    var newCodebook = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 };
    var table = _Table(topValues: [0, 0], lowValues: [0, 0]); // both blocks: codebook entry 0

    // Picture 0: full codebook (all 1s).
    decoder.TryDecode(new(0, _Picture(oldCodebook, palette: null, table)), out var first);
    Assert.That(first.PixelData, Is.EqualTo(Enumerable.Repeat((byte)1, _WIDTH * _HEIGHT)));

    // Pictures 1-7: seven of the eight pieces of a new codebook, plus this picture's own index table —
    // still built from the OLD codebook, since the new one has not finished accumulating yet.
    for (var i = 0; i < 7; ++i) {
      decoder.TryDecode(new(0, _PictureWithCodebookPiece(newCodebook.AsSpan(i, 1).ToArray(), table)), out var middle);
      Assert.That(middle.PixelData, Is.EqualTo(Enumerable.Repeat((byte)1, _WIDTH * _HEIGHT)), $"picture {i + 1}");
    }

    // Picture 8: the eighth and final piece arrives, completing the new codebook — but THIS picture
    // still reads the old one; the new one is not current until the picture after it.
    decoder.TryDecode(new(0, _PictureWithCodebookPiece(newCodebook.AsSpan(7, 1).ToArray(), table)), out var eighth);
    Assert.That(eighth.PixelData, Is.EqualTo(Enumerable.Repeat((byte)1, _WIDTH * _HEIGHT)), "the delivering picture itself");

    // Picture 9: the new codebook is current at last.
    decoder.TryDecode(new(0, _Picture(codebook: [], palette: null, table)), out var ninth);
    Assert.That(ninth.PixelData, Is.EqualTo(Enumerable.Repeat((byte)9, _WIDTH * _HEIGHT)), "the picture after the one that completed it");
  }

  // ============================================================================================
  // Palette
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASixBitPaletteEntryIsWidenedByRepeatingItsTopBits() {
    var decoder = VqaVideoDecoder.Create(_Stream());
    var palette = new byte[768];
    palette[0] = 63; // red of colour 0: six-bit maximum
    var table = _Table(topValues: [0, 0], lowValues: [0x0f, 0x0f]);

    decoder.TryDecode(new(0, _Picture(codebook: [], palette, table)), out var picture);

    Assert.That(picture.Palette![0], Is.EqualTo(255)); // (63 << 2) | (63 >> 4) = 255
  }

  /// <summary>Four real files from the original Command &amp; Conquer demo carry a 753-byte palette
  /// chunk — 251 colours, not the full 256 — and nothing past what a chunk states is touched.</summary>
  [Test]
  [Category("Unit")]
  public void APaletteChunkNamingFewerThanTwoHundredFiftySixColoursLeavesTheRestUntouched() {
    var decoder = VqaVideoDecoder.Create(_Stream());
    var shortPalette = new byte[6]; // two colours only
    shortPalette[0] = 63; // colour 0's red: six-bit maximum
    shortPalette[3] = 32; // colour 1's red
    var table = _Table(topValues: [0, 0], lowValues: [0x0f, 0x0f]);

    decoder.TryDecode(new(0, _Picture(codebook: [], shortPalette, table)), out var picture);

    Assert.That(picture.Palette![0], Is.EqualTo(255), "colour 0, stated");
    Assert.That(picture.Palette![3], Is.GreaterThan(0), "colour 1, stated");
    Assert.That(picture.Palette![6], Is.EqualTo(0), "colour 2, never stated, stays at its default");
  }

  // ============================================================================================
  // Malformed pictures
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnUncompressedIndexTableShorterThanThePictureNeedsRefusesByName() {
    var decoder = VqaVideoDecoder.Create(_Stream());
    var tooShortTable = new byte[] { 0 }; // the real table needs blocksWide*blocksHigh*2 = 4 bytes

    var chunks = new System.Collections.Generic.List<byte>();
    chunks.AddRange(_Chunk("VPT0", tooShortTable));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, chunks.ToArray()), out _));
    Assert.That(failure!.Message, Does.Contain("index table"));
    Assert.That(failure.Message, Does.Contain("4"));
    Assert.That(failure.Message, Does.Contain("1"));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(int version = 2, bool highColour = false, int width = _WIDTH) {
    var header = new byte[42];
    BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)version);
    header[2] = (byte)(highColour ? 0x10 : 0);
    header[10] = _BLOCK_WIDTH;
    header[11] = _BLOCK_HEIGHT;
    return new() {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("WSVQ"),
      Width = width, Height = _HEIGHT, CodecPrivateData = header,
    };
  }

  private static byte[] _Table(byte[] topValues, byte[] lowValues) => [.. topValues, .. lowValues];

  private static byte[] _Chunk(string id, byte[] payload) {
    var chunk = new byte[8 + payload.Length + (payload.Length & 1)];
    System.Text.Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
    BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)payload.Length);
    payload.CopyTo(chunk, 8);
    return chunk;
  }

  private static byte[] _Picture(byte[] codebook, byte[]? palette, byte[] table) {
    var chunks = new System.Collections.Generic.List<byte>();
    if (codebook.Length > 0)
      chunks.AddRange(_Chunk("CBF0", codebook));
    if (palette != null)
      chunks.AddRange(_Chunk("CPL0", palette));
    chunks.AddRange(_Chunk("VPT0", table));
    return chunks.ToArray();
  }

  private static byte[] _PictureWithCodebookPiece(byte[] piece, byte[] table) {
    var chunks = new System.Collections.Generic.List<byte>();
    chunks.AddRange(_Chunk("CBP0", piece));
    chunks.AddRange(_Chunk("VPT0", table));
    return chunks.ToArray();
  }
}
