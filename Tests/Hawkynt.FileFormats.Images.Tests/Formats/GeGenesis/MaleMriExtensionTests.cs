using System;
using System.IO;
using FileFormat.Core;
using FileFormat.GeGenesis;

namespace FileFormat.GeGenesis.Tests;

/// <summary>
/// XnView's "Male MRI" — <c>.pd</c>, <c>.t1</c> and <c>.t2</c> — is this format under the other half
/// of the Visible Human dataset.
/// </summary>
/// <remarks>
/// The two rows of XnView's format table, "Male Normal CT" for <c>.fre</c> and "Male MRI" for the
/// three others, name one and the same loader address between them, and its converter reads an
/// <c>IMGF</c> file under either name at the size the control header states. Claiming the three
/// extensions here is therefore not a guess about what an MRI slice looks like; it is the same
/// reader under the names the dataset gave it.
/// </remarks>
[TestFixture]
public sealed class MaleMriExtensionTests {

  private static string[] _ExtensionsOf<T>() where T : IImageFormatMetadata<T> => T.FileExtensions;

  [Test]
  [Category("Unit")]
  public void FileExtensions_CarryTheThreePulseSequences([Values(".pd", ".t1", ".t2")] string extension)
    => Assert.That(_ExtensionsOf<GeGenesisFile>(), Does.Contain(extension));

  /// <summary>
  /// The claim is only worth making because the reader says no to something else: an MRI slice under
  /// one of these names that is not a GE Genesis image is refused rather than drawn.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AFileThatIsNotAGeGenesisImageIsRefused() {
    var foreign = new byte[4096];
    for (var i = 0; i < foreign.Length; ++i)
      foreign[i] = (byte)(i * 7);

    Assert.Throws<InvalidDataException>(() => GeGenesisReader.FromBytes(foreign));
  }
}
