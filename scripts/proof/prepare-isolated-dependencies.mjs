// ADR-0072: bounded preparation of dependencies for the local toolchain proof. Proof preparation only, never a product feature.
//
// It reads the repository's COMMITTED NuGet lockfiles and package-lock.json files (the blobs of HEAD, never a working-copy,
// untracked or ignored file), selects exact package identities, versions and integrity values, and places only those exact bytes
// into a new, owned staging root:
//   - each exact .nupkg (including the locked Devalente.Shared packages) from the named NuGet package cache into a flat local feed,
//     verified against the lockfile contentHash. That is NuGet's package CONTENT hash, computed by the installed .NET SDK's own
//     NuGet.Packaging (see nuget-content-hasher.mjs), NOT the SHA-512 of the whole file: a signed package is hashed with its signature
//     entry and the ZIP bookkeeping it affects normalized away, so a raw whole-file digest falsely rejects a valid signed package.
//     The raw archive SHA-256/SHA-512 is recorded separately as the identity of the exact bytes transferred and staged;
//   - each lockfile-SRI-addressed npm tarball that the supported target (Windows x64, fixed; see SUPPORTED_NPM_TARGET) needs, from the
//     named npm cache's content store into a new owned cache, verified against its SRI digest, through the cacache module already
//     bundled with the running node's npm (used only to write the owned cache). Entries whose os/cpu constraints exclude the target are
//     omitted only when the lockfile marks them optional, exactly as npm skips them; a required incompatible entry is a blocker.
// Every source file is read once through one bounded reader (a regular file, never a link, physically inside its root, within the
// size bound), verified in memory, and exactly those verified bytes are written to staging; the source is never reopened, and the
// content-hash validator is only ever handed those same admitted bytes. Before anything is written, the staging root must not be,
// contain, sit inside or alias (through a link or junction) the repository or either source cache, physically and
// case-insensitively on Windows, and it must be new or empty.
// It never copies expanded package trees, NuGet.Config, .nupkg.metadata, HTTP or plugin caches, credential providers, source
// indexes, metadata, .npmrc or node_modules; never searches the user profile; never writes to a source cache; never rebuilds or
// repacks a package; never reaches a network. Git runs read-only with no system or global configuration. It writes an owned
// NuGet.Config whose only source is the local feed, and a bounded manifest of package identities, integrity values, the raw archive
// identities, the npm target and every omission with its reason, the commit and the committed lockfile hashes, without any source
// path, configuration or account data.
//
// A matching NuGet content hash proves the package CONTENT is the locked one. It verifies no signer, certificate or revocation and
// does not even prove a signature is still present, so ordinary NuGet signature, revocation, audit and warnings-as-errors validation
// stays mandatory in the restore the proof performs afterwards.
//
// The validator exchange is bounded and truthful (see nuget-content-hasher.mjs): one deadline covers every exchange including blocked
// input, any anomaly is terminal, and the validator is closed, with its exit proved, BEFORE any conclusion or manifest is written. If its
// closure cannot be proved the run is refused and its disposable directory is preserved, never removed or ignored.
//
// npm admission is decided per lock entry (document and location). Only the transfer of bytes is deduplicated by tarball, so a required
// incompatible entry stays a blocker even when another entry that shares its tarball is needed, and an optional exclusion is recorded
// even when its bytes are staged for another entry (tarballNeededElsewhere).
//
// Restoring and building with what it prepared is the proof itself, which needs an identified outbound-isolation boundary first.
//
// Usage:
//   node prepare-isolated-dependencies.mjs --repository <dir> --staging <new dir> --nuget-cache <dir> --npm-cache <dir>
//        [--dotnet-sdk <installed .NET SDK directory>]
// --dotnet-sdk is required whenever a locked NuGet package must be validated; without a working SDK validator the run is refused
// before anything is written, never degraded to a raw digest.
// Exit codes: 0 Prepared, 2 DependenciesUnavailable, 3 IntegrityMismatch (content hash mismatch or a malformed archive), 4
// TargetIncompatible (a required npm entry excludes the target), 64 usage, unsafe input or an unavailable validator.

import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { lstat, mkdir, open, readFile, readdir, realpath, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import os from 'node:os';
import path from 'node:path';
import { ContentHasherUnavailable, startNuGetContentHasher } from './nuget-content-hasher.mjs';

const DEFAULT_MAX_ARCHIVE_BYTES = 512 * 1024 * 1024;
const MAX_LOCKFILE_BYTES = 16 * 1024 * 1024;
const LOCKFILE_NAMES = new Set(['packages.lock.json', 'package-lock.json']);
const SKIPPED_DIRECTORIES = new Set(['node_modules', 'bin', 'obj', 'target', 'dist', 'test-results', 'logs']);

export class PreparationError extends Error {}

function parseArguments(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 2) {
    const key = argv[index];
    const value = argv[index + 1];
    if (!key?.startsWith('--') || value === undefined) {
      throw new PreparationError('usage');
    }
    options[key.slice(2)] = value;
  }

  for (const required of ['repository', 'staging', 'nuget-cache', 'npm-cache']) {
    if (!options[required] || !path.isAbsolute(options[required])) {
      throw new PreparationError(`--${required} must be an absolute path`);
    }
  }

  if (options['dotnet-sdk'] !== undefined && !path.isAbsolute(options['dotnet-sdk'])) {
    throw new PreparationError('--dotnet-sdk must be an absolute path');
  }

  return options;
}

const comparable = (value) => {
  const trimmed = value.length > path.parse(value).root.length ? value.replace(/[\\/]+$/, '') : value;
  return process.platform === 'win32' ? trimmed.toLowerCase() : trimmed;
};

/** The physical location of a path: the real path of its nearest existing ancestor (resolving links and junctions) plus the rest. */
async function physicalPath(candidate) {
  let current = path.resolve(candidate);
  const remainder = [];
  for (;;) {
    const resolved = await realpath(current).catch(() => null);
    if (resolved !== null) {
      return path.join(resolved, ...remainder.reverse());
    }

    const parent = path.dirname(current);
    if (parent === current) {
      return path.resolve(candidate);
    }

    remainder.push(path.basename(current));
    current = parent;
  }
}

const overlaps = (left, right) => {
  const a = comparable(left);
  const b = comparable(right);
  return a === b || a.startsWith(b + path.sep) || b.startsWith(a + path.sep)
    || a.startsWith(b + '/') || b.startsWith(a + '/');
};

/** Refuses a staging root that is, contains, is inside or aliases any source root, physically and lexically. */
async function refuseOverlap(staging, sources) {
  const physicalStaging = await physicalPath(staging);
  for (const source of sources) {
    const physicalSource = await physicalPath(source);
    if (overlaps(staging, source) || overlaps(physicalStaging, physicalSource)) {
      throw new PreparationError('the staging root overlaps a source root');
    }
  }
}

function git(repository, args, maxBuffer) {
  return execFileSync('git', ['-C', repository, ...args], {
    maxBuffer,
    stdio: ['ignore', 'pipe', 'ignore'],
    windowsHide: true,
    env: {
      PATH: process.env.PATH ?? process.env.Path ?? '',
      SystemRoot: process.env.SystemRoot ?? '',
      GIT_CONFIG_NOSYSTEM: '1',
      GIT_CONFIG_GLOBAL: process.platform === 'win32' ? 'NUL' : os.devNull,
      GIT_TERMINAL_PROMPT: '0',
      GIT_OPTIONAL_LOCKS: '0',
    },
  });
}

/** The committed lockfiles of HEAD and their exact committed bytes; working-copy, untracked and ignored files are never read. */
function committedLockfiles(repository) {
  let commit;
  let names;
  try {
    commit = git(repository, ['rev-parse', '--verify', 'HEAD^{commit}'], 1024).toString('utf8').trim();
    names = git(repository, ['ls-tree', '-r', '-z', '--name-only', commit], 64 * 1024 * 1024).toString('utf8').split('\0');
  } catch {
    throw new PreparationError('the repository has no readable committed tree');
  }

  const selected = names
    .filter((name) => name.length > 0)
    .filter((name) => LOCKFILE_NAMES.has(name.split('/').at(-1)))
    .filter((name) => !name.split('/').slice(0, -1).some((segment) => SKIPPED_DIRECTORIES.has(segment)))
    .sort();
  if (selected.length === 0) {
    throw new PreparationError('no committed lockfile');
  }

  const files = selected.map((name) => {
    let bytes;
    try {
      bytes = git(repository, ['cat-file', 'blob', `${commit}:${name}`], MAX_LOCKFILE_BYTES + 1);
    } catch {
      throw new PreparationError('a committed lockfile could not be read');
    }

    if (bytes.length > MAX_LOCKFILE_BYTES) {
      throw new PreparationError('lockfile too large');
    }

    return { name, kind: name.endsWith('package-lock.json') ? 'npm' : 'nuget', bytes };
  });
  return { commit, files };
}

const sha256 = (bytes) => createHash('sha256').update(bytes).digest('hex').toUpperCase();

/** Exact locked NuGet packages across every framework and runtime section; project references are not packages. */
export function selectNuGetPackages(lockDocuments) {
  const selected = new Map();
  for (const document of lockDocuments) {
    if (document.version !== 1 && document.version !== 2) {
      throw new PreparationError('unsupported NuGet lockfile version');
    }

    for (const section of Object.values(document.dependencies ?? {})) {
      for (const [id, entry] of Object.entries(section)) {
        if (entry.type === 'Project') {
          continue;
        }

        if (typeof entry.resolved !== 'string' || typeof entry.contentHash !== 'string') {
          throw new PreparationError('malformed NuGet lockfile entry');
        }

        const key = `${id.toLowerCase()}/${entry.resolved.toLowerCase()}`;
        const existing = selected.get(key);
        if (existing && existing.contentHash !== entry.contentHash) {
          throw new PreparationError('conflicting locked content hash');
        }

        selected.set(key, { id, version: entry.resolved, contentHash: entry.contentHash });
      }
    }
  }

  return [...selected.values()].sort((a, b) => `${a.id}/${a.version}`.localeCompare(`${b.id}/${b.version}`));
}

/**
 * The one platform the proof supports, fixed in code: it is the Windows x64 profile of ADR-0072, not a setting. A different platform
 * needs its own decision and its own evidence, so there is no option, environment variable or file that changes it.
 */
export const SUPPORTED_NPM_TARGET = Object.freeze({ os: 'win32', cpu: 'x64' });

/** An os/cpu constraint as package-lock records it: a non-empty string or array of non-empty strings, null when absent. */
function platformConstraint(value) {
  if (value === undefined) {
    return null;
  }

  const items = typeof value === 'string' ? [value] : value;
  if (!Array.isArray(items) || items.length === 0 || items.some((item) => typeof item !== 'string' || item === '' || item === '!')) {
    throw new PreparationError('malformed package-lock platform constraint');
  }

  return items;
}

/**
 * npm's own rule for an os/cpu list (npm-install-checks): the sole entry `any` allows everything; otherwise no negated entry may equal
 * the value, and the value must equal a non-negated entry unless every entry is negated.
 */
function constraintAllows(items, value) {
  if (items.length === 1 && items[0] === 'any') {
    return true;
  }

  let negated = 0;
  let matched = false;
  for (const item of items) {
    if (item.startsWith('!')) {
      negated++;
      if (item.slice(1) === value) {
        return false;
      }
    } else if (item === value) {
      matched = true;
    }
  }

  return matched || negated === items.length;
}

function npmPlatform(entry, target) {
  const osConstraint = platformConstraint(entry.os);
  const cpuConstraint = platformConstraint(entry.cpu);
  if (entry.optional !== undefined && typeof entry.optional !== 'boolean') {
    throw new PreparationError('malformed package-lock entry');
  }

  const applicable = (osConstraint === null || constraintAllows(osConstraint, target.os))
    && (cpuConstraint === null || constraintAllows(cpuConstraint, target.cpu));
  // A libc constraint is evaluated by npm only for a Linux libc family. For the supported Windows target it is never evidence in either
  // direction, so an entry that os and cpu would otherwise admit is not silently trusted: it blocks as an unsupported constraint.
  if (applicable && entry.libc !== undefined) {
    throw new PreparationError('unsupported package-lock platform constraint');
  }

  return { applicable, optional: entry.optional === true, os: osConstraint, cpu: cpuConstraint };
}

/**
 * Exact locked npm tarballs for the target, from every package entry with a resolved registry tarball and an SRI integrity value.
 * Returns { selected, omitted, incompatible }. Admission is decided PER LOCK ENTRY (one entry per document and location); only the
 * transfer of bytes is deduplicated afterwards, so a fact about one entry can never be absorbed by another entry that shares its tarball:
 *   selected      the distinct identities (name, version, tarball URL and SRI) of entries the target needs (os/cpu admit it, or declare
 *                 no constraint), each required to be present and intact;
 *   omitted       one record per incompatible entry the lockfile marks optional, which npm itself does not install, with its location,
 *                 constraints and whether the same tarball is staged for another entry anyway (`tarballNeededElsewhere`);
 *   incompatible  one record per incompatible entry that is NOT optional (including dev-optional and peer-optional): required content
 *                 the target cannot take, which is a blocker, never a silent omission, even when the same tarball is needed elsewhere.
 * Every list is sorted by identity, location and document, so the result does not depend on entry or document order. Original
 * lockfiles are never rewritten.
 */
export function selectNpmPackages(lockDocuments, target = SUPPORTED_NPM_TARGET) {
  const entries = [];
  lockDocuments.forEach((lockDocument, document) => {
    if (lockDocument.lockfileVersion !== 2 && lockDocument.lockfileVersion !== 3) {
      throw new PreparationError('unsupported package-lock version');
    }

    for (const [location, entry] of Object.entries(lockDocument.packages ?? {})) {
      if (location === '' || entry.link === true || entry.resolved === undefined) {
        continue;
      }

      if (typeof entry.resolved !== 'string' || typeof entry.integrity !== 'string' || !/^sha512-[A-Za-z0-9+/]{86}==$/.test(entry.integrity)) {
        throw new PreparationError('malformed package-lock entry');
      }

      const name = location.slice(location.lastIndexOf('node_modules/') + 'node_modules/'.length);
      entries.push({ document, location, name, version: entry.version, resolved: entry.resolved, integrity: entry.integrity, ...npmPlatform(entry, target) });
    }
  });

  const text = (value) => String(value ?? '');
  const byEntry = (a, b) => `${a.name}@${a.version}`.localeCompare(`${b.name}@${b.version}`)
    || text(a.location).localeCompare(text(b.location)) || a.document - b.document;
  entries.sort(byEntry);

  const needed = entries.filter((entry) => entry.applicable);
  const neededTarballs = new Set(needed.map((entry) => entry.integrity));
  const selected = new Map();
  for (const entry of needed) {
    selected.set(`${entry.name}@${entry.version}\n${entry.resolved}\n${entry.integrity}`,
      { name: entry.name, version: entry.version, resolved: entry.resolved, integrity: entry.integrity });
  }

  const exclusion = (entry, reason) => ({
    name: entry.name,
    version: entry.version,
    integrity: entry.integrity,
    document: entry.document,
    location: entry.location,
    reason,
    os: entry.os,
    cpu: entry.cpu,
    tarballNeededElsewhere: neededTarballs.has(entry.integrity),
  });
  return {
    selected: [...selected.values()],
    omitted: entries.filter((entry) => !entry.applicable && entry.optional).map((entry) => exclusion(entry, 'OptionalPlatformMismatch')),
    incompatible: entries.filter((entry) => !entry.applicable && !entry.optional).map((entry) => exclusion(entry, 'RequiredPlatformMismatch')),
  };
}

/**
 * Reads a source file once: it must be a regular file, never a link, physically inside its root and within the size bound, checked
 * on the opened handle as well. Returns the bytes, or null when the file is absent or violates a constraint.
 */
async function readContainedRegularFile(root, candidate, maxBytes) {
  const resolvedRoot = await realpath(root).catch(() => null);
  if (resolvedRoot === null) {
    return null;
  }

  const relative = path.relative(path.resolve(root), path.resolve(candidate));
  if (relative.startsWith('..') || path.isAbsolute(relative)) {
    return null;
  }

  const link = await lstat(candidate).catch(() => null);
  if (link === null || !link.isFile() || link.isSymbolicLink() || link.size > maxBytes) {
    return null;
  }

  const physical = await realpath(candidate).catch(() => null);
  const physicalRelative = physical === null ? '..' : path.relative(resolvedRoot, physical);
  if (physicalRelative.startsWith('..') || path.isAbsolute(physicalRelative)) {
    return null;
  }

  const handle = await open(physical, 'r').catch(() => null);
  if (handle === null) {
    return null;
  }

  try {
    const opened = await handle.stat();
    if (!opened.isFile() || opened.size > maxBytes || opened.size !== link.size) {
      return null;
    }

    const bytes = Buffer.alloc(opened.size);
    let offset = 0;
    while (offset < bytes.length) {
      const { bytesRead } = await handle.read(bytes, offset, bytes.length - offset, offset);
      if (bytesRead === 0) {
        return null;
      }

      offset += bytesRead;
    }

    // The file must end where its size said it did: a growing file is refused rather than read partially.
    const { bytesRead: excess } = await handle.read(Buffer.alloc(1), 0, 1, offset);
    return excess === 0 ? bytes : null;
  } finally {
    await handle.close();
  }
}

/**
 * Each archive is read once; NuGet's content hash is computed from those same admitted bytes and compared with the lockfile value. The
 * raw whole-file SHA-256 and SHA-512 are a different fact (the identity of the exact bytes, which differs from the content hash for a
 * signed package) and are recorded beside it, and the staged file is read back from the owned feed and compared with them.
 *   missing     the archive is absent or violates the read constraints;
 *   mismatched  a readable package whose content hash is not the locked one (never copied);
 *   invalid     bytes that are not a readable package archive at all (never copied).
 */
async function prepareNuGet(packages, cacheRoot, feed, maxBytes, hasher) {
  const missing = [];
  const mismatched = [];
  const invalid = [];
  const archives = [];
  for (const entry of packages) {
    const id = entry.id.toLowerCase();
    const version = entry.version.toLowerCase();
    const bytes = await readContainedRegularFile(cacheRoot, path.join(cacheRoot, id, version, `${id}.${version}.nupkg`), maxBytes);
    if (bytes === null) {
      missing.push({ id: entry.id, version: entry.version });
      continue;
    }

    const archiveSha256 = sha256(bytes);
    const archiveSha512 = createHash('sha512').update(bytes).digest('base64');
    const validation = await hasher.hash(bytes);
    if (validation.malformed) {
      invalid.push({ id: entry.id, version: entry.version, reason: 'MalformedArchive', archiveSha256 });
      continue;
    }

    if (validation.contentHash !== entry.contentHash) {
      mismatched.push({ id: entry.id, version: entry.version, signed: validation.signed, archiveSha256 });
      continue;
    }

    // Exactly the verified bytes, written exclusively: the owned feed never overwrites anything and the source is not reopened.
    const staged = path.join(feed, `${id}.${version}.nupkg`);
    await writeFile(staged, bytes, { flag: 'wx' });
    if (sha256(await readFile(staged)) !== archiveSha256) {
      throw new PreparationError('a staged archive differs from the verified bytes');
    }

    archives.push({ id: entry.id, version: entry.version, contentHash: entry.contentHash, signed: validation.signed, archiveSha256, archiveSha512 });
  }

  return { missing, mismatched, invalid, archives };
}

function loadBundledCacache() {
  const bundled = path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'node_modules', 'cacache', 'package.json');
  return createRequire(bundled)('./');
}

/** The content-store location of a SHA-512 SRI value in a cacache index (content-v2/sha512/aa/bb/rest). */
function contentPath(cacheIndex, integrity) {
  const hex = Buffer.from(integrity.slice('sha512-'.length), 'base64').toString('hex');
  return path.join(cacheIndex, 'content-v2', 'sha512', hex.slice(0, 2), hex.slice(2, 4), hex.slice(4));
}

/**
 * Byte transfer for the selected tarballs, deduplicated by integrity: each content file is read once however many identities or tarball
 * URLs name it, verified against its SRI value, and written to the owned cache under every distinct URL key npm may look it up by.
 * Missing and mismatched are reported per identity, so no entry's fact is hidden by a shared tarball.
 */
async function prepareNpm(packages, sourceCache, ownedCache, cacache, maxBytes) {
  const missing = [];
  const mismatched = [];
  const sourceIndex = path.join(sourceCache, '_cacache');
  const ownedIndex = path.join(ownedCache, '_cacache');
  const byIntegrity = new Map();
  for (const entry of packages) {
    byIntegrity.set(entry.integrity, [...(byIntegrity.get(entry.integrity) ?? []), entry]);
  }

  for (const [integrity, entries] of byIntegrity) {
    const identities = [...new Map(entries.map((entry) => [`${entry.name}@${entry.version}`, { name: entry.name, version: entry.version }])).values()];
    const bytes = await readContainedRegularFile(sourceCache, contentPath(sourceIndex, integrity), maxBytes);
    if (bytes === null) {
      missing.push(...identities);
      continue;
    }

    if ('sha512-' + createHash('sha512').update(bytes).digest('base64') !== integrity) {
      mismatched.push(...identities);
      continue;
    }

    // A new index entry under each key npm uses for the locked tarball, holding exactly the verified bytes, in the owned cache only.
    for (const resolved of new Set(entries.map((entry) => entry.resolved))) {
      await cacache.put(ownedIndex, `make-fetch-happen:request-cache:${resolved}`, bytes, { integrity });
    }
  }

  return { missing, mismatched };
}

function ownedNuGetConfig(feed) {
  const escaped = feed.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
  return `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="isolated-local-feed" value="${escaped}" />
  </packageSources>
  <packageSourceCredentials>
    <clear />
  </packageSourceCredentials>
  <disabledPackageSources>
    <clear />
  </disabledPackageSources>
</configuration>
`;
}

/**
 * The NuGet content-hash validator for this run: the caller's (a test double or a shared real one, which the caller then closes) or the
 * one the named SDK provides. A run with locked NuGet packages and no working validator is refused here, before anything is written.
 */
async function openNuGetContentHasher(options, injected, startHasher) {
  if (injected !== undefined) {
    return { hasher: injected, owned: false };
  }

  if (options['dotnet-sdk'] === undefined) {
    throw new PreparationError('--dotnet-sdk is required to validate NuGet content hashes');
  }

  try {
    return { hasher: await startHasher({ sdkDirectory: path.resolve(options['dotnet-sdk']) }), owned: true };
  } catch (error) {
    if (error instanceof ContentHasherUnavailable) {
      throw new PreparationError(error.code === 'SdkIncomplete' || error.code === 'StartFailed'
        ? 'the NuGet content-hash validator is unavailable'
        : `the NuGet content-hash validator is unavailable (${error.code})`);
    }

    throw error;
  }
}

const VALIDATOR_NOT_CLOSED = 'the NuGet content-hash validator did not close; its scratch directory was preserved';

/** Closes a validator this run started and states an unresolved closure; a validator the caller owns is the caller's to close. */
async function closeOwnedValidator(validator) {
  if (validator?.owned && !(await validator.hasher.close()).closed) {
    throw new PreparationError(VALIDATOR_NOT_CLOSED);
  }
}

export async function prepare(
  options,
  {
    cacache = undefined,
    maxArchiveBytes = DEFAULT_MAX_ARCHIVE_BYTES,
    nugetContentHasher = undefined,
    startContentHasher = startNuGetContentHasher,
  } = {},
) {
  const repository = path.resolve(options.repository);
  const staging = path.resolve(options.staging);
  const nugetCache = path.resolve(options['nuget-cache']);
  const sourceNpmCache = path.resolve(options['npm-cache']);

  // Every boundary is proved before the first write.
  await refuseOverlap(staging, [repository, nugetCache, sourceNpmCache]);
  const existing = await readdir(staging).catch(() => null);
  if (existing !== null && existing.length > 0) {
    throw new PreparationError('the staging root must be new or empty');
  }

  const committed = committedLockfiles(repository);
  const nugetDocuments = [];
  const npmDocuments = [];
  for (const file of committed.files) {
    let document;
    try {
      document = JSON.parse(file.bytes.toString('utf8'));
    } catch {
      throw new PreparationError('a committed lockfile is not JSON');
    }

    (file.kind === 'npm' ? npmDocuments : nugetDocuments).push(document);
  }

  const nugetPackages = selectNuGetPackages(nugetDocuments);
  const npmSelection = selectNpmPackages(npmDocuments);

  // The validator is proved (a known-answer archive) before the first write, so an unavailable one leaves nothing behind.
  const validator = nugetPackages.length > 0 ? await openNuGetContentHasher(options, nugetContentHasher, startContentHasher) : null;
  const npmLockfiles = committed.files.filter((file) => file.kind === 'npm').map((file) => file.name);
  const withLockfile = (list) => list.map(({ document, ...rest }) => ({ lockfile: npmLockfiles[document], ...rest }));
  let failed = null;
  try {
    await mkdir(staging, { recursive: true });
    // Re-proved on the created directory, so an alias appearing in between is refused before anything is written inside it.
    await refuseOverlap(staging, [repository, nugetCache, sourceNpmCache]);
    if ((await readdir(staging)).length > 0) {
      throw new PreparationError('the staging root must be new or empty');
    }

    const feed = path.join(staging, 'nuget-feed');
    const npmCache = path.join(staging, 'npm-cache');
    await mkdir(feed);
    await mkdir(npmCache);
    await mkdir(path.join(staging, 'nuget-packages'));
    await mkdir(path.join(staging, 'profile'));

    let nuget = { missing: [], mismatched: [], invalid: [], archives: [] };
    if (validator !== null) {
      try {
        nuget = await prepareNuGet(nugetPackages, nugetCache, feed, maxArchiveBytes, validator.hasher);
      } catch (error) {
        if (error instanceof ContentHasherUnavailable) {
          throw new PreparationError('the NuGet content-hash validator became unavailable');
        }

        throw error;
      }

      // Closed (and proved closed) before anything is concluded or written: an unresolved validator leaves no manifest behind.
      await closeOwnedValidator(validator);
    }

    const npm = await prepareNpm(npmSelection.selected, sourceNpmCache, npmCache, cacache ?? loadBundledCacache(), maxArchiveBytes);
    await writeFile(path.join(staging, 'NuGet.Config'), ownedNuGetConfig(feed), { flag: 'wx' });

    // The committed inputs must not have moved during preparation.
    if (committedLockfiles(repository).commit !== committed.commit) {
      throw new PreparationError('the committed tree changed during preparation');
    }

    // Integrity failures come first: they say content is wrong. An incompatible required entry says the lockfile cannot be satisfied for
    // the target; only then does absent content (which may simply be uncached) decide the outcome. All classes are still recorded.
    const outcome = nuget.mismatched.length + nuget.invalid.length + npm.mismatched.length > 0
      ? 'IntegrityMismatch'
      : npmSelection.incompatible.length > 0
        ? 'TargetIncompatible'
        : nuget.missing.length + npm.missing.length > 0
          ? 'DependenciesUnavailable'
          : 'Prepared';
    const manifest = {
      version: 3,
      outcome,
      commit: committed.commit,
      lockfiles: committed.files.map(({ name, bytes }) => ({ path: name, sha256: sha256(bytes) })),
      nuget: {
        contentHash: validator === null ? null : {
          definition: 'NuGet package content hash (NuGet.Packaging PackageArchiveReader.GetContentHash); not the raw archive digest',
          validator: validator.hasher.provenance ?? null,
          signatureTrustValidated: false,
        },
        packages: nugetPackages,
        archives: nuget.archives,
        missing: nuget.missing,
        mismatched: nuget.mismatched,
        invalid: nuget.invalid,
      },
      npm: {
        target: { ...SUPPORTED_NPM_TARGET, source: 'fixed supported Windows x64 proof profile (ADR-0072)' },
        packages: npmSelection.selected.map(({ name, version, integrity }) => ({ name, version, integrity })),
        omitted: withLockfile(npmSelection.omitted),
        incompatible: withLockfile(npmSelection.incompatible),
        missing: npm.missing,
        mismatched: npm.mismatched,
      },
    };
    await writeFile(path.join(staging, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n', { flag: 'wx' });
    return manifest;
  } catch (error) {
    failed = error;
    throw error;
  } finally {
    // Idempotent: after the explicit close above this only repeats its answer. On any other failure it still closes the validator, and an
    // unresolved closure is never lost behind the error that was already propagating.
    if (validator?.owned && !(await validator.hasher.close()).closed
      && !(failed instanceof PreparationError && failed.message.includes('did not close'))) {
      throw new PreparationError(failed instanceof PreparationError
        ? `${failed.message}; the NuGet content-hash validator also did not close; its scratch directory was preserved`
        : VALIDATOR_NOT_CLOSED);
    }
  }
}

const EXIT_CODES = { Prepared: 0, DependenciesUnavailable: 2, IntegrityMismatch: 3, TargetIncompatible: 4 };

if (process.argv[1] && import.meta.url === new URL(`file:///${path.resolve(process.argv[1]).replaceAll('\\', '/')}`).href) {
  try {
    const manifest = await prepare(parseArguments(process.argv.slice(2)));
    console.log(JSON.stringify({
      outcome: manifest.outcome,
      nugetPackages: manifest.nuget.packages.length,
      npmPackages: manifest.npm.packages.length,
      npmTarget: `${manifest.npm.target.os}/${manifest.npm.target.cpu}`,
      npmOptionalOmitted: manifest.npm.omitted.length,
      missing: [...manifest.nuget.missing.map((p) => `${p.id}@${p.version}`), ...manifest.npm.missing.map((p) => `${p.name}@${p.version}`)],
      mismatched: [...manifest.nuget.mismatched.map((p) => `${p.id}@${p.version}`), ...manifest.npm.mismatched.map((p) => `${p.name}@${p.version}`)],
      invalid: manifest.nuget.invalid.map((p) => `${p.id}@${p.version}`),
      incompatible: manifest.npm.incompatible.map((p) => `${p.name}@${p.version}`),
    }));
    process.exitCode = EXIT_CODES[manifest.outcome];
  } catch (error) {
    // Never a path, stack or content: only the closed reason.
    console.error(`preparation refused: ${error instanceof PreparationError ? error.message : 'unexpected failure'}`);
    process.exitCode = 64;
  }
}
