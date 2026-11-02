// ADR-0072 proof tooling: gives an unsigned .nupkg NuGet's own signed-package archive layout, entirely in memory, through the installed SDK's
// NuGet.Packaging SignedPackageArchive.AddSignatureAsync (the public call NuGet's signer ends with). Loaded by nuget-content-hash.fsx (its
// startup self-check) and by nuget-signed-fixture.fsx (the harness fixture tool).
//
// There is NO certificate, private key, PFX, key store, signer, timestamp, file or network involved: this code touches no X509 type. The
// signature entry's content is a fixed placeholder that is not a CMS signature, so the result shows how NuGet's content hash treats the
// signed layout. It is NOT a validly signed package; a restore's signature, trust, revocation and audit checks would (rightly) reject it.
// The caller must already have loaded the SDK's NuGet.Common, NuGet.Versioning, NuGet.Frameworks and NuGet.Packaging assemblies.

module SignedLayout

open System
open System.IO
open System.Threading
open System.Threading.Tasks

let private placeholderSignature =
    Text.Encoding.ASCII.GetBytes(String.replicate 64 "SYNTHETIC-PLACEHOLDER-NOT-A-CMS-SIGNATURE;")

/// The unsigned archive with NuGet's signed layout and a placeholder signature entry, built and returned in memory.
let addPlaceholderSignature (unsigned: byte[]) : byte[] =
    let archiveType = Type.GetType("NuGet.Packaging.Signing.SignedPackageArchive, NuGet.Packaging", true)
    let addSignature = archiveType.GetMethod("AddSignatureAsync", [| typeof<Stream>; typeof<CancellationToken> |])
    use readStream = new MemoryStream(unsigned, false)
    use writeStream = new MemoryStream()
    use archive =
        archiveType.GetConstructor([| typeof<Stream>; typeof<Stream> |]).Invoke([| box readStream; box writeStream |]) :?> IDisposable
    use signatureStream = new MemoryStream(placeholderSignature, false)
    (addSignature.Invoke(archive, [| box signatureStream; box CancellationToken.None |]) :?> Task).GetAwaiter().GetResult()
    writeStream.ToArray()
