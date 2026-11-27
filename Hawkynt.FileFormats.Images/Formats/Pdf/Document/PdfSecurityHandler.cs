using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace FileFormat.Pdf.Document;

/// <summary>
/// The standard security handler of PDF 32000-1 §7.6.3 and ISO 32000-2 §7.6.4, opened with the
/// empty user password.
/// </summary>
/// <remarks>
/// Written from the normative algorithms (rung 3): Algorithm 2 for the RC4 and AES-128 file key,
/// Algorithm 1 for the per-object key, Algorithms 4 and 5 to check the user password, and Algorithm
/// 2.A with 2.B for the AES-256 revisions. A document that opens without a password is the common
/// case — permissions-only encryption — and is what is supported; one that needs a password is
/// refused by name rather than rendered as noise.
/// <para/>
/// MD5, the SHA-2 hashes and AES come from the base class library, as SHA-256 already does elsewhere
/// in this package; RC4 is not offered there and is a dozen lines.
/// </remarks>
internal sealed class PdfSecurityHandler {

  /// <summary>The padding string of Algorithm 2, step a, which stands in for an empty password.</summary>
  private static readonly byte[] _Padding = [
    0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
    0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
  ];

  private enum Method { None, Rc4, AesV2, AesV3 }

  private readonly byte[] _key;
  private readonly Method _streamMethod;
  private readonly Method _stringMethod;
  private readonly Dictionary<string, Method> _filters = new(StringComparer.Ordinal);

  private PdfSecurityHandler(byte[] key, Method streams, Method strings, Dictionary<string, Method> filters) {
    this._key = key;
    this._streamMethod = streams;
    this._stringMethod = strings;
    this._filters = filters;
  }

  /// <summary>Opens the encryption dictionary with the empty user password.</summary>
  /// <exception cref="NotSupportedException">The handler is not the standard one, or a password is needed.</exception>
  public static PdfSecurityHandler Open(PdfDocument document, PdfDict encrypt, byte[]? firstId) {
    var filter = document.Resolve(encrypt.Raw("Filter")) as string;
    if (filter != "Standard")
      throw new NotSupportedException($"PDF encryption with the /{filter ?? "?"} security handler is not supported; only the standard password handler is.");

    var v = PdfValues.Integer(document.Resolve(encrypt.Raw("V")), 0);
    var r = PdfValues.Integer(document.Resolve(encrypt.Raw("R")), 2);
    var lengthBits = PdfValues.Integer(document.Resolve(encrypt.Raw("Length")), 40);
    var o = document.Resolve(encrypt.Raw("O")) as byte[] ?? [];
    var u = document.Resolve(encrypt.Raw("U")) as byte[] ?? [];
    var p = PdfValues.Integer(document.Resolve(encrypt.Raw("P")), 0);
    var encryptMetadata = document.Resolve(encrypt.Raw("EncryptMetadata")) is not false;
    var id = firstId ?? [];

    var filters = new Dictionary<string, Method>(StringComparer.Ordinal) { ["Identity"] = Method.None };
    Method streams, strings;
    if (v >= 4) {
      if (document.Resolve(encrypt.Raw("CF")) is PdfDict cf)
        foreach (var (name, value) in cf) {
          if (document.Resolve(value) is not PdfDict entry)
            continue;

          var cfm = document.Resolve(entry.Raw("CFM")) as string;
          filters[name] = cfm switch {
            "V2" => Method.Rc4,
            "AESV2" => Method.AesV2,
            "AESV3" => Method.AesV3,
            "None" or null => Method.None,
            _ => throw new NotSupportedException($"The PDF crypt filter method /{cfm} is not supported."),
          };
          if (cfm == "V2" && document.Resolve(entry.Raw("Length")) is { } len && PdfValues.IsNumber(len)) {
            var l = PdfValues.Integer(len);
            lengthBits = l <= 32 ? l * 8 : l;
          }
        }

      streams = _Lookup(filters, document.Resolve(encrypt.Raw("StmF")) as string ?? "Identity");
      strings = _Lookup(filters, document.Resolve(encrypt.Raw("StrF")) as string ?? "Identity");
    } else {
      streams = strings = Method.Rc4;
    }

    if (v is not (1 or 2 or 4 or 5))
      throw new NotSupportedException($"PDF encryption version V {v} is not supported.");

    byte[] key;
    if (r >= 5) {
      key = _OpenAes256(r, o, u, document.Resolve(encrypt.Raw("UE")) as byte[] ?? []);
    } else {
      var length = r == 2 ? 5 : Math.Clamp(lengthBits / 8, 5, 16);
      key = _FileKey(r, length, o, p, id, encryptMetadata);
      if (!_UserPasswordMatches(r, key, u, id))
        throw new NotSupportedException("This PDF is encrypted with a user password; opening it needs that password, which is not supported.");
    }

    return new(key, streams, strings, filters);
  }

  private static Method _Lookup(Dictionary<string, Method> filters, string name)
    => filters.TryGetValue(name, out var method) ? method : throw new NotSupportedException($"The PDF crypt filter /{name} is not defined by the document.");

  /// <summary>Decrypts a string belonging to an object.</summary>
  public byte[] DecryptString(byte[] data, int objectNumber, int generation) => this._Decrypt(this._stringMethod, data, objectNumber, generation);

  /// <summary>Decrypts a stream belonging to an object, honouring a /Crypt filter that names its own crypt filter.</summary>
  public byte[] DecryptStream(byte[] data, int objectNumber, int generation, string? cryptFilterName) {
    var method = this._streamMethod;
    if (cryptFilterName != null)
      method = this._filters.TryGetValue(cryptFilterName, out var named) ? named : method;

    return this._Decrypt(method, data, objectNumber, generation);
  }

  private byte[] _Decrypt(Method method, byte[] data, int objectNumber, int generation) {
    switch (method) {
      case Method.None:
        return data;
      case Method.AesV3:
        return _AesCbcDecrypt(this._key, data);
    }

    // Algorithm 1: the file key, the low three bytes of the object number and two of the generation,
    // and for AES the four bytes "sAlT", hashed and cut to the key length plus five.
    var material = new byte[this._key.Length + 5 + (method == Method.AesV2 ? 4 : 0)];
    this._key.CopyTo(material, 0);
    var at = this._key.Length;
    material[at] = (byte)objectNumber;
    material[at + 1] = (byte)(objectNumber >> 8);
    material[at + 2] = (byte)(objectNumber >> 16);
    material[at + 3] = (byte)generation;
    material[at + 4] = (byte)(generation >> 8);
    if (method == Method.AesV2) {
      material[at + 5] = 0x73;
      material[at + 6] = 0x41;
      material[at + 7] = 0x6C;
      material[at + 8] = 0x54;
    }

    var objectKey = MD5.HashData(material).AsSpan(0, Math.Min(this._key.Length + 5, 16)).ToArray();
    return method == Method.AesV2 ? _AesCbcDecrypt(objectKey, data) : Rc4(objectKey, data);
  }

  /// <summary>RC4: the key schedule, then the keystream XORed over the data.</summary>
  public static byte[] Rc4(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data) {
    Span<byte> s = stackalloc byte[256];
    for (var i = 0; i < 256; ++i)
      s[i] = (byte)i;

    for (int i = 0, j = 0; i < 256; ++i) {
      j = (j + s[i] + key[i % key.Length]) & 0xFF;
      (s[i], s[j]) = (s[j], s[i]);
    }

    var output = new byte[data.Length];
    for (int n = 0, i = 0, j = 0; n < data.Length; ++n) {
      i = (i + 1) & 0xFF;
      j = (j + s[i]) & 0xFF;
      (s[i], s[j]) = (s[j], s[i]);
      output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 0xFF]);
    }

    return output;
  }

  /// <summary>AES-CBC with the IV in the first block and PKCS#5 padding, tolerating a bad pad as viewers do.</summary>
  private static byte[] _AesCbcDecrypt(byte[] key, byte[] data) {
    if (data.Length < 16)
      return [];

    var blocks = (data.Length - 16) / 16 * 16;
    if (blocks == 0)
      return [];

    using var aes = Aes.Create();
    aes.Key = key;
    var plain = aes.DecryptCbc(data.AsSpan(16, blocks), data.AsSpan(0, 16), PaddingMode.None);
    var pad = plain[^1];
    if (pad is >= 1 and <= 16 && pad <= plain.Length) {
      var valid = true;
      for (var i = plain.Length - pad; i < plain.Length; ++i)
        valid &= plain[i] == pad;
      if (valid)
        return plain[..^pad];
    }

    return plain;
  }

  /// <summary>Algorithm 2: the file key for revisions 2 to 4, from the empty user password.</summary>
  private static byte[] _FileKey(int revision, int length, byte[] owner, int permissions, byte[] id, bool encryptMetadata) {
    using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
    md5.AppendData(_Padding);
    md5.AppendData(owner.AsSpan(0, Math.Min(32, owner.Length)));
    md5.AppendData([(byte)permissions, (byte)(permissions >> 8), (byte)(permissions >> 16), (byte)(permissions >> 24)]);
    md5.AppendData(id);
    if (revision >= 4 && !encryptMetadata)
      md5.AppendData([0xFF, 0xFF, 0xFF, 0xFF]);

    var hash = md5.GetHashAndReset();
    if (revision >= 3)
      for (var i = 0; i < 50; ++i)
        hash = MD5.HashData(hash.AsSpan(0, length));

    return hash.AsSpan(0, length).ToArray();
  }

  /// <summary>Algorithms 4 and 5: whether the key opens the user entry.</summary>
  private static bool _UserPasswordMatches(int revision, byte[] key, byte[] user, byte[] id) {
    if (user.Length < 16)
      return false;

    if (revision == 2) {
      var expected = Rc4(key, _Padding);
      return expected.AsSpan().SequenceEqual(user.AsSpan(0, Math.Min(32, user.Length)));
    }

    using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
    md5.AppendData(_Padding);
    md5.AppendData(id);
    var value = Rc4(key, md5.GetHashAndReset());
    var round = new byte[key.Length];
    for (var i = 1; i <= 19; ++i) {
      for (var k = 0; k < key.Length; ++k)
        round[k] = (byte)(key[k] ^ i);
      value = Rc4(round, value);
    }

    return value.AsSpan(0, 16).SequenceEqual(user.AsSpan(0, 16));
  }

  /// <summary>Algorithm 2.A with the empty password: check the user entry, then unwrap the file key.</summary>
  private static byte[] _OpenAes256(int revision, byte[] owner, byte[] user, byte[] userKey) {
    if (user.Length < 48 || userKey.Length < 32)
      throw new InvalidDataException("The PDF's AES-256 encryption entries are too short.");

    var validationSalt = user.AsSpan(32, 8);
    var keySalt = user.AsSpan(40, 8);
    var check = _Hash(revision, [], validationSalt, []);
    if (!check.AsSpan(0, 32).SequenceEqual(user.AsSpan(0, 32)))
      throw new NotSupportedException("This PDF is encrypted with a user password; opening it needs that password, which is not supported.");

    var intermediate = _Hash(revision, [], keySalt, []);
    using var aes = Aes.Create();
    aes.Key = intermediate.AsSpan(0, 32).ToArray();
    return aes.DecryptCbc(userKey.AsSpan(0, 32), new byte[16], PaddingMode.None);
  }

  /// <summary>The revision 5 hash (one SHA-256) or Algorithm 2.B for revision 6.</summary>
  private static byte[] _Hash(int revision, ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> userData) {
    var input = new byte[password.Length + salt.Length + userData.Length];
    password.CopyTo(input);
    salt.CopyTo(input.AsSpan(password.Length));
    userData.CopyTo(input.AsSpan(password.Length + salt.Length));
    var k = SHA256.HashData(input);
    if (revision < 6)
      return k;

    using var aes = Aes.Create();
    for (var round = 0; ; ++round) {
      var unit = password.Length + k.Length + userData.Length;
      var k1 = new byte[unit * 64];
      for (var r = 0; r < 64; ++r) {
        var at = r * unit;
        password.CopyTo(k1.AsSpan(at));
        k.CopyTo(k1.AsSpan(at + password.Length));
        userData.CopyTo(k1.AsSpan(at + password.Length + k.Length));
      }

      aes.Key = k.AsSpan(0, 16).ToArray();
      var e = aes.EncryptCbc(k1, k.AsSpan(16, 16), PaddingMode.None);

      // The first sixteen bytes as a big-endian number, modulo three, which is the sum of the bytes
      // modulo three because 256 is one more than a multiple of three.
      var sum = 0;
      for (var i = 0; i < 16; ++i)
        sum += e[i];

      k = (sum % 3) switch {
        0 => SHA256.HashData(e),
        1 => SHA384.HashData(e),
        _ => SHA512.HashData(e),
      };

      if (round >= 63 && e[^1] <= round + 1 - 32)
        break;
    }

    return k.AsSpan(0, 32).ToArray();
  }
}
