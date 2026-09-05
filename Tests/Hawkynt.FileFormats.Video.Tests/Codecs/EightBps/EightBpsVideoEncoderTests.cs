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
/// The 8BPS encoder, checked by decoding what it writes with the decoder beside it.
/// </summary>
/// <remarks>
/// The coding is lossless, so the whole of the contract is that the picture comes back exactly — over
/// widths that are and are not whole multiples of a run's own limits, over pictures that are noise
/// and pictures that are flat, since PackBits takes entirely different paths through the two. The
/// sample description is checked against what the decoder reads out of it, because that is the
/// description a muxer will be handed; and the shortest coding of a few hand-written rows is pinned
/// down outright, because "lossless" says nothing about whether a row was coded well.
/// <para/>
/// What is not here is the measurement that actually settles the format: the frames were muxed into
/// a QuickTime file and handed to ffmpeg's own 8bps decoder, which reproduced them sample for sample
/// at all three depths. That needs ffmpeg on the machine and does not belong in a unit test; the
/// numbers are in the commit that added this encoder.
/// </remarks>
[TestFixture]
public sealed class EightBpsVideoEncoderTests {

  private static readonly CodecTag _EightBps = CodecTag.FromCharacters("8BPS");

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheDescriptionIsAWholeSampleEntryTheDecoderReads([Values(24, 32)] int depth) {
    var encoder = EightBpsVideoEncoder.Create(_Requested(6, 3, depth));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(_EightBps));
      Assert.That(stream.Handler, Is.EqualTo(_EightBps));
      Assert.That(stream.Width, Is.EqualTo(6));
      Assert.That(stream.Height, Is.EqualTo(3));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(depth));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(86));
    });

    var entry = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(entry), Is.EqualTo(86), "box length");
      Assert.That(entry[4..8], Is.EqualTo("8BPS"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 24)), Is.EqualTo(6), "width");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 26)), Is.EqualTo(3), "height");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 74)), Is.EqualTo(depth), "depth");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 76)), Is.EqualTo(0xFFFF), "no colour table");
    });

    Assert.That(EightBpsVideoDecoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<EightBpsVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void AnEightBitDescriptionCarriesTheColourTable() {
    var encoder = EightBpsVideoEncoder.Create(_Requested(5, 2));
    Assert.That(encoder.TryEncode(_Indexed(5, 2, 1, 16), 0, out var packet), Is.True);

    var stream = encoder.DescribeStream();
    var entry = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 74)), Is.EqualTo(8), "depth");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 76)), Is.EqualTo(0), "colour table follows");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 78 + 6)), Is.EqualTo(15), "entries less one");
      Assert.That(entry.Length, Is.EqualTo(8 + 78 + 8 + 16 * 8));
    });

    // Entry 3 is R=9, G=10, B=11; the index leads and each channel is sixteen bits with the eight repeated.
    Assert.That(entry[(8 + 78 + 8 + 3 * 8)..(8 + 78 + 8 + 4 * 8)], Is.EqualTo(new byte[] { 0, 3, 9, 9, 10, 10, 11, 11 }));

    var decoder = EightBpsVideoDecoder.Create(stream);
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Palette!.AsSpan(0, 16 * 3).ToArray(), Is.EqualTo(_Palette(16)));
    Assert.That(frame.PixelData, Is.All.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void TheDescriptionCannotBeGivenBeforeTheDepthOrPaletteIsKnown() {
    var undecided = EightBpsVideoEncoder.Create(_Requested(4, 4));
    Assert.That(Assert.Throws<InvalidOperationException>(() => undecided.DescribeStream())!.Message, Does.Contain("depth"));

    var indexed = EightBpsVideoEncoder.Create(_Requested(4, 4, 8));
    Assert.That(Assert.Throws<InvalidOperationException>(() => indexed.DescribeStream())!.Message, Does.Contain("palette"));
  }

  [Test]
  [Category("Unit")]
  public void ADescriptionThatAlreadyCarriesAColourTableCanBeGivenBeforeTheFirstPicture() {
    var first = EightBpsVideoEncoder.Create(_Requested(4, 4));
    Assert.That(first.TryEncode(_Indexed(4, 4, 2, 16), 0, out _), Is.True);
    var described = first.DescribeStream();

    var second = EightBpsVideoEncoder.Create(described);
    var again = second.DescribeStream();

    Assert.That(again.CodecPrivateData.ToArray(), Is.EqualTo(described.CodecPrivateData.ToArray()));
    Assert.That(second.TryEncode(_Indexed(4, 4, 3, 16), 0, out _), Is.True, "a picture with the same sixteen colours is taken");
  }

  // ============================================================================================
  // The round trip
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ATwentyFourBitSequenceRoundTripsExactly([Values(1, 7, 64, 127, 128, 129, 300)] int width) {
    var height = width switch { 1 => 1, 7 => 5, 64 => 40, 300 => 3, _ => 2 };

    _AssertRoundTrip(width, height, _Sequence(width, height, PixelFormat.Rgb24, seed: width));
  }

  [Test]
  [Category("Unit")]
  public void AThirtyTwoBitSequenceRoundTripsExactly([Values(1, 9, 48, 255, 257)] int width) {
    var height = width switch { 1 => 3, 9 => 7, 48 => 32, _ => 2 };

    _AssertRoundTrip(width, height, _Sequence(width, height, PixelFormat.Rgba32, seed: width));
  }

  [Test]
  [Category("Unit")]
  public void AnEightBitSequenceRoundTripsExactly([Values(1, 5, 64, 128, 301)] int width) {
    var height = width switch { 1 => 1, 5 => 9, 64 => 40, 128 => 4, _ => 3 };

    _AssertRoundTrip(width, height, _Sequence(width, height, PixelFormat.Indexed8, seed: width));
  }

  [Test]
  [Category("Unit")]
  public void ARowLongerThanEveryOpcodeCanStateRoundTrips() {
    // 1000 pixels a row: runs longer than 128 and literal stretches longer than 128 both have to be
    // split, and the decoder must land exactly where the split says.
    var random = new Random(5);
    var pictures = new List<RawImage>();
    for (var frame = 0; frame < 3; ++frame) {
      var pixels = new byte[1000 * 3 * 2];
      for (var i = 0; i < pixels.Length;) {
        var length = random.Next(1, 400) * 3;
        var colour = (byte)random.Next(0, 256);
        for (var n = 0; n < length && i < pixels.Length; ++n, ++i)
          pixels[i] = frame == 2 ? (byte)random.Next(0, 256) : colour;
      }

      pictures.Add(new() { Width = 1000, Height = 2, Format = PixelFormat.Rgb24, PixelData = pixels });
    }

    _AssertRoundTrip(1000, 2, pictures);
  }

  [Test]
  [Category("Unit")]
  public void TheFramesSurviveAQuickTimeFileAndComeBackThroughTheRegistry([Values(8, 24, 32)] int depth) {
    var format = depth switch { 8 => PixelFormat.Indexed8, 24 => PixelFormat.Rgb24, _ => PixelFormat.Rgba32 };
    var pictures = _Sequence(21, 10, format, seed: depth);
    var encoder = EightBpsVideoEncoder.Create(_Requested(21, 10));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var file = VideoIO.Mux<Mp4Writer>([encoder.DescribeStream()], packets);
    var container = Mp4Container.FromBytes(file);
    var stream = Mp4Container.Streams(container).Single();
    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(_EightBps));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(depth));
    });

    var decoded = VideoFormatRegistry.DecodeFrames(file).Select(f => f.Image).ToList();
    Assert.That(decoded.Count, Is.EqualTo(pictures.Count));
    for (var i = 0; i < pictures.Count; ++i)
      Assert.That(decoded[i].PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
  }

  // ============================================================================================
  // The packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatRowIsOneRepeatAPlaneAndAStripedRowIsOneLiteral() {
    var encoder = EightBpsVideoEncoder.Create(_Requested(4, 2, 24));
    var pixels = new byte[4 * 2 * 3];
    for (var x = 0; x < 4; ++x) {
      pixels[x * 3] = 7;                 // row 0 is flat: one repeat a plane
      pixels[x * 3 + 1] = 8;
      pixels[x * 3 + 2] = 9;
      pixels[(4 + x) * 3] = (byte)x;     // row 1 rises: one literal a plane
      pixels[(4 + x) * 3 + 1] = (byte)(x + 10);
      pixels[(4 + x) * 3 + 2] = (byte)(x + 20);
    }

    Assert.That(encoder.TryEncode(new() { Width = 4, Height = 2, Format = PixelFormat.Rgb24, PixelData = pixels }, 0, out var packet), Is.True);
    var data = packet.Data.ToArray();

    // Three planes of two rows: a repeat costs two bytes, a literal of four costs five.
    Assert.That(data[..12], Is.EqualTo(new byte[] { 0, 2, 0, 5, 0, 2, 0, 5, 0, 2, 0, 5 }), "the length tables");
    Assert.That(data[12..19], Is.EqualTo(new byte[] { 253, 7, 3, 0, 1, 2, 3 }), "red: a repeat of four, then a literal of four");
    Assert.That(data.Length, Is.EqualTo(12 + 3 * 7));
  }

  [Test]
  [Category("Unit")]
  public void ARunOfTwoInsideALiteralStaysInTheLiteral() {
    // Breaking out for a pair costs a repeat and a fresh literal control byte where staying costs
    // the two bytes themselves, so the whole row is one literal.
    var encoder = EightBpsVideoEncoder.Create(_Requested(6, 1, 8));
    var picture = _Indexed(6, 1, 0, 16);
    new byte[] { 1, 2, 3, 3, 4, 5 }.CopyTo(picture.PixelData, 0);

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 0, 7, 5, 1, 2, 3, 3, 4, 5 }));
  }

  [Test]
  [Category("Unit")]
  public void EveryRowIsCodedAtItsShortest([Values(1, 2, 3, 8, 256)] int colours) {
    // Measured against a plain dynamic programme that tries every literal length and every repeat
    // length at every byte, written here on its own terms rather than the encoder's; the alphabet is
    // narrowed and widened so that both the row full of runs and the row with none are reached.
    var random = new Random(colours);
    for (var round = 0; round < 8; ++round) {
      var row = new byte[200];
      for (var i = 0; i < row.Length; ++i)
        row[i] = (byte)random.Next(0, colours);

      var encoder = EightBpsVideoEncoder.Create(_Requested(row.Length, 1, 8));
      var picture = _Indexed(row.Length, 1, 0, 256);
      row.CopyTo(picture.PixelData, 0);
      Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);

      // One plane of one row: two bytes of length table, then the coded row.
      Assert.That(packet.Data.Length - 2, Is.EqualTo(_ShortestCoding(row)), $"{colours} colours, round {round}");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(packet.Data.Span), Is.EqualTo(packet.Data.Length - 2), "the table states the row's own length");
    }
  }

  [Test]
  [Category("Unit")]
  public void EveryFrameIsWholeAndEveryFrameIsAKeyFrame() {
    var encoder = EightBpsVideoEncoder.Create(_Requested(4, 4, 24));
    var picture = _Rgb(4, 4, 9);

    Assert.That(encoder.TryEncode(picture, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(picture, 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.IsKeyFrame, Is.True, "the format carries no reference to the frame before");
      Assert.That(second.Data.ToArray(), Is.EqualTo(first.Data.ToArray()), "so the same picture is the same packet");
    });
  }

  [Test]
  [Category("Unit")]
  public void TimestampsPassThroughUntouched() {
    var encoder = EightBpsVideoEncoder.Create(_Requested(4, 4, 24, index: 3));
    var picture = _Rgb(4, 4, 2);

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
    var encoder = EightBpsVideoEncoder.Create(_Requested(4, 4, 24));
    Assert.That(encoder.TryEncode(_Rgb(4, 4, 2), 0, out _), Is.True);

    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFormatThatIsNotWrittenIsRefusedByName([Values(PixelFormat.Bgra32, PixelFormat.Gray8, PixelFormat.Indexed4, PixelFormat.Rgb565)] PixelFormat format) {
    var encoder = EightBpsVideoEncoder.Create(_Requested(4, 4));
    var picture = new RawImage { Width = 4, Height = 4, Format = format, PixelData = new byte[4 * 4 * 4], Palette = _Palette(16), PaletteCount = 16 };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain(format.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void ADepthThatIsNotWrittenIsRefusedByName([Values(16, 4, 40)] int depth) {
    var failure = Assert.Throws<NotSupportedException>(() => EightBpsVideoEncoder.Create(_Requested(4, 4, depth)));
    Assert.That(failure!.Message, Does.Contain($"{depth} bits per pixel"));
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsRefused() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 4, Height = 4 };

    Assert.Throws<NotSupportedException>(() => EightBpsVideoEncoder.Create(sound));
  }

  [Test]
  [Category("Unit")]
  public void APictureWithNoPixelsIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => EightBpsVideoEncoder.Create(_Requested(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void AWidthWhoseRowLengthCannotBeStatedIsRefused() {
    // A row of noise costs a control byte for every 128 of its own, so the sixteen-bit length runs
    // out well before the sixteen-bit width does.
    var failure = Assert.Throws<NotSupportedException>(() => EightBpsVideoEncoder.Create(_Requested(65535, 1)));
    Assert.That(failure!.Message, Does.Contain("65026"));

    Assert.DoesNotThrow(() => EightBpsVideoEncoder.Create(_Requested(65026, 1)));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfAnotherDepthThanTheStreamIsRefused() {
    var encoder = EightBpsVideoEncoder.Create(_Requested(4, 4, 32));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Rgb(4, 4, 1), 0, out _));
    Assert.That(failure!.Message, Does.Contain("cannot change between frames"));
  }

  [Test]
  [Category("Unit")]
  public void APalettisedPictureWithoutAPaletteIsRefused() {
    var encoder = EightBpsVideoEncoder.Create(_Requested(4, 4));
    var bare = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Indexed8, PixelData = new byte[16] };

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(bare, 0, out _));
    Assert.That(failure!.Message, Does.Contain("without a palette"));
  }

  [Test]
  [Category("Unit")]
  public void AnIndexPastTheEndOfThePaletteIsRefused() {
    var encoder = EightBpsVideoEncoder.Create(_Requested(4, 1));
    var picture = _Indexed(4, 1, 0, 16);
    picture.PixelData[2] = 16;

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("index 16"));
  }

  [Test]
  [Category("Unit")]
  public void APaletteThatChangesBetweenFramesIsRefused() {
    var encoder = EightBpsVideoEncoder.Create(_Requested(4, 4));
    Assert.That(encoder.TryEncode(_Indexed(4, 4, 1, 16), 0, out _), Is.True);

    var other = _Indexed(4, 4, 1, 16);
    other.Palette![0] ^= 0xFF;

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(other, 1, out _));
    Assert.That(failure!.Message, Does.Contain("different palette"));
  }

  [Test]
  [Category("Unit")]
  public void AGeometryChangeMidStreamIsRefused() {
    var encoder = EightBpsVideoEncoder.Create(_Requested(8, 8, 24));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Rgb(4, 4, 1), 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static void _AssertRoundTrip(int width, int height, IReadOnlyList<RawImage> pictures) {
    var encoder = EightBpsVideoEncoder.Create(_Requested(width, height));
    var packets = new List<CodedPacket>();
    for (var i = 0; i < pictures.Count; ++i) {
      Assert.That(encoder.TryEncode(pictures[i], i, out var packet), Is.True);
      Assert.That(packet.IsKeyFrame, Is.True, $"frame {i} stands on its own");
      packets.Add(packet);
    }

    var stream = encoder.DescribeStream();
    var decoder = VideoFormatRegistry.CreateDecoder(stream);
    Assert.That(decoder, Is.InstanceOf<EightBpsVideoDecoder>());

    for (var i = 0; i < pictures.Count; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True);
      Assert.Multiple(() => {
        Assert.That(decoded.Width, Is.EqualTo(width));
        Assert.That(decoded.Height, Is.EqualTo(height));
        Assert.That(decoded.Format, Is.EqualTo(pictures[i].Format));
        Assert.That(decoded.PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
        if (pictures[i].Palette != null)
          Assert.That(
            decoded.Palette!.AsSpan(0, pictures[i].PaletteCount * 3).ToArray(),
            Is.EqualTo(pictures[i].Palette!.AsSpan(0, pictures[i].PaletteCount * 3).ToArray()),
            $"palette of frame {i}");
      });
    }
  }

  /// <summary>
  /// Four pictures over the two shapes PackBits behaves differently on: noise, which is all literals;
  /// one flat colour, which is all repeats; long horizontal bands, which is both; and noise again.
  /// </summary>
  private static List<RawImage> _Sequence(int width, int height, PixelFormat format, int seed) {
    var random = new Random(seed);
    var bytesPerPixel = RawImage.BytesPerPixel(format);
    var colours = format == PixelFormat.Indexed8 ? 16 : 256;
    var stride = width * bytesPerPixel;

    var noise = new byte[stride * height];
    for (var i = 0; i < noise.Length; ++i)
      noise[i] = (byte)random.Next(0, colours);

    var flat = new byte[stride * height];
    Array.Fill(flat, (byte)(colours / 2));

    var bands = new byte[stride * height];
    for (var y = 0; y < height; ++y) {
      var at = 0;
      while (at < stride) {
        var run = Math.Min(random.Next(1, 200), stride - at);
        var value = (byte)random.Next(0, colours);
        bands.AsSpan(y * stride + at, run).Fill(value);
        at += run;
      }
    }

    var again = new byte[stride * height];
    for (var i = 0; i < again.Length; ++i)
      again[i] = (byte)random.Next(0, colours);

    return
    [
      _Picture(width, height, noise, format),
      _Picture(width, height, flat, format),
      _Picture(width, height, bands, format),
      _Picture(width, height, again, format),
    ];
  }

  /// <summary>
  /// The fewest bytes a row can be coded in, found by trying every opcode of every length at every
  /// byte — a literal of one to 128, and a repeat of two to 128 wherever the bytes are all the same.
  /// </summary>
  private static int _ShortestCoding(byte[] row) {
    var cost = new int[row.Length + 1];
    for (var i = row.Length - 1; i >= 0; --i) {
      var best = int.MaxValue;
      var flat = true;
      for (var length = 1; length <= 128 && i + length <= row.Length; ++length) {
        flat &= row[i + length - 1] == row[i];
        best = Math.Min(best, 1 + length + cost[i + length]);
        if (flat && length >= 2)
          best = Math.Min(best, 2 + cost[i + length]);
      }

      cost[i] = best;
    }

    return cost[0];
  }

  private static RawImage _Picture(int width, int height, byte[] pixels, PixelFormat format) => new() {
    Width = width,
    Height = height,
    Format = format,
    PixelData = pixels,
    Palette = format == PixelFormat.Indexed8 ? _Palette(16) : null,
    PaletteCount = format == PixelFormat.Indexed8 ? 16 : 0,
  };

  private static RawImage _Rgb(int width, int height, byte fill) {
    var pixels = new byte[width * height * 3];
    Array.Fill(pixels, fill);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Indexed(int width, int height, byte fill, int colours) {
    var pixels = new byte[width * height];
    Array.Fill(pixels, fill);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _Palette(colours),
      PaletteCount = colours,
    };
  }

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
    Codec = _EightBps,
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}
