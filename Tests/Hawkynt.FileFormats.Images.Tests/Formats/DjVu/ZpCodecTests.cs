using FileFormat.DjVu.Codec;

namespace FileFormat.DjVu.Tests;

[TestFixture]
public sealed class ZpCodecTests {

  [Test]
  public void StateTable_PublishedValuesAreStable() {
    Assert.Multiple(() => {
      Assert.That(ZpTables.P, Has.Length.EqualTo(256));
      Assert.That(ZpTables.M, Has.Length.EqualTo(256));
      Assert.That(ZpTables.Up, Has.Length.EqualTo(256));
      Assert.That(ZpTables.Dn, Has.Length.EqualTo(256));
      Assert.That(ZpTables.P[0], Is.EqualTo(0x8000));
      Assert.That(ZpTables.P[83], Is.EqualTo(0x5695));
      Assert.That(ZpTables.P[250], Is.EqualTo(0x481a));
      Assert.That(ZpTables.M[3], Is.EqualTo(0x10a5));
      Assert.That(ZpTables.M[82], Is.EqualTo(0x7fff));
      Assert.That(ZpTables.M[83], Is.Zero);
      Assert.That(ZpTables.Up[0], Is.EqualTo(84));
      Assert.That(ZpTables.Dn[0], Is.EqualTo(145));
    });
  }

  [Test]
  public void Passthrough_AlternatingBitsHaveStableBitstream() {
    var encoder = new ZpEncoder();

    for (var i = 0; i < 32; ++i)
      encoder.EncodePassthrough(i & 1);

    var encoded = encoder.Finish();
    Assert.That(encoded, Is.EqualTo(new byte[] { 0xaa, 0xaa, 0xaa, 0xaa }));

    var decoder = new ZpDecoder(encoded);
    for (var i = 0; i < 32; ++i)
      Assert.That(decoder.DecodePassthrough(), Is.EqualTo(i & 1), $"bit {i}");
  }

  [Test]
  public void AdaptiveCoder_RoundTripsMultipleIndependentContexts() {
    const int bitCount = 4096;
    var encodedContexts = new ZpContext[4];
    var expected = new (int Context, int Bit)[bitCount];
    var encoder = new ZpEncoder();
    uint state = 0x9e3779b9;

    for (var i = 0; i < bitCount; ++i) {
      state ^= state << 13;
      state ^= state >> 17;
      state ^= state << 5;

      var context = i & 3;
      var bit = (int)(state & 1);
      expected[i] = (context, bit);
      encoder.EncodeBit(bit, ref encodedContexts[context]);
    }

    var payload = encoder.Finish();
    var decoder = new ZpDecoder(payload);
    var decodedContexts = new ZpContext[4];

    for (var i = 0; i < expected.Length; ++i) {
      var (context, bit) = expected[i];
      Assert.That(decoder.DecodeBit(ref decodedContexts[context]), Is.EqualTo(bit), $"bit {i}");
    }

    for (var i = 0; i < encodedContexts.Length; ++i)
      Assert.That(decodedContexts[i].Value, Is.EqualTo(encodedContexts[i].Value), $"context {i}");
  }
}
