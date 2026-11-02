// ADR-0072 harness support: runs one child process to completion with every phase bounded and the outcome stated truthfully. Used by the
// fixture tools of the dependency preparation harness; it is not part of preparation itself.
//
//   StartFailed     the process could not be started (nothing was running);
//   InputRejected   the input could not be delivered (a write failed or the child closed its input), the child is ended;
//   ProcessError    the process reported an error after it started, the child is ended;
//   TimedOut        one deadline covers the input writes, any backpressure and the exit: it passed, the child is killed;
//   OutputExceeded  standard output passed its byte bound: the child is killed and no output is returned;
//   Exited          the process ended by itself and its output fit the bound.
// closureProved is true only when the process's exit was observed (immediately for Exited, within exitWaitMs after a kill otherwise). When it
// is false the child may still be running: the caller must not remove anything the child can hold, and should report the unresolved state.
// Standard error is drained and discarded. Closure here means the child itself; its descendants are not tracked.
import { spawn } from 'node:child_process';

const DEFAULT_EXIT_WAIT_MS = 5000;

export function runBoundedChild(executable, args, {
  env,
  cwd,
  input = Buffer.alloc(0),
  timeoutMs,
  maxStdoutBytes,
  exitWaitMs = DEFAULT_EXIT_WAIT_MS,
  spawnProcess = spawn,
}) {
  return new Promise((resolve) => {
    let child;
    try {
      child = spawnProcess(executable, args, { env, cwd, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    } catch {
      resolve({ status: 'StartFailed', code: null, stdout: Buffer.alloc(0), closureProved: true });
      return;
    }

    const chunks = [];
    let received = 0;
    let status = null;
    let finished = false;
    let exited = false;
    let timer = null;
    let giveUp = null;

    const finish = (closureProved) => {
      if (finished) {
        return;
      }

      finished = true;
      clearTimeout(timer);
      clearTimeout(giveUp);
      for (const stream of [child.stdin, child.stdout, child.stderr]) {
        try {
          stream?.destroy();
        } catch {
          // Reported through closureProved, never assumed gone.
        }
      }

      resolve({
        status: status ?? 'Exited',
        code: child.exitCode,
        stdout: status === null ? Buffer.concat(chunks) : Buffer.alloc(0),
        closureProved,
      });
    };

    /** A terminal anomaly: record it once, end the child and wait (bounded) for its exit before answering. */
    const end = (reason) => {
      if (status !== null || finished) {
        return;
      }

      status = reason;
      try {
        child.kill();
      } catch {
        // The exit wait below decides whether the child is gone.
      }

      if (exited) {
        finish(true);
      } else {
        giveUp = setTimeout(() => finish(false), exitWaitMs);
      }
    };

    // Kept for tests: a child that was never created has no pid and nothing to wait for.
    child.once('error', () => {
      if (child.pid === undefined) {
        status ??= 'StartFailed';
        exited = true;
        finish(true);
      } else {
        end('ProcessError');
      }
    });
    child.once('exit', () => {
      exited = true;
      if (status !== null) {
        finish(true);
      }
    });
    child.once('close', () => {
      exited = true;
      finish(true);
    });

    child.stdout.on('data', (chunk) => {
      if (status !== null) {
        return;
      }

      received += chunk.length;
      if (received > maxStdoutBytes) {
        end('OutputExceeded');
        return;
      }

      chunks.push(chunk);
    });
    child.stderr.on('data', () => {});
    child.stdout.on('error', () => end('InputRejected'));
    child.stderr.on('error', () => {});
    child.stdin.on('error', () => end('InputRejected'));

    // One deadline from here: it is already running while the input is written, so blocked input cannot outlive it.
    timer = setTimeout(() => end('TimedOut'), timeoutMs);
    try {
      child.stdin.end(input);
    } catch {
      end('InputRejected');
    }
  });
}
