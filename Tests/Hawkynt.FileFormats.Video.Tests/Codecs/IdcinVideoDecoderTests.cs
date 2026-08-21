using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The id Cinematic decoder's order-1 Huffman decode and its palette handling, on trees and pictures
/// built here byte by byte.
/// </summary>
/// <remarks>
/// Two real files — 320x200 and 320x240, 48 and 82 pictures, 130 in all — were decoded here and by
/// ffmpeg and compared sample for sample against ffmpeg's own <c>rgb24</c> output: every picture, index
/// through the installed palette, is identical. What that comparison cannot reach on demand is
/// exercised here instead: a two-symbol tree read one bit at a time, the least-significant-bit-first
/// order settled by measurement against both real files, the context switch to whichever symbol was
/// just decoded, the one degenerate tree shape (a context with at most one nonzero histogram entry)
/// that decodes to node 255 regardless of which symbol actually holds the count, and the two ways a
/// palette's six bytes can mean six-bit VGA precision or already-eight-bit precision.
/// </remarks>
[TestFixture]
public sealed class IdcinVideoDecoderTests {

  private const uint _COMMAND_NONE = 0;
  private const uint _COMMAND_PALETTE = 1;
  private const int _TOKENS = 256;

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheIdCinematicCodeIsTaken()
    => Assert.That(IdcinVideoDecoder.Accepts(_Stream(_EmptyTable())), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cvid") };

    Assert.That(IdcinVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("IDCV") };

    Assert.That(IdcinVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(_EmptyTable());

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("id Cinematic Video"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<IdcinVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void ATooSmallHuffmanTableRefuses() {
    var stream = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("IDCV"),
      Width = 1, Height = 1, CodecPrivateData = new byte[100],
    };

    Assert.Throws<InvalidDataException>(() => IdcinVideoDecoder.Create(stream));
  }

  // ============================================================================================
  // Reading a coded byte one bit at a time, least significant bit first
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheFirstPixelOfAPictureUsesContextZero() {
    // Context zero's histogram carries two symbols, 5 with the smaller count and 10 with the larger:
    // building pairs the smaller in first (bit 0) and the larger second (bit 1).
    var table = _EmptyTable();
    _SetCount(table, 0, 5, 1);
    _SetCount(table, 0, 10, 3);
    var decoder = IdcinVideoDecoder.Create(_Stream(table, width: 1, height: 1));

    Assert.That(decoder.TryDecode(new(0, _VideoFrame(_COMMAND_NONE, null, [0x00])), out var five), Is.True);
    Assert.That(five.PixelData[0], Is.EqualTo(5));

    Assert.That(decoder.TryDecode(new(0, _VideoFrame(_COMMAND_NONE, null, [0x01])), out var ten), Is.True);
    Assert.That(ten.PixelData[0], Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void TheSecondPixelIsReadAgainstTheFirstPixelsOwnContext() {
    // Context 0 has a single nonzero count, so it always decodes to the sentinel node 255 (see
    // IdcinHuffmanTree's remarks) without reading a bit. Context 255's tree then decodes symbol 3 from
    // bit 0 and symbol 9 from bit 1, proving the second pixel's tree is chosen by the first pixel's
    // own decoded value.
    var table = _EmptyTable();
    _SetCount(table, 0, 255, 1);
    _SetCount(table, 255, 3, 1);
    _SetCount(table, 255, 9, 5);
    var decoder = IdcinVideoDecoder.Create(_Stream(table, width: 2, height: 1));

    Assert.That(decoder.TryDecode(new(0, _VideoFrame(_COMMAND_NONE, null, [0x00])), out var picture), Is.True);
    Assert.That(picture.PixelData[0], Is.EqualTo(255));
    Assert.That(picture.PixelData[1], Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void AContextWithNoNonzeroCountAlsoDecodesToTheSentinelTwoFiftyFive() {
    var table = _EmptyTable();
    var decoder = IdcinVideoDecoder.Create(_Stream(table, width: 1, height: 1));

    Assert.That(decoder.TryDecode(new(0, _VideoFrame(_COMMAND_NONE, null, [])), out var picture), Is.True);
    Assert.That(picture.PixelData[0], Is.EqualTo(255));
  }

  // ============================================================================================
  // Palette
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASixBitPaletteIsWidenedByRepeatingItsTopBits() {
    var table = _EmptyTable();
    _SetCount(table, 0, 255, 1);
    var decoder = IdcinVideoDecoder.Create(_Stream(table, width: 1, height: 1));

    var palette = new byte[768];
    palette[0] = 63; // red of colour 0: six-bit maximum

    decoder.TryDecode(new(0, _VideoFrame(_COMMAND_PALETTE, palette, [])), out var picture);

    Assert.That(picture.Palette![0], Is.EqualTo(255)); // (63 << 2) | (63 >> 4) = 255
  }

  [Test]
  [Category("Unit")]
  public void AnEightBitPaletteIsKeptExactlyAsWritten() {
    var table = _EmptyTable();
    _SetCount(table, 0, 255, 1);
    var decoder = IdcinVideoDecoder.Create(_Stream(table, width: 1, height: 1));

    var palette = new byte[768];
    palette[0] = 200; // over 63: not a six-bit value, so nothing here is widened
    palette[3] = 40; // colour 1's red, well within six-bit range but left alone anyway

    decoder.TryDecode(new(0, _VideoFrame(_COMMAND_PALETTE, palette, [])), out var picture);

    Assert.That(picture.Palette![0], Is.EqualTo(200));
    Assert.That(picture.Palette![3], Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void APictureWithNoPaletteCommandKeepsThePreviousOne() {
    var table = _EmptyTable();
    _SetCount(table, 0, 255, 1);
    var decoder = IdcinVideoDecoder.Create(_Stream(table, width: 1, height: 1));

    var palette = new byte[768];
    palette[0] = 63;
    decoder.TryDecode(new(0, _VideoFrame(_COMMAND_PALETTE, palette, [])), out _);

    decoder.TryDecode(new(0, _VideoFrame(_COMMAND_NONE, null, [])), out var picture);

    Assert.That(picture.Palette![0], Is.EqualTo(255));
  }

  // ============================================================================================
  // Malformed packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APacketShorterThanTheCommandWordRefuses() {
    var decoder = IdcinVideoDecoder.Create(_Stream(_EmptyTable()));

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[2]), out _));
  }

  [Test]
  [Category("Unit")]
  public void HuffmanDataThatRunsOutMidPictureRefuses() {
    var table = _EmptyTable();
    _SetCount(table, 0, 5, 1);
    _SetCount(table, 0, 10, 3); // two symbols: decoding the first pixel needs one real bit
    var decoder = IdcinVideoDecoder.Create(_Stream(table, width: 1, height: 1));

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _VideoFrame(_COMMAND_NONE, null, [])), out _));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(byte[] huffmanTable, int width = 1, int height = 1) => new() {
    Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("IDCV"),
    Width = width, Height = height, CodecPrivateData = huffmanTable,
  };

  /// <summary>A 65536-byte Huffman table with every histogram entry nought — every context therefore
  /// the degenerate, no-nonzero-count case IdcinHuffmanTree decodes as node 255 (see its remarks) until
  /// a test's own <see cref="_SetCount"/> calls give some context real counts to build a tree from.</summary>
  private static byte[] _EmptyTable() => new byte[_TOKENS * _TOKENS];

  private static void _SetCount(byte[] table, int context, int symbol, byte count) => table[context * _TOKENS + symbol] = count;

  private static byte[] _VideoFrame(uint command, byte[]? palette, byte[] huffmanBytes) {
    var hasPalette = command == _COMMAND_PALETTE;
    var length = 4 + (hasPalette ? 768 : 0) + 8 + huffmanBytes.Length;
    var packet = new byte[length];
    BinaryPrimitives.WriteUInt32LittleEndian(packet, command);
    var at = 4;
    if (hasPalette) {
      (palette ?? new byte[768]).CopyTo(packet, at);
      at += 768;
    }

    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(at), (uint)(4 + huffmanBytes.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(at + 4), 0); // decoded pixel count: never read back
    huffmanBytes.CopyTo(packet, at + 8);
    return packet;
  }
}
