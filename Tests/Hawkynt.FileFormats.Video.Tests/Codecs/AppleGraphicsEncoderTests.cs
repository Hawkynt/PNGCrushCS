using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Mp4;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Apple Graphics (SMC) encoder, checked by decoding what it writes.
/// </summary>
/// <remarks>
/// The whole of the contract is that the indices come back: every block of an eight-bit palettised
/// picture is representable, so nothing here rounds and a round trip that is not exact is a defect
/// rather than a cost. Twenty-seven files this encoder wrote were handed to ffmpeg's own SMC decoder
/// and came back identical — see <c>codec-notes.md</c> — which is the measurement that matters;
/// what these tests add is what a whole-file comparison does not isolate: the sample description a
/// muxer is handed, which opcode a given picture is written with, the key-frame flag, and the
/// refusals.
/// </remarks>
[TestFixture]
public sealed class AppleGraphicsEncoderTests {

  private static readonly CodecTag _SMC = CodecTag.FromCharacters("smc ");

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheDescriptionIsAWholeSampleEntryTheDecoderReads() {
    var encoder = AppleGraphicsEncoder.Create(_Requested(6, 3));
    Assert.That(encoder.TryEncode(_Indexed(6, 3, 2, 16), 0, out _), Is.True);
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(_SMC));
      Assert.That(stream.Handler, Is.EqualTo(_SMC));
      Assert.That(stream.Width, Is.EqualTo(6));
      Assert.That(stream.Height, Is.EqualTo(3));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(8 + 78 + 8 + 16 * 8));
    });

    var entry = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(entry), Is.EqualTo(entry.Length), "box length");
      Assert.That(entry[4..8], Is.EqualTo("smc "u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 24)), Is.EqualTo(6), "width");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 26)), Is.EqualTo(3), "height");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 74)), Is.EqualTo(8), "depth");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 76)), Is.EqualTo(0), "a table of its own follows");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 78 + 6)), Is.EqualTo(15), "one less than the entry count");
    });

    Assert.That(AppleGraphicsDecoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<AppleGraphicsDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheStreamCannotBeDescribedBeforeThePaletteIsKnown() {
    var encoder = AppleGraphicsEncoder.Create(_Requested(4, 4));

    var failure = Assert.Throws<InvalidOperationException>(() => encoder.DescribeStream());
    Assert.That(failure!.Message, Does.Contain("before its palette is known"));
  }

  [Test]
  [Category("Unit")]
  public void ADescriptionThatAlreadyCarriesAColourTableLendsIt() {
    var first = AppleGraphicsEncoder.Create(_Requested(4, 4));
    Assert.That(first.TryEncode(_Indexed(4, 4, 3, 16), 0, out _), Is.True);
    var described = first.DescribeStream();

    var second = AppleGraphicsEncoder.Create(described);
    Assert.That(second.DescribeStream().CodecPrivateData.ToArray(), Is.EqualTo(described.CodecPrivateData.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsThisEncoderForTheCode() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Requested(4, 4));

    Assert.That(encoder, Is.InstanceOf<AppleGraphicsEncoder>());
  }

  // ============================================================================================
  // The round trip
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASequenceRoundTripsExactly([Values(1, 3, 5, 17, 64, 130)] int width) {
    // Widths and heights that are not a whole number of blocks are coded padded and shown cropped.
    var height = width switch { 1 => 7, 3 => 1, 5 => 3, 17 => 13, 64 => 40, _ => 90 };
    var pictures = _Sequence(width, height, seed: width);

    _AssertRoundTrip(width, height, pictures);
  }

  [Test]
  [Category("Unit")]
  public void APictureOfNothingButNoiseRoundTripsExactly() {
    // Every block sixteen distinct colours, which is the one coding nothing can be shared across.
    var random = new Random(7);
    var pixels = new byte[64 * 64];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)random.Next(0, 256);

    _AssertRoundTrip(64, 64, [_Picture(64, 64, pixels, 256)]);
  }

  [Test]
  [Category("Unit")]
  public void ARunLongerThanOneOpcodeCanStateRoundTrips() {
    // 2048 blocks of one colour: the run opcodes state at most 256 blocks apiece, so the walk has to
    // split them and the decoder has to land where the split says.
    var pictures = new List<RawImage> {
      _Picture(256, 128, new byte[256 * 128], 4),
      _Picture(256, 128, _Filled(256 * 128, 3), 4),
    };

    _AssertRoundTrip(256, 128, pictures);
  }

  [Test]
  [Category("Unit")]
  public void TheFramesSurviveAQuickTimeFileAndComeBackThroughTheRegistry() {
    var pictures = _Sequence(21, 10, seed: 3);
    var encoder = AppleGraphicsEncoder.Create(_Requested(21, 10));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var file = VideoIO.Mux<Mp4Writer>([encoder.DescribeStream()], packets);
    var container = Mp4Container.FromBytes(file);
    var stream = Mp4Container.Streams(container).Single();
    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(_SMC));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
    });

    var decoded = VideoFormatRegistry.DecodeFrames(file).Select(f => f.Image).ToList();
    Assert.That(decoded.Count, Is.EqualTo(pictures.Count));
    for (var i = 0; i < pictures.Count; ++i)
      Assert.That(decoded[i].PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
  }

  // ============================================================================================
  // Which opcode a picture is written with
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheChunkOpensWithItsOwnLength() {
    var encoder = AppleGraphicsEncoder.Create(_Requested(4, 4));
    Assert.That(encoder.TryEncode(_Indexed(4, 4, 1, 4), 0, out var packet), Is.True);

    var data = packet.Data.ToArray();
    Assert.Multiple(() => {
      Assert.That(data[0], Is.EqualTo(0), "the flags byte the format leaves at zero");
      Assert.That((data[1] << 16) | (data[2] << 8) | data[3], Is.EqualTo(data.Length), "the chunk length");
    });
  }

  [Test]
  [Category("Unit")]
  public void OneBlockOfOneColourIsOneColourOpcodeAndOneIndex() {
    var encoder = AppleGraphicsEncoder.Create(_Requested(4, 4));
    Assert.That(encoder.TryEncode(_Indexed(4, 4, 5, 8), 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 6, 0x60, 5 }));
  }

  [Test]
  [Category("Unit")]
  public void APictureIdenticalToTheOneBeforeIsWrittenAsSkips() {
    var encoder = AppleGraphicsEncoder.Create(_Requested(8, 4));
    var picture = _Indexed(8, 4, 2, 8);
    Assert.That(encoder.TryEncode(picture, 0, out _), Is.True);
    Assert.That(encoder.TryEncode(picture, 1, out var second), Is.True);

    Assert.Multiple(() => {
      // Two blocks skipped: one opcode with the count in its own low nibble.
      Assert.That(second.Data.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 5, 0x01 }));
      Assert.That(second.IsKeyFrame, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void BlocksBeyondWhatOneCodedRunHoldsAreWrittenAsARepeat() {
    // Seventeen identical four-colour blocks in a row. A run sharing one set of colours states its
    // length in the low nibble of its opcode and so stops at sixteen; the seventeenth is cheaper as
    // a repeat of the one before it than as a run of its own.
    const int width = 17 * 4;
    var pixels = new byte[width * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var encoder = AppleGraphicsEncoder.Create(_Requested(width, 4));
    Assert.That(encoder.TryEncode(_Picture(width, 4, pixels, 4), 0, out var packet), Is.True);

    var data = packet.Data.ToArray();
    Assert.Multiple(() => {
      Assert.That(data.Length, Is.EqualTo(4 + 1 + 4 + 16 * 4 + 1));
      Assert.That(data[4], Is.EqualTo(0xAF), "sixteen blocks against four colours stated here");
      Assert.That(data[^1], Is.EqualTo(0x20), "and the seventeenth repeating the sixteenth");
      Assert.That(packet.IsKeyFrame, Is.True, "a repeat needs no frame before it");
    });
  }

  [Test]
  [Category("Unit")]
  public void ACachedColourOpcodeOnlyEverNamesAnEntryTheSamePacketWrote() {
    // The one thing a decoder can disagree about: whether the colour caches survive a packet. A
    // stream that never names an entry the packet has not already written reads the same either way.
    var encoder = AppleGraphicsEncoder.Create(_Requested(96, 72));
    var cached = 0;

    for (var shift = 0; shift < 4; ++shift) {
      Assert.That(encoder.TryEncode(_Checkerboards(96, 72, shift), shift, out var packet), Is.True);
      cached += _RefuseCacheReferenceAcrossPackets(packet.Data.Span, 96, 72);
    }

    Assert.That(cached, Is.GreaterThan(0), "the cached spellings are used at all");
  }

  // ============================================================================================
  // The packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TimestampsPassThroughUntouched() {
    var encoder = AppleGraphicsEncoder.Create(_Requested(4, 4, index: 3));
    var picture = _Indexed(4, 4, 2, 8);

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
    var encoder = AppleGraphicsEncoder.Create(_Requested(4, 4));
    Assert.That(encoder.TryEncode(_Indexed(4, 4, 2, 8), 0, out _), Is.True);

    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFormatThatIsNotWrittenIsRefusedByName(
    [Values(PixelFormat.Rgb24, PixelFormat.Rgba32, PixelFormat.Gray8, PixelFormat.Indexed4)] PixelFormat format) {
    var encoder = AppleGraphicsEncoder.Create(_Requested(4, 4));
    var picture = new RawImage {
      Width = 4, Height = 4, Format = format, PixelData = new byte[4 * 4 * 4], Palette = _Palette(16), PaletteCount = 16,
    };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain(format.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void ADepthThatIsNotWrittenIsRefusedByName([Values(4, 16, 24, 32, 40)] int depth) {
    var failure = Assert.Throws<NotSupportedException>(() => AppleGraphicsEncoder.Create(_Requested(4, 4, depth)));
    Assert.That(failure!.Message, Does.Contain($"{depth} bits per pixel"));
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsRefused() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 4, Height = 4 };

    Assert.Throws<NotSupportedException>(() => AppleGraphicsEncoder.Create(sound));
  }

  [Test]
  [Category("Unit")]
  public void APalettisedPictureWithoutAPaletteIsRefused() {
    var encoder = AppleGraphicsEncoder.Create(_Requested(4, 4));
    var bare = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Indexed8, PixelData = new byte[16] };

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(bare, 0, out _));
    Assert.That(failure!.Message, Does.Contain("without a palette"));
  }

  [Test]
  [Category("Unit")]
  public void APaletteThatChangesBetweenFramesIsRefused() {
    var encoder = AppleGraphicsEncoder.Create(_Requested(4, 4));
    Assert.That(encoder.TryEncode(_Indexed(4, 4, 1, 16), 0, out _), Is.True);

    var other = _Indexed(4, 4, 1, 16);
    other.Palette![0] ^= 0xFF;

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(other, 1, out _));
    Assert.That(failure!.Message, Does.Contain("different palette"));
  }

  [Test]
  [Category("Unit")]
  public void AnIndexPastTheEndOfThePaletteIsRefused() {
    var encoder = AppleGraphicsEncoder.Create(_Requested(4, 1));
    var picture = _Indexed(4, 1, 0, 16);
    picture.PixelData[2] = 16;

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("index 16"));
  }

  [Test]
  [Category("Unit")]
  public void AGeometryChangeMidStreamIsRefused() {
    var encoder = AppleGraphicsEncoder.Create(_Requested(8, 8));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Indexed(4, 4, 1, 16), 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static void _AssertRoundTrip(int width, int height, IReadOnlyList<RawImage> pictures) {
    var encoder = AppleGraphicsEncoder.Create(_Requested(width, height));
    var packets = new List<CodedPacket>();
    foreach (var picture in pictures) {
      Assert.That(encoder.TryEncode(picture, packets.Count, out var packet), Is.True);
      packets.Add(packet);
    }

    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    Assert.That(decoder, Is.InstanceOf<AppleGraphicsDecoder>());

    for (var i = 0; i < pictures.Count; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True);
      Assert.Multiple(() => {
        Assert.That(decoded.Width, Is.EqualTo(width));
        Assert.That(decoded.Height, Is.EqualTo(height));
        Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
        Assert.That(decoded.PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
      });
    }

    Assert.That(packets[0].IsKeyFrame, Is.True, "the first frame is written whole");
  }

  /// <summary>
  /// Walks one chunk's opcodes, checking that every cached-colour opcode names an entry this chunk
  /// has already written, and answers how many of them there were.
  /// </summary>
  private static int _RefuseCacheReferenceAcrossPackets(ReadOnlySpan<byte> chunk, int width, int height) {
    var total = (width + 3) / 4 * ((height + 3) / 4);
    var written = new int[3];
    var cached = 0;
    var at = 4;
    var block = 0;

    while (block < total) {
      var opcodeByte = chunk[at++];
      var run = (opcodeByte & 0x0F) + 1;
      switch (opcodeByte & 0xF0) {
        case 0x00: break;
        case 0x10: run = chunk[at++] + 1; break;
        case 0x20: break;
        case 0x30: run = chunk[at++] + 1; break;
        case 0x60: ++at; break;
        case 0x70: run = chunk[at++] + 1; ++at; break;
        case 0x80: at += 2 + run * 2; written[0] = Math.Min(written[0] + 1, 256); break;
        case 0x90:
          Assert.That((int)chunk[at], Is.LessThan(written[0]), "a colour pair this chunk has not written");
          ++cached;
          at += 1 + run * 2;
          break;
        case 0xA0: at += 4 + run * 4; written[1] = Math.Min(written[1] + 1, 256); break;
        case 0xB0:
          Assert.That((int)chunk[at], Is.LessThan(written[1]), "a colour quad this chunk has not written");
          ++cached;
          at += 1 + run * 4;
          break;
        case 0xC0: at += 8 + run * 6; written[2] = Math.Min(written[2] + 1, 256); break;
        case 0xD0:
          Assert.That((int)chunk[at], Is.LessThan(written[2]), "a colour octet this chunk has not written");
          ++cached;
          at += 1 + run * 6;
          break;
        case 0xE0: at += run * 16; break;
        default: throw new InvalidDataException($"opcode 0x{opcodeByte:X2}");
      }

      block += run;
    }

    var length = chunk.Length;
    var reached = at;
    var accounted = block;
    Assert.Multiple(() => {
      Assert.That(reached, Is.EqualTo(length), "the chunk ends where the last opcode does");
      Assert.That(accounted, Is.EqualTo(total), "every block is accounted for");
    });

    return cached;
  }

  /// <summary>
  /// Six pictures: one built so that its blocks hold every count of distinct colours from one to
  /// sixteen, the same with a band changed, the same with the top rows changed, the same again with
  /// nothing changed, one with a single pixel changed, and a fully random one.
  /// </summary>
  /// <remarks>
  /// The first picture is not noise, because noise is sixteen distinct colours in every block and
  /// would reach only the one coding that shares nothing. Each block draws from a pool of one to
  /// sixteen colours instead, so one-, two-, four-, eight- and sixteen-colour blocks all occur, and
  /// neighbouring blocks drawing from the same pool give the runs that share a set of colours
  /// something to run over.
  /// </remarks>
  private static List<RawImage> _Sequence(int width, int height, int seed) {
    var random = new Random(seed);
    const int colours = 128;

    var first = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var pool = 1 + (y / 4 * 3 + x / 4) % 16;
      first[y * width + x] = (byte)(random.Next(0, pool) * 7 + (x / 4 + y / 4) % 3);
    }

    var band = (byte[])first.Clone();
    for (var y = height / 3; y < Math.Max(height / 3 + 1, height * 2 / 3); ++y)
    for (var x = width / 4; x < Math.Max(width / 4 + 1, width * 3 / 4); ++x)
      band[y * width + x] = (byte)((band[y * width + x] + 1) % colours);

    var rows = (byte[])band.Clone();
    for (var x = 0; x < width; ++x)
      rows[x] = (byte)random.Next(0, colours);

    var pixel = (byte[])rows.Clone();
    pixel[^1] = (byte)((pixel[^1] + 1) % colours);

    var last = new byte[width * height];
    for (var i = 0; i < last.Length; ++i)
      last[i] = (byte)random.Next(0, colours);

    return
    [
      _Picture(width, height, first, colours),
      _Picture(width, height, band, colours),
      _Picture(width, height, rows, colours),
      _Picture(width, height, rows, colours),
      _Picture(width, height, pixel, colours),
      _Picture(width, height, last, colours),
    ];
  }

  /// <summary>
  /// A picture of checkerboards, each eight-pixel cell drawn in one of four colour pairs, shifted
  /// sideways by <paramref name="shift"/>. Blocks come out holding two or four distinct colours and
  /// the same few sets of them recur across the picture, which is what a colour cache is for.
  /// </summary>
  private static RawImage _Checkerboards(int width, int height, int shift) {
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)(((x + shift) / 8 + y / 8) % 4 * 2 + ((x + y) & 1));

    return _Picture(width, height, pixels, 8);
  }

  private static byte[] _Filled(int length, byte value) {
    var pixels = new byte[length];
    Array.Fill(pixels, value);
    return pixels;
  }

  private static RawImage _Picture(int width, int height, byte[] pixels, int colours) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Indexed8,
    PixelData = pixels,
    Palette = _Palette(colours),
    PaletteCount = colours,
  };

  private static RawImage _Indexed(int width, int height, byte fill, int colours)
    => _Picture(width, height, _Filled(width * height, fill), colours);

  private static byte[] _Palette(int colours) {
    var palette = new byte[colours * 3];
    for (var i = 0; i < colours; ++i) {
      palette[i * 3] = (byte)(i * 3);
      palette[i * 3 + 1] = (byte)(i * 3 + 1);
      palette[i * 3 + 2] = (byte)(i * 3 + 2);
    }

    return palette;
  }

  private static MediaStreamInfo _Requested(int width, int height, int bitsPerPixel = 0, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("smc "),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}
