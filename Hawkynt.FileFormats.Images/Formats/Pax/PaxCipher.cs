using System;
using System.Text;

namespace FileFormat.Pax;

/// <summary>The keys a Pick Ax password stands for, and the six ways a row can be encrypted with them.</summary>
/// <remarks>
/// None of this is written down anywhere Smaller Animals published; it was measured against their
/// own PAXIO.dll and ImgSource 5:
/// <list type="bullet">
/// <item>The Blowfish key is the first seven bytes of the Sapphire II hash of the password — a
/// 56-bit key, the export limit of the day.</item>
/// <item>The chaining value for the CBC modes is those seven bytes and a zero.</item>
/// <item>Every encryption call starts afresh: CBC restarts from that value and Sapphire is rekeyed,
/// once for a row's eight-byte header and once for the row itself.</item>
/// </list>
/// </remarks>
internal sealed class PaxCipher {

  /// <summary>Bytes in the Blowfish key a password becomes.</summary>
  internal const int KeySize = 7;

  private readonly byte[] _key;
  private readonly byte[] _iv;
  private readonly PaxBlowfish _blowfish;

  public PaxCipher(string password) {
    ArgumentNullException.ThrowIfNull(password);
    _key = DeriveKey(password);
    _iv = new byte[8];
    _key.CopyTo(_iv, 0);
    _blowfish = new(_key);
  }

  /// <summary>The seven-byte Blowfish key for <paramref name="password"/>.</summary>
  /// <remarks>
  /// The password reaches the hash as the bytes of a C string in the Windows ANSI code page; Latin-1
  /// is the same for every character a Western code page holds, and the only choice that does not
  /// depend on the machine.
  /// </remarks>
  internal static byte[] DeriveKey(string password) => PaxSapphire.Hash(Encoding.Latin1.GetBytes(password), KeySize);

  /// <summary>Plain Blowfish over whole blocks — the header, palette and tags.</summary>
  public void DecryptHeader(Span<byte> data) {
    for (var i = 0; i + 8 <= data.Length; i += 8)
      _blowfish.DecryptBlock(data.Slice(i, 8));
  }

  /// <inheritdoc cref="DecryptHeader"/>
  public void EncryptHeader(Span<byte> data) {
    for (var i = 0; i + 8 <= data.Length; i += 8)
      _blowfish.EncryptBlock(data.Slice(i, 8));
  }

  /// <summary>Undoes <see cref="Encrypt"/> for one call's worth of row bytes.</summary>
  public void Decrypt(PaxEncryption encryption, Span<byte> data) {
    switch (encryption) {
      case PaxEncryption.Blowfish:
        DecryptHeader(data);
        break;
      case PaxEncryption.BlowfishCbc:
        _DecryptCbc(data);
        break;
      case PaxEncryption.Sapphire:
        _DecryptSapphire(data);
        break;
      case PaxEncryption.BlowfishThenSapphire:
        _DecryptSapphire(data);
        DecryptHeader(data);
        break;
      case PaxEncryption.SapphireThenBlowfish:
        DecryptHeader(data);
        _DecryptSapphire(data);
        break;
      case PaxEncryption.BlowfishCbcThenSapphire:
        _DecryptSapphire(data);
        _DecryptCbc(data);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(encryption), encryption, "Not a Pick Ax encryption type.");
    }
  }

  /// <summary>Encrypts one call's worth of row bytes; the length must be a multiple of eight.</summary>
  public void Encrypt(PaxEncryption encryption, Span<byte> data) {
    switch (encryption) {
      case PaxEncryption.Blowfish:
        EncryptHeader(data);
        break;
      case PaxEncryption.BlowfishCbc:
        _EncryptCbc(data);
        break;
      case PaxEncryption.Sapphire:
        _EncryptSapphire(data);
        break;
      case PaxEncryption.BlowfishThenSapphire:
        EncryptHeader(data);
        _EncryptSapphire(data);
        break;
      case PaxEncryption.SapphireThenBlowfish:
        _EncryptSapphire(data);
        EncryptHeader(data);
        break;
      case PaxEncryption.BlowfishCbcThenSapphire:
        _EncryptCbc(data);
        _EncryptSapphire(data);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(encryption), encryption, "Not a Pick Ax encryption type.");
    }
  }

  private void _DecryptCbc(Span<byte> data) {
    Span<byte> previous = stackalloc byte[8];
    Span<byte> current = stackalloc byte[8];
    _iv.CopyTo(previous);
    for (var i = 0; i + 8 <= data.Length; i += 8) {
      var block = data.Slice(i, 8);
      block.CopyTo(current);
      _blowfish.DecryptBlock(block);
      for (var j = 0; j < 8; ++j)
        block[j] ^= previous[j];
      current.CopyTo(previous);
    }
  }

  private void _EncryptCbc(Span<byte> data) {
    Span<byte> previous = stackalloc byte[8];
    _iv.CopyTo(previous);
    for (var i = 0; i + 8 <= data.Length; i += 8) {
      var block = data.Slice(i, 8);
      for (var j = 0; j < 8; ++j)
        block[j] ^= previous[j];
      _blowfish.EncryptBlock(block);
      block.CopyTo(previous);
    }
  }

  private void _DecryptSapphire(Span<byte> data) {
    var sapphire = PaxSapphire.Keyed(_key);
    for (var i = 0; i < data.Length; ++i)
      data[i] = sapphire.Decrypt(data[i]);
  }

  private void _EncryptSapphire(Span<byte> data) {
    var sapphire = PaxSapphire.Keyed(_key);
    for (var i = 0; i < data.Length; ++i)
      data[i] = sapphire.Encrypt(data[i]);
  }
}
