#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

// The Rust shell owns only: native window lifecycle, starting and observing the packaged
// .NET sidecar, per-launch bootstrap generation and transfer, sidecar shutdown/process
// ownership, and one narrowly scoped command that returns the in-memory launch session to
// the bundled WebView. It never owns workflow policy, persistence, Git, agents, GitHub, or
// domain decisions — all of that lives in the .NET host. See
// docs/architecture/system-overview.md for the full bootstrap contract this implements.

use rand::RngCore;
use serde::Serialize;
use std::sync::Mutex;
use std::time::Duration;
use tauri::{AppHandle, Emitter, Manager, RunEvent, State};
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;
use tokio::sync::watch;
use tokio::time::{timeout_at, Instant};

const SIDECAR_NAME: &str = "devalcopilot-api";
// The .NET host only reads the stdin bootstrap frame when launched with this exact,
// non-secret flag — never a shell string, never anything derived from the secret.
const SIDECAR_BOOTSTRAP_ARG: &str = "--bootstrap-stdin";
const READY_MARKER: &str = "DEVALCOPILOT_SIDECAR_READY ";
const SIDECAR_DISCONNECTED_EVENT: &str = "devalcopilot://sidecar-disconnected";
const BOOTSTRAP_VERSION: u32 = 1;
const READY_TIMEOUT: Duration = Duration::from_secs(30);

#[derive(Clone, Serialize)]
struct LaunchSession {
    #[serde(rename = "baseUrl")]
    base_url: String,
    secret: String,
}

/// The sidecar's observed lifecycle from the shell's point of view. A previous session can
/// never be reused: once `Exited`, this never returns to `Ready` for the same launch.
///
/// Carried on a `tokio::sync::watch` channel rather than a `Mutex` + `Notify` pair: a
/// waiter that subscribes after a state change still observes it (`watch::Receiver` always
/// reflects the latest sent value, and `changed()` returns immediately if the value moved
/// since this receiver last looked), so there is no gap between checking the current state
/// and starting to wait in which a transition could be missed. `Notify::notify_waiters`
/// does not have this property: it wakes only whichever waiters were already registered at
/// the moment it is called, which is exactly the lost-wakeup this replaces.
#[derive(Clone)]
enum SidecarState {
    Starting,
    Ready(LaunchSession),
    Exited,
}

struct AppState {
    sidecar: watch::Sender<SidecarState>,
    // Held for the app's lifetime so the sidecar's stdin pipe stays open (the shell's
    // liveness signal to the host) and so an explicit shutdown can kill the owned process.
    child: Mutex<Option<CommandChild>>,
}

/// One incoming fact about the sidecar process, decoupled from `tauri_plugin_shell`'s
/// `CommandEvent` (and from "the event stream ended" having no event of its own) so the
/// lifecycle reducer below can be unit-tested without a running app.
enum SidecarFact {
    BecameReady(LaunchSession),
    Terminated,
}

/// Pure reducer for the sidecar's lifecycle. A plain `match` on `CommandEvent` is not enough
/// to make `Exited` truly terminal: nothing stops a late `Stdout` ready line — arriving after
/// `Terminated` due to ordinary channel/OS scheduling — from sending `Ready` right back onto
/// the watch channel, and nothing invalidates a `Ready` session if the event stream simply
/// ends without an explicit `Terminated`/`Error` event. This type owns both invariants in one
/// place instead of scattering them across the event loop:
///
/// - once `state` is `Exited`, no later fact can move it anywhere else;
/// - a disconnect notification is owed exactly once, and only for a sidecar that was actually
///   `Ready` at the moment it became terminal — never for one that never got ready, and never
///   twice for the same termination.
struct SidecarLifecycle {
    state: SidecarState,
    disconnect_emitted: bool,
}

impl SidecarLifecycle {
    fn new() -> Self {
        Self {
            state: SidecarState::Starting,
            disconnect_emitted: false,
        }
    }

    /// Applies one fact and returns whether this call is the one that owes a disconnect
    /// notification.
    fn apply(&mut self, fact: SidecarFact) -> bool {
        let was_ready = matches!(self.state, SidecarState::Ready(_));

        self.state = match self.state {
            SidecarState::Exited => SidecarState::Exited,
            _ => match fact {
                SidecarFact::BecameReady(session) => SidecarState::Ready(session),
                SidecarFact::Terminated => SidecarState::Exited,
            },
        };

        let just_terminated = matches!(self.state, SidecarState::Exited);
        if was_ready && just_terminated && !self.disconnect_emitted {
            self.disconnect_emitted = true;
            true
        } else {
            false
        }
    }

    /// The event stream ended with no further events coming, ever — equivalent to a
    /// `Terminated` fact for lifecycle purposes regardless of whether the sidecar was still
    /// `Starting` or already `Ready`.
    fn apply_stream_end(&mut self) -> bool {
        self.apply(SidecarFact::Terminated)
    }
}

fn generate_secret() -> String {
    // A CSPRNG, per the platform cryptography standard — never the non-cryptographic
    // `rand::thread_rng()` default for a security-relevant token. 32 bytes matches the
    // 64-hex-character secret the .NET bootstrap reader requires.
    let mut bytes = [0u8; 32];
    rand::rngs::OsRng.fill_bytes(&mut bytes);
    bytes.iter().map(|byte| format!("{byte:02x}")).collect()
}

/// Waits for `Ready` or `Exited` on a state channel, or gives up at `deadline`. Kept free of
/// any Tauri type so it can be unit-tested with a plain `watch::channel` and no app context.
async fn wait_for_session(
    mut sidecar: watch::Receiver<SidecarState>,
    deadline: Instant,
) -> Option<LaunchSession> {
    loop {
        {
            let state = sidecar.borrow();
            match &*state {
                SidecarState::Ready(session) => return Some(session.clone()),
                SidecarState::Exited => return None,
                SidecarState::Starting => {}
            }
        }

        if timeout_at(deadline, sidecar.changed()).await.is_err() {
            return None;
        }
    }
}

/// The one narrowly scoped command exposed to the bundled WebView. Waits for the sidecar to
/// become ready (or fail) rather than requiring React to poll, and never echoes anything
/// once the sidecar has already exited — the caller gets `None` and must not retry with a
/// stale secret.
#[tauri::command]
async fn get_launch_session(state: State<'_, AppState>) -> Result<Option<LaunchSession>, ()> {
    let deadline = Instant::now() + READY_TIMEOUT;
    Ok(wait_for_session(state.sidecar.subscribe(), deadline).await)
}

fn spawn_sidecar(app: &AppHandle) -> Result<(), tauri_plugin_shell::Error> {
    let secret = generate_secret();
    // Exactly one versioned frame, newline-terminated, written once. Never argv, never an
    // environment variable, never a file: the shell's stdin pipe to the child is the only
    // channel, and it stays open afterward with no further writes as the liveness signal.
    let frame = format!("{{\"version\":{BOOTSTRAP_VERSION},\"secret\":\"{secret}\"}}\n");

    let (mut events, mut child) = app
        .shell()
        .sidecar(SIDECAR_NAME)?
        .args([SIDECAR_BOOTSTRAP_ARG])
        .spawn()?;
    child.write(frame.as_bytes())?;

    let state = app.state::<AppState>();
    *state.child.lock().unwrap() = Some(child);
    let sidecar_sender = state.sidecar.clone();

    let app_handle = app.clone();
    tauri::async_runtime::spawn(async move {
        let mut lifecycle = SidecarLifecycle::new();

        while let Some(event) = events.recv().await {
            match event {
                CommandEvent::Stdout(bytes) => {
                    let Ok(line) = String::from_utf8(bytes) else {
                        continue;
                    };
                    let Some(payload) = line.strip_prefix(READY_MARKER) else {
                        // Ordinary host log output, not the readiness protocol — ignored.
                        continue;
                    };
                    let Ok(parsed) = serde_json::from_str::<serde_json::Value>(payload) else {
                        continue;
                    };
                    let Some(port) = parsed.get("port").and_then(|value| value.as_u64()) else {
                        continue;
                    };

                    let session = LaunchSession {
                        base_url: format!("http://127.0.0.1:{port}"),
                        secret: secret.clone(),
                    };

                    // A no-op once `lifecycle.state` is already `Exited`: a late ready line
                    // racing behind an already-processed Terminated/Error event must never
                    // resurrect a stale session.
                    lifecycle.apply(SidecarFact::BecameReady(session));
                    let _ = sidecar_sender.send(lifecycle.state.clone());
                }
                CommandEvent::Terminated(_) | CommandEvent::Error(_) => {
                    let owes_disconnect = lifecycle.apply(SidecarFact::Terminated);
                    let _ = sidecar_sender.send(lifecycle.state.clone());

                    if owes_disconnect {
                        // The host exited first: discard the retained session and let the
                        // WebView present a disconnected/recovery state. A previous session
                        // is never reused.
                        let _ = app_handle.emit(SIDECAR_DISCONNECTED_EVENT, ());
                    }
                }
                CommandEvent::Stderr(_) => {
                    // Ordinary diagnostics (the host redirects its own logs here in
                    // bootstrap mode); never part of the readiness protocol.
                }
                _ => {}
            }
        }

        // The event stream ended without an explicit Terminated/Error event. Still never
        // leave a waiting `get_launch_session` call blocked forever, whether the sidecar was
        // still `Starting` or was `Ready` and disappeared without a clean termination event —
        // and still owes exactly one disconnect notification if it had been ready.
        let owes_disconnect = lifecycle.apply_stream_end();
        let _ = sidecar_sender.send(lifecycle.state.clone());
        if owes_disconnect {
            let _ = app_handle.emit(SIDECAR_DISCONNECTED_EVENT, ());
        }
    });

    Ok(())
}

fn main() {
    let (sidecar_sender, _) = watch::channel(SidecarState::Starting);

    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(AppState {
            sidecar: sidecar_sender,
            child: Mutex::new(None),
        })
        .invoke_handler(tauri::generate_handler![get_launch_session])
        .setup(|app| {
            spawn_sidecar(app.handle())?;
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building the DevalCopilot application")
        .run(|app_handle, event| {
            // Explicit shell shutdown terminates the owned sidecar process: on this single-
            // process sidecar (no further children spawned in this phase) killing the direct
            // child is the whole process tree.
            if let RunEvent::Exit = event {
                let state = app_handle.state::<AppState>();
                let child = state.child.lock().unwrap().take();
                if let Some(child) = child {
                    let _ = child.kill();
                }
            }
        });
}

#[cfg(test)]
mod tests {
    use super::*;

    fn far_future_deadline() -> Instant {
        Instant::now() + Duration::from_secs(60)
    }

    fn fake_session(tag: &str) -> LaunchSession {
        LaunchSession {
            base_url: format!("http://127.0.0.1:0/{tag}"),
            secret: tag.to_string(),
        }
    }

    #[tokio::test]
    async fn returns_ready_immediately_when_already_ready_before_the_wait_begins() {
        let (_tx, rx) = watch::channel(SidecarState::Ready(fake_session("a")));

        let result = wait_for_session(rx, far_future_deadline()).await;

        assert_eq!(result.map(|session| session.secret), Some("a".to_string()));
    }

    // The lost-wakeup regression test: yielding once lets the spawned task run its first
    // iteration (borrow sees `Starting`, then it parks inside `changed().await`) before this
    // task sends the update. With the old Mutex + Notify design, a send landing in that
    // exact gap — after the check, before the waiter was registered — would never wake the
    // waiter. `watch::Receiver::changed()` cannot miss it: it compares against this
    // receiver's own last-seen version, which the send has already moved past.
    #[tokio::test]
    async fn observes_ready_sent_while_the_call_is_already_waiting() {
        let (tx, rx) = watch::channel(SidecarState::Starting);

        let waiter = tokio::spawn(wait_for_session(rx, far_future_deadline()));
        tokio::task::yield_now().await;
        tx.send(SidecarState::Ready(fake_session("b"))).unwrap();

        let result = waiter.await.unwrap();

        assert_eq!(result.map(|session| session.secret), Some("b".to_string()));
    }

    #[tokio::test]
    async fn returns_none_immediately_when_already_exited_before_the_wait_begins() {
        let (_tx, rx) = watch::channel(SidecarState::Exited);

        let result = wait_for_session(rx, far_future_deadline()).await;

        assert!(result.is_none());
    }

    #[tokio::test]
    async fn returns_none_when_exited_is_sent_while_the_call_is_already_waiting() {
        let (tx, rx) = watch::channel(SidecarState::Starting);

        let waiter = tokio::spawn(wait_for_session(rx, far_future_deadline()));
        tokio::task::yield_now().await;
        tx.send(SidecarState::Exited).unwrap();

        let result = waiter.await.unwrap();

        assert!(result.is_none());
    }

    #[tokio::test(start_paused = true)]
    async fn times_out_while_genuinely_still_starting() {
        let (_tx, rx) = watch::channel(SidecarState::Starting);
        let deadline = Instant::now() + Duration::from_secs(5);

        let result = wait_for_session(rx, deadline).await;

        assert!(result.is_none());
    }

    #[tokio::test]
    async fn never_returns_a_previous_ready_session_once_exited() {
        let (tx, _rx) = watch::channel(SidecarState::Ready(fake_session("c")));
        tx.send(SidecarState::Exited).unwrap();

        // A late subscriber — analogous to a second `get_launch_session` call arriving
        // after the sidecar has already exited — must see `Exited`, never the stale
        // `Ready` value that preceded it on the channel.
        let late_subscriber = tx.subscribe();
        let result = wait_for_session(late_subscriber, far_future_deadline()).await;

        assert!(result.is_none());
    }

    #[test]
    fn late_ready_fact_after_exit_does_not_resurrect_the_session() {
        let mut lifecycle = SidecarLifecycle::new();
        lifecycle.apply(SidecarFact::Terminated);
        assert!(matches!(lifecycle.state, SidecarState::Exited));

        let owes_disconnect = lifecycle.apply(SidecarFact::BecameReady(fake_session("late")));

        assert!(matches!(lifecycle.state, SidecarState::Exited));
        assert!(!owes_disconnect);
    }

    #[test]
    fn stream_end_after_ready_without_an_explicit_terminated_event_invalidates_the_session_and_emits_once(
    ) {
        let mut lifecycle = SidecarLifecycle::new();
        lifecycle.apply(SidecarFact::BecameReady(fake_session("a")));

        let owes_disconnect = lifecycle.apply_stream_end();

        assert!(matches!(lifecycle.state, SidecarState::Exited));
        assert!(owes_disconnect);

        // A second stream-end-shaped call for the same lifecycle (e.g. a duplicate signal)
        // must not emit the disconnect notification twice.
        let owes_disconnect_again = lifecycle.apply_stream_end();
        assert!(!owes_disconnect_again);
    }

    #[test]
    fn stream_end_while_still_starting_invalidates_without_owing_a_disconnect() {
        let mut lifecycle = SidecarLifecycle::new();

        let owes_disconnect = lifecycle.apply_stream_end();

        assert!(matches!(lifecycle.state, SidecarState::Exited));
        assert!(!owes_disconnect);
    }

    #[test]
    fn terminated_fact_after_ready_owes_a_disconnect_exactly_once_even_if_applied_twice() {
        let mut lifecycle = SidecarLifecycle::new();
        lifecycle.apply(SidecarFact::BecameReady(fake_session("b")));

        let first = lifecycle.apply(SidecarFact::Terminated);
        let second = lifecycle.apply(SidecarFact::Terminated);

        assert!(first);
        assert!(!second);
    }
}
