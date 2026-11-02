// Controlled fixtures for the dependency preparation harness: structurally valid .nupkg archives built in memory, and the same archive given
// NuGet's own signed-package layout by the installed SDK's NuGet.Packaging, entirely in memory (nuget-signed-fixture.fsx).
//
// Confinement, stated as far as it is demonstrated: no certificate, private key, PFX, key store, signer, timestamp or network is involved
// anywhere (the F# fixture code touches no X509 type, which a reader can verify in its few lines), the child gets a cleared environment of
// one disposable directory, and nothing is read from a real package, cache, registry or user profile. The signature entry is a placeholder,
// so a signed-layout fixture is NOT a validly signed package. Operating-system effects of the SDK child itself (for example what the .NET
// runtime does at startup) are not audited; they are limited by the cleared environment, not proved absent.
import { mkdir, mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { crc32, deflateRawSync } from 'node:zlib';
import { runBoundedChild } from './bounded-child.mjs';
import { SDK_CHILD_HOME_DIRECTORIES, sdkChildEnvironment } from './nuget-content-hasher.mjs';

const utf8 = (text) => Buffer.from(text, 'utf8');

/**
 * A ZIP archive with entries in the given order. An entry is { name, data, method }: method 'deflate' (default) or 'stored'. Every entry
 * gets a real local header, central directory record and CRC-32, and the archive ends in a real end-of-central-directory record.
 */
export function buildZip(entries) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const entry of entries) {
    const name = utf8(entry.name);
    const data = Buffer.from(entry.data);
    const stored = entry.method === 'stored';
    const body = stored ? data : deflateRawSync(data);
    const checksum = crc32(data) >>> 0;
    const method = stored ? 0 : 8;

    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0x0800, 6);
    local.writeUInt16LE(method, 8);
    local.writeUInt32LE(checksum, 14);
    local.writeUInt32LE(body.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(name.length, 26);
    locals.push(local, name, body);

    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0x0800, 8);
    central.writeUInt16LE(method, 10);
    central.writeUInt32LE(checksum, 16);
    central.writeUInt32LE(body.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(name.length, 28);
    central.writeUInt32LE(offset, 42);
    centrals.push(central, name);

    offset += local.length + name.length + body.length;
  }

  const directory = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}

/** A structurally valid unsigned NuGet package: content types, relationships, a nuspec and one payload file. */
export function buildNupkg({
  id = 'Fixture.Package',
  version = '1.0.0',
  payload = Buffer.from('payload bytes '.repeat(64)),
  payloadMethod = 'deflate',
} = {}) {
  return buildZip([
    {
      name: '[Content_Types].xml',
      data: utf8('<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        + '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />'
        + '<Default Extension="nuspec" ContentType="application/octet" /><Default Extension="dll" ContentType="application/octet" /></Types>'),
    },
    {
      name: '_rels/.rels',
      data: utf8('<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        + `<Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/${id}.nuspec" Id="R1" /></Relationships>`),
    },
    {
      name: `${id}.nuspec`,
      data: utf8(`<?xml version="1.0" encoding="utf-8"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>`
        + `<id>${id}</id><version>${version}</version><authors>Fixture</authors><description>Controlled fixture</description></metadata></package>`),
    },
    { name: `lib/net10.0/${id}.dll`, data: payload, method: payloadMethod },
  ]);
}


const SIGNED_FIXTURE_SCRIPT = fileURLToPath(new URL('./nuget-signed-fixture.fsx', import.meta.url));
const SIGNED_FIXTURE_TIMEOUT_MS = 120_000;
const MAX_SIGNED_FIXTURE_BYTES = 8 * 1024 * 1024;

/**
 * The package with NuGet's signed-package layout and a placeholder signature entry, built by the installed SDK in memory. Returns
 * { bytes, reason, scratchPreserved }: bytes is null with a closed reason when the SDK could not do it, so the caller reports the evidence
 * it could not produce. The disposable directory is removed only when the child's exit was proved; otherwise it is preserved and
 * scratchPreserved says so.
 */
export async function addNuGetSignedLayout(sdkDirectory, packageBytes, { timeoutMs = SIGNED_FIXTURE_TIMEOUT_MS, script = SIGNED_FIXTURE_SCRIPT } = {}) {
  const home = await mkdtemp(path.join(os.tmpdir(), 'devalcopilot-signed-layout-'));
  let closureProved = true;
  try {
    for (const sub of SDK_CHILD_HOME_DIRECTORIES) {
      await mkdir(path.join(home, sub), { recursive: true });
    }

    const header = Buffer.alloc(4);
    header.writeInt32LE(packageBytes.length);
    const run = await runBoundedChild(path.resolve(sdkDirectory, '..', '..', process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'),
      [path.join(sdkDirectory, 'FSharp', 'fsi.dll'), '--quiet', '--exec', script, sdkDirectory], {
        env: sdkChildEnvironment(sdkDirectory, home),
        cwd: home,
        input: Buffer.concat([header, packageBytes]),
        timeoutMs,
        maxStdoutBytes: MAX_SIGNED_FIXTURE_BYTES,
      });
    closureProved = run.closureProved;
    if (run.status !== 'Exited' || run.code !== 0 || run.stdout.length < 4 || run.stdout.readInt32LE(0) !== run.stdout.length - 4) {
      return { bytes: null, reason: `the signed-layout tool did not complete (${run.status})`, scratchPreserved: !closureProved };
    }

    return { bytes: run.stdout.subarray(4), reason: null, scratchPreserved: false };
  } finally {
    if (closureProved) {
      await rm(home, { recursive: true, force: true, maxRetries: 2, retryDelay: 100 }).catch(() => {});
    }
  }
}
