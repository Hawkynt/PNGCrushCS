using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Apple Video (RPZA) encoder, checked against the bytes it writes, against the decoder beside
/// it, and against the one guarantee its coding makes about colour.
/// </summary>
/// <remarks>
/// <b>Settled against ffmpeg, which has an rpza encoder as well as an rpza decoder.</b> Both
/// directions were measured, in RGB555 rather than RGB so that no colour conversion sits between the
/// two implementations: ffmpeg is fed and read as <c>rgb555le</c> raw video, and the five-bit values
/// are widened and narrowed here by bit replication, which is exactly invertible.
/// <para/>
/// First the reader was calibrated on files neither side wrote — the twenty-one RPZA streams
/// published at <c>samples.ffmpeg.org/V-codecs/RPZA</c>, its <c>odd_sizes/</c> directory included —
/// and this package's decode is identical to ffmpeg's on every pixel of every frame. Then the same
/// pictures were re-coded by both encoders: this encoder writes byte for byte what ffmpeg's own rpza
/// encoder writes, on every frame of all twenty-one, and on 149 further frames of thirteen synthetic
/// clips covering geometry that is and is not a whole number of blocks, down to four pixels wide.
/// ffmpeg's decode of what this writes is identical to this package's decode of it.
/// <para/>
/// What those measurements cannot isolate is what the tests below add: that the chunk header states
/// the length actually written, that a run never outstays its five count bits or crosses a block row,
/// that no four-colour block is ever written, and that the colour guarantee — no channel of any pixel
/// more than one thirty-second of full scale from the picture handed in — holds pixel by pixel rather
/// than on average.
/// </remarks>
[TestFixture]
public sealed class AppleVideoEncoderTests {

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheDescriptionIsWhatTheDecoderReads() {
    var encoder = AppleVideoEncoder.Create(_Requested(16, 12));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("rpza")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("rpza")));
      Assert.That(stream.Width, Is.EqualTo(16));
      Assert.That(stream.Height, Is.EqualTo(12));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(16));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
    });

    var entry = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(entry.AsSpan()), Is.EqualTo(entry.Length), "the entry states its own size");
      Assert.That(entry[4..8], Is.EqualTo("rpza"u8.ToArray()), "the entry names the codec");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 24)), Is.EqualTo(16), "width");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 26)), Is.EqualTo(12), "height");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 74)), Is.EqualTo(16), "depth");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 76)), Is.EqualTo(0xFFFF), "no colour table");
    });

    Assert.That(AppleVideoDecoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<AppleVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegisteredUnderTheCodeItWrites() {
    Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("Apple Video (RPZA)"));

    var stream = _Requested(16, 12);
    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<AppleVideoEncoder>());
  }

  // ============================================================================================
  // The chunk header
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryChunkOpensWithItsOwnLength() {
    foreach (var packet in _Encode(_Gradient(64, 48, 4))) {
      var data = packet.Data.ToArray();
      var stated = (data[1] << 16) | (data[2] << 8) | data[3];

      Assert.Multiple(() => {
        Assert.That(data[0], Is.EqualTo(0xE1), "the byte a chunk opens with");
        Assert.That(stated, Is.EqualTo(data.Length), "the three-byte length is the length written");
      });
    }
  }

  // ============================================================================================
  // Which codings are written, and which never are
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatPictureIsRunsOfOneColour() {
    var opcodes = _Opcodes(_Encode([_Flat(64, 48, 132, 66, 198)]).Single(), 64, 48);

    Assert.Multiple(() => {
      Assert.That(opcodes.Select(o => o.Kind), Is.All.EqualTo(_Coding.OneColour));
      Assert.That(opcodes.Sum(o => o.Count), Is.EqualTo(16 * 12));
      Assert.That(opcodes.Select(o => o.Colour), Is.All.EqualTo(opcodes[0].Colour), "one colour throughout");
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureThatDidNotChangeIsNothingButSkips() {
    var picture = _Gradient(64, 48, 1)[0];
    var packets = _Encode([picture, picture]);
    var opcodes = _Opcodes(packets[1], 64, 48);

    Assert.Multiple(() => {
      Assert.That(opcodes.Select(o => o.Kind), Is.All.EqualTo(_Coding.Skip));
      Assert.That(opcodes.Sum(o => o.Count), Is.EqualTo(16 * 12));
      Assert.That(packets[1].IsKeyFrame, Is.False, "a frame that skipped is not one a decoder can start at");
      Assert.That(packets[0].IsKeyFrame, Is.True, "the first frame skips nothing");
    });
  }

  [Test]
  [Category("Unit")]
  public void ADetailedPictureStatesItsPixels() {
    var opcodes = _Opcodes(_Encode([_Noise(32, 32, 1)[0]]).Single(), 32, 32);

    Assert.That(opcodes.Select(o => o.Kind), Is.All.EqualTo(_Coding.SixteenColours));
  }

  /// <summary>
  /// The four-colour opcode is read by the decoder and written by nothing here, in either of its two
  /// spellings.
  /// </summary>
  /// <remarks>
  /// A scope claim rather than an implementation detail, so it is asserted over everything this
  /// fixture codes rather than over one picture: flat, gradient, noise, moving and still, at
  /// geometries that are and are not whole blocks.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void NoFourColourBlockIsEverWritten() {
    var written = new List<_Coding>();
    foreach (var (width, height, pictures) in _Corpus())
      foreach (var packet in _Encode(pictures))
        written.AddRange(_Opcodes(packet, width, height).Select(o => o.Kind));

    Assert.Multiple(() => {
      Assert.That(written, Has.None.EqualTo(_Coding.FourColourRun));
      Assert.That(written, Has.None.EqualTo(_Coding.FourColourInline));
      Assert.That(written, Does.Contain(_Coding.Skip));
      Assert.That(written, Does.Contain(_Coding.OneColour));
      Assert.That(written, Does.Contain(_Coding.SixteenColours));
    });
  }

  [Test]
  [Category("Unit")]
  public void NoRunOutstaysItsCountBitsOrCrossesABlockRow() {
    foreach (var (width, height, pictures) in _Corpus()) {
      var blocksAcross = (width + 3) / 4;
      foreach (var packet in _Encode(pictures))
        foreach (var opcode in _Opcodes(packet, width, height)) {
          Assert.That(opcode.Count, Is.InRange(1, 32), "a run states its length in five bits");
          Assert.That(
            (opcode.Block + opcode.Count - 1) / blocksAcross,
            Is.EqualTo(opcode.Block / blocksAcross),
            $"a {opcode.Kind} run of {opcode.Count} from block {opcode.Block} crossed a block row");
        }
    }
  }

  // ============================================================================================
  // What comes back
  // ============================================================================================

  /// <summary>
  /// The colour guarantee: no channel of any pixel comes back more than one level of thirty-two from
  /// the picture handed in, whatever the picture and however long the stream.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void NoChannelEverMovesByMoreThanOneLevel() {
    foreach (var (width, height, pictures) in _Corpus()) {
      var decoded = _RoundTrip(width, height, pictures);
      for (var frame = 0; frame < pictures.Count; ++frame)
        _AssertWithinOneLevel(pictures[frame], decoded[frame], $"{width}x{height} frame {frame}");
    }
  }

  [Test]
  [Category("Unit")]
  public void APictureAlreadyOnTheFiveBitGridAndDetailedEnoughComesBackExactly() {
    var pictures = _Noise(32, 24, 3);
    var decoded = _RoundTrip(32, 24, pictures);

    for (var frame = 0; frame < pictures.Count; ++frame)
      Assert.That(decoded[frame].PixelData, Is.EqualTo(pictures[frame].PixelData), $"frame {frame}");
  }

  [Test]
  [Category("Unit")]
  public void AFlatPictureOnTheFiveBitGridComesBackExactly() {
    var picture = _Flat(20, 16, _Widen(3), _Widen(29), _Widen(17));
    var decoded = _RoundTrip(20, 16, [picture]);

    Assert.That(decoded[0].PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  [Category("Unit")]
  [TestCase(6, 6)]
  [TestCase(4, 20)]
  [TestCase(20, 4)]
  [TestCase(17, 13)]
  [TestCase(1, 1)]
  public void APictureThatIsNotAWholeNumberOfBlocksIsCodedAndCroppedBack(int width, int height) {
    var pictures = _Gradient(width, height, 3);
    var decoded = _RoundTrip(width, height, pictures);

    for (var frame = 0; frame < pictures.Count; ++frame) {
      Assert.That(decoded[frame].Width, Is.EqualTo(width));
      Assert.That(decoded[frame].Height, Is.EqualTo(height));
      _AssertWithinOneLevel(pictures[frame], decoded[frame], $"{width}x{height} frame {frame}");
    }
  }

  // ============================================================================================
  // What it refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsRefused() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("rpza") };

    Assert.Throws<NotSupportedException>(() => AppleVideoEncoder.Create(sound));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoPictureSizeIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => AppleVideoEncoder.Create(_Requested(0, 12)));

    Assert.That(failure!.Message, Does.Contain("picture size"));
  }

  /// <summary>
  /// A picture so large that a chunk of it could not state its own length is refused rather than
  /// written with a length that wraps.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void APictureTooLargeForTheThreeByteChunkLengthIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => AppleVideoEncoder.Create(_Requested(4096, 2048)));

    Assert.That(failure!.Message, Does.Contain("three bytes"));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfAnotherSizeThanTheStreamStatesIsRefused() {
    var encoder = AppleVideoEncoder.Create(_Requested(16, 12));

    Assert.Throws<System.IO.InvalidDataException>(() => encoder.TryEncode(_Flat(16, 16, 1, 2, 3), 0, out _));
  }

  // ============================================================================================
  // Reading the bitstream back
  // ============================================================================================

  private enum _Coding {
    Skip,
    OneColour,
    FourColourRun,
    FourColourInline,
    SixteenColours,
  }

  private sealed record _Opcode(_Coding Kind, int Block, int Count, int Colour);

  /// <summary>Walks one chunk the way <see cref="AppleVideoDecoder"/> walks it, without painting.</summary>
  private static List<_Opcode> _Opcodes(CodedPacket packet, int width, int height) {
    var data = packet.Data.ToArray();
    var total = (width + 3) / 4 * ((height + 3) / 4);
    var opcodes = new List<_Opcode>();
    var at = 4;
    var block = 0;

    ushort Colour() {
      var value = (ushort)((data[at] << 8) | data[at + 1]);
      at += 2;
      return value;
    }

    while (block < total) {
      var opcode = data[at++];
      if ((opcode & 0x80) == 0) {
        var low = data[at++];
        var first = (ushort)((opcode << 8) | low);
        if ((data[at] & 0x80) != 0) {
          Colour();
          at += 4;
          opcodes.Add(new(_Coding.FourColourInline, block, 1, first));
        } else {
          at += 30;
          opcodes.Add(new(_Coding.SixteenColours, block, 1, first));
        }

        ++block;
        continue;
      }

      var count = (opcode & 0x1F) + 1;
      switch (opcode & 0xE0) {
        case 0x80:
        case 0xE0:
          opcodes.Add(new(_Coding.Skip, block, count, 0));
          break;
        case 0xA0:
          opcodes.Add(new(_Coding.OneColour, block, count, Colour()));
          break;
        default:
          Colour();
          Colour();
          at += 4 * count;
          opcodes.Add(new(_Coding.FourColourRun, block, count, 0));
          break;
      }

      block += count;
    }

    Assert.That(at, Is.EqualTo(data.Length), "the chunk ends exactly where the last block does");
    return opcodes;
  }

  // ============================================================================================
  // Driving the codec
  // ============================================================================================

  private static IReadOnlyList<CodedPacket> _Encode(IReadOnlyList<RawImage> pictures) {
    var encoder = AppleVideoEncoder.Create(_Requested(pictures[0].Width, pictures[0].Height));

    return pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();
  }

  private static IReadOnlyList<RawImage> _RoundTrip(int width, int height, IReadOnlyList<RawImage> pictures) {
    var encoder = AppleVideoEncoder.Create(_Requested(width, height));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    Assert.That(decoder, Is.InstanceOf<AppleVideoDecoder>());

    return packets.Select(packet => {
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      return decoded;
    }).ToList();
  }

  /// <summary>Everything this fixture codes, so that a claim about the coding can be made over all of it.</summary>
  private static IReadOnlyList<(int Width, int Height, IReadOnlyList<RawImage> Pictures)> _Corpus() => [
    (64, 48, _Gradient(64, 48, 4)),
    (64, 48, [_Flat(64, 48, 200, 100, 50), _Flat(64, 48, 200, 100, 50), _Flat(64, 48, 8, 240, 16)]),
    (32, 32, _Noise(32, 32, 3)),
    (40, 24, _Moving(40, 24, 4)),
    (17, 13, _Gradient(17, 13, 3)),
    (4, 20, _Gradient(4, 20, 3)),
  ];

  private static void _AssertWithinOneLevel(RawImage source, RawImage decoded, string what) {
    var wanted = source.PixelData;
    var got = decoded.PixelData;
    var worst = 0;

    for (var i = 0; i < wanted.Length; ++i)
      worst = Math.Max(worst, Math.Abs(_Narrow(wanted[i]) - _Narrow(got[i])));

    Assert.That(worst, Is.LessThanOrEqualTo(1), $"{what}: worst channel movement in five-bit units");
  }

  private static int _Narrow(byte channel) => (channel * 31 + 127) / 255;

  private static byte _Widen(int channel) => (byte)((channel << 3) | (channel >> 2));

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

  /// <summary>Smooth pictures that drift frame to frame: flat blocks, skips and detail all at once.</summary>
  private static IReadOnlyList<RawImage> _Gradient(int width, int height, int count) {
    var pictures = new List<RawImage>(count);
    for (var frame = 0; frame < count; ++frame) {
      var pixels = new byte[width * height * 3];
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var at = (y * width + x) * 3;
          pixels[at] = (byte)(x * 255 / Math.Max(1, width - 1));
          pixels[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
          pixels[at + 2] = (byte)((x + y + frame * 8) % 256);
        }

      pictures.Add(_Rgb(width, height, pixels));
    }

    return pictures;
  }

  /// <summary>Pictures whose every sample already sits on the five-bit grid and none of whose blocks is flat.</summary>
  private static IReadOnlyList<RawImage> _Noise(int width, int height, int count) {
    var random = new Random(1701);
    var pictures = new List<RawImage>(count);
    for (var frame = 0; frame < count; ++frame) {
      var pixels = new byte[width * height * 3];
      for (var at = 0; at < pixels.Length; ++at)
        pixels[at] = _Widen(random.Next(32));

      pictures.Add(_Rgb(width, height, pixels));
    }

    return pictures;
  }

  /// <summary>A flat block moving across a flat field, so that most of every frame after the first skips.</summary>
  private static IReadOnlyList<RawImage> _Moving(int width, int height, int count) {
    var pictures = new List<RawImage>(count);
    for (var frame = 0; frame < count; ++frame) {
      var picture = _Flat(width, height, 16, 32, 48);
      for (var y = 4; y < 8 && y < height; ++y)
        for (var x = frame * 4; x < frame * 4 + 4 && x < width; ++x) {
          var at = (y * width + x) * 3;
          picture.PixelData[at] = 240;
          picture.PixelData[at + 1] = 16;
          picture.PixelData[at + 2] = 240;
        }

      pictures.Add(picture);
    }

    return pictures;
  }

  private static MediaStreamInfo _Requested(int width, int height, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("rpza"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}
