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
/// The Cinepak encoder, checked against the decoder beside it and against the shape of the bytes it
/// writes.
/// </summary>
/// <remarks>
/// The coding is lossy — four luminances and one chrominance pair for sixteen pixels — so "the decoder
/// gets the input back" is only a contract for the pictures the format can hold: a flat one in a colour
/// the colour space can state, and one whose every 2x2 square is one of those colours. Those are
/// asserted sample for sample. Everything else is asserted on what the bitstream must look like rather
/// than on how close the picture came, because a threshold on closeness would pass for an encoder that
/// had quietly stopped choosing between the codings at all.
/// <para/>
/// <b>Which colours are exact.</b> The inverse matrix doubles the red and blue differences, so a flat
/// colour is stateable only where a luminance and a chrominance pair land on it — 2669700 of the
/// 16777216, by exhaustive search of the decoder's own arithmetic. All 256 greys and all eight corners
/// of the colour cube are among them. Swept over a 64-by-64-by-64 grid of flat colours, 82972 of the
/// 262144 came back exactly and every one of the rest was out by exactly one level on at least one
/// channel and by no more than one anywhere, which is what
/// <see cref="AFlatPictureOfAnyColourComesBackWithinOneLevel"/> holds to.
/// <para/>
/// <b>Measured against ffmpeg.</b> Thirteen sequences — flat grey, flat red, flat blue, colour-cube
/// blocks, vertical bars, a grey ramp, a colour gradient, noise, a still, a pan, a tall narrow picture,
/// and ffmpeg's own <c>testsrc2</c> and <c>mandelbrot</c> at 320x240 — 62 frames in all, were muxed
/// into AVIs and decoded by ffmpeg 9.0.1. It accepted every frame of every file and its picture is
/// identical to this package's decode, sample for sample, on all thirteen: 0 differing bytes of
/// 5716992. Between them they cover all three vector chunks (0x30, 0x31, 0x32), the colour and the grey
/// codebook chunks (0x20/0x22 and 0x24/0x26), intra and inter strips, and frames of one strip and of
/// two.
/// <para/>
/// <b>Against ffmpeg's own encoder</b>, on the same sources, this one is smaller on eleven of the
/// thirteen and the same size on the other two, and closer to the source on ten, level on one and
/// behind on two — the grey ramp, where ffmpeg spends two and a half times the bytes to gain 1.65 dB,
/// and the tall gradient, by 0.01 dB at two thirds the size. The two 320x240 sequences:
/// <c>testsrc2</c> 55646 bytes at 51.15 dB against ffmpeg's 76018 at 44.35, and <c>mandelbrot</c>
/// 92974 at 24.61 against 145055 at 24.24. Encoding those ten frames takes about twice as long as
/// ffmpeg's encoder takes.
/// </remarks>
[TestFixture]
public sealed class CinepakVideoEncoderTests {

  private const int _FRAME_HEADER_LENGTH = 10;
  private const int _STRIP_HEADER_LENGTH = 12;
  private const int _CHUNK_HEADER_LENGTH = 4;

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheDescriptionIsWhatTheDecoderReads() {
    var encoder = CinepakVideoEncoder.Create(_Requested(16, 12));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("cvid")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("cvid")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(16));
      Assert.That(stream.Height, Is.EqualTo(12));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(40));
    });

    var format = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan()), Is.EqualTo(40), "biSize");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(4)), Is.EqualTo(16), "biWidth");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8)), Is.EqualTo(12), "biHeight");
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(format.AsSpan(14)), Is.EqualTo(24), "biBitCount");
      Assert.That(format[16..20], Is.EqualTo("cvid"u8.ToArray()), "biCompression");
    });

    Assert.That(CinepakVideoDecoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<CinepakVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegisteredUnderTheCodeItWrites() {
    Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("Cinepak"));

    var stream = _Requested(16, 12);
    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<CinepakVideoEncoder>());
  }

  // ============================================================================================
  // What the format can hold exactly
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatGreyPictureComesBackExactly([Values(0, 1, 17, 128, 199, 255)] int level) {
    // Every grey is stateable: both chrominances are nought and the luminance is the level itself.
    var picture = _Flat(32, 24, (byte)level, (byte)level, (byte)level);
    var decoded = _RoundTrip(32, 24, [picture, picture, picture]);

    foreach (var frame in decoded)
      Assert.That(frame.PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AFlatPictureOfAnyCornerOfTheColourCubeComesBackExactly([Range(0, 7)] int corner) {
    // The corners need the chrominance pushed against a stop to state them — a flat red is luminance 1
    // with both differences at the ends of their range, which is nowhere near what the forward
    // transform rounds to. Coming back exactly is what says the entry was solved rather than converted.
    var (red, green, blue) = _Corner(corner);
    var picture = _Flat(32, 24, red, green, blue);

    Assert.That(_RoundTrip(32, 24, [picture]).Single().PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void EveryBlockOfColourCubeQuadrantsComesBackExactly() {
    // One corner of the colour cube per 2x2 square, which is exactly what a V4 entry states: four
    // pixels of one colour, one entry a quadrant. Nothing here is approximated, so the whole picture
    // has to survive — and it is where the V4 coding and its flag bits are reached.
    var random = new Random(31);
    var pixels = new byte[32 * 24 * 3];
    for (var y = 0; y < 24; y += 2)
      for (var x = 0; x < 32; x += 2) {
        var (red, green, blue) = _Corner(random.Next(8));
        for (var row = 0; row < 2; ++row)
          for (var column = 0; column < 2; ++column) {
            var at = ((y + row) * 32 + x + column) * 3;
            pixels[at] = red;
            pixels[at + 1] = green;
            pixels[at + 2] = blue;
          }
      }

    var picture = _Rgb(32, 24, pixels);
    Assert.That(_RoundTrip(32, 24, [picture]).Single().PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AFlatPictureOfAnyColourComesBackWithinOneLevel() {
    // Most colours are not stateable at all, and the guarantee for those is that the miss is one level
    // on a channel and never two. Anything looser would pass for a solver that had stopped searching.
    var random = new Random(7);
    for (var attempt = 0; attempt < 24; ++attempt) {
      var red = (byte)random.Next(256);
      var green = (byte)random.Next(256);
      var blue = (byte)random.Next(256);
      var picture = _Flat(16, 12, red, green, blue);
      var decoded = _RoundTrip(16, 12, [picture]).Single();

      for (var at = 0; at < decoded.PixelData.Length; ++at)
        Assert.That(
          Math.Abs(decoded.PixelData[at] - picture.PixelData[at]), Is.LessThanOrEqualTo(1),
          $"sample {at} of a flat {red},{green},{blue}");
    }
  }

  // ============================================================================================
  // The inter frames
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureIdenticalToTheOneBeforeIsNothingButSkips() {
    var picture = _Flat(32, 24, 60, 60, 60);
    var packets = _Encode([picture, picture]);
    var frame = packets[1].Data.ToArray();

    Assert.Multiple(() => {
      Assert.That(packets[0].IsKeyFrame, Is.True);
      Assert.That(packets[1].IsKeyFrame, Is.False);
      Assert.That(frame[0], Is.EqualTo(1), "the frame inherits the codebooks it does not restate");
    });

    var strips = _Strips(frame);
    Assert.That(strips, Has.Count.EqualTo(1));
    Assert.That(strips[0].Identifier, Is.EqualTo(0x11), "an inter-coded strip");

    var chunks = strips[0].Chunks;
    Assert.Multiple(() => {
      Assert.That(chunks.Select(c => c.Type), Is.EqualTo(new[] { 0x20, 0x22, 0x31 }).AsCollection);
      Assert.That(chunks[0].Length, Is.EqualTo(_CHUNK_HEADER_LENGTH), "no V4 entry is restated");
      Assert.That(chunks[1].Length, Is.EqualTo(_CHUNK_HEADER_LENGTH), "and no V1 entry either");
      // Forty-eight blocks, one bit each, which is two words of flags and no vector bytes at all.
      Assert.That(chunks[2].Length, Is.EqualTo(_CHUNK_HEADER_LENGTH + 8));
    });
  }

  [Test]
  [Category("Unit")]
  public void AStillPictureIsWrittenWholeAgainEveryTwentyFifthFrame() {
    var picture = _Flat(16, 12, 40, 40, 40);
    var packets = _Encode(Enumerable.Repeat(picture, 30).ToList());

    Assert.That(
      packets.Select(p => p.IsKeyFrame),
      Is.EqualTo(Enumerable.Range(0, 30).Select(i => i % 25 == 0)).AsCollection);
  }

  [Test]
  [Category("Unit")]
  public void OnlyTheBlocksThatChangedAreRestated() {
    // One block of a flat picture turns white. A skipped block says nothing, so what the second frame
    // costs is two words of flags, one changed block's bit pair, and the one codebook entry it needs.
    var first = _Flat(32, 24, 100, 100, 100);
    var changed = (byte[])first.PixelData.Clone();
    for (var y = 4; y < 8; ++y)
      for (var x = 8; x < 12; ++x) {
        var at = (y * 32 + x) * 3;
        changed[at] = 255;
        changed[at + 1] = 255;
        changed[at + 2] = 255;
      }

    var pictures = new[] { first, _Rgb(32, 24, changed) };
    var decoded = _RoundTrip(32, 24, pictures);

    Assert.Multiple(() => {
      Assert.That(decoded[1].PixelData, Is.EqualTo(changed), "both colours are stateable, so nothing is lost");
      Assert.That(_Encode(pictures)[1].Data.Length, Is.LessThan(48), "and nothing but that block is written");
    });
  }

  // ============================================================================================
  // The shape of the bytes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFrameStatesItsOwnLengthSizeAndStripCount() {
    var packet = _Encode([_Noise(64, 48, 3)])[0];
    var frame = packet.Data.ToArray();

    Assert.Multiple(() => {
      Assert.That(frame[0], Is.EqualTo(0), "a whole picture states no inheritance");
      Assert.That((frame[1] << 16) | (frame[2] << 8) | frame[3], Is.EqualTo(frame.Length));
      Assert.That(_Be16(frame, 4), Is.EqualTo(64));
      Assert.That(_Be16(frame, 6), Is.EqualTo(48));
      Assert.That(_Be16(frame, 8), Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryStripStatesItsRowsRelativelyAndCoversTheWholeWidth() {
    // 320x240 is 4800 blocks, which is more than one codebook's worth, so the frame is cut in two. Both
    // strips state a top of nought and a bottom of their own height — the relative form every encoder
    // writes and every decoder puts one strip under the last.
    var frame = _Encode([_Noise(320, 240, 5)])[0].Data.ToArray();
    var strips = _Strips(frame);

    Assert.That(strips, Has.Count.EqualTo(2));
    Assert.That(_Be16(frame, 8), Is.EqualTo(2), "and the header says so");

    foreach (var strip in strips)
      Assert.Multiple(() => {
        Assert.That(strip.Identifier, Is.EqualTo(0x10));
        Assert.That(strip.Top, Is.EqualTo(0));
        Assert.That(strip.Left, Is.EqualTo(0));
        Assert.That(strip.Bottom, Is.EqualTo(120), "each strip is half of the 240 rows");
        Assert.That(strip.Right, Is.EqualTo(320));
      });

    Assert.That(strips.Sum(s => s.Length), Is.EqualTo(frame.Length - _FRAME_HEADER_LENGTH));
  }

  [Test]
  [Category("Unit")]
  public void EveryStripCarriesBothCodebooksAndExactlyOneVectorChunk() {
    foreach (var picture in new[] { _Noise(64, 48, 11), _Flat(64, 48, 90, 90, 90), _Gradient(64, 48) })
      foreach (var strip in _Strips(_Encode([picture])[0].Data.ToArray())) {
        var types = strip.Chunks.Select(c => c.Type).ToArray();

        Assert.That(types, Has.Length.EqualTo(3));
        Assert.That(types[0], Is.AnyOf(0x20, 0x24), "the V4 codebook comes first, colour or grey");
        Assert.That(types[1], Is.AnyOf(0x22, 0x26), "then the V1 codebook");
        Assert.That(types[2], Is.AnyOf(0x30, 0x31, 0x32), "then exactly one vector chunk");
        Assert.That(strip.Chunks.Sum(c => c.Length), Is.EqualTo(strip.Length - _STRIP_HEADER_LENGTH));
      }
  }

  [Test]
  [Category("Unit")]
  public void AGreyPictureIsWrittenWithTheGreyCodebookChunks() {
    // Both chrominances are nought for every entry, and the grey chunks are the form that says so in
    // four bytes an entry instead of six.
    var picture = _Rgb(128, 96, _Fill(128, 96, (x, y) => {
      var level = (byte)((x * 2 + y) & 0xFF);
      return (level, level, level);
    }));

    var frame = _Encode([picture])[0].Data.ToArray();
    var strip = _Strips(frame).Single();
    var (v1Used, v4Used) = _References(frame, strip);

    Assert.Multiple(() => {
      Assert.That(v1Used, Is.Not.Empty, "the picture reaches the V1 coding");
      Assert.That(v4Used, Is.Not.Empty, "and the V4 coding, so both codebooks say something");
      Assert.That(strip.Chunks[0].Type, Is.EqualTo(0x24));
      Assert.That(strip.Chunks[1].Type, Is.EqualTo(0x26));
      Assert.That((strip.Chunks[0].Length - _CHUNK_HEADER_LENGTH) / 4, Is.EqualTo(v4Used.Count), "four bytes an entry");
      Assert.That((strip.Chunks[1].Length - _CHUNK_HEADER_LENGTH) / 4, Is.EqualTo(v1Used.Count));
    });
  }

  [Test]
  [Category("Unit")]
  public void ACodebookHoldsNothingNoBlockAsksFor() {
    // Entries are renumbered on the way out so that what is written is a run from nought with no gaps.
    // A codebook longer than the references in the strip would be bytes nothing ever reads.
    var frame = _Encode([_Gradient(64, 48)])[0].Data.ToArray();
    var strip = _Strips(frame).Single();
    var v4Entries = (strip.Chunks[0].Length - _CHUNK_HEADER_LENGTH) / (strip.Chunks[0].Type == 0x24 ? 4 : 6);
    var v1Entries = (strip.Chunks[1].Length - _CHUNK_HEADER_LENGTH) / (strip.Chunks[1].Type == 0x26 ? 4 : 6);
    var (v1Used, v4Used) = _References(frame, strip);

    Assert.Multiple(() => {
      Assert.That(v1Entries, Is.EqualTo(v1Used.Count), "V1 entries written against V1 entries referred to");
      Assert.That(v4Entries, Is.EqualTo(v4Used.Count), "V4 entries written against V4 entries referred to");
      Assert.That(v1Used, Is.EquivalentTo(Enumerable.Range(0, v1Entries)), "and the numbers run from nought");
      Assert.That(v4Used, Is.EquivalentTo(Enumerable.Range(0, v4Entries)));
    });
  }

  [Test]
  [Category("Unit")]
  public void AVectorChunkAccountsForEveryBlockOfItsStrip() {
    // Walking the flag bits and the references of a strip has to land exactly on the end of the chunk
    // once every block is accounted for. A block's two mode bits may straddle a flag word, and its
    // references then come after the next word rather than the one it started in — a writer that had
    // that the other way round would end this walk somewhere else.
    foreach (var pictures in new[] {
               new[] { _Noise(64, 48, 3) },
               [_Gradient(64, 48)],
               [_Noise(64, 48, 5), _Noise(64, 48, 6)],
             }) {
      var packets = _Encode(pictures);
      foreach (var packet in packets) {
        var frame = packet.Data.ToArray();
        foreach (var strip in _Strips(frame))
          Assert.That(_Walk(frame, strip), Is.EqualTo(strip.Chunks[2].Length - _CHUNK_HEADER_LENGTH),
            "the vector chunk is exactly as long as the blocks of the strip need");
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void TheSamePictureAlwaysWritesTheSameBytes() {
    // The quantiser has no random state, which is the difference from the reference encoder's ELBG.
    var pictures = new[] { _Noise(64, 48, 3), _Gradient(64, 48), _Noise(64, 48, 4) };
    var first = _Encode(pictures).Select(p => p.Data.ToArray()).ToArray();
    var second = _Encode(pictures).Select(p => p.Data.ToArray()).ToArray();

    for (var frame = 0; frame < first.Length; ++frame)
      Assert.That(second[frame], Is.EqualTo(first[frame]), $"frame {frame}");
  }

  // ============================================================================================
  // Through a container
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheFramesSurviveAnAviAndComeBackThroughTheRegistry() {
    var pictures = new[] { _Flat(20, 12, 255, 0, 0), _Flat(20, 12, 0, 255, 0), _Flat(20, 12, 90, 90, 90) };
    var encoder = CinepakVideoEncoder.Create(_Requested(20, 12));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var container = AviContainer.FromBytes(avi);
    Assert.That(AviContainer.Streams(container).Single().Codec, Is.EqualTo(CodecTag.FromCharacters("cvid")));

    var decoded = VideoFormatRegistry.DecodeFrames(avi).Select(f => f.Image).ToList();
    Assert.That(decoded, Has.Count.EqualTo(pictures.Length));
    for (var i = 0; i < pictures.Length; ++i)
      Assert.That(decoded[i].PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
  }

  // ============================================================================================
  // The packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TimestampsPassThroughUntouched() {
    var encoder = CinepakVideoEncoder.Create(_Requested(8, 8, index: 3));
    var picture = _Flat(8, 8, 8, 16, 24);

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
    var encoder = CinepakVideoEncoder.Create(_Requested(8, 8));
    Assert.That(encoder.TryEncode(_Flat(8, 8, 8, 16, 24), 0, out _), Is.True);

    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsRefused() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 8, Height = 8 };

    Assert.Throws<NotSupportedException>(() => CinepakVideoEncoder.Create(sound));
  }

  [Test]
  [Category("Unit")]
  public void APictureThatIsNotAWholeNumberOfBlocksIsRefusedRatherThanPadded() {
    var failure = Assert.Throws<NotSupportedException>(() => CinepakVideoEncoder.Create(_Requested(17, 12)));

    Assert.That(failure!.Message, Does.Contain("17x12"));
    Assert.That(failure.Message, Does.Contain("4x4 blocks"));
  }

  [Test]
  [Category("Unit")]
  public void APictureWithNoSizeIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => CinepakVideoEncoder.Create(_Requested(0, 0)));

    Assert.That(failure!.Message, Does.Contain("0x0"));
  }

  [Test]
  [Category("Unit")]
  public void APictureTooLargeForAStripLengthIsRefusedByName() {
    // 8192 wide is 2048 blocks a row, and eight rows of them is more than a strip's length field can
    // state; the height is only eight rows of blocks, so it cannot be cut any finer than that.
    var failure = Assert.Throws<NotSupportedException>(() => CinepakVideoEncoder.Create(_Requested(65536, 8)));

    Assert.That(failure!.Message, Does.Contain("states its length in two"));
  }

  [Test]
  [Category("Unit")]
  public void AGeometryChangeMidStreamIsRefused() {
    var encoder = CinepakVideoEncoder.Create(_Requested(16, 16));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Flat(8, 8, 1, 2, 3), 0, out _));
    Assert.That(failure!.Message, Does.Contain("16x16"));
  }

  // ============================================================================================
  // Reading the bytes back
  // ============================================================================================

  private sealed record _Chunk(int Type, int Length, int At);

  private sealed record _Strip(int Identifier, int Length, int Top, int Left, int Bottom, int Right, List<_Chunk> Chunks);

  private static List<_Strip> _Strips(byte[] frame) {
    var strips = new List<_Strip>();
    var at = _FRAME_HEADER_LENGTH;

    for (var strip = 0; strip < _Be16(frame, 8); ++strip) {
      var length = (frame[at + 1] << 16) | (frame[at + 2] << 8) | frame[at + 3];
      var chunks = new List<_Chunk>();
      for (var chunk = at + _STRIP_HEADER_LENGTH; chunk < at + length;) {
        var size = (frame[chunk + 1] << 16) | (frame[chunk + 2] << 8) | frame[chunk + 3];
        Assert.That(size, Is.GreaterThanOrEqualTo(_CHUNK_HEADER_LENGTH), "a chunk shorter than its own header");
        chunks.Add(new(frame[chunk], size, chunk));
        chunk += size;
      }

      strips.Add(new(
        frame[at], length, _Be16(frame, at + 4), _Be16(frame, at + 6), _Be16(frame, at + 8), _Be16(frame, at + 10),
        chunks));
      at += length;
    }

    Assert.That(at, Is.EqualTo(frame.Length), "the strips account for the whole frame");
    return strips;
  }

  /// <summary>
  /// Walks the vector chunk of one strip the way the decoder does, and says how many bytes it read.
  /// </summary>
  private static int _Walk(byte[] frame, _Strip strip) {
    var blocks = (strip.Right - strip.Left) / 4 * ((strip.Bottom - strip.Top) / 4);
    var chunk = strip.Chunks[2];
    var body = frame[(chunk.At + _CHUNK_HEADER_LENGTH)..(chunk.At + chunk.Length)];

    if (chunk.Type == 0x32)
      return blocks;

    var at = 0;
    var flags = 0u;
    var left = 0;

    bool Bit() {
      if (left == 0) {
        Assert.That(at + 4, Is.LessThanOrEqualTo(body.Length), "a flag word past the end of the chunk");
        flags = (uint)((body[at] << 24) | (body[at + 1] << 16) | (body[at + 2] << 8) | body[at + 3]);
        at += 4;
        left = 32;
      }

      --left;
      return ((flags >> left) & 1) != 0;
    }

    for (var block = 0; block < blocks; ++block) {
      if (chunk.Type == 0x31 && !Bit())
        continue;

      // Read the bit before moving on, because that read may itself pull in the next flag word and so
      // move where the references start.
      var quadrants = Bit();
      at += quadrants ? 4 : 1;
      Assert.That(at, Is.LessThanOrEqualTo(body.Length), $"block {block} reaches past the end of the chunk");
    }

    return at;
  }

  /// <summary>Which codebook entries the vector chunk of one strip refers to.</summary>
  private static (HashSet<int> V1, HashSet<int> V4) _References(byte[] frame, _Strip strip) {
    var blocks = (strip.Right - strip.Left) / 4 * ((strip.Bottom - strip.Top) / 4);
    var chunk = strip.Chunks[2];
    var body = frame[(chunk.At + _CHUNK_HEADER_LENGTH)..(chunk.At + chunk.Length)];
    var v1 = new HashSet<int>();
    var v4 = new HashSet<int>();

    if (chunk.Type == 0x32) {
      for (var block = 0; block < blocks; ++block)
        v1.Add(body[block]);

      return (v1, v4);
    }

    var at = 0;
    var flags = 0u;
    var left = 0;

    bool Bit() {
      if (left == 0) {
        flags = (uint)((body[at] << 24) | (body[at + 1] << 16) | (body[at + 2] << 8) | body[at + 3]);
        at += 4;
        left = 32;
      }

      --left;
      return ((flags >> left) & 1) != 0;
    }

    for (var block = 0; block < blocks; ++block) {
      if (chunk.Type == 0x31 && !Bit())
        continue;

      if (Bit())
        for (var quadrant = 0; quadrant < 4; ++quadrant)
          v4.Add(body[at++]);
      else
        v1.Add(body[at++]);
    }

    return (v1, v4);
  }

  private static int _Be16(byte[] data, int at) => (data[at] << 8) | data[at + 1];

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static IReadOnlyList<CodedPacket> _Encode(IReadOnlyList<RawImage> pictures) {
    var encoder = CinepakVideoEncoder.Create(_Requested(pictures[0].Width, pictures[0].Height));

    return pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();
  }

  private static IReadOnlyList<RawImage> _RoundTrip(int width, int height, IReadOnlyList<RawImage> pictures) {
    var encoder = CinepakVideoEncoder.Create(_Requested(width, height));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    Assert.That(decoder, Is.InstanceOf<CinepakVideoDecoder>());

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

  private static (byte Red, byte Green, byte Blue) _Corner(int index) => (
    (byte)((index & 1) != 0 ? 255 : 0), (byte)((index & 2) != 0 ? 255 : 0), (byte)((index & 4) != 0 ? 255 : 0));

  private static RawImage _Rgb(int width, int height, byte[] pixels) => new() {
    Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels,
  };

  private static RawImage _Flat(int width, int height, byte red, byte green, byte blue)
    => _Rgb(width, height, _Fill(width, height, (_, _) => (red, green, blue)));

  private static RawImage _Gradient(int width, int height)
    => _Rgb(width, height, _Fill(width, height, (x, y) => ((byte)(x * 3), (byte)(y * 5), (byte)((x + y) * 2))));

  private static RawImage _Noise(int width, int height, int seed) {
    var random = new Random(seed);
    var pixels = new byte[width * height * 3];
    random.NextBytes(pixels);
    return _Rgb(width, height, pixels);
  }

  private static byte[] _Fill(int width, int height, Func<int, int, (byte, byte, byte)> paint) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var (red, green, blue) = paint(x, y);
        var at = (y * width + x) * 3;
        pixels[at] = red;
        pixels[at + 1] = green;
        pixels[at + 2] = blue;
      }

    return pixels;
  }

  private static MediaStreamInfo _Requested(int width, int height, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("cvid"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}
