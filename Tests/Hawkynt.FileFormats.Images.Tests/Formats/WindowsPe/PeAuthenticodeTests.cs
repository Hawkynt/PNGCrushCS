using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.WindowsPe.Tests;

/// <summary>
/// Editing a PE resource drops the file's Authenticode certificate, and says so by doing it.
/// </summary>
/// <remarks>
/// <para>
/// The digest an Authenticode signature is taken over covers the whole file apart from three
/// things: the optional header's CheckSum field, the security data directory entry, and the
/// certificate table itself. A resource edit changes bytes inside that range whichever path it
/// takes -- overwriting a leaf in place moves no offsets at all and still invalidates the
/// signature -- so there is no version of this operation that leaves a signed binary signed.
/// </para>
/// <para>
/// Of the three possible behaviours, carrying the certificate through is the worst: the result
/// still advertises a signature, no longer verifies, and is handed back as a success, so anything
/// downstream that reads a present certificate as "this file is signed" is misled by a file this
/// library produced. Refusing outright would be honest but would make a signed executable
/// uneditable for no gain, since an unsigned edit is exactly what the caller is asking for.
/// Stripping is what an edited binary actually is, so that is what happens.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PeAuthenticodeTests {

  private const int _SecurityDirectoryIndex = 4;

  /// <summary>A recognisable stand-in for a WIN_CERTIFICATE blob.</summary>
  private static byte[] _CertificateBytes(int length = 64) {
    var certificate = new byte[length];
    BinaryPrimitives.WriteUInt32LittleEndian(certificate, (uint)length); // dwLength
    BinaryPrimitives.WriteUInt16LittleEndian(certificate.AsSpan(4), 0x0200); // WIN_CERT_REVISION_2_0
    BinaryPrimitives.WriteUInt16LittleEndian(certificate.AsSpan(6), 0x0002); // PKCS_SIGNED_DATA
    for (var i = 8; i < length; ++i)
      certificate[i] = 0xC5;

    return certificate;
  }

  /// <summary>An editable PE whose security data directory points at a certificate table.</summary>
  private static byte[] _Sign(byte[] unsigned, byte[] certificate, byte[]? appendedAfterCertificate = null) {
    var trailer = appendedAfterCertificate ?? [];
    var signed = new byte[unsigned.Length + certificate.Length + trailer.Length];
    unsigned.CopyTo(signed, 0);
    certificate.CopyTo(signed, unsigned.Length);
    trailer.CopyTo(signed, unsigned.Length + certificate.Length);

    var securityOffset = _SecurityDirectoryOffset(signed);
    BinaryPrimitives.WriteUInt32LittleEndian(signed.AsSpan(securityOffset), (uint)unsigned.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(signed.AsSpan(securityOffset + 4), (uint)certificate.Length);
    return signed;
  }

  private static int _SecurityDirectoryOffset(byte[] pe) {
    var peOffset = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(60));
    var optionalOffset = peOffset + 4 + 20;
    var magic = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(optionalOffset));
    var dataDirectoryOffset = optionalOffset + (magic == 0x20B ? 112 : 96);
    return dataDirectoryOffset + _SecurityDirectoryIndex * 8;
  }

  private static (uint Offset, uint Size) _SecurityDirectory(byte[] pe) {
    var offset = _SecurityDirectoryOffset(pe);
    return (
      BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(offset)),
      BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(offset + 4))
    );
  }

  private static bool _Contains(byte[] haystack, byte[] needle) {
    for (var i = 0; i + needle.Length <= haystack.Length; ++i) {
      var found = true;
      for (var j = 0; j < needle.Length; ++j)
        if (haystack[i + j] != needle[j]) {
          found = false;
          break;
        }

      if (found)
        return true;
    }

    return false;
  }

  private static byte[] _UnsignedPeWithBitmapAndCursor() {
    var cursor = PeResourceReader
      .FromBytes(MinimalPeBuilder.BuildWithCursorGroup(
        MinimalPeBuilder.CreateMinimalIconEntry(16, 16),
        width: 16,
        height: 16,
        hotspotX: 3,
        hotspotY: 7,
        groupId: 23
      ))
      .ImageResources
      .Single();

    return PeResourceWriter.ToBytes(new PeResourceFile {
      ImageResources = [
        new PeImageResource {
          ResourceType = PeImageResourceType.Cursor,
          ResourceTypeId = 12,
          ResourceId = 23,
          Data = cursor.Data,
        },
        new PeImageResource {
          ResourceType = PeImageResourceType.EmbeddedImage,
          ResourceTypeId = 10,
          ResourceId = 7,
          Data = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8],
        }
      ],
      ModuleKind = PeResourceModuleKind.Dll,
    });
  }

  /// <summary>A raw resource replacement that fits in place still drops the certificate.</summary>
  /// <remarks>
  /// The in-place path is the interesting one: it shifts nothing, so an editor that only worried
  /// about moving file offsets would leave the signature looking untouched while the bytes it
  /// covers have changed underneath it.
  /// </remarks>
  [Test]
  public void ReplaceResource_FittingInPlace_RemovesTheCertificateTable() {
    var certificate = _CertificateBytes();
    var unsigned = _UnsignedPeWithBitmapAndCursor();
    var signed = _Sign(unsigned, certificate);

    Assume.That(_SecurityDirectory(signed).Size, Is.EqualTo((uint)certificate.Length));

    var edited = FormatIO.Write(
      PeResourceFile.ReadEditable(signed).ReplaceResource(10, 7, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A })
    );

    Assert.Multiple(() => {
      Assert.That(_SecurityDirectory(edited).Offset, Is.Zero, "the security directory's file offset");
      Assert.That(_SecurityDirectory(edited).Size, Is.Zero, "the security directory's size");
      Assert.That(_Contains(edited, certificate), Is.False, "the certificate bytes must be gone");
      Assert.That(edited, Has.Length.EqualTo(unsigned.Length), "the table is cut off the end");
    });
  }

  /// <summary>The growing path drops it too, and does not merely relocate it.</summary>
  [Test]
  public void ReplaceResource_GrowingTheSection_RemovesTheCertificateTable() {
    var certificate = _CertificateBytes();
    var signed = _Sign(_UnsignedPeWithBitmapAndCursor(), certificate);
    var replacement = new byte[4096];
    replacement[0] = 0x89;
    replacement[1] = (byte)'P';
    replacement[2] = (byte)'N';
    replacement[3] = (byte)'G';
    replacement[4] = 0x0D;
    replacement[5] = 0x0A;
    replacement[6] = 0x1A;
    replacement[7] = 0x0A;

    var edited = FormatIO.Write(PeResourceFile.ReadEditable(signed).ReplaceResource(10, 7, replacement));

    Assert.Multiple(() => {
      Assert.That(_SecurityDirectory(edited), Is.EqualTo((0u, 0u)));
      Assert.That(_Contains(edited, certificate), Is.False, "the certificate bytes must be gone");
    });
  }

  /// <summary>Replacing an image through a cursor group takes the same route.</summary>
  [Test]
  public void ReplaceImage_CursorGroup_RemovesTheCertificateTable() {
    var certificate = _CertificateBytes();
    var signed = _Sign(_UnsignedPeWithBitmapAndCursor(), certificate);
    var replacement = new RawImage {
      Width = 8,
      Height = 8,
      Format = PixelFormat.Rgb24,
      PixelData = Enumerable.Range(0, 8 * 8 * 3).Select(static i => (byte)(i * 13 + 5)).ToArray(),
    };

    var edited = FormatIO.Write(
      PeResourceFile.ReadEditable(signed).ReplaceImage(PeImageResourceType.Cursor, 23, 0, replacement)
    );

    Assert.Multiple(() => {
      Assert.That(_SecurityDirectory(edited), Is.EqualTo((0u, 0u)));
      Assert.That(_Contains(edited, certificate), Is.False, "the certificate bytes must be gone");
    });
  }

  /// <summary>
  /// Where something has been appended behind the certificate, the directory is cleared but the
  /// file is not truncated, because truncating would take the appended data with it.
  /// </summary>
  [Test]
  public void ReplaceResource_DataAppendedBehindTheCertificate_ClearsWithoutTruncating() {
    var certificate = _CertificateBytes();
    var trailer = Enumerable.Range(0, 32).Select(static i => (byte)(0xA0 + i % 16)).ToArray();
    var signed = _Sign(_UnsignedPeWithBitmapAndCursor(), certificate, trailer);

    var edited = FormatIO.Write(
      PeResourceFile.ReadEditable(signed).ReplaceResource(10, 7, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A })
    );

    Assert.Multiple(() => {
      Assert.That(_SecurityDirectory(edited), Is.EqualTo((0u, 0u)), "no certificate table is declared any more");
      Assert.That(_Contains(edited, trailer), Is.True, "appended data behind the certificate is not this editor's to delete");
    });
  }

  /// <summary>An unsigned binary is not shortened by a strip that has nothing to strip.</summary>
  /// <remarks>
  /// The guard against the opposite defect: a stripper that trusted a zero offset less carefully
  /// could truncate a perfectly good file at 0.
  /// </remarks>
  [Test]
  public void ReplaceResource_UnsignedBinary_KeepsItsLengthAndEmptyDirectory() {
    var unsigned = _UnsignedPeWithBitmapAndCursor();

    var edited = FormatIO.Write(
      PeResourceFile.ReadEditable(unsigned).ReplaceResource(10, 7, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A })
    );

    Assert.Multiple(() => {
      Assert.That(_SecurityDirectory(edited), Is.EqualTo((0u, 0u)));
      Assert.That(edited, Has.Length.EqualTo(unsigned.Length));
    });
  }

  /// <summary>The resources still read back correctly once the certificate is gone.</summary>
  /// <remarks>
  /// Stripping moves the end of the file, so this is the check that nothing inside it moved with
  /// the certificate.
  /// </remarks>
  [Test]
  public void ReplaceResource_AfterStripping_LeavesTheOtherResourcesIntact() {
    var signed = _Sign(_UnsignedPeWithBitmapAndCursor(), _CertificateBytes());
    var before = PeResourceReader.FromBytes(signed);

    var edited = FormatIO.Write(
      PeResourceFile.ReadEditable(signed).ReplaceResource(10, 7, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A })
    );
    var after = PeResourceReader.FromBytes(edited);

    var cursorBefore = before.ImageResources.Single(resource => resource.ResourceType == PeImageResourceType.Cursor);
    var cursorAfter = after.ImageResources.Single(resource => resource.ResourceType == PeImageResourceType.Cursor);

    Assert.Multiple(() => {
      Assert.That(cursorAfter.ResourceId, Is.EqualTo(cursorBefore.ResourceId));
      Assert.That(cursorAfter.Data, Is.EqualTo(cursorBefore.Data), "the untouched cursor group survives the strip");
    });
  }
}
