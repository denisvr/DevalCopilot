// ADR-0072: NuGet-compatible package content hashing for the dependency preparation proof, through the NuGet.Packaging assembly that
// the installed .NET SDK already ships. Proof tooling only, never a product feature, and no dependency, package or network access.
//
// A NuGet lockfile's contentHash is NOT the SHA-512 of the whole .nupkg: for a signed package NuGet hashes the archive with the
// signature entry and the ZIP bookkeeping it affects normalized away, so a raw whole-file digest is a false rejection. NuGet's own
// PackageArchiveReader.GetContentHash is the supported definition, so it is called here rather than re-implemented. For an unsigned
// package that definition is the whole-archive SHA-512, which the reader is handed from the same already-admitted bytes.
//
// It verifies no signer, certificate or revocation: a matching content hash is NOT signature trust, and ordinary NuGet signature,
// revocation and audit validation stays mandatory. It never opens a path other than the SDK's own assemblies, and never reads
// .nupkg.sha512 or .nupkg.metadata.
//
// Startup self-check (before READY): an in-memory unsigned archive must hash to its own SHA-512 and report unsigned, and the same archive
// given NuGet's signed layout by the SDK's own writer (nuget-signed-layout.fsx, no key material) must report signed and hash to the SAME
// value. That covers the SDK's signed-normalization relation for that sample; it does not prove every signed-archive variant or every
// possible future change to signed hashing, so the harness keeps its own signed and unsigned controls.
//
// Protocol (one frame in flight at a time, the answer always paired with its frame):
//   READY                                   the self-check passed (first line); on failure: SELFCHECK-FAILED, then exit
//   <frame>   a 4-byte little-endian length and that many archive bytes, repeated, ended by end of input
//   OK <n> <base64 content hash> <S|U>      answer to frame n (counted from 0); S: signed layout, U: unsigned
//   ERR <n> malformed                       frame n is not a readable package archive
// Usage: dotnet <sdk>/FSharp/fsi.dll --quiet --exec nuget-content-hash.fsx <sdk directory>

#load "nuget-signed-layout.fsx"

open System
open System.IO
open System.IO.Compression
open System.Reflection
open System.Security.Cryptography
open System.Threading

let sdkDirectory = fsi.CommandLineArgs.[1]

for name in [ "NuGet.Common"; "NuGet.Versioning"; "NuGet.Frameworks"; "NuGet.Packaging" ] do
    Assembly.LoadFrom(Path.Combine(sdkDirectory, name + ".dll")) |> ignore

let readerType = Type.GetType("NuGet.Packaging.PackageArchiveReader, NuGet.Packaging", true)
let readerConstructor = readerType.GetConstructor([| typeof<Stream> |])

let contentHashMethod =
    readerType.GetMethod("GetContentHash", [| typeof<CancellationToken>; typeof<Func<string>> |])

let isSignedMethod = readerType.GetMethod("IsSignedAsync", [| typeof<CancellationToken> |])

let input = Console.OpenStandardInput()
let output = Console.OpenStandardOutput()

let reply (line: string) =
    let bytes = Text.Encoding.UTF8.GetBytes(line + "\n")
    output.Write(bytes, 0, bytes.Length)
    output.Flush()

let readExactly (count: int) =
    let buffer = Array.zeroCreate<byte> count
    let mutable offset = 0
    while offset < count do
        let read = input.Read(buffer, offset, count - offset)
        if read = 0 then failwith "truncated frame"
        offset <- offset + read
    buffer

let readFrame () =
    // End of input is only legal between frames.
    let first = input.ReadByte()
    if first < 0 then
        None
    else
        let rest = readExactly 3
        let length = BitConverter.ToInt32([| byte first; rest.[0]; rest.[1]; rest.[2] |], 0)
        if length < 0 then failwith "invalid frame length"
        Some(readExactly length)

let hashFrame (bytes: byte[]) =
    use stream = new MemoryStream(bytes, false)
    use reader = readerConstructor.Invoke([| box stream |]) :?> IDisposable
    let token = CancellationToken.None
    let signedTask = isSignedMethod.Invoke(reader, [| box token |]) :?> System.Threading.Tasks.Task<bool>
    let signed = signedTask.GetAwaiter().GetResult()
    // Only consulted by NuGet for an unsigned archive, and computed from the very bytes just admitted.
    let rawSha512 = Func<string>(fun () -> Convert.ToBase64String(SHA512.HashData(bytes)))
    // NuGet's content hash is already the base64 text a lockfile records.
    let hash = contentHashMethod.Invoke(reader, [| box token; box rawSha512 |]) :?> string
    hash, signed

let selfCheckArchive () =
    use memory = new MemoryStream()
    (
        use zip = new ZipArchive(memory, ZipArchiveMode.Create, true)
        let entry = zip.CreateEntry("self-check.txt", CompressionLevel.NoCompression)
        use stream = entry.Open()
        let payload = Text.Encoding.ASCII.GetBytes "validator self-check payload"
        stream.Write(payload, 0, payload.Length)
    )
    memory.ToArray()

let selfCheckPasses () =
    try
        let unsigned = selfCheckArchive ()
        let expected = Convert.ToBase64String(SHA512.HashData(unsigned))
        let unsignedHash, unsignedFlag = hashFrame unsigned
        let signedHash, signedFlag = hashFrame (SignedLayout.addPlaceholderSignature unsigned)
        unsignedHash = expected && not unsignedFlag && signedHash = expected && signedFlag
    with _ ->
        false

if not (selfCheckPasses ()) then
    reply "SELFCHECK-FAILED"
    exit 4

reply "READY"

let mutable running = true
let mutable index = 0

while running do
    match readFrame () with
    | None -> running <- false
    | Some bytes ->
        try
            let hash, signed = hashFrame bytes
            reply (sprintf "OK %d %s %s" index hash (if signed then "S" else "U"))
        with _ ->
            reply (sprintf "ERR %d malformed" index)

        index <- index + 1
