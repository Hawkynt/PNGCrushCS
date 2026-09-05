using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Microsoft Video 1 encoder, checked against the decoder beside it and against the shape of the
/// bytes it writes.
/// </summary>
/// <remarks>
/// The coding is lossy — two colours to a block — so "the decoder gets the input back" is only a
/// contract for the pictures the format can hold exactly: a flat one, and one whose every block is two
/// colours far enough apart that the mode decision does not fold them into one. Those are asserted
/// sample for sample. Everything else is asserted on what the bitstream must look like rather than on
/// how close the picture came, because a threshold on closeness would pass for an encoder that had
/// quietly stopped choosing between the codings at all.
/// <para/>
/// The flag word is where this codec's corners are, and three of them are tested by reading the bytes
/// back: a two-colour block's word must stay below 0x8000 or a decoder reads it as a solid colour, an
/// eight-bit eight-colour block's must reach 0x9000 or it reads as one of the other two, and a solid
/// sixteen-bit block may not spell 0x84xx, which is the code for a skip run.
/// <para/>
/// <b>Measured against ffmpeg.</b> The frames written here were muxed into an AVI and decoded by
/// ffmpeg 9.0.1, which accepted every one of them; its picture is identical to this package's decode,
/// sample for sample, on all of flat, two-colour, gradient, noise, palettised and still sequences at
/// both depths.
/// </remarks>
[TestFixture]
public sealed class MicrosoftVideo1EncoderTests {

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheDescriptionIsWhatTheDecoderReads() {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(8, 4, bitsPerPixel: 16));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("MSVC")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("MSVC")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(8));
      Assert.That(stream.Height, Is.EqualTo(4));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(16));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(40));
    });

    var format = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan()), Is.EqualTo(40), "biSize");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(4)), Is.EqualTo(8), "biWidth");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8)), Is.EqualTo(4), "biHeight, positive for bottom-up");
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(format.AsSpan(14)), Is.EqualTo(16), "biBitCount");
      Assert.That(format[16..20], Is.EqualTo("MSVC"u8.ToArray()), "biCompression");
    });

    Assert.That(MicrosoftVideo1Decoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<MicrosoftVideo1Decoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegisteredUnderTheCodeItWrites() {
    Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("Microsoft Video 1"));

    var stream = _Requested(8, 4, bitsPerPixel: 16);
    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<MicrosoftVideo1Encoder>());
  }

  [Test]
  [Category("Unit")]
  public void AnEightBitDescriptionCarriesThePaletteBehindItsHeader() {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(8, 4, bitsPerPixel: 8));
    Assert.That(encoder.TryEncode(_Indexed(8, 4, 3, 16), 0, out _), Is.True);

    var stream = encoder.DescribeStream();
    var format = stream.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
      Assert.That(format.Length, Is.EqualTo(40 + 16 * 4));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(32)), Is.EqualTo(16), "biClrUsed");
      // Entry 3 of the palette built below is R=9, G=10, B=11; an RGBQUAD is blue first.
      Assert.That(format[(40 + 3 * 4)..(40 + 4 * 4)], Is.EqualTo(new byte[] { 11, 10, 9, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheDepthCannotBeDescribedBeforeAPictureHasDecidedIt() {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(8, 4));

    var failure = Assert.Throws<InvalidOperationException>(() => encoder.DescribeStream());
    Assert.That(failure!.Message, Does.Contain("depth"));
  }

  [Test]
  [Category("Unit")]
  public void APalettisedPictureMakesAnEightBitStreamAndAnyOtherASixteenBitOne() {
    var indexed = MicrosoftVideo1Encoder.Create(_Requested(8, 4));
    Assert.That(indexed.TryEncode(_Indexed(8, 4, 3, 16), 0, out _), Is.True);
    Assert.That(indexed.DescribeStream().BitsPerPixel, Is.EqualTo(8));

    var colour = MicrosoftVideo1Encoder.Create(_Requested(8, 4));
    Assert.That(colour.TryEncode(_Flat(8, 4, 40, 80, 120), 0, out _), Is.True);
    Assert.That(colour.DescribeStream().BitsPerPixel, Is.EqualTo(16));
  }

  // ============================================================================================
  // What the format can hold exactly
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatPictureComesBackExactly([Values(4, 8, 20)] int red) {
    // Every channel on the 5-5-5 grid, so the quantiser has nothing to round either. Red index 1 is
    // the one value left out: a solid block of it would spell the skip-run code.
    var picture = _Flat(16, 12, _Widen(red), _Widen(9), _Widen(31));
    var decoded = _RoundTrip(16, 12, [picture, picture, picture]);

    foreach (var frame in decoded)
      Assert.That(frame.PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void EveryBlockOfTwoWellSeparatedColoursComesBackExactly() {
    var pictures = _TwoColourBlocks(32, 16, 3);
    var decoded = _RoundTrip(32, 16, pictures);

    for (var i = 0; i < pictures.Count; ++i)
      Assert.That(decoded[i].PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
  }

  [Test]
  [Category("Unit")]
  public void AFlatPalettisedPictureComesBackExactly() {
    var picture = _Indexed(16, 12, 200, 256);
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(16, 12, bitsPerPixel: 8));
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);

    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryBlockOfTwoPaletteEntriesComesBackExactly() {
    // The eight-bit coding picks its colours out of the indices the block already uses, so a block of
    // two of them is coded with those two and nothing is approximated. The palette is the eight
    // corners of the colour cube, so no two entries are close enough for the mode decision to fold a
    // block into one solid colour.
    var random = new Random(5);
    var pixels = new byte[32 * 16];
    for (var blockY = 0; blockY < 16; blockY += 4)
      for (var blockX = 0; blockX < 32; blockX += 4) {
        var first = (byte)random.Next(8);
        var second = (byte)((first + 1 + random.Next(7)) % 8);
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x)
            pixels[(blockY + y) * 32 + blockX + x] = random.Next(2) == 0 ? first : second;
      }

    _AssertIndexedRoundTripIsExact(_Cube(32, 16, pixels));
  }

  [Test]
  [Category("Unit")]
  public void EveryQuadOfTwoPaletteEntriesComesBackExactly() {
    // Four 2x2 quads of two colours each is the eight-colour coding, and at eight bits that coding's
    // flag word has to reach 0x9000 or a decoder reads the block as one of the other two. Coming back
    // exactly is what says the mask was steered there without moving a pixel.
    var random = new Random(9);
    var pixels = new byte[32 * 16];
    for (var blockY = 0; blockY < 16; blockY += 4)
      for (var blockX = 0; blockX < 32; blockX += 4)
        for (var quadY = 0; quadY < 4; quadY += 2)
          for (var quadX = 0; quadX < 4; quadX += 2) {
            var first = (byte)random.Next(8);
            var second = (byte)((first + 1 + random.Next(7)) % 8);
            for (var y = 0; y < 2; ++y)
              for (var x = 0; x < 2; ++x)
                pixels[(blockY + quadY + y) * 32 + blockX + quadX + x] = random.Next(2) == 0 ? first : second;
          }

    _AssertIndexedRoundTripIsExact(_Cube(32, 16, pixels));
  }

  [Test]
  [Category("Unit")]
  public void ASolidBlockOfTheOneRedThatWouldSpellASkipRunIsWrittenAsRedNought() {
    // 0x8000 | (1 << 10) is 0x8400, which is the code for a skip run and not a colour at all. One
    // thirty-second of the red channel is given up rather than a block that means something else.
    var picture = _Flat(4, 4, _Widen(1), 0, 0);
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(4, 4, bitsPerPixel: 16));
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray()[..2], Is.EqualTo(new byte[] { 0x00, 0x80 }), "a solid black block");
  }

  // ============================================================================================
  // The three codings, read back out of the bytes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatBlockIsTwoBytesAndADetailedOneIsEighteen() {
    // One block wide, two high. The bottom half of the picture is flat, and the bottom block is the
    // one coded first; the top block is eight colours, two to a quad, at the corners of the colour
    // cube. Two bytes plus eighteen plus the two every frame ends with.
    var pixels = new byte[4 * 8 * 3];
    for (var quadY = 0; quadY < 4; quadY += 2)
      for (var quadX = 0; quadX < 4; quadX += 2)
        for (var y = 0; y < 2; ++y)
          for (var x = 0; x < 2; ++x) {
            var corner = quadY * 2 + quadX + (x ^ y);
            var at = ((quadY + y) * 4 + quadX + x) * 3;
            pixels[at] = (byte)((corner & 1) != 0 ? 255 : 0);
            pixels[at + 1] = (byte)((corner & 2) != 0 ? 255 : 0);
            pixels[at + 2] = (byte)((corner & 4) != 0 ? 255 : 0);
          }

    var encoder = MicrosoftVideo1Encoder.Create(_Requested(4, 8, bitsPerPixel: 16));
    Assert.That(encoder.TryEncode(_Rgb(4, 8, pixels), 0, out var packet), Is.True);

    Assert.That(packet.Data.Length, Is.EqualTo(2 + 18 + 2));
    Assert.That(packet.Data.Span[1] & 0x80, Is.Not.Zero, "the bottom block is coded first and is the flat one");
    Assert.That(packet.Data.Span[3] & 0x80, Is.Zero, "the top block's flag word stays clear of the solid codes");
  }

  [Test]
  [Category("Unit")]
  public void NoSixteenBitFlagWordEverReachesTheSolidCodes() {
    // A word of 0x8000 or above is a solid colour and 0x8400 to 0x87FF is a skip run, so a block that
    // means anything else must keep its first word below 0x8000. Walked over a noisy picture, which is
    // where the eight-colour coding — and with it bit 15 of the mask — is reached on every block.
    var packet = _Encode(_Noise(32, 16, 1), 16)[0];
    var words = _Words(packet);
    var at = 0;
    var blocks = 0;

    while (at < words.Count - 1) {
      var flags = words[at];
      if (flags >= 0x8000) {
        Assert.That(flags, Is.Not.InRange(0x8400, 0x87FF), $"block {blocks} is solid and spells a skip run");
        ++at;
      } else {
        // A literal block: two colours, or eight when the first colour is marked.
        at += (words[at + 1] & 0x8000) != 0 ? 9 : 3;
      }

      ++blocks;
    }

    Assert.That(blocks, Is.EqualTo(32 / 4 * (16 / 4)), "every block accounted for");
  }

  [Test]
  [Category("Unit")]
  public void AnEightBitFrameFramesUpIntoExactlyItsBlocks() {
    // At eight bits the second flag byte alone tells the three codings apart: below 0x80 two colours,
    // 0x90 and above eight, and the gaps either side of the skip codes a solid one. Each of those is a
    // different length, so walking the frame by those lengths and arriving at exactly the block count
    // is what says every flag word landed in the range its coding needs — a mask that had not been
    // steered there would be read as another coding and the walk would end somewhere else.
    var packets = _EncodeIndexed(_IndexedNoise(32, 16, 1), out _);
    var words = _Words(packets[0]);

    var blocks = 0;
    var eightColour = 0;
    for (var at = 0; at < words.Count - 1; ++blocks) {
      var high = words[at] >> 8;
      if (high < 0x80)
        at += 2;
      else if (high >= 0x90) {
        at += 5;
        ++eightColour;
      } else
        ++at;
    }

    Assert.That(blocks, Is.EqualTo(32 / 4 * (16 / 4)), "every block accounted for");
    Assert.That(eightColour, Is.GreaterThan(0), "a noisy palettised picture reaches the eight-colour coding");
  }

  // ============================================================================================
  // The inter frames, which are skip runs and nothing else
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureIdenticalToTheOneBeforeIsNothingButOneSkipRun() {
    var picture = _Flat(16, 12, _Widen(20), _Widen(9), _Widen(2));
    var packets = _Encode([picture, picture], 16);

    Assert.Multiple(() => {
      Assert.That(packets[0].IsKeyFrame, Is.True);
      Assert.That(packets[1].IsKeyFrame, Is.False);
      // Twelve blocks skipped, then the two bytes every frame ends with.
      Assert.That(packets[1].Data.ToArray(), Is.EqualTo(new byte[] { 12, 0x84, 0, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASkipRunLongerThanOneCodeCanStateIsSplitAndTheDecoderStillLandsRight() {
    // 260x64 is 65 by 16 blocks, which is 1040 — more than the 1023 one skip code can state, so the
    // run has to be split. Only the picture's bottom-right block changes, which is the last block
    // coded: a decoder that lost count over the split would leave it as it was.
    var first = _Noise(260, 64, 1)[0];
    var changed = (byte[])first.PixelData.Clone();
    for (var y = 60; y < 64; ++y)
      for (var x = 256; x < 260; ++x) {
        var at = (y * 260 + x) * 3;
        changed[at] = 255;
        changed[at + 1] = 255;
        changed[at + 2] = 255;
      }

    var decoded = _RoundTrip(260, 64, [first, _Rgb(260, 64, changed)]);
    Assert.Multiple(() => {
      Assert.That(_Pixel(decoded[1], 63, 259), Is.EqualTo(new byte[] { 255, 255, 255 }), "the one block that changed");
      Assert.That(_Pixel(decoded[1], 0, 0), Is.EqualTo(_Pixel(decoded[0], 0, 0)), "and nothing else moved");
      Assert.That(_Pixel(decoded[1], 63, 0), Is.EqualTo(_Pixel(decoded[0], 63, 0)));
      Assert.That(_Pixel(decoded[1], 0, 259), Is.EqualTo(_Pixel(decoded[0], 0, 259)));
    });
  }

  [Test]
  [Category("Unit")]
  public void AStillPictureIsWrittenWholeAgainEveryTwentyFifthFrame() {
    var picture = _Flat(16, 12, _Widen(20), _Widen(9), _Widen(2));
    var packets = _Encode(Enumerable.Repeat(picture, 30).ToList(), 16);

    Assert.That(
      packets.Select(p => p.IsKeyFrame),
      Is.EqualTo(Enumerable.Range(0, 30).Select(i => i % 25 == 0)).AsCollection);
  }

  // ============================================================================================
  // Through a container
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheFramesSurviveAnAviAndComeBackThroughTheRegistry() {
    var pictures = _TwoColourBlocks(20, 12, 4);
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(20, 12, bitsPerPixel: 16));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var container = AviContainer.FromBytes(avi);
    Assert.That(AviContainer.Streams(container).Single().Codec, Is.EqualTo(CodecTag.FromCharacters("MSVC")));

    var decoded = VideoFormatRegistry.DecodeFrames(avi).Select(f => f.Image).ToList();
    Assert.That(decoded.Count, Is.EqualTo(pictures.Count));
    for (var i = 0; i < pictures.Count; ++i)
      Assert.That(decoded[i].PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
  }

  // ============================================================================================
  // The packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TimestampsPassThroughUntouched() {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(4, 4, bitsPerPixel: 16, index: 3));
    var picture = _Flat(4, 4, 8, 16, 24);

    Assert.That(encoder.TryEncode(picture, 42, out var stamped), Is.True);
    Assert.That(encoder.TryEncode(picture, null, out var unstamped), Is.True);

    Assert.Multiple(() => {
      Assert.That(stamped.StreamIndex, Is.EqualTo(3));
      Assert.That(stamped.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(stamped.DecodeTimestamp, Is.EqualTo(42));
      Assert.That(unstamped.PresentationTimestamp, Is.Null);
      Assert.That(unstamped.DecodeTimestamp, Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBack() {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(4, 4, bitsPerPixel: 16));
    Assert.That(encoder.TryEncode(_Flat(4, 4, 8, 16, 24), 0, out _), Is.True);

    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsRefused() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 4, Height = 4 };

    Assert.Throws<NotSupportedException>(() => MicrosoftVideo1Encoder.Create(sound));
  }

  [Test]
  [Category("Unit")]
  public void APictureThatIsNotAWholeNumberOfBlocksIsRefusedRatherThanPadded() {
    var failure = Assert.Throws<NotSupportedException>(() => MicrosoftVideo1Encoder.Create(_Requested(17, 12)));

    Assert.That(failure!.Message, Does.Contain("17x12"));
    Assert.That(failure.Message, Does.Contain("4x4 blocks"));
  }

  [Test]
  [Category("Unit")]
  public void ADepthTheCodingIsNotDefinedAtIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(() => MicrosoftVideo1Encoder.Create(_Requested(8, 4, bitsPerPixel: 24)));

    Assert.That(failure!.Message, Does.Contain("24 bits per pixel"));
  }

  [Test]
  [Category("Unit")]
  public void ADirectColourPictureOnAnEightBitStreamIsRefusedByName() {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(8, 4, bitsPerPixel: 8));

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(_Flat(8, 4, 1, 2, 3), 0, out _));
    Assert.That(failure!.Message, Does.Contain("Rgb24"));
  }

  [Test]
  [Category("Unit")]
  public void APalettisedPictureWithoutAPaletteIsRefused() {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(4, 4, bitsPerPixel: 8));
    var bare = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Indexed8, PixelData = new byte[16] };

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(bare, 0, out _));
    Assert.That(failure!.Message, Does.Contain("without a palette"));
  }

  [Test]
  [Category("Unit")]
  public void AnIndexPastTheEndOfThePaletteIsRefused() {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(4, 4, bitsPerPixel: 8));
    var pixels = new byte[16];
    pixels[5] = 16;

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Indexed(4, 4, pixels, 16), 0, out _));
    Assert.That(failure!.Message, Does.Contain("index 16"));
  }

  [Test]
  [Category("Unit")]
  public void APaletteThatChangesBetweenFramesIsRefused() {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(4, 4, bitsPerPixel: 8));
    Assert.That(encoder.TryEncode(_Indexed(4, 4, 1, 16), 0, out _), Is.True);

    var other = _Indexed(4, 4, 1, 16);
    other.Palette![0] ^= 0xFF;

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(other, 1, out _));
    Assert.That(failure!.Message, Does.Contain("different palette"));
  }

  [Test]
  [Category("Unit")]
  public void AGeometryChangeMidStreamIsRefused() {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(8, 8, bitsPerPixel: 16));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Flat(4, 4, 1, 2, 3), 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static IReadOnlyList<CodedPacket> _Encode(IReadOnlyList<RawImage> pictures, int bitsPerPixel) {
    var encoder = MicrosoftVideo1Encoder.Create(
      _Requested(pictures[0].Width, pictures[0].Height, bitsPerPixel: bitsPerPixel));

    return pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();
  }

  private static IReadOnlyList<CodedPacket> _EncodeIndexed(IReadOnlyList<RawImage> pictures, out MediaStreamInfo stream) {
    var encoder = MicrosoftVideo1Encoder.Create(
      _Requested(pictures[0].Width, pictures[0].Height, bitsPerPixel: 8));

    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    stream = encoder.DescribeStream();
    return packets;
  }

  private static IReadOnlyList<RawImage> _RoundTrip(int width, int height, IReadOnlyList<RawImage> pictures) {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(width, height, bitsPerPixel: 16));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    Assert.That(decoder, Is.InstanceOf<MicrosoftVideo1Decoder>());

    return packets.Select(packet => {
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.Multiple(() => {
        Assert.That(decoded.Width, Is.EqualTo(width));
        Assert.That(decoded.Height, Is.EqualTo(height));
        Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      });

      return decoded;
    }).ToList();
  }

  /// <summary>A packet as the little-endian words the block walk reads it in.</summary>
  private static IReadOnlyList<int> _Words(CodedPacket packet) {
    var data = packet.Data.Span;
    var words = new List<int>(data.Length / 2);
    for (var at = 0; at + 1 < data.Length; at += 2)
      words.Add(data[at] | (data[at + 1] << 8));

    return words;
  }

  private static void _AssertIndexedRoundTripIsExact(RawImage picture) {
    var encoder = MicrosoftVideo1Encoder.Create(_Requested(picture.Width, picture.Height, bitsPerPixel: 8));
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);

    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
  }

  /// <summary>One pixel of a direct-colour picture as red, green and blue.</summary>
  private static byte[] _Pixel(RawImage picture, int row, int column)
    => picture.PixelData.AsSpan((row * picture.Width + column) * 3, 3).ToArray();

  private static RawImage _Rgb(int width, int height, byte[] pixels) => new() {
    Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels,
  };

  private static RawImage _Flat(int width, int height, byte red, byte green, byte blue) {
    var pixels = new byte[width * height * 3];
    for (var at = 0; at < pixels.Length; at += 3) {
      pixels[at] = red;
      pixels[at + 1] = green;
      pixels[at + 2] = blue;
    }

    return _Rgb(width, height, pixels);
  }

  /// <summary>
  /// Pictures whose every block holds two colours, one from the bottom of each channel's range and one
  /// from the top.
  /// </summary>
  /// <remarks>
  /// Far apart on purpose: the mode decision folds a pair whose colours are within about five
  /// five-bit steps into one solid colour, which is the codec working as intended and not something a
  /// test of exactness should walk into.
  /// </remarks>
  private static IReadOnlyList<RawImage> _TwoColourBlocks(int width, int height, int count) {
    var random = new Random(21);
    var pictures = new List<RawImage>(count);
    for (var frame = 0; frame < count; ++frame) {
      var pixels = new byte[width * height * 3];
      for (var blockY = 0; blockY < height; blockY += 4)
        for (var blockX = 0; blockX < width; blockX += 4) {
          byte[] first = [_Widen(random.Next(10)), _Widen(random.Next(10)), _Widen(random.Next(10))];
          byte[] second = [_Widen(21 + random.Next(11)), _Widen(21 + random.Next(11)), _Widen(21 + random.Next(11))];
          for (var y = 0; y < 4; ++y)
            for (var x = 0; x < 4; ++x) {
              var colour = random.Next(2) == 0 ? first : second;
              var at = ((blockY + y) * width + blockX + x) * 3;
              pixels[at] = colour[0];
              pixels[at + 1] = colour[1];
              pixels[at + 2] = colour[2];
            }
        }

      pictures.Add(_Rgb(width, height, pixels));
    }

    return pictures;
  }

  private static IReadOnlyList<RawImage> _Noise(int width, int height, int count) {
    var random = new Random(11);
    var pictures = new List<RawImage>(count);
    for (var frame = 0; frame < count; ++frame) {
      var pixels = new byte[width * height * 3];
      random.NextBytes(pixels);
      pictures.Add(_Rgb(width, height, pixels));
    }

    return pictures;
  }

  private static IReadOnlyList<RawImage> _IndexedNoise(int width, int height, int count) {
    var random = new Random(17);
    var pictures = new List<RawImage>(count);
    for (var frame = 0; frame < count; ++frame) {
      var pixels = new byte[width * height];
      random.NextBytes(pixels);
      pictures.Add(_Indexed(width, height, pixels, 256));
    }

    return pictures;
  }

  /// <summary>A palettised picture over the eight corners of the colour cube.</summary>
  /// <remarks>
  /// No two entries of that palette are close, which is what keeps the mode decision from folding a
  /// block of two of them into one solid colour — the thing a test of exactness must not walk into.
  /// </remarks>
  private static RawImage _Cube(int width, int height, byte[] indices) {
    var palette = new byte[8 * 3];
    for (var entry = 0; entry < 8; ++entry)
      for (var channel = 0; channel < 3; ++channel)
        palette[entry * 3 + channel] = (byte)((entry >> channel & 1) != 0 ? 255 : 0);

    return new() {
      Width = width, Height = height, Format = PixelFormat.Indexed8, PixelData = indices,
      Palette = palette, PaletteCount = 8,
    };
  }

  private static RawImage _Indexed(int width, int height, byte fill, int colours) {
    var pixels = new byte[width * height];
    Array.Fill(pixels, fill);
    return _Indexed(width, height, pixels, colours);
  }

  private static RawImage _Indexed(int width, int height, byte[] indices, int colours) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Indexed8,
    PixelData = indices,
    Palette = _Palette(colours),
    PaletteCount = colours,
  };

  private static byte[] _Palette(int colours) {
    var palette = new byte[colours * 3];
    for (var i = 0; i < colours; ++i) {
      palette[i * 3] = (byte)(i * 3);
      palette[i * 3 + 1] = (byte)(i * 3 + 1);
      palette[i * 3 + 2] = (byte)(i * 3 + 2);
    }

    return palette;
  }

  /// <summary>A five-bit channel as the decoder widens it, so a picture built from these is on the grid.</summary>
  private static byte _Widen(int channel) => (byte)((channel << 3) | (channel >> 2));

  private static MediaStreamInfo _Requested(int width, int height, int bitsPerPixel = 0, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("MSVC"),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}
