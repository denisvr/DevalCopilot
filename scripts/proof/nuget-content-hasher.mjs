// ADR-0072: the Node side of NuGet-compatible content hashing for the dependency preparation proof (proof tooling only).
//
// A lockfile contentHash is NuGet's package content hash, not the SHA-512 of the whole .nupkg: a signed package is hashed with its
// signature entry and the ZIP bookkeeping that entry affects normalized away. The definition is NuGet's own
// PackageArchiveReader.GetContentHash from the NuGet.Packaging assembly the installed .NET SDK already ships, so this starts exactly one
// child, `dotnet <sdk>/FSharp/fsi.dll nuget-content-hash.fsx`, and hands it only archive bytes that the caller already admitted through its
// bounded single read, over standard input. Nothing here reopens a source path or reads .nupkg.sha512 or .nupkg.metadata.
//
// The child gets a cleared environment pointing at one disposable directory, never the user profile, a package cache or a network
// configuration, and it installs, downloads and trusts nothing. A content hash is NOT signature trust: it verifies no signer, certificate or
// revocation, and ordinary NuGet signature, revocation and audit validation stays mandatory.
//
// Completion is bounded and truthful:
//   - Every exchange (the input writes, any backpressure and the response) is covered by ONE deadline that starts before the first byte is
//     written. A write that blocks because the child stopped reading therefore times out like a missing answer.
//   - Any anomaly is terminal: a timeout, a rejected write, an exit, an unsolicited, excess, oversized, malformed or mis-numbered line, or a
//     failed startup self-check. The validator then rejects the affected exchange, cancels its pipes (destroys them, which cancels pending
//     writes and reads), asks the child to end, ignores all later output and rejects every later request. There is no retry, no fallback
//     to a raw digest and no way for a late reply to satisfy a later request; each answer also carries the number of its frame.
//   - close() is idempotent (every call returns the same result), bounded, and reports what it could prove. The disposable directory is the
//     child's working directory: it is removed only after the child's exit was observed, otherwise it is PRESERVED and the result says so
//     (closed: false), never silently ignored.
// Closure means the validator's own process exited and its pipes are destroyed here; descendants of that process are not tracked (the
// SDK's fsi starts none).
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { lstat, mkdir, mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export class ContentHasherUnavailable extends Error {
  /** code: SdkIncomplete, StartFailed, SelfCheckFailed, Timeout, ProtocolViolation, ProcessExited, ProcessError, InputRejected, FrameTooLarge, Closed. */
  constructor(message, code = 'Unavailable') {
    super(message);
    this.code = code;
  }
}

const DEFAULT_SCRIPT = fileURLToPath(new URL('./nuget-content-hash.fsx', import.meta.url));
const DEFAULT_TIMEOUT_MS = 120_000;
const DEFAULT_GRACEFUL_EXIT_MS = 5000;
const DEFAULT_KILL_WAIT_MS = 5000;
const MAX_LINE_CHARACTERS = 4096;
const REQUIRED_ASSEMBLIES = ['NuGet.Common', 'NuGet.Versioning', 'NuGet.Frameworks', 'NuGet.Packaging'];
const ANSWER = /^(?:OK (\d+) ([A-Za-z0-9+/]{86}==) ([SU])|ERR (\d+) (malformed))$/;

// A well-formed ZIP with no entries (only an end-of-central-directory record). Its answer is known without trusting the validator: it is
// unsigned, so NuGet's content hash is the whole-archive SHA-512.
const KNOWN_ANSWER_ARCHIVE = Buffer.from('504b0506000000000000000000000000000000000000', 'hex');
const KNOWN_ANSWER_HASH = createHash('sha512').update(KNOWN_ANSWER_ARCHIVE).digest('base64');

/** The only environment the SDK child receives: every location it could write to or read configuration from is one owned directory. */
export function sdkChildEnvironment(sdkDirectory, home) {
  return {
    SystemRoot: process.env.SystemRoot ?? '',
    PATH: path.resolve(sdkDirectory, '..', '..'),
    USERPROFILE: home,
    HOME: home,
    APPDATA: path.join(home, 'AppData', 'Roaming'),
    LOCALAPPDATA: path.join(home, 'AppData', 'Local'),
    TEMP: path.join(home, 'tmp'),
    TMP: path.join(home, 'tmp'),
    DOTNET_CLI_HOME: home,
    NUGET_PACKAGES: path.join(home, 'nuget-packages'),
    NUGET_HTTP_CACHE_PATH: path.join(home, 'http-cache'),
    NUGET_PLUGINS_CACHE_PATH: path.join(home, 'plugins-cache'),
    DOTNET_NOLOGO: '1',
    DOTNET_CLI_TELEMETRY_OPTOUT: '1',
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1',
    DOTNET_GENERATE_ASPNET_CERTIFICATE: 'false',
  };
}

export const SDK_CHILD_HOME_DIRECTORIES = ['AppData/Roaming', 'AppData/Local', 'tmp', 'nuget-packages', 'http-cache', 'plugins-cache'];

async function isRegularFile(candidate) {
  const info = await lstat(candidate).catch(() => null);
  return info !== null && info.isFile() && !info.isSymbolicLink();
}

const raceWithTimeout = (promise, milliseconds) => new Promise((resolve) => {
  const timer = setTimeout(() => resolve(false), milliseconds);
  promise.then(() => {
    clearTimeout(timer);
    resolve(true);
  });
});

/**
 * Starts the validator and proves it before it is trusted: its own startup self-check (an unsigned archive and the same archive in NuGet's
 * signed layout must hash alike, see nuget-content-hash.fsx) and then an archive whose answer is known here. Returns
 * { hash(bytes), close(), state, scratchDirectory, provenance }; hash resolves to { contentHash, signed } or { malformed: true } and
 * rejects with ContentHasherUnavailable (terminal) when the validator cannot answer truthfully in time.
 * Timings are whole-exchange bounds in milliseconds. spawnProcess is a test seam for the process boundary only.
 */
export async function startNuGetContentHasher({
  sdkDirectory,
  scriptPath = DEFAULT_SCRIPT,
  startupTimeoutMs = DEFAULT_TIMEOUT_MS,
  exchangeTimeoutMs = DEFAULT_TIMEOUT_MS,
  gracefulExitMs = DEFAULT_GRACEFUL_EXIT_MS,
  killWaitMs = DEFAULT_KILL_WAIT_MS,
  spawnProcess = spawn,
}) {
  if (typeof sdkDirectory !== 'string' || !path.isAbsolute(sdkDirectory)) {
    throw new ContentHasherUnavailable('the SDK directory must be an absolute path', 'SdkIncomplete');
  }

  const dotnet = path.resolve(sdkDirectory, '..', '..', process.platform === 'win32' ? 'dotnet.exe' : 'dotnet');
  const fsi = path.join(sdkDirectory, 'FSharp', 'fsi.dll');
  const required = [dotnet, fsi, scriptPath, ...REQUIRED_ASSEMBLIES.map((name) => path.join(sdkDirectory, `${name}.dll`))];
  for (const file of required) {
    if (!(await isRegularFile(file))) {
      throw new ContentHasherUnavailable('the SDK does not provide the NuGet content-hash validator', 'SdkIncomplete');
    }
  }

  const home = await mkdtemp(path.join(os.tmpdir(), 'devalcopilot-nuget-validator-'));
  let child;
  try {
    for (const sub of SDK_CHILD_HOME_DIRECTORIES) {
      await mkdir(path.join(home, sub), { recursive: true });
    }

    child = spawnProcess(dotnet, [fsi, '--quiet', '--exec', scriptPath, sdkDirectory], {
      cwd: home,
      env: sdkChildEnvironment(sdkDirectory, home),
      windowsHide: true,
      stdio: ['pipe', 'pipe', 'pipe'],
    });
  } catch {
    // Nothing was started, so nothing can hold the directory.
    await rm(home, { recursive: true, force: true }).catch(() => {});
    throw new ContentHasherUnavailable('the NuGet content-hash validator could not be started', 'StartFailed');
  }

  // 'Running' until the first terminal event; then 'Failed' (an anomaly) or 'Closed' (close() was called). Never back to Running.
  let phase = 'Running';
  let failure = null;
  let exchange = null; // { kind: 'ready' | 'frame', index, resolve, reject, timer }
  let nextIndex = 0;
  let pending = '';
  let exited = false;
  const exitObserved = new Promise((resolve) => {
    child.once('exit', () => {
      exited = true;
      resolve();
    });
    // A process that never started has no exit to wait for.
    child.once('error', () => {
      if (child.pid === undefined) {
        exited = true;
        resolve();
      }
    });
  });

  const rejectExchange = (error) => {
    const current = exchange;
    if (current === null) {
      return;
    }

    exchange = null;
    clearTimeout(current.timer);
    current.reject(error);
  };

  const destroyPipes = () => {
    for (const stream of [child.stdin, child.stdout, child.stderr]) {
      try {
        stream?.destroy();
      } catch {
        // A pipe that cannot be destroyed is reported by close() through the unproved exit, never assumed gone.
      }
    }
  };

  /** The one terminal transition: reject the affected exchange, cancel the pending I/O, ask the child to end and ignore everything after. */
  const fail = (code, message) => {
    if (phase !== 'Running') {
      return;
    }

    phase = 'Failed';
    failure = new ContentHasherUnavailable(message, code);
    destroyPipes();
    try {
      child.kill();
    } catch {
      // close() proves or fails to prove the exit; a refused signal is not an exit.
    }

    rejectExchange(failure);
  };

  const beginExchange = (kind, index, timeoutMs, resolve, reject) => {
    exchange = {
      kind,
      index,
      resolve,
      reject,
      timer: setTimeout(() => fail('Timeout', 'the NuGet content-hash validator timed out'), timeoutMs),
    };
  };

  const deliver = (line) => {
    const current = exchange;
    if (current.kind === 'ready') {
      if (line === 'READY') {
        exchange = null;
        clearTimeout(current.timer);
        current.resolve();
      } else {
        fail('SelfCheckFailed', 'the NuGet content-hash validator failed its startup self-check');
      }

      return;
    }

    const match = ANSWER.exec(line);
    const index = match === null ? null : Number(match[1] ?? match[4]);
    if (match === null || index !== current.index) {
      fail('ProtocolViolation', 'the NuGet content-hash validator answered outside its protocol');
      return;
    }

    exchange = null;
    clearTimeout(current.timer);
    current.resolve(match[5] === 'malformed' ? { malformed: true } : { contentHash: match[2], signed: match[3] === 'S' });
  };

  child.stdout.on('data', (chunk) => {
    if (phase !== 'Running') {
      return;
    }

    // Output while no answer is expected is unsolicited. An answer is one short line, and one line per exchange: more than one complete
    // line in a chunk, or a line that never ends, is excess. Nothing is queued: the first violation is terminal.
    pending += chunk.toString('utf8');
    if (exchange === null) {
      fail('ProtocolViolation', 'the NuGet content-hash validator wrote unsolicited output');
      return;
    }

    const lines = pending.split('\n');
    pending = lines.pop();
    if (lines.length > 1 || pending.length > MAX_LINE_CHARACTERS || (lines.length === 1 && lines[0].length > MAX_LINE_CHARACTERS)) {
      fail('ProtocolViolation', 'the NuGet content-hash validator wrote excess or oversized output');
      return;
    }

    if (lines.length === 1) {
      if (pending.length > 0) {
        fail('ProtocolViolation', 'the NuGet content-hash validator wrote excess or oversized output');
        return;
      }

      deliver(lines[0].replace(/\r$/, ''));
    }
  });
  // Child diagnostics are drained and discarded: they can carry paths, and nothing here reports them.
  child.stderr.on('data', () => {});
  child.stdin.on('error', () => fail('InputRejected', 'the NuGet content-hash validator rejected input'));
  child.stdout.on('error', () => fail('ProcessError', 'the NuGet content-hash validator output failed'));
  child.stderr.on('error', () => {});
  child.on('error', () => fail('ProcessError', 'the NuGet content-hash validator process failed'));
  // Judged on 'close' (the process ended and its output was read), so a final line such as a failed self-check keeps its own code.
  child.on('close', () => fail('ProcessExited', 'the NuGet content-hash validator ended unexpectedly'));

  let closing = null;
  const close = () => {
    closing ??= (async () => {
      if (phase === 'Running') {
        phase = 'Closed';
        failure = new ContentHasherUnavailable('the NuGet content-hash validator is closed', 'Closed');
        rejectExchange(failure);
      }

      // Graceful first: end of input lets a healthy validator leave by itself. Then a kill, with the exit awaited (bounded) in both cases.
      try {
        child.stdin.end();
      } catch {
        // Already destroyed by a terminal failure.
      }

      let proved = exited || (await raceWithTimeout(exitObserved, gracefulExitMs));
      if (!proved) {
        try {
          child.kill();
        } catch {
          // Reported below as an exit that was not observed.
        }

        proved = await raceWithTimeout(exitObserved, killWaitMs);
      }

      destroyPipes();
      if (!proved) {
        return Object.freeze({ closed: false, reason: 'ProcessExitNotObserved', scratchPreserved: true });
      }

      try {
        await rm(home, { recursive: true, force: true, maxRetries: 2, retryDelay: 100 });
      } catch {
        return Object.freeze({ closed: false, reason: 'ScratchNotRemoved', scratchPreserved: true });
      }

      return Object.freeze({ closed: true, reason: null, scratchPreserved: false });
    })();
    return closing;
  };

  let serial = Promise.resolve();
  const hash = (bytes) => {
    const run = () => new Promise((resolve, reject) => {
      if (phase !== 'Running') {
        reject(failure ?? new ContentHasherUnavailable('the NuGet content-hash validator is closed', 'Closed'));
        return;
      }

      if (bytes.length > 0x7fffffff) {
        // The caller's input is unusable; the validator itself is unaffected.
        reject(new ContentHasherUnavailable('the archive exceeds the validator frame bound', 'FrameTooLarge'));
        return;
      }

      // The single deadline starts here, before the first byte is written: a blocked write is a timeout like a missing answer.
      beginExchange('frame', nextIndex++, exchangeTimeoutMs, resolve, reject);
      const header = Buffer.alloc(4);
      header.writeInt32LE(bytes.length);
      const written = (error) => {
        if (error) {
          fail('InputRejected', 'the NuGet content-hash validator rejected input');
        }
      };
      try {
        child.stdin.write(header, written);
        child.stdin.write(bytes, written);
      } catch {
        fail('InputRejected', 'the NuGet content-hash validator rejected input');
      }
    });
    // One frame in flight at a time keeps every answer paired with its archive.
    const result = serial.then(run, run);
    serial = result.then(() => {}, () => {});
    return result;
  };

  const leaf = path.basename(sdkDirectory);
  const hasher = {
    hash,
    close,
    get state() {
      return phase;
    },
    scratchDirectory: home,
    // The SDK directory's leaf is its version (for example 10.0.401); the rest of the path is never recorded.
    provenance: {
      source: 'installed .NET SDK NuGet.Packaging',
      sdkVersion: /^\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?$/.test(leaf) ? leaf : null,
    },
  };

  try {
    await new Promise((resolve, reject) => beginExchange('ready', -1, startupTimeoutMs, resolve, reject));
    const answer = await hash(KNOWN_ANSWER_ARCHIVE);
    if (answer.malformed || answer.signed || answer.contentHash !== KNOWN_ANSWER_HASH) {
      fail('SelfCheckFailed', 'the NuGet content-hash validator failed its known-answer check');
      throw failure;
    }
  } catch (error) {
    const closure = await close();
    const cause = error instanceof ContentHasherUnavailable ? error
      : new ContentHasherUnavailable('the NuGet content-hash validator failed its startup check', 'SelfCheckFailed');
    if (!closure.closed) {
      throw new ContentHasherUnavailable('the NuGet content-hash validator failed and did not close; its scratch directory was preserved',
        cause.code);
    }

    throw cause;
  }

  return hasher;
}
