// ADR-0072 harness check for the dependency preparation tool. Fabricated caches, lockfiles and disposable Git repositories only (no real
// package, cache, registry, user profile or network), but the NuGet archives are structurally valid packages and the content hash is
// computed by the installed SDK's own NuGet.Packaging. The signed-layout fixture is the same package given NuGet's signed layout in memory by
// the SDK's own SignedPackageArchive; no certificate, private key, PFX, key store or signer is involved anywhere, and it is NOT a validly
// signed package (its signature entry is a placeholder). Random bytes with a self-consistent digest prove nothing about NuGet compatibility
// and are used here only as the malformed case. Run with: node --test scripts/proof/prepare-isolated-dependencies.test.mjs
// The real-validator cases need an installed .NET SDK with NuGet.Packaging and FSharp/fsi.dll (the highest installed one is found from
// PATH, or set DEVALCOPILOT_DOTNET_SDK to an SDK directory). Without one they are SKIPPED, visibly, never silently passed.
import assert from 'node:assert/strict';
import { execFile, execFileSync, spawn } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { existsSync, readdirSync, realpathSync } from 'node:fs';
import { mkdir, mkdtemp, open, readFile, readdir, rm, stat, symlink, writeFile } from 'node:fs/promises';
import { EventEmitter } from 'node:events';
import { createRequire } from 'node:module';
import os from 'node:os';
import path from 'node:path';
import { PassThrough } from 'node:stream';
import { after, before, test as nodeTest } from 'node:test';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import { ContentHasherUnavailable, sdkChildEnvironment, startNuGetContentHasher } from './nuget-content-hasher.mjs';
import { PreparationError, SUPPORTED_NPM_TARGET, prepare, selectNpmPackages, selectNuGetPackages } from './prepare-isolated-dependencies.mjs';
import { addNuGetSignedLayout, buildNupkg, buildZip } from './prepare-isolated-dependencies.fixtures.mjs';

const cacache = createRequire(
  path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'node_modules', 'cacache', 'package.json'))('./');
const toolPath = fileURLToPath(new URL('./prepare-isolated-dependencies.mjs', import.meta.url));

let root;
let validator = null;
let signedFixture = { bytes: null, reason: 'the SDK was not found' };
let symlinksAvailable = false;

/**
 * node:test with a lazy skip: `skip` may be a function evaluated when the test runs (after the shared setup), returning a reason string
 * to skip visibly or a falsy value to run. A skipped case is reported as skipped with its reason, never as passed.
 */
function test(name, optionsOrBody, maybeBody) {
  const [options, body] = typeof optionsOrBody === 'function' ? [{}, optionsOrBody] : [optionsOrBody, maybeBody];
  const { skip, ...rest } = options;
  return nodeTest(name, rest, async (t) => {
    const reason = typeof skip === 'function' ? skip() : skip;
    if (reason) {
      t.skip(typeof reason === 'string' ? reason : undefined);
      return;
    }

    await body(t);
  });
}

const unsignedFixture = buildNupkg({ payload: Buffer.from('FIXTURE-PAYLOAD-MARKER '.repeat(40)), payloadMethod: 'stored' });
const rawSha512 = (bytes) => createHash('sha512').update(bytes).digest('base64');

function findSdkDirectory() {
  const usable = (directory) => existsSync(path.join(directory, 'FSharp', 'fsi.dll')) && existsSync(path.join(directory, 'NuGet.Packaging.dll'));
  if (process.env.DEVALCOPILOT_DOTNET_SDK) {
    return usable(process.env.DEVALCOPILOT_DOTNET_SDK) ? process.env.DEVALCOPILOT_DOTNET_SDK : null;
  }

  const executable = process.platform === 'win32' ? 'dotnet.exe' : 'dotnet';
  for (const entry of (process.env.PATH ?? '').split(path.delimiter).filter(Boolean)) {
    const candidate = path.join(entry, executable);
    if (!existsSync(candidate)) {
      continue;
    }

    const sdkRoot = path.join(path.dirname(realpathSync(candidate)), 'sdk');
    if (!existsSync(sdkRoot)) {
      continue;
    }

    const numeric = (name) => name.split(/[.-]/).map((part) => Number.parseInt(part, 10) || 0);
    const versions = readdirSync(sdkRoot, { withFileTypes: true })
      .filter((item) => item.isDirectory() && /^\d+\.\d+\.\d+$/.test(item.name) && usable(path.join(sdkRoot, item.name)))
      .map((item) => item.name)
      .sort((a, b) => {
        const left = numeric(a);
        const right = numeric(b);
        return left[0] - right[0] || left[1] - right[1] || left[2] - right[2];
      });
    if (versions.length > 0) {
      return path.join(sdkRoot, versions.at(-1));
    }
  }

  return null;
}

const sdk = findSdkDirectory();

before(async () => {
  root = await mkdtemp(path.join(os.tmpdir(), 'devalcopilot-dependency-prep-'));
  symlinksAvailable = process.platform !== 'win32' || await canSymlink();
  if (sdk !== null) {
    validator = await startNuGetContentHasher({ sdkDirectory: sdk });
    signedFixture = await addNuGetSignedLayout(sdk, unsignedFixture);
  }
});

after(async () => {
  await validator?.close();
  await rm(root, { recursive: true, force: true });
});

const requiresSdk = () => (sdk === null ? 'no installed .NET SDK with NuGet.Packaging and fsi was found' : false);
const requiresSigned = () => requiresSdk() || (signedFixture.bytes === null ? `the SDK could not build the signed-layout fixture: ${signedFixture.reason}` : false);

const git = (repository, ...args) => execFileSync('git', [
  '-C', repository, '-c', 'core.autocrlf=false', '-c', 'user.name=Fixture Author', '-c', 'user.email=fixture@example.invalid', ...args,
], { stdio: 'pipe', env: { ...process.env, GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: process.platform === 'win32' ? 'NUL' : os.devNull } });

function contentFile(cacheIndex, integrity) {
  const hex = Buffer.from(integrity.slice('sha512-'.length), 'base64').toString('hex');
  return path.join(cacheIndex, 'content-v2', 'sha512', hex.slice(0, 2), hex.slice(2, 4), hex.slice(4));
}

const sri = (bytes) => 'sha512-' + createHash('sha512').update(bytes).digest('base64');

/**
 * A disposable repository with committed lockfiles plus fabricated caches.
 *   nuget  { archive, contentHash } of the single locked NuGet package (default: the unsigned fixture, whose content hash is its
 *          whole-archive SHA-512 by NuGet's definition); archive null leaves it absent from the cache;
 *   npm    extra lock entries { name, version, platform: { os, cpu, optional, ... }, cached: bool|'tampered', bytes }.
 */
async function scene(name, { nuget = {}, link = false, npm = [], includeBaseNpm = true } = {}) {
  const base = path.join(root, name);
  const repository = path.join(base, 'repository');
  const nugetCache = path.join(base, 'nuget-cache');
  const npmCache = path.join(base, 'npm-cache');
  await mkdir(path.join(repository, 'src', 'App'), { recursive: true });
  await mkdir(path.join(repository, 'web'), { recursive: true });

  const archive = 'archive' in nuget ? nuget.archive : unsignedFixture;
  const contentHash = nuget.contentHash ?? rawSha512(unsignedFixture);
  const lock = {
    version: 1,
    dependencies: {
      'net10.0': {
        'Devalente.Shared.Results': { type: 'Direct', requested: '[0.2.0, )', resolved: '0.2.0', contentHash },
        'Example.Project': { type: 'Project' },
      },
      'net10.0/win-x64': {
        'Devalente.Shared.Results': { type: 'Direct', requested: '[0.2.0, )', resolved: '0.2.0', contentHash },
      },
    },
  };
  await writeFile(path.join(repository, 'src', 'App', 'packages.lock.json'), JSON.stringify(lock));

  const packageDirectory = path.join(nugetCache, 'devalente.shared.results', '0.2.0');
  await mkdir(packageDirectory, { recursive: true });
  const archivePath = path.join(packageDirectory, 'devalente.shared.results.0.2.0.nupkg');
  if (link) {
    const elsewhere = path.join(base, 'elsewhere.nupkg');
    await writeFile(elsewhere, archive);
    await symlink(elsewhere, archivePath, 'file');
  } else if (archive !== null) {
    await writeFile(archivePath, archive);
  }

  // Files the tool must never copy or trust: a stale companion hash and recorded sources.
  await writeFile(path.join(packageDirectory, '.nupkg.metadata'), '{"source":"https://private.example/feed"}');
  await writeFile(path.join(packageDirectory, 'devalente.shared.results.0.2.0.nupkg.sha512'), 'AAAA');
  await writeFile(path.join(nugetCache, 'NuGet.Config'), '<configuration><packageSourceCredentials /></configuration>');

  const tarball = randomBytes(1024);
  const integrity = sri(tarball);
  const resolved = 'https://registry.npmjs.org/example/-/example-1.0.0.tgz';
  const packages = { '': { name: 'web' } };
  if (includeBaseNpm) {
    packages['node_modules/example'] = { version: '1.0.0', resolved, integrity };
    await cacache.put(path.join(npmCache, '_cacache'), `make-fetch-happen:request-cache:${resolved}`, tarball, { integrity });
  }

  const extras = [];
  for (const entry of npm) {
    const bytes = entry.bytes ?? randomBytes(256);
    const entryIntegrity = sri(bytes);
    const entryResolved = `https://registry.npmjs.org/${entry.name}/-/${path.basename(entry.name)}-${entry.version}.tgz`;
    packages[`node_modules/${entry.name}`] = { version: entry.version, resolved: entryResolved, integrity: entryIntegrity, ...entry.platform };
    if (entry.cached !== false) {
      await cacache.put(path.join(npmCache, '_cacache'), `make-fetch-happen:request-cache:${entryResolved}`, bytes, { integrity: entryIntegrity });
      if (entry.cached === 'tampered') {
        await writeFile(contentFile(path.join(npmCache, '_cacache'), entryIntegrity), Buffer.concat([bytes, Buffer.from([7])]));
      }
    }

    extras.push({ ...entry, bytes, integrity: entryIntegrity });
  }

  await writeFile(path.join(repository, 'web', 'package-lock.json'), JSON.stringify({ lockfileVersion: 3, packages }));

  git(repository, 'init', '-q', '--initial-branch', 'main');
  git(repository, 'add', '-A');
  git(repository, 'commit', '-q', '-m', 'Fixture lockfiles');

  return { base, repository, nugetCache, npmCache, staging: path.join(base, 'staging'), archive, tarball, integrity, extras, archivePath };
}

async function snapshot(directory) {
  const entries = [];
  async function walk(current) {
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const full = path.join(current, entry.name);
      if (entry.isDirectory()) {
        entries.push(`${path.relative(directory, full)}/`);
        await walk(full);
      } else {
        const info = await stat(full).catch(() => null);
        entries.push(`${path.relative(directory, full)}:${info?.size}:${info?.mtimeMs}`);
      }
    }
  }

  await walk(directory);
  return entries.sort();
}

const options = (s, staging = s.staging) => ({ repository: s.repository, staging, 'nuget-cache': s.nugetCache, 'npm-cache': s.npmCache });
const run = (s, extra = {}, staging = s.staging) => prepare(options(s, staging), { nugetContentHasher: validator, ...extra });

async function sources(s) {
  return { nuget: await snapshot(s.nugetCache), npm: await snapshot(s.npmCache), repository: await snapshot(s.repository) };
}

const expectRefusal = (pattern) => (error) => error instanceof PreparationError && pattern.test(error.message);

// ---------------------------------------------------------------------------------------------------------------------------------------
// Preparation with the installed SDK's NuGet content hash

test('exact locked archives and tarball content are prepared into owned staging, and nothing else is copied', { skip: requiresSdk }, async () => {
  const s = await scene('prepared');
  const before = await sources(s);

  const manifest = await run(s);

  assert.equal(manifest.outcome, 'Prepared');
  assert.deepEqual(await readdir(path.join(s.staging, 'nuget-feed')), ['devalente.shared.results.0.2.0.nupkg']);
  assert.deepEqual(await readFile(path.join(s.staging, 'nuget-feed', 'devalente.shared.results.0.2.0.nupkg')), unsignedFixture);
  assert.deepEqual(await cacache.get.byDigest(path.join(s.staging, 'npm-cache', '_cacache'), s.integrity), s.tarball);

  const config = await readFile(path.join(s.staging, 'NuGet.Config'), 'utf8');
  assert.match(config, /<clear \/>/);
  assert.equal((config.match(/<add key=/g) ?? []).length, 1);
  assert.doesNotMatch(config, /private\.example|https?:/);

  const manifestText = await readFile(path.join(s.staging, 'manifest.json'), 'utf8');
  assert.doesNotMatch(manifestText, new RegExp(path.basename(root)));
  assert.doesNotMatch(manifestText, /private\.example/);
  assert.match(manifest.commit, /^[0-9a-f]{40}$/);
  assert.equal(manifest.version, 3);
  assert.deepEqual(manifest.lockfiles.map((lock) => lock.path), ['src/App/packages.lock.json', 'web/package-lock.json']);
  assert.ok(manifest.lockfiles.every((lock) => /^[0-9A-F]{64}$/.test(lock.sha256)));
  assert.deepEqual(manifest.nuget.packages, [{ id: 'Devalente.Shared.Results', version: '0.2.0', contentHash: rawSha512(unsignedFixture) }]);

  // The content hash and the raw archive identity are separate facts, both recorded; neither is signature trust.
  assert.deepEqual(manifest.nuget.archives, [{
    id: 'Devalente.Shared.Results',
    version: '0.2.0',
    contentHash: rawSha512(unsignedFixture),
    signed: false,
    archiveSha256: createHash('sha256').update(unsignedFixture).digest('hex').toUpperCase(),
    archiveSha512: rawSha512(unsignedFixture),
  }]);
  assert.equal(manifest.nuget.contentHash.signatureTrustValidated, false);
  assert.match(manifest.nuget.contentHash.definition, /not the raw archive digest/);
  assert.match(manifest.nuget.contentHash.validator.sdkVersion, /^\d+\.\d+\.\d+$/);
  assert.doesNotMatch(JSON.stringify(manifest.nuget.contentHash), /[\\/]/);

  // The source caches and the repository are read only.
  assert.deepEqual(await sources(s), before);
});

test('an archive in the NuGet signed layout is accepted by its NuGet content hash although its raw archive digest differs from the lockfile value', { skip: requiresSigned }, async () => {
  const signed = signedFixture.bytes;
  // The distinction the old tool got wrong: signing changes the file bytes but not the locked content hash.
  assert.notEqual(rawSha512(signed), rawSha512(unsignedFixture));
  const s = await scene('signed', { nuget: { archive: signed, contentHash: rawSha512(unsignedFixture) } });

  const manifest = await run(s);

  assert.equal(manifest.outcome, 'Prepared');
  assert.deepEqual(manifest.nuget.mismatched, []);
  const [archive] = manifest.nuget.archives;
  assert.equal(archive.signed, true);
  assert.equal(archive.contentHash, rawSha512(unsignedFixture));
  assert.equal(archive.archiveSha512, rawSha512(signed));
  assert.notEqual(archive.archiveSha512, archive.contentHash);
  assert.equal(archive.archiveSha256, createHash('sha256').update(signed).digest('hex').toUpperCase());
  // Exact transfer: the staged file is the signed archive byte for byte, never a normalized or repacked copy.
  assert.deepEqual(await readFile(path.join(s.staging, 'nuget-feed', 'devalente.shared.results.0.2.0.nupkg')), signed);
});

test('a signed-layout archive whose lockfile value is its raw whole-file digest is a mismatch, never accepted by raw hashing', { skip: requiresSigned }, async () => {
  const signed = signedFixture.bytes;
  const s = await scene('signed-raw-lock', { nuget: { archive: signed, contentHash: rawSha512(signed) } });

  const manifest = await run(s);

  assert.equal(manifest.outcome, 'IntegrityMismatch');
  assert.deepEqual(manifest.nuget.mismatched, [{
    id: 'Devalente.Shared.Results', version: '0.2.0', signed: true, archiveSha256: createHash('sha256').update(signed).digest('hex').toUpperCase(),
  }]);
  assert.deepEqual(await readdir(path.join(s.staging, 'nuget-feed')), []);
});

test('changed package content is a mismatch for unsigned and for signed archives and is never copied', { skip: requiresSigned }, async () => {
  const change = (bytes) => {
    const tampered = Buffer.from(bytes);
    const at = tampered.indexOf('FIXTURE-PAYLOAD-MARKER');
    assert.ok(at > 0, 'the stored payload is locatable');
    tampered[at + 3] ^= 0x01;
    return tampered;
  };

  const unsigned = await scene('tampered-unsigned', { nuget: { archive: change(unsignedFixture) } });
  const signed = await scene('tampered-signed', { nuget: { archive: change(signedFixture.bytes), contentHash: rawSha512(unsignedFixture) } });

  for (const [s, wasSigned] of [[unsigned, false], [signed, true]]) {
    const manifest = await run(s);

    assert.equal(manifest.outcome, 'IntegrityMismatch', s.base);
    assert.deepEqual(manifest.nuget.mismatched.map(({ id, version, signed }) => ({ id, version, signed })),
      [{ id: 'Devalente.Shared.Results', version: '0.2.0', signed: wasSigned }]);
    assert.deepEqual(manifest.nuget.invalid, []);
    assert.deepEqual(await readdir(path.join(s.staging, 'nuget-feed')), []);
  }
});

test('malformed and truncated archives are invalid, never copied, and never fall back to a raw digest', { skip: requiresSigned }, async () => {
  const random = randomBytes(2048);
  const cases = {
    'random-bytes': random,
    'truncated-unsigned': unsignedFixture.subarray(0, unsignedFixture.length - 30),
    'truncated-signed': signedFixture.bytes.subarray(0, signedFixture.bytes.length - 40),
    'empty-file': Buffer.alloc(0),
  };

  for (const [name, archive] of Object.entries(cases)) {
    // The lock value is the raw digest of those very bytes: only a raw fallback could ever accept them.
    const s = await scene(`malformed-${name}`, { nuget: { archive, contentHash: rawSha512(archive) } });

    const manifest = await run(s);

    assert.equal(manifest.outcome, 'IntegrityMismatch', name);
    assert.deepEqual(manifest.nuget.invalid.map(({ id, version, reason }) => ({ id, version, reason })),
      [{ id: 'Devalente.Shared.Results', version: '0.2.0', reason: 'MalformedArchive' }], name);
    assert.deepEqual(manifest.nuget.mismatched, [], name);
    assert.deepEqual(manifest.nuget.archives, [], name);
    assert.deepEqual(await readdir(path.join(s.staging, 'nuget-feed')), [], name);
  }
});

test('content-hash equality is not signature trust: a changed signature entry or a stripped signature still matches the content hash', { skip: requiresSigned }, async () => {
  const signed = Buffer.from(signedFixture.bytes);
  const at = signed.indexOf('.signature.p7s');
  assert.ok(at > 0);
  signed[at + '.signature.p7s'.length + 64] ^= 0x01;

  const changedSignature = await scene('signature-changed', { nuget: { archive: signed, contentHash: rawSha512(unsignedFixture) } });
  const stripped = await scene('signature-stripped', { nuget: { archive: unsignedFixture, contentHash: rawSha512(unsignedFixture) } });

  const first = await run(changedSignature);
  const second = await run(stripped);

  // These are the documented limits of a content hash; ordinary NuGet signature, revocation and audit validation stays mandatory.
  assert.equal(first.outcome, 'Prepared');
  assert.equal(first.nuget.archives[0].signed, true);
  assert.equal(second.outcome, 'Prepared');
  assert.equal(second.nuget.archives[0].signed, false);
  for (const manifest of [first, second]) {
    assert.equal(manifest.nuget.contentHash.signatureTrustValidated, false);
  }
});

test('the real validator reports the content hash, the raw identity difference and the signature shape of controlled archives', { skip: requiresSigned }, async () => {
  const unsigned = await validator.hash(unsignedFixture);
  const signed = await validator.hash(signedFixture.bytes);

  assert.deepEqual(unsigned, { contentHash: rawSha512(unsignedFixture), signed: false });
  assert.deepEqual(signed, { contentHash: rawSha512(unsignedFixture), signed: true });
  assert.notEqual(signed.contentHash, rawSha512(signedFixture.bytes));
  assert.deepEqual(await validator.hash(Buffer.from('not an archive')), { malformed: true });
  assert.deepEqual(await validator.hash(unsignedFixture.subarray(0, 100)), { malformed: true });
  assert.deepEqual(await validator.hash(buildZip([{ name: 'readme.txt', data: Buffer.from('a zip that is not a package') }])).then((r) => r.malformed ?? false),
    false, 'a plain well-formed ZIP is an unsigned archive to NuGet; package shape is the restore\'s concern');
  // Frames are paired with their answers across several archives in one validator session.
  const interleaved = await Promise.all([validator.hash(signedFixture.bytes), validator.hash(unsignedFixture), validator.hash(randomBytes(64))]);
  assert.deepEqual(interleaved.map((r) => r.signed ?? 'malformed'), [true, false, 'malformed']);
});

// ---------------------------------------------------------------------------------------------------------------------------------------
// An unavailable validator refuses precisely, before anything is written

test('without a usable validator the run is refused before anything is written, never degraded to a raw digest', async () => {
  const s = await scene('no-validator');
  const missingSdk = path.join(s.base, 'no-such-sdk');

  await assert.rejects(() => prepare(options(s)), expectRefusal(/--dotnet-sdk is required to validate NuGet content hashes/));
  await assert.rejects(() => prepare({ ...options(s), 'dotnet-sdk': missingSdk }), expectRefusal(/NuGet content-hash validator is unavailable/));
  await assert.rejects(() => prepare({ ...options(s), 'dotnet-sdk': path.join(s.base, 'sdk-without-parts') }),
    expectRefusal(/NuGet content-hash validator is unavailable/));

  // Nothing was created, so a later run with a working validator is not blocked by leftovers.
  assert.equal(existsSync(s.staging), false);
});

// ---------------------------------------------------------------------------------------------------------------------------------------
// The validator's exchange is bounded, terminal on any anomaly and closes truthfully (WEP-F1)

const STUB_PREAMBLE = `open System
open System.Security.Cryptography
let output = Console.OpenStandardOutput()
let reply (line: string) =
    let bytes = Text.Encoding.UTF8.GetBytes(line + "\\n")
    output.Write(bytes, 0, bytes.Length)
    output.Flush()
let input = Console.OpenStandardInput()
let readExactly (count: int) =
    let buffer = Array.zeroCreate<byte> count
    let mutable offset = 0
    while offset < count do
        let read = input.Read(buffer, offset, count - offset)
        if read = 0 then failwith "end of input"
        offset <- offset + read
    buffer
let readFrame () = readExactly (BitConverter.ToInt32(readExactly 4, 0))
let hashOf (bytes: byte[]) = Convert.ToBase64String(SHA512.HashData bytes)
let anyHash = String.replicate 86 "A" + "=="
`;
// A valid startup, then whatever the scenario does: READY and a correct answer to the known-answer frame (frame 0).
const STUB_HANDSHAKE = 'reply "READY"\nreply (sprintf "OK 0 %s U" (hashOf (readFrame ())))\n';

async function stub(name, body, { handshake = true } = {}) {
  const scriptPath = path.join(root, `stub-${name}.fsx`);
  await writeFile(scriptPath, STUB_PREAMBLE + (handshake ? STUB_HANDSHAKE : '') + body);
  return scriptPath;
}

const startStub = (scriptPath, extra = {}) => startNuGetContentHasher({
  sdkDirectory: sdk, scriptPath, startupTimeoutMs: 60_000, exchangeTimeoutMs: 60_000, ...extra,
});
const scratchDirectories = async () => (await readdir(os.tmpdir())).filter((name) => name.startsWith('devalcopilot-nuget-validator-')).length;
const refusal = (code) => (error) => error instanceof ContentHasherUnavailable && error.code === code;
const settledWithin = (promise, milliseconds) => Promise.race([
  promise.then(() => 'resolved', () => 'rejected'),
  new Promise((resolve) => setTimeout(() => resolve('pending'), milliseconds)),
]);
const until = async (condition, milliseconds) => {
  const deadline = Date.now() + milliseconds;
  while (!condition() && Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
};

/** Every terminal failure: the affected call settles, the state is Failed, later requests settle at once with the same refusal, close proves closure. */
async function assertTerminalAndClosed(validatorUnderTest, error) {
  assert.equal(validatorUnderTest.state, 'Failed');
  const next = validatorUnderTest.hash(Buffer.from('later request'));
  assert.equal(await settledWithin(next, 500), 'rejected', 'a later request is refused at once, never left pending');
  await assert.rejects(() => next, (later) => later === error);
  const [first, second] = await Promise.all([validatorUnderTest.close(), validatorUnderTest.close()]);
  assert.deepEqual(first, { closed: true, reason: null, scratchPreserved: false });
  assert.equal(second, first, 'close is idempotent: every call returns the same result');
  assert.equal(existsSync(validatorUnderTest.scratchDirectory), false, 'proved closure removes the disposable directory');
}

test('startup failures are precise terminal refusals that leave no scratch behind: no READY, wrong self-check, garbage, hang, early exit, oversized line', { skip: requiresSdk }, async () => {
  const before = await scratchDirectories();
  const cases = [
    ['no-ready-answer', 'reply (sprintf "OK 0 %s U" anyHash)\n', 'SelfCheckFailed', {}],
    ['garbage', 'reply "hello"\n', 'SelfCheckFailed', {}],
    ['selfcheck-failed', 'reply "SELFCHECK-FAILED"\n', 'SelfCheckFailed', {}],
    ['wrong-known-answer', 'reply "READY"\nreadFrame () |> ignore\nreply (sprintf "OK 0 %s U" anyHash)\n', 'SelfCheckFailed', {}],
    ['early-exit', 'exit 3\n', 'ProcessExited', {}],
    ['hang', 'System.Threading.Thread.Sleep 60000\n', 'Timeout', { startupTimeoutMs: 2000 }],
    ['oversized-line', 'reply (String.replicate 9000 "x")\n', 'ProtocolViolation', {}],
  ];

  for (const [name, body, code, extra] of cases) {
    const scriptPath = await stub(`startup-${name}`, body, { handshake: false });

    await assert.rejects(() => startStub(scriptPath, extra), refusal(code), name);
  }

  // Missing SDK pieces and relative paths never start anything.
  await assert.rejects(() => startNuGetContentHasher({ sdkDirectory: path.join(root, 'nothing') }), refusal('SdkIncomplete'));
  await assert.rejects(() => startNuGetContentHasher({ sdkDirectory: 'relative' }), refusal('SdkIncomplete'));
  assert.equal(await scratchDirectories(), before);
});

test('a stopped reader after a valid handshake cannot outlive the whole-exchange deadline, even with a blocked 4 MiB write', { skip: requiresSdk }, async () => {
  const stopped = await startStub(await stub('stopped-reader', 'System.Threading.Thread.Sleep 60000\n'), { exchangeTimeoutMs: 2000 });

  const started = Date.now();
  const hashing = stopped.hash(Buffer.alloc(4 * 1024 * 1024));
  const error = await hashing.then(() => null, (failure) => failure);
  const elapsed = Date.now() - started;

  assert.ok(error !== null && refusal('Timeout')(error), `settled with a timeout (${error?.code})`);
  // The old tool was still pending at roughly 1.5 times its deadline; the deadline now starts before the first byte is written.
  assert.ok(elapsed >= 1800 && elapsed < 3500, `settled at ${elapsed} ms for a 2000 ms deadline`);
  await assertTerminalAndClosed(stopped, error);
});

test('a missing answer to a small frame, a malformed answer and an answer numbered for another frame are terminal', { skip: requiresSdk }, async () => {
  const scenarios = [
    ['silent', 'readFrame () |> ignore\nSystem.Threading.Thread.Sleep 60000\n', 'Timeout', { exchangeTimeoutMs: 1500 }],
    ['malformed', 'readFrame () |> ignore\nreply "garbage"\n', 'ProtocolViolation', {}],
    ['wrong-number', 'readFrame () |> ignore\nreply (sprintf "OK 7 %s U" anyHash)\n', 'ProtocolViolation', {}],
    ['stale-number', 'readFrame () |> ignore\nreply (sprintf "OK 0 %s U" anyHash)\n', 'ProtocolViolation', {}],
    ['exit', 'readFrame () |> ignore\nexit 5\n', 'ProcessExited', {}],
  ];

  for (const [name, body, code, extra] of scenarios) {
    const subject = await startStub(await stub(`exchange-${name}`, body), extra);

    const error = await subject.hash(Buffer.from('a small frame')).then(() => null, (failure) => failure);

    assert.ok(error !== null && refusal(code)(error), `${name}: ${error?.code}`);
    await assertTerminalAndClosed(subject, error);
  }
});

test('excess, oversized, unsolicited and flooding output is refused without being queued', { skip: requiresSdk }, async () => {
  // More than one line for one frame, in one write.
  const excess = await startStub(await stub('output-excess', 'readFrame () |> ignore\nreply (sprintf "OK 1 %s U\\nOK 2 %s U" anyHash anyHash)\n'));
  const excessError = await excess.hash(Buffer.from('frame')).then(() => null, (failure) => failure);
  assert.ok(excessError !== null && refusal('ProtocolViolation')(excessError));
  await assertTerminalAndClosed(excess, excessError);

  // A line that never ends, after a frame.
  const endless = await startStub(await stub('output-endless',
    'readFrame () |> ignore\noutput.Write(Array.create 6000 (byte (int \'x\')), 0, 6000)\noutput.Flush()\nSystem.Threading.Thread.Sleep 60000\n'));
  const endlessError = await endless.hash(Buffer.from('frame')).then(() => null, (failure) => failure);
  assert.ok(endlessError !== null && refusal('ProtocolViolation')(endlessError));
  await assertTerminalAndClosed(endless, endlessError);

  // Output while no answer is expected (idle after the handshake), a single line and then a flood of 200000 lines.
  for (const [name, body] of [
    ['unsolicited', 'System.Threading.Thread.Sleep 400\nreply "unsolicited"\nSystem.Threading.Thread.Sleep 60000\n'],
    ['flood', 'System.Threading.Thread.Sleep 400\nreply (String.replicate 200000 "x\\n")\nSystem.Threading.Thread.Sleep 60000\n'],
  ]) {
    const idle = await startStub(await stub(`output-${name}`, body));
    await until(() => idle.state === 'Failed', 8000);

    assert.equal(idle.state, 'Failed', `${name}: the validator became terminal on its own`);
    const error = await idle.hash(Buffer.from('frame')).then(() => null, (failure) => failure);
    assert.ok(error !== null && refusal('ProtocolViolation')(error), name);
    await assertTerminalAndClosed(idle, error);
  }
});

test('a late answer after a timeout never satisfies a later request', { skip: requiresSdk }, async () => {
  const subject = await startStub(await stub('late-answer',
    'readFrame () |> ignore\nSystem.Threading.Thread.Sleep 2500\nreply (sprintf "OK 1 %s U" anyHash)\nSystem.Threading.Thread.Sleep 2000\n'), { exchangeTimeoutMs: 800 });

  const error = await subject.hash(Buffer.from('first')).then(() => null, (failure) => failure);
  assert.ok(error !== null && refusal('Timeout')(error));
  // Wait past the moment the stale answer would have arrived; the later request still cannot be answered by it.
  await new Promise((resolve) => setTimeout(resolve, 3000));

  await assert.rejects(() => subject.hash(Buffer.from('second')), (later) => later === error);
  await assertTerminalAndClosed(subject, error);
});

test('close is idempotent, bounded and truthful for a healthy validator, and a closed validator refuses further work', { skip: requiresSdk }, async () => {
  const subject = await startNuGetContentHasher({ sdkDirectory: sdk });
  assert.equal(subject.state, 'Running');
  assert.deepEqual(await subject.hash(unsignedFixture), { contentHash: rawSha512(unsignedFixture), signed: false });

  const started = Date.now();
  const results = await Promise.all([subject.close(), subject.close(), subject.close()]);

  assert.ok(Date.now() - started < 8000);
  assert.deepEqual(results[0], { closed: true, reason: null, scratchPreserved: false });
  assert.ok(results.every((result) => result === results[0]));
  assert.equal(await subject.close(), results[0]);
  assert.equal(existsSync(subject.scratchDirectory), false);
  assert.equal(subject.state, 'Closed');
  await assert.rejects(() => subject.hash(unsignedFixture), refusal('Closed'));
});

/** A process double whose end is controlled: answers the startup like a real validator, then does whatever the test says. */
function fakeValidatorProcess({ exitsOnKill }) {
  const child = new EventEmitter();
  child.stdin = new PassThrough();
  child.stdout = new PassThrough();
  child.stderr = new PassThrough();
  child.pid = 4242;
  child.exitCode = null;
  child.kills = 0;
  child.kill = () => {
    child.kills++;
    if (exitsOnKill) {
      setImmediate(() => {
        child.exitCode = 0;
        child.emit('exit', 0);
        child.emit('close', 0);
      });
    }

    return true;
  };
  const known = createHash('sha512').update(Buffer.from('504b0506000000000000000000000000000000000000', 'hex')).digest('base64');
  let received = 0;
  child.stdout.write('READY\n');
  child.stdin.on('data', (chunk) => {
    received += chunk.length;
    if (received === 26) {
      child.stdout.write(`OK 0 ${known} U\n`);
    }
  });
  return child;
}

test('scratch is preserved and the unresolved closure reported while the validator process cannot be shown to have exited', { skip: requiresSdk }, async () => {
  const fake = fakeValidatorProcess({ exitsOnKill: false });
  const subject = await startNuGetContentHasher({ sdkDirectory: sdk, spawnProcess: () => fake, gracefulExitMs: 150, killWaitMs: 150 });

  const started = Date.now();
  const result = await subject.close();

  assert.ok(Date.now() - started < 3000, 'closure is bounded');
  assert.deepEqual(result, { closed: false, reason: 'ProcessExitNotObserved', scratchPreserved: true });
  assert.ok(fake.kills >= 1, 'a kill was requested');
  assert.equal(fake.stdin.destroyed && fake.stdout.destroyed, true, 'pending I/O was cancelled');
  assert.equal(existsSync(subject.scratchDirectory), true, 'an unproved working directory is never removed');
  assert.equal(await subject.close(), result, 'the unresolved answer is stable');
  await rm(subject.scratchDirectory, { recursive: true, force: true });

  const killed = fakeValidatorProcess({ exitsOnKill: true });
  const other = await startNuGetContentHasher({ sdkDirectory: sdk, spawnProcess: () => killed, gracefulExitMs: 150, killWaitMs: 1000 });
  assert.deepEqual(await other.close(), { closed: true, reason: null, scratchPreserved: false });
  assert.ok(killed.kills >= 1);
  assert.equal(existsSync(other.scratchDirectory), false);
});

test('scratch that cannot be removed is preserved and reported, never ignored', { skip: () => requiresSdk() || (process.platform === 'win32' ? false : 'needs Windows directory locking') }, async () => {
  const subject = await startNuGetContentHasher({ sdkDirectory: sdk });
  // A live process whose working directory is inside the scratch pins it exactly as a lingering validator child would. (An open file
  // handle would not: Node opens files with delete sharing.)
  const holder = spawn(process.execPath, ['-e', 'setTimeout(() => {}, 60000)'], {
    cwd: path.join(subject.scratchDirectory, 'tmp'), stdio: 'ignore', windowsHide: true,
  });
  await new Promise((resolve, reject) => {
    holder.once('spawn', resolve);
    holder.once('error', reject);
  });
  try {
    const result = await subject.close();

    assert.deepEqual(result, { closed: false, reason: 'ScratchNotRemoved', scratchPreserved: true });
    assert.equal(existsSync(subject.scratchDirectory), true);
    assert.equal(await subject.close(), result, 'the unresolved answer is stable');
  } finally {
    holder.kill();
    await new Promise((resolve) => (holder.exitCode === null ? holder.once('exit', resolve) : resolve()));
    await rm(subject.scratchDirectory, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  }
});

test('a validator lost during the run refuses the whole run, and an unresolved closure is reported and leaves no manifest', { skip: requiresSdk }, async () => {
  const lost = await scene('validator-lost');
  const unavailable = { hash: async () => { throw new ContentHasherUnavailable('lost', 'Timeout'); }, provenance: null };

  await assert.rejects(() => prepare(options(lost), { nugetContentHasher: unavailable }), expectRefusal(/became unavailable/));

  assert.deepEqual(await readdir(path.join(lost.staging, 'nuget-feed')), []);
  assert.equal(existsSync(path.join(lost.staging, 'manifest.json')), false);

  const unresolved = { closed: false, reason: 'ProcessExitNotObserved', scratchPreserved: true };
  const withUnresolvedClosure = (override = {}) => async (startOptions) => {
    const real = await startNuGetContentHasher(startOptions);
    return { ...real, ...override, close: async () => { await real.close(); return unresolved; } };
  };

  const clean = await scene('closure-unresolved');
  await assert.rejects(() => prepare({ ...options(clean), 'dotnet-sdk': sdk }, { startContentHasher: withUnresolvedClosure() }),
    expectRefusal(/^the NuGet content-hash validator did not close; its scratch directory was preserved$/));
  assert.equal(existsSync(path.join(clean.staging, 'manifest.json')), false, 'no manifest is written beside an unresolved validator');

  const both = await scene('closure-unresolved-after-loss');
  const losing = withUnresolvedClosure({ hash: async () => { throw new ContentHasherUnavailable('lost', 'Timeout'); } });
  await assert.rejects(() => prepare({ ...options(both), 'dotnet-sdk': sdk }, { startContentHasher: losing }),
    expectRefusal(/became unavailable; the NuGet content-hash validator also did not close; its scratch directory was preserved/));
});

test('a validator that stalls mid-run refuses the whole preparation within the bound and is closed', { skip: requiresSdk }, async () => {
  const s = await scene('validator-stalls');
  const stalled = await stub('prepare-stall', 'System.Threading.Thread.Sleep 60000\n');
  const before = await scratchDirectories();

  const started = Date.now();
  await assert.rejects(
    () => prepare({ ...options(s), 'dotnet-sdk': sdk },
      { startContentHasher: (startOptions) => startNuGetContentHasher({ ...startOptions, scriptPath: stalled, exchangeTimeoutMs: 1500 }) }),
    expectRefusal(/became unavailable/));

  assert.ok(Date.now() - started < 9000, 'refused within the exchange bound plus closure');
  assert.equal(existsSync(path.join(s.staging, 'manifest.json')), false);
  assert.equal(await scratchDirectories(), before, 'the validator was closed and its disposable directory removed');
});

test('the validator child gets only owned locations, never the user profile, a package cache or credentials', () => {
  const home = path.join(root, 'validator-home');
  const environment = sdkChildEnvironment(path.join(root, 'dotnet', 'sdk', '10.0.401'), home);

  assert.deepEqual(Object.keys(environment).sort(), [
    'APPDATA', 'DOTNET_CLI_HOME', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'DOTNET_NOLOGO',
    'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'HOME', 'LOCALAPPDATA', 'NUGET_HTTP_CACHE_PATH', 'NUGET_PACKAGES', 'NUGET_PLUGINS_CACHE_PATH',
    'PATH', 'SystemRoot', 'TEMP', 'TMP', 'USERPROFILE',
  ]);
  for (const key of ['APPDATA', 'DOTNET_CLI_HOME', 'HOME', 'LOCALAPPDATA', 'NUGET_HTTP_CACHE_PATH', 'NUGET_PACKAGES', 'NUGET_PLUGINS_CACHE_PATH', 'TEMP', 'TMP', 'USERPROFILE']) {
    assert.ok(environment[key] === home || environment[key].startsWith(home + path.sep), key);
  }

  assert.equal(environment.PATH, path.join(root, 'dotnet'));
  assert.ok(!Object.keys(environment).some((key) => /TOKEN|KEY$|PASSWORD|SECRET|PROXY|^ANTHROPIC|^NUGET_.*(SOURCE|CONFIG)/i.test(key)));
});

test('a repository without locked NuGet packages needs no SDK', async () => {
  const s = await scene('npm-only');
  await writeFile(path.join(s.repository, 'src', 'App', 'packages.lock.json'), JSON.stringify({ version: 1, dependencies: { 'net10.0': {} } }));
  git(s.repository, 'add', '-A');
  git(s.repository, 'commit', '-q', '-m', 'No NuGet packages');

  const manifest = await prepare(options(s));

  assert.equal(manifest.outcome, 'Prepared');
  assert.equal(manifest.nuget.contentHash, null);
  assert.deepEqual(manifest.nuget.archives, []);
});

// ---------------------------------------------------------------------------------------------------------------------------------------
// Boundaries kept from earlier reviews (WEP-R3 and the single-read rules)

test('a staging root equal to an empty source cache is refused before anything is written (review WEP-R3)', async () => {
  const s = await scene('equal-source');
  const emptySource = path.join(s.base, 'empty-npm-cache');
  await mkdir(emptySource);

  await assert.rejects(
    () => prepare({ ...options(s, emptySource), 'npm-cache': emptySource }, { nugetContentHasher: validator ?? undefined }),
    expectRefusal(/overlaps a source root|--dotnet-sdk is required/));

  assert.deepEqual(await readdir(emptySource), []);
});

test('a staging root inside, around or aliasing a source root is refused and nothing is written', async () => {
  const s = await scene('overlap');
  const before = await sources(s);
  const junction = path.join(s.base, 'npm-alias');
  await symlink(s.npmCache, junction, 'junction');

  const candidates = [
    path.join(s.nugetCache, 'staging'),
    s.base,
    path.join(s.repository, 'staging'),
    path.join(junction, 'staging'),
  ];
  if (process.platform === 'win32') {
    candidates.push(path.join(s.npmCache.toUpperCase(), 'staging'));
  }

  for (const staging of candidates) {
    await assert.rejects(() => prepare(options(s, staging)), expectRefusal(/overlaps a source root/), staging);
  }

  assert.deepEqual(await sources(s), before);
});

test('only committed lockfile bytes are authority: untracked, ignored and modified working copies are not read', { skip: requiresSdk }, async () => {
  const s = await scene('committed-only');
  const stranger = { version: 1, dependencies: { 'net10.0': { 'Stranger.Package': { type: 'Direct', resolved: '9.9.9', contentHash: 'AAAA' } } } };
  await mkdir(path.join(s.repository, 'untracked'), { recursive: true });
  await writeFile(path.join(s.repository, 'untracked', 'packages.lock.json'), JSON.stringify(stranger));
  await writeFile(path.join(s.repository, '.gitignore'), 'ignored/\n');
  await mkdir(path.join(s.repository, 'ignored'), { recursive: true });
  await writeFile(path.join(s.repository, 'ignored', 'package-lock.json'), '{"lockfileVersion":3,"packages":{"node_modules/x":{"resolved":"r","integrity":"sha1-x"}}}');
  await writeFile(path.join(s.repository, 'src', 'App', 'packages.lock.json'), JSON.stringify(stranger));

  const manifest = await run(s);

  assert.equal(manifest.outcome, 'Prepared');
  assert.deepEqual(manifest.lockfiles.map((lock) => lock.path), ['src/App/packages.lock.json', 'web/package-lock.json']);
  assert.deepEqual(manifest.nuget.packages.map((entry) => entry.id), ['Devalente.Shared.Results']);
});

test('a repository without a committed lockfile is refused', async () => {
  const repository = path.join(root, 'no-lockfile');
  await mkdir(repository, { recursive: true });
  await writeFile(path.join(repository, 'README.md'), '# none\n');
  git(repository, 'init', '-q', '--initial-branch', 'main');
  git(repository, 'add', '-A');
  git(repository, 'commit', '-q', '-m', 'No lockfile');

  await assert.rejects(
    () => prepare({ repository, staging: path.join(root, 'no-lockfile-staging'), 'nuget-cache': path.join(root, 'none-a'), 'npm-cache': path.join(root, 'none-b') }),
    expectRefusal(/no committed lockfile/));
});

test('archives above the bound are never read whole and are unavailable', { skip: requiresSdk }, async () => {
  const s = await scene('oversized');

  const manifest = await run(s, { maxArchiveBytes: 512 });

  assert.equal(manifest.outcome, 'DependenciesUnavailable');
  assert.deepEqual(manifest.nuget.missing, [{ id: 'Devalente.Shared.Results', version: '0.2.0' }]);
  assert.deepEqual(manifest.npm.missing, [{ name: 'example', version: '1.0.0' }]);
  assert.deepEqual(await readdir(path.join(s.staging, 'nuget-feed')), []);
  assert.deepEqual(await readdir(path.join(s.staging, 'npm-cache')), []);
});

test('a missing archive or tarball is dependencies unavailable with the exact identities and no fallback', { skip: requiresSdk }, async () => {
  const s = await scene('missing', { nuget: { archive: null }, includeBaseNpm: true });
  await rm(contentFile(path.join(s.npmCache, '_cacache'), s.integrity));

  const manifest = await run(s);

  assert.equal(manifest.outcome, 'DependenciesUnavailable');
  assert.deepEqual(manifest.nuget.missing, [{ id: 'Devalente.Shared.Results', version: '0.2.0' }]);
  assert.deepEqual(manifest.npm.missing, [{ name: 'example', version: '1.0.0' }]);
});

test('a linked archive is never followed', { skip: () => requiresSdk() || (symlinksAvailable ? false : 'symbolic links cannot be created here') }, async () => {
  const s = await scene('linked', { link: true });

  const manifest = await run(s);

  assert.equal(manifest.outcome, 'DependenciesUnavailable');
  assert.deepEqual(await readdir(path.join(s.staging, 'nuget-feed')), []);
});

test('a staging root that is not new or empty is refused', async () => {
  const s = await scene('occupied');
  await mkdir(s.staging, { recursive: true });
  await writeFile(path.join(s.staging, 'existing'), 'x');

  await assert.rejects(() => prepare(options(s), { nugetContentHasher: validator ?? undefined }), PreparationError);
  assert.deepEqual(await readdir(s.staging), ['existing']);
});

// ---------------------------------------------------------------------------------------------------------------------------------------
// Target-aware npm selection (WEP-F2)

const PLATFORM_LOCK = (packages) => ({
  lockfileVersion: 3,
  packages: {
    '': { name: 'fixture' },
    ...Object.fromEntries(Object.entries(packages).map(([name, entry]) => [
      `node_modules/${name}`,
      { version: '1.0.0', resolved: `https://registry.npmjs.org/${name}/-/x-1.0.0.tgz`, integrity: sri(Buffer.from(name)), ...entry },
    ])),
  },
});
const names = (list) => list.map((item) => item.name);

test('npm selection follows the supported Windows x64 target: observed optional native-binding shapes', () => {
  const lock = PLATFORM_LOCK({
    '@oxlint/binding-win32-x64-msvc': { optional: true, os: ['win32'], cpu: ['x64'] },
    '@oxlint/binding-win32-arm64-msvc': { optional: true, os: ['win32'], cpu: ['arm64'] },
    '@oxlint/binding-win32-ia32-msvc': { optional: true, os: ['win32'], cpu: ['ia32'] },
    '@oxlint/binding-linux-x64-gnu': { optional: true, os: ['linux'], cpu: ['x64'], libc: ['glibc'] },
    '@oxlint/binding-darwin-arm64': { optional: true, os: ['darwin'], cpu: ['arm64'] },
    '@oxlint/binding-android-arm64': { optional: true, os: ['android'], cpu: ['arm64'] },
    'plain-package': {},
    'typescript': { dev: true },
  });
  const before = JSON.stringify(lock);

  const result = selectNpmPackages([lock], SUPPORTED_NPM_TARGET);

  assert.deepEqual(SUPPORTED_NPM_TARGET, { os: 'win32', cpu: 'x64' });
  assert.deepEqual(names(result.selected), ['@oxlint/binding-win32-x64-msvc', 'plain-package', 'typescript']);
  assert.deepEqual(names(result.omitted), [
    '@oxlint/binding-android-arm64', '@oxlint/binding-darwin-arm64', '@oxlint/binding-linux-x64-gnu',
    '@oxlint/binding-win32-arm64-msvc', '@oxlint/binding-win32-ia32-msvc',
  ]);
  assert.deepEqual(result.incompatible, []);
  assert.ok(result.omitted.every((entry) => entry.reason === 'OptionalPlatformMismatch'));
  assert.deepEqual(result.omitted.find((entry) => entry.name === '@oxlint/binding-linux-x64-gnu').os, ['linux']);
  // The original lockfile is never rewritten.
  assert.equal(JSON.stringify(lock), before);
});

test('npm selection for another platform selects that platform and excludes Windows x64', () => {
  const lock = PLATFORM_LOCK({
    'binding-win32-x64': { optional: true, os: ['win32'], cpu: ['x64'] },
    'binding-darwin-arm64': { optional: true, os: ['darwin'], cpu: ['arm64'] },
  });

  const mac = selectNpmPackages([lock], { os: 'darwin', cpu: 'arm64' });
  const windows = selectNpmPackages([lock], { os: 'win32', cpu: 'x64' });

  assert.deepEqual(names(mac.selected), ['binding-darwin-arm64']);
  assert.deepEqual(names(mac.omitted), ['binding-win32-x64']);
  assert.deepEqual(names(windows.selected), ['binding-win32-x64']);
  assert.deepEqual(names(windows.omitted), ['binding-darwin-arm64']);
});

test('npm os and cpu lists follow npm: negation, the sole any entry and mixed lists', () => {
  const table = [
    [{ os: ['!win32'] }, false],
    [{ os: ['!linux'] }, true],
    [{ os: ['!linux', '!darwin'] }, true],
    [{ os: ['win32', '!linux'] }, true],
    [{ os: ['linux', '!win32'] }, false],
    [{ os: ['linux', 'darwin'] }, false],
    [{ os: ['linux', 'win32'] }, true],
    [{ os: ['any'] }, true],
    [{ os: ['any', 'linux'] }, false],
    [{ os: ['any', 'win32'] }, true],
    [{ os: 'win32' }, true],
    [{ os: 'linux' }, false],
    [{ cpu: ['!x64'] }, false],
    [{ cpu: ['x64', 'arm64'] }, true],
    [{ cpu: 'arm64' }, false],
    [{ os: ['win32'], cpu: ['arm64'] }, false],
    [{ os: ['!win32'], cpu: ['x64'] }, false],
    [{ os: ['win32'], cpu: ['!arm64'] }, true],
    [{}, true],
  ];

  for (const [platform, expected] of table) {
    const result = selectNpmPackages([PLATFORM_LOCK({ candidate: { optional: true, ...platform } })]);

    assert.equal(result.selected.length === 1, expected, JSON.stringify(platform));
    assert.equal(result.omitted.length === 1, !expected, JSON.stringify(platform));
  }
});

test('a required incompatible entry is a blocker, never a silent omission; dev-optional and peer-optional are not optional', () => {
  const lock = PLATFORM_LOCK({
    'needs-linux': { os: ['linux'] },
    'dev-optional-linux': { devOptional: true, os: ['linux'] },
    'peer-optional-darwin': { peerOptional: true, os: ['darwin'] },
    'optional-linux': { optional: true, os: ['linux'] },
    'denies-windows': { dev: true, os: ['!win32'] },
  });

  const result = selectNpmPackages([lock]);

  assert.deepEqual(names(result.incompatible), ['denies-windows', 'dev-optional-linux', 'needs-linux', 'peer-optional-darwin']);
  assert.ok(result.incompatible.every((entry) => entry.reason === 'RequiredPlatformMismatch'));
  assert.deepEqual(names(result.omitted), ['optional-linux']);
  assert.deepEqual(result.selected, []);
});

// Admission is a per-entry fact; only byte transfer is deduplicated by tarball (WEP-F2).
const SHARED_BYTES = Buffer.from('one tarball named by several lock entries');
const SHARED_SRI = sri(SHARED_BYTES);
const lockEntry = (name, platform) => [
  `node_modules/${name}`,
  { version: '2.0.0', resolved: `https://registry.npmjs.org/${name}/-/${name}-2.0.0.tgz`, integrity: SHARED_SRI, ...platform },
];
const sharedLock = (...entries) => ({ lockfileVersion: 3, packages: Object.fromEntries(entries) });
const summary = (result) => ({
  selected: names(result.selected),
  omitted: result.omitted.map((entry) => `${entry.name}:${entry.tarballNeededElsewhere}`),
  incompatible: result.incompatible.map((entry) => `${entry.name}:${entry.tarballNeededElsewhere}`),
});

test('a required incompatible entry stays a blocker when another entry sharing its tarball is applicable, in both entry orders', () => {
  const linux = lockEntry('linux-only', { os: ['linux'] });
  const windows = lockEntry('windows-only', { os: ['win32'] });

  const first = selectNpmPackages([sharedLock(linux, windows)]);
  const second = selectNpmPackages([sharedLock(windows, linux)]);

  for (const result of [first, second]) {
    assert.deepEqual(summary(result), { selected: ['windows-only'], omitted: [], incompatible: ['linux-only:true'] });
    assert.equal(result.incompatible[0].reason, 'RequiredPlatformMismatch');
    assert.deepEqual(result.incompatible[0].os, ['linux']);
  }

  assert.deepEqual(first, second, 'the result does not depend on entry order');
});

test('admission facts survive tarball sharing across lock documents, in both document orders', () => {
  const linux = sharedLock(lockEntry('linux-only', { os: ['linux'] }));
  const windows = sharedLock(lockEntry('windows-only', { os: ['win32'] }));

  const linuxFirst = selectNpmPackages([linux, windows]);
  const windowsFirst = selectNpmPackages([windows, linux]);

  assert.deepEqual(summary(linuxFirst), { selected: ['windows-only'], omitted: [], incompatible: ['linux-only:true'] });
  assert.deepEqual(summary(windowsFirst), summary(linuxFirst));
  // Each record names the document it came from, so the blocker is attributable.
  assert.equal(linuxFirst.incompatible[0].document, 0);
  assert.equal(windowsFirst.incompatible[0].document, 1);

  // The same required exclusion in two documents is two facts; the same applicable entry in two documents is one identity to stage.
  const repeated = selectNpmPackages([linux, linux, windows, windows]);
  assert.deepEqual(repeated.incompatible.map((entry) => entry.document), [0, 1]);
  assert.deepEqual(names(repeated.selected), ['windows-only']);
});

test('optional and required variants of one tarball each keep their own facts, whichever order they come in', () => {
  const optionalLinux = lockEntry('optional-linux', { optional: true, os: ['linux'] });
  const optionalDarwin = lockEntry('optional-darwin', { optional: true, os: ['darwin'] });
  const requiredLinux = lockEntry('required-linux', { os: ['linux'] });
  const requiredWindows = lockEntry('required-windows', { os: ['win32'] });
  const needsNothing = lockEntry('plain', {});
  const scenarios = [
    [[optionalLinux, requiredWindows], { selected: ['required-windows'], omitted: ['optional-linux:true'], incompatible: [] }],
    [[optionalLinux, optionalDarwin], { selected: [], omitted: ['optional-darwin:false', 'optional-linux:false'], incompatible: [] }],
    [[optionalLinux, requiredLinux], { selected: [], omitted: ['optional-linux:false'], incompatible: ['required-linux:false'] }],
    [[requiredLinux, requiredWindows], { selected: ['required-windows'], omitted: [], incompatible: ['required-linux:true'] }],
    [[optionalLinux, requiredLinux, needsNothing], { selected: ['plain'], omitted: ['optional-linux:true'], incompatible: ['required-linux:true'] }],
    [[requiredWindows, needsNothing], { selected: ['plain', 'required-windows'], omitted: [], incompatible: [] }],
  ];

  for (const [entries, expected] of scenarios) {
    const label = entries.map(([location]) => location).join(' + ');

    assert.deepEqual(summary(selectNpmPackages([sharedLock(...entries)])), expected, label);
    assert.deepEqual(summary(selectNpmPackages([sharedLock(...[...entries].reverse())])), expected, `${label} (reversed)`);
  }
});

test('a required incompatible entry that shares its tarball with a needed one can never produce Prepared, in both orders', { skip: requiresSdk }, async () => {
  const linux = { name: 'linux-only', version: '1.0.0', platform: { os: ['linux'] }, bytes: SHARED_BYTES };
  const windows = { name: 'windows-only', version: '1.0.0', platform: { os: ['win32'] }, bytes: SHARED_BYTES };

  for (const [order, npm] of [['linux-first', [linux, windows]], ['windows-first', [windows, linux]]]) {
    const s = await scene(`shared-required-${order}`, { npm });

    const manifest = await run(s);

    assert.equal(manifest.outcome, 'TargetIncompatible', order);
    assert.notEqual(manifest.outcome, 'Prepared', order);
    assert.deepEqual(manifest.npm.incompatible.map(({ name, version, reason, lockfile, location, tarballNeededElsewhere }) => (
      { name, version, reason, lockfile, location, tarballNeededElsewhere })),
    [{ name: 'linux-only', version: '1.0.0', reason: 'RequiredPlatformMismatch', lockfile: 'web/package-lock.json',
      location: 'node_modules/linux-only', tarballNeededElsewhere: true }], order);
    assert.ok(manifest.npm.packages.some((entry) => entry.name === 'windows-only'), order);
    // The needed identity's bytes were still transferred and verified.
    assert.deepEqual(await cacache.get.byDigest(path.join(s.staging, 'npm-cache', '_cacache'), SHARED_SRI), SHARED_BYTES, order);
  }
});

test('missing or tampered content named by several applicable identities blocks for each of them', { skip: requiresSdk }, async () => {
  const entries = (cached) => [
    { name: 'a-shared', version: '1.0.0', platform: {}, bytes: SHARED_BYTES, cached },
    { name: 'b-shared', version: '2.0.0', platform: {}, bytes: SHARED_BYTES, cached },
  ];
  const missing = await scene('shared-missing', { npm: entries(false) });
  const tampered = await scene('shared-tampered', { npm: entries('tampered') });

  const first = await run(missing);
  const second = await run(tampered);

  assert.equal(first.outcome, 'DependenciesUnavailable');
  assert.deepEqual(first.npm.missing, [{ name: 'a-shared', version: '1.0.0' }, { name: 'b-shared', version: '2.0.0' }]);
  assert.equal(second.outcome, 'IntegrityMismatch');
  assert.deepEqual(second.npm.mismatched, [{ name: 'a-shared', version: '1.0.0' }, { name: 'b-shared', version: '2.0.0' }]);
  await assert.rejects(() => cacache.get.byDigest(path.join(tampered.staging, 'npm-cache', '_cacache'), SHARED_SRI), 'the tampered tarball never reaches the owned cache');
});

test('malformed or unsupported platform metadata is a precise refusal, not an omission', () => {
  const refuse = (entry, pattern) => assert.throws(() => selectNpmPackages([PLATFORM_LOCK({ candidate: entry })]), expectRefusal(pattern), JSON.stringify(entry));

  refuse({ optional: true, os: 7 }, /malformed package-lock platform constraint/);
  refuse({ optional: true, os: [] }, /malformed package-lock platform constraint/);
  refuse({ optional: true, os: [''] }, /malformed package-lock platform constraint/);
  refuse({ optional: true, os: ['!'] }, /malformed package-lock platform constraint/);
  refuse({ optional: true, cpu: [1] }, /malformed package-lock platform constraint/);
  refuse({ optional: true, os: { win32: true } }, /malformed package-lock platform constraint/);
  refuse({ optional: 'yes', os: ['linux'] }, /malformed package-lock entry/);
  // libc is not evidence for the Windows target in either direction, so it never lets an otherwise admitted entry through.
  refuse({ optional: true, os: ['win32'], cpu: ['x64'], libc: ['glibc'] }, /unsupported package-lock platform constraint/);
  refuse({ optional: true, libc: ['musl'] }, /unsupported package-lock platform constraint/);
  assert.throws(() => selectNpmPackages([{ lockfileVersion: 1, packages: {} }]), PreparationError);
  assert.throws(() => selectNpmPackages([{ lockfileVersion: 3, packages: { 'node_modules/x': { resolved: 'r', integrity: 'sha1-abc' } } }]), PreparationError);
  // An entry that os already excludes is excluded whatever its libc says, exactly as npm excludes it.
  assert.equal(selectNpmPackages([PLATFORM_LOCK({ c: { optional: true, os: ['linux'], libc: ['glibc'] } })]).omitted.length, 1);
  assert.deepEqual(selectNpmPackages([{ lockfileVersion: 3, packages: { 'node_modules/w': { link: true, resolved: 'w' } } }]),
    { selected: [], omitted: [], incompatible: [] });
});

// ---------------------------------------------------------------------------------------------------------------------------------------
// npm through the whole preparation

const WINDOWS = { optional: true, os: ['win32'], cpu: ['x64'] };
const LINUX = { optional: true, os: ['linux'], cpu: ['x64'], libc: ['glibc'] };

test('optional entries for other platforms are omitted with the target and reason, even when their content is cached, and nothing else changes', { skip: requiresSdk }, async () => {
  const s = await scene('npm-target', {
    npm: [
      { name: '@native/win32-x64', version: '1.2.3', platform: WINDOWS },
      { name: '@native/linux-x64', version: '1.2.3', platform: LINUX, cached: false },
      { name: '@native/linux-cached', version: '1.2.3', platform: { optional: true, os: ['linux'], cpu: ['arm64'] } },
    ],
  });
  const before = await sources(s);

  const manifest = await run(s);

  assert.equal(manifest.outcome, 'Prepared');
  assert.deepEqual(manifest.npm.target, { os: 'win32', cpu: 'x64', source: 'fixed supported Windows x64 proof profile (ADR-0072)' });
  assert.deepEqual(manifest.npm.packages.map((entry) => entry.name), ['@native/win32-x64', 'example']);
  assert.deepEqual(manifest.npm.omitted.map(({ name, version, reason, os: osConstraint, cpu }) => ({ name, version, reason, os: osConstraint, cpu })), [
    { name: '@native/linux-cached', version: '1.2.3', reason: 'OptionalPlatformMismatch', os: ['linux'], cpu: ['arm64'] },
    { name: '@native/linux-x64', version: '1.2.3', reason: 'OptionalPlatformMismatch', os: ['linux'], cpu: ['x64'] },
  ]);
  assert.deepEqual(manifest.npm.incompatible, []);
  assert.deepEqual(manifest.npm.missing, []);

  const owned = path.join(s.staging, 'npm-cache', '_cacache');
  const [applicable] = s.extras;
  assert.deepEqual(await cacache.get.byDigest(owned, applicable.integrity), applicable.bytes);
  for (const omitted of s.extras.slice(1)) {
    await assert.rejects(() => cacache.get.byDigest(owned, omitted.integrity), 'an omitted entry is never copied');
  }

  assert.deepEqual(await sources(s), before);
});

test('applicable npm content that is missing or tampered still blocks, whatever else is omitted', { skip: requiresSdk }, async () => {
  const missing = await scene('npm-applicable-missing', {
    npm: [{ name: '@native/win32-x64', version: '1.2.3', platform: WINDOWS, cached: false }, { name: '@native/linux', version: '1.0.0', platform: LINUX, cached: false }],
  });
  const tampered = await scene('npm-applicable-tampered', {
    npm: [{ name: '@native/win32-x64', version: '1.2.3', platform: WINDOWS, cached: 'tampered' }, { name: '@native/linux', version: '1.0.0', platform: LINUX, cached: false }],
  });

  const first = await run(missing);
  const second = await run(tampered);

  assert.equal(first.outcome, 'DependenciesUnavailable');
  assert.deepEqual(first.npm.missing, [{ name: '@native/win32-x64', version: '1.2.3' }]);
  assert.deepEqual(first.npm.omitted.map((entry) => entry.name), ['@native/linux']);
  assert.equal(second.outcome, 'IntegrityMismatch');
  assert.deepEqual(second.npm.mismatched, [{ name: '@native/win32-x64', version: '1.2.3' }]);
});

test('a required incompatible npm entry is TargetIncompatible with its identity and constraints, never silently omitted', { skip: requiresSdk }, async () => {
  const s = await scene('npm-required-incompatible', {
    npm: [
      { name: 'needs-linux', version: '4.5.6', platform: { os: ['linux'] } },
      { name: '@native/linux-x64', version: '1.0.0', platform: LINUX, cached: false },
    ],
  });

  const manifest = await run(s);

  assert.equal(manifest.outcome, 'TargetIncompatible');
  assert.deepEqual(manifest.npm.incompatible.map(({ name, version, reason, os: osConstraint }) => ({ name, version, reason, os: osConstraint })),
    [{ name: 'needs-linux', version: '4.5.6', reason: 'RequiredPlatformMismatch', os: ['linux'] }]);
  assert.deepEqual(manifest.npm.omitted.map((entry) => entry.name), ['@native/linux-x64']);
  assert.ok(!manifest.npm.packages.some((entry) => entry.name === 'needs-linux'));
  // The lockfile on disk and in Git is unchanged.
  assert.match(git(s.repository, 'status', '--porcelain').toString(), /^$/);
});

test('an integrity failure outranks an incompatible entry, which outranks absent content', { skip: requiresSdk }, async () => {
  const incompatible = { name: 'needs-linux', version: '1.0.0', platform: { os: ['linux'] } };
  const both = await scene('npm-both', { npm: [incompatible, { name: 'win', version: '1.0.0', platform: WINDOWS, cached: 'tampered' }] });
  const withMissing = await scene('npm-incompatible-and-missing', { npm: [incompatible, { name: 'win', version: '1.0.0', platform: WINDOWS, cached: false }] });

  assert.equal((await run(both)).outcome, 'IntegrityMismatch');
  const second = await run(withMissing);
  assert.equal(second.outcome, 'TargetIncompatible');
  assert.deepEqual(second.npm.missing, [{ name: 'win', version: '1.0.0' }]);
});

test('tampered npm content is an integrity mismatch and never reaches the owned cache', { skip: requiresSdk }, async () => {
  const s = await scene('npm-tampered');
  const content = contentFile(path.join(s.npmCache, '_cacache'), s.integrity);
  await writeFile(content, Buffer.concat([s.tarball, Buffer.from([7])]));

  const manifest = await run(s);

  assert.equal(manifest.outcome, 'IntegrityMismatch');
  assert.deepEqual(manifest.npm.mismatched, [{ name: 'example', version: '1.0.0' }]);
  assert.deepEqual(await readdir(path.join(s.staging, 'npm-cache')), []);
});

test('selection rejects malformed or conflicting NuGet lock entries and skips project references', () => {
  assert.throws(() => selectNuGetPackages([{ version: 3, dependencies: {} }]), PreparationError);
  assert.throws(() => selectNuGetPackages([{ version: 1, dependencies: { x: { A: { type: 'Direct', resolved: '1.0.0' } } } }]), PreparationError);
  assert.throws(() => selectNuGetPackages([
    { version: 1, dependencies: { a: { A: { type: 'Direct', resolved: '1.0.0', contentHash: 'one' } } } },
    { version: 1, dependencies: { b: { A: { type: 'Direct', resolved: '1.0.0', contentHash: 'two' } } } },
  ]), PreparationError);
  assert.deepEqual(selectNuGetPackages([{ version: 1, dependencies: { a: { P: { type: 'Project' } } } }]), []);
});

// ---------------------------------------------------------------------------------------------------------------------------------------
// The command line: arguments, outcomes and exit codes

const execute = promisify(execFile);
async function cli(args) {
  try {
    const { stdout, stderr } = await execute(process.execPath, [toolPath, ...args], { windowsHide: true, timeout: 120_000 });
    return { code: 0, stdout, stderr };
  } catch (error) {
    return { code: error.code, stdout: error.stdout, stderr: error.stderr };
  }
}

const cliArguments = (s, extra = []) => [
  '--repository', s.repository, '--staging', s.staging, '--nuget-cache', s.nugetCache, '--npm-cache', s.npmCache, ...extra,
];

test('the command line prepares with --dotnet-sdk and reports the target and omissions with exit 0', { skip: requiresSdk }, async () => {
  const s = await scene('cli-prepared', { npm: [{ name: '@native/linux', version: '1.0.0', platform: LINUX, cached: false }] });
  const scratch = async () => (await readdir(os.tmpdir())).filter((name) => name.startsWith('devalcopilot-nuget-validator-')).length;
  const scratchBefore = await scratch();

  const result = await cli(cliArguments(s, ['--dotnet-sdk', sdk]));

  assert.equal(result.code, 0, result.stderr);
  const summary = JSON.parse(result.stdout);
  assert.equal(summary.outcome, 'Prepared');
  assert.equal(summary.npmTarget, 'win32/x64');
  assert.equal(summary.npmOptionalOmitted, 1);
  // The validator's disposable directory is gone once the run ends.
  assert.equal(await scratch(), scratchBefore);
});

test('the command line exits 3 for a content mismatch, 4 for a required incompatibility and 64 for refusals', { skip: requiresSigned }, async () => {
  const mismatch = await scene('cli-mismatch', { nuget: { archive: signedFixture.bytes, contentHash: rawSha512(signedFixture.bytes) } });
  const incompatible = await scene('cli-incompatible', { npm: [{ name: 'needs-linux', version: '1.0.0', platform: { os: ['linux'] } }] });
  const refused = await scene('cli-no-sdk');

  assert.equal((await cli(cliArguments(mismatch, ['--dotnet-sdk', sdk]))).code, 3);
  assert.equal((await cli(cliArguments(incompatible, ['--dotnet-sdk', sdk]))).code, 4);
  const noSdk = await cli(cliArguments(refused));
  assert.equal(noSdk.code, 64);
  assert.match(noSdk.stderr, /--dotnet-sdk is required to validate NuGet content hashes/);
  assert.equal(existsSync(refused.staging), false);
  const relative = await cli(cliArguments(refused, ['--dotnet-sdk', 'relative']));
  assert.equal(relative.code, 64);
  assert.match(relative.stderr, /--dotnet-sdk must be an absolute path/);
});

async function canSymlink() {
  const probe = path.join(os.tmpdir(), `devalcopilot-symlink-probe-${process.pid}`);
  try {
    await writeFile(`${probe}.target`, 'x');
    await symlink(`${probe}.target`, probe, 'file');
    return true;
  } catch {
    return false;
  } finally {
    await rm(probe, { force: true });
    await rm(`${probe}.target`, { force: true });
  }
}

// ---------------------------------------------------------------------------------------------------------------------------------------
// Fixture confinement (WEP-V2): no certificate, key or signer anywhere

test('no fixture, validator or helper code references a certificate, key, PFX or signer, so signing confinement does not rest on environment redirection', async () => {
  const files = [
    'prepare-isolated-dependencies.mjs', 'prepare-isolated-dependencies.fixtures.mjs', 'nuget-content-hasher.mjs', 'bounded-child.mjs',
    'nuget-content-hash.fsx', 'nuget-signed-fixture.fsx', 'nuget-signed-layout.fsx',
  ];
  const forbidden = /X509|\.pfx|CertificateRequest|CertificateProvider|SignPackageRequest|SignatureProvider|RSA\b|ECDsa|nuget['"\s,]+sign|PrivateKey|KeyStorageFlags|CertStore/i;

  for (const file of files) {
    const source = await readFile(new URL(file, import.meta.url), 'utf8');
    // Only code is checked: a comment may say what the code does not do.
    const code = source.split('\n').map((line) => line.replace(/\/\/.*$/, '')).join('\n');

    assert.doesNotMatch(code, forbidden, file);
  }
});

test('the startup self-check covers the signed normalization relation, not only the unsigned path', { skip: requiresSdk }, async () => {
  const directory = path.join(root, 'selfcheck-signed');
  await mkdir(directory, { recursive: true });
  for (const name of ['nuget-content-hash.fsx', 'nuget-signed-layout.fsx']) {
    await writeFile(path.join(directory, name), await readFile(new URL(name, import.meta.url)));
  }

  const script = path.join(directory, 'nuget-content-hash.fsx');
  const startCopy = () => startNuGetContentHasher({ sdkDirectory: sdk, scriptPath: script, startupTimeoutMs: 60_000 });

  // An unmodified copy passes, so the refusal below is caused by the change alone.
  await (await startCopy()).close();

  // The signed-layout archive now also differs in payload content: what a signed hash that no longer normalizes the added signature away
  // would look like to the self-check. An unsigned-only known answer could not see it.
  const layout = path.join(directory, 'nuget-signed-layout.fsx');
  const original = await readFile(layout, 'utf8');
  assert.ok(original.includes('    writeStream.ToArray()'));
  await writeFile(layout, original.replace('    writeStream.ToArray()', '    (let bytes = writeStream.ToArray() in bytes.[60] <- bytes.[60] ^^^ 1uy; bytes)'));

  // Whatever a failing assertion would have started is closed, so a regression reads as a failure and not as a hang.
  await assert.rejects(async () => (await startCopy()).close(), refusal('SelfCheckFailed'));
});
