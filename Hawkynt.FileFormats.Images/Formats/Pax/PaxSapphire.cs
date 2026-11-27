using System;

namespace FileFormat.Pax;

/// <summary>Sapphire II, the byte-wise stream cipher and hash Pick Ax uses.</summary>
/// <remarks>
/// Michael Paul Johnson's cipher (1995, placed in the public domain; later shipped in early
/// Crypto++ releases). A 256-entry card deck is shuffled by the key and then stirred by every byte
/// that passes through, so the keystream depends on the plaintext as well as the key. Pick Ax uses
/// it twice: unkeyed, as a hash that turns the password into the Blowfish key, and keyed with that
/// result, as one of its payload ciphers.
/// </remarks>
internal sealed class PaxSapphire {

  private readonly byte[] _cards = new byte[256];
  private byte _rotor;
  private byte _ratchet;
  private byte _avalanche;
  private byte _lastPlain;
  private byte _lastCipher;

  private PaxSapphire() { }

  /// <summary>A deck shuffled by <paramref name="key"/>, ready to encrypt or decrypt.</summary>
  public static PaxSapphire Keyed(ReadOnlySpan<byte> key) {
    var result = new PaxSapphire();
    if (key.IsEmpty) {
      result._HashInit();
      return result;
    }

    var cards = result._cards;
    for (var i = 0; i < 256; ++i)
      cards[i] = (byte)i;

    var keyPosition = 0;
    byte runningSum = 0;
    for (var i = 255; i >= 0; --i) {
      var toSwap = result._KeyRand(i, key, ref runningSum, ref keyPosition);
      (cards[i], cards[toSwap]) = (cards[toSwap], cards[i]);
    }

    result._rotor = cards[1];
    result._ratchet = cards[3];
    result._avalanche = cards[5];
    result._lastPlain = cards[7];
    result._lastCipher = cards[runningSum];
    return result;
  }

  /// <summary>The Sapphire II hash of <paramref name="data"/>, <paramref name="length"/> bytes long.</summary>
  public static byte[] Hash(ReadOnlySpan<byte> data, int length) {
    var sapphire = new PaxSapphire();
    sapphire._HashInit();
    foreach (var value in data)
      sapphire.Encrypt(value);

    for (var i = 255; i >= 0; --i)
      sapphire.Encrypt((byte)i);

    var result = new byte[length];
    for (var i = 0; i < length; ++i)
      result[i] = sapphire.Encrypt(0);

    return result;
  }

  private void _HashInit() {
    _rotor = 1;
    _ratchet = 3;
    _avalanche = 5;
    _lastPlain = 7;
    _lastCipher = 11;
    for (var i = 0; i < 256; ++i)
      _cards[i] = (byte)(255 - i);
  }

  private int _KeyRand(int limit, ReadOnlySpan<byte> key, ref byte runningSum, ref int keyPosition) {
    if (limit == 0)
      return 0;

    var mask = 1;
    while (mask < limit)
      mask = (mask << 1) + 1;

    var retries = 0;
    int result;
    do {
      runningSum = (byte)(_cards[runningSum] + key[keyPosition++]);
      if (keyPosition >= key.Length) {
        keyPosition = 0;
        runningSum = (byte)(runningSum + key.Length);
      }

      result = mask & runningSum;
      if (++retries > 11)
        result %= limit;
    } while (result > limit);

    return result;
  }

  private byte _Shuffle() {
    var cards = _cards;
    _ratchet = (byte)(_ratchet + cards[_rotor++]);
    var swap = cards[_lastCipher];
    cards[_lastCipher] = cards[_ratchet];
    cards[_ratchet] = cards[_lastPlain];
    cards[_lastPlain] = cards[_rotor];
    cards[_rotor] = swap;
    _avalanche = (byte)(_avalanche + cards[swap]);
    return (byte)(cards[(byte)(cards[_ratchet] + cards[_rotor])]
                  ^ cards[cards[(byte)(cards[_lastPlain] + cards[_lastCipher] + cards[_avalanche])]]);
  }

  public byte Encrypt(byte plain) {
    _lastCipher = (byte)(plain ^ _Shuffle());
    _lastPlain = plain;
    return _lastCipher;
  }

  public byte Decrypt(byte cipher) {
    _lastPlain = (byte)(cipher ^ _Shuffle());
    _lastCipher = cipher;
    return _lastPlain;
  }
}
