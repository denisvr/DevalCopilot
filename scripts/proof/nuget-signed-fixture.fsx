// ADR-0072 harness fixture tool (test support only, never part of preparation): reads an unsigned .nupkg from standard input and writes the
// same archive with NuGet's own signed-package layout to standard output, through nuget-signed-layout.fsx. In memory only: there is no
// certificate, private key, PFX, key store, signer, timestamp, file or network, and the signature entry is a placeholder that is not a CMS
// signature. The result is NOT a validly signed package (see nuget-signed-layout.fsx).
//
// Protocol: standard input is a 4-byte little-endian length and that many package bytes; standard output is a 4-byte little-endian length
// and that many bytes of the signed-layout archive. Any failure writes nothing to standard output and exits non-zero.
// Usage: dotnet <sdk>/FSharp/fsi.dll --quiet --exec nuget-signed-fixture.fsx <sdk directory>

#load "nuget-signed-layout.fsx"

open System
open System.IO
open System.Reflection

let sdkDirectory = fsi.CommandLineArgs.[1]

for name in [ "NuGet.Common"; "NuGet.Versioning"; "NuGet.Frameworks"; "NuGet.Packaging" ] do
    Assembly.LoadFrom(Path.Combine(sdkDirectory, name + ".dll")) |> ignore

let readExactly (stream: Stream) (count: int) =
    let buffer = Array.zeroCreate<byte> count
    let mutable offset = 0
    while offset < count do
        let read = stream.Read(buffer, offset, count - offset)
        if read = 0 then failwith "truncated input"
        offset <- offset + read
    buffer

let input = Console.OpenStandardInput()
let length = BitConverter.ToInt32(readExactly input 4, 0)
let signed = SignedLayout.addPlaceholderSignature (readExactly input length)

let output = Console.OpenStandardOutput()
output.Write(BitConverter.GetBytes(signed.Length), 0, 4)
output.Write(signed, 0, signed.Length)
output.Flush()
