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
// ADR-0072: the one closed startup-failure frame the host writes on the same stdout protocol channel before it exits nonzero.
const FAILED_MARKER: &str = "DEVALCOPILOT_SIDECAR_FAILED ";
const STARTUP_FAILURE_VERSION: u64 = 1;
// Every valid protocol line is far shorter; anything longer is never parsed.
const MAX_PROTOCOL_LINE_BYTES: usize = 512;
const SIDECAR_DISCONNECTED_EVENT: &str = "devalcopilot://sidecar-disconnected";
const BOOTSTRAP_VERSION: u32 = 1;
const READY_TIMEOUT: Duration = Duration::from_secs(30);

// The closed values the host may report (ADR-0072); anything else makes the whole line malformed.
const STARTUP_STAGES: &[&str] = &[
    "StorageSchema",
    "LocalExecutionSequences",
    "ProcessAttemptOutput",
    "ProcessAttempts",
    "CapabilityCatalog",
    "CapabilityProbes",
    "LocalCommitOperations",
    "Workspaces",
    "VerificationOutput",
    "VerificationExecutions",
    "AgentAttemptOutput",
    "AgentAttempts",
    "ImplementationAttempts",
    "PlanningSequences",
    "AgentSupervisions",
];
const STARTUP_REASONS: &[&str] = &["StorageUnavailable", "StateIncoherent", "Unclassified"];
const STARTUP_WARNINGS: &[&str] = &[
    "OptionalOutputDeferred",
    "RedundantCleanupDeferred",
    "SupervisionObservationsUnclosed",
];

#[derive(Clone, Serialize)]
struct LaunchSession {
    #[serde(rename = "baseUrl")]
    base_url: String,
    secret: String,
    /// ADR-0072: the closed, nonfatal startup warning categories the host reported with readiness.
    #[serde(rename = "startupWarnings")]
    startup_warnings: Vec<String>,
}

/// ADR-0072: the validated closed diagnosis of a fatal host startup failure. Only these three closed names ever reach the WebView.
#[derive(Clone, Debug, PartialEq, Serialize)]
struct StartupFailure {
    stage: String,
    reason: String,
    action: String,
}

/// The error `get_launch_session` returns for a launch whose host reported a fatal startup failure.
#[derive(Clone, Debug, PartialEq, Serialize)]
struct LaunchError {
    kind: &'static str,
    stage: String,
    reason: String,
    action: String,
}

impl From<StartupFailure> for LaunchError {
    fn from(failure: StartupFailure) -> Self {
        Self {
            kind: "startup-failed",
            stage: failure.stage,
            reason: failure.reason,
            action: failure.action,
        }
    }
}

/// One stdout protocol line, already bounded and validated; anything else is ignored as ordinary output.
#[derive(Debug, PartialEq)]
enum ProtocolLine {
    Ready { port: u16, warnings: Vec<String> },
    Failed(StartupFailure),
    Ignored,
}

/// Pure, bounded parser of one stdout line. A line longer than the bound, not UTF-8, without a known prefix, with malformed JSON,
/// an unexpected key, a wrong version, an unknown closed value or an action that does not follow from the reason is ignored, so a
/// fragment of a frame split across events can never be read as a frame.
fn parse_protocol_line(bytes: &[u8]) -> ProtocolLine {
    if bytes.len() > MAX_PROTOCOL_LINE_BYTES {
        return ProtocolLine::Ignored;
    }
    let Ok(text) = std::str::from_utf8(bytes) else {
        return ProtocolLine::Ignored;
    };
    let line = text.trim_end_matches(['\r', '\n']);
    if let Some(payload) = line.strip_prefix(READY_MARKER) {
        return parse_ready(payload).unwrap_or(ProtocolLine::Ignored);
    }
    if let Some(payload) = line.strip_prefix(FAILED_MARKER) {
        return parse_failure(payload)
            .map(ProtocolLine::Failed)
            .unwrap_or(ProtocolLine::Ignored);
    }
    ProtocolLine::Ignored
}

fn parse_ready(payload: &str) -> Option<ProtocolLine> {
    let parsed = serde_json::from_str::<serde_json::Value>(payload).ok()?;
    let object = parsed.as_object()?;
    if object.keys().any(|key| key != "port" && key != "warnings") {
        return None;
    }
    let port = u16::try_from(object.get("port")?.as_u64()?)
        .ok()
        .filter(|port| *port != 0)?;
    let mut warnings = Vec::new();
    if let Some(value) = object.get("warnings") {
        for item in value.as_array()? {
            let name = item.as_str()?;
            if !STARTUP_WARNINGS.contains(&name)
                || warnings.iter().any(|known: &String| known == name)
            {
                return None;
            }
            warnings.push(name.to_string());
        }
    }
    Some(ProtocolLine::Ready { port, warnings })
}

fn parse_failure(payload: &str) -> Option<StartupFailure> {
    let parsed = serde_json::from_str::<serde_json::Value>(payload).ok()?;
    let object = parsed.as_object()?;
    if object.len() != 4 || object.get("version")?.as_u64()? != STARTUP_FAILURE_VERSION {
        return None;
    }
    let stage = object.get("stage")?.as_str()?;
    let reason = object.get("reason")?.as_str()?;
    let action = object.get("action")?.as_str()?;
    let expected_action = if reason == "StorageUnavailable" {
        "RestoreStorageAccess"
    } else {
        "RequestReviewedRepair"
    };
    if !STARTUP_STAGES.contains(&stage)
        || !STARTUP_REASONS.contains(&reason)
        || action != expected_action
    {
        return None;
    }
    Some(StartupFailure {
        stage: stage.to_string(),
        reason: reason.to_string(),
        action: action.to_string(),
    })
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
    /// ADR-0072: the host reported a fatal startup failure. Terminal like `Exited`: a late ready line never restores a session.
    Failed(StartupFailure),
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
    StartupFailed(StartupFailure),
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

        self.state = match &self.state {
            SidecarState::Exited => SidecarState::Exited,
            SidecarState::Failed(failure) => SidecarState::Failed(failure.clone()),
            _ => match fact {
                SidecarFact::BecameReady(session) => SidecarState::Ready(session),
                // A failure frame is only a startup fact; after readiness it can only invalidate the session.
                SidecarFact::StartupFailed(failure) if !was_ready => SidecarState::Failed(failure),
                SidecarFact::StartupFailed(_) | SidecarFact::Terminated => SidecarState::Exited,
            },
        };

        let just_terminated = matches!(self.state, SidecarState::Exited | SidecarState::Failed(_));
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
) -> Result<Option<LaunchSession>, StartupFailure> {
    loop {
        {
            let state = sidecar.borrow();
            match &*state {
                SidecarState::Ready(session) => return Ok(Some(session.clone())),
                SidecarState::Exited => return Ok(None),
                SidecarState::Failed(failure) => return Err(failure.clone()),
                SidecarState::Starting => {}
            }
        }

        if timeout_at(deadline, sidecar.changed()).await.is_err() {
            return Ok(None);
        }
    }
}

/// The one narrowly scoped command exposed to the bundled WebView. Waits for the sidecar to
/// become ready (or fail) rather than requiring React to poll, and never echoes anything
/// once the sidecar has already exited — the caller gets `None` and must not retry with a
/// stale secret. ADR-0072: a host that reported a fatal startup failure yields the closed
/// `startup-failed` error instead, with only its stage, reason and action names.
#[tauri::command]
async fn get_launch_session(
    state: State<'_, AppState>,
) -> Result<Option<LaunchSession>, LaunchError> {
    let deadline = Instant::now() + READY_TIMEOUT;
    wait_for_session(state.sidecar.subscribe(), deadline)
        .await
        .map_err(LaunchError::from)
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
                    // One event may carry more than one line; each is bounded and parsed alone.
                    for segment in bytes.split(|byte| *byte == b'\n') {
                        let fact = match parse_protocol_line(segment) {
                            ProtocolLine::Ready { port, warnings } => {
                                SidecarFact::BecameReady(LaunchSession {
                                    base_url: format!("http://127.0.0.1:{port}"),
                                    secret: secret.clone(),
                                    startup_warnings: warnings,
                                })
                            }
                            ProtocolLine::Failed(failure) => SidecarFact::StartupFailed(failure),
                            // Ordinary host output, a fragment or a malformed record — ignored.
                            ProtocolLine::Ignored => continue,
                        };

                        // A no-op once `lifecycle.state` is already `Exited` or `Failed`: a late
                        // ready line racing behind an already-processed termination or failure must
                        // never resurrect a stale session.
                        let owes_disconnect = lifecycle.apply(fact);
                        let _ = sidecar_sender.send(lifecycle.state.clone());
                        if owes_disconnect {
                            let _ = app_handle.emit(SIDECAR_DISCONNECTED_EVENT, ());
                        }
                    }
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
            startup_warnings: Vec::new(),
        }
    }

    #[tokio::test]
    async fn returns_ready_immediately_when_already_ready_before_the_wait_begins() {
        let (_tx, rx) = watch::channel(SidecarState::Ready(fake_session("a")));

        let result = wait_for_session(rx, far_future_deadline()).await;

        assert_eq!(
            result.unwrap().map(|session| session.secret),
            Some("a".to_string())
        );
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

        assert_eq!(
            result.unwrap().map(|session| session.secret),
            Some("b".to_string())
        );
    }

    #[tokio::test]
    async fn returns_none_immediately_when_already_exited_before_the_wait_begins() {
        let (_tx, rx) = watch::channel(SidecarState::Exited);

        let result = wait_for_session(rx, far_future_deadline()).await;

        assert!(matches!(result, Ok(None)));
    }

    #[tokio::test]
    async fn returns_none_when_exited_is_sent_while_the_call_is_already_waiting() {
        let (tx, rx) = watch::channel(SidecarState::Starting);

        let waiter = tokio::spawn(wait_for_session(rx, far_future_deadline()));
        tokio::task::yield_now().await;
        tx.send(SidecarState::Exited).unwrap();

        let result = waiter.await.unwrap();

        assert!(matches!(result, Ok(None)));
    }

    #[tokio::test(start_paused = true)]
    async fn times_out_while_genuinely_still_starting() {
        let (_tx, rx) = watch::channel(SidecarState::Starting);
        let deadline = Instant::now() + Duration::from_secs(5);

        let result = wait_for_session(rx, deadline).await;

        assert!(matches!(result, Ok(None)));
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

        assert!(matches!(result, Ok(None)));
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

    fn failure(stage: &str, reason: &str, action: &str) -> StartupFailure {
        StartupFailure {
            stage: stage.to_string(),
            reason: reason.to_string(),
            action: action.to_string(),
        }
    }

    #[test]
    fn a_valid_failure_frame_is_parsed_into_its_closed_names() {
        let line = br#"DEVALCOPILOT_SIDECAR_FAILED {"version":1,"stage":"StorageSchema","reason":"StorageUnavailable","action":"RestoreStorageAccess"}"#;

        assert_eq!(
            parse_protocol_line(line),
            ProtocolLine::Failed(failure(
                "StorageSchema",
                "StorageUnavailable",
                "RestoreStorageAccess"
            ))
        );
    }

    #[test]
    fn a_failure_frame_with_a_trailing_newline_is_still_one_frame() {
        let line = b"DEVALCOPILOT_SIDECAR_FAILED {\"version\":1,\"stage\":\"AgentAttempts\",\"reason\":\"StateIncoherent\",\"action\":\"RequestReviewedRepair\"}\r\n";

        assert_eq!(
            parse_protocol_line(line),
            ProtocolLine::Failed(failure(
                "AgentAttempts",
                "StateIncoherent",
                "RequestReviewedRepair"
            ))
        );
    }

    #[test]
    fn malformed_unknown_wrong_version_mismatched_extra_or_oversized_failure_frames_are_ignored() {
        let cases: Vec<Vec<u8>> = vec![
            br#"DEVALCOPILOT_SIDECAR_FAILED {"version":1,"stage":"StorageSchema","reason":"StorageUnavailable""#.to_vec(),
            br#"DEVALCOPILOT_SIDECAR_FAILED {"version":2,"stage":"StorageSchema","reason":"StorageUnavailable","action":"RestoreStorageAccess"}"#.to_vec(),
            br#"DEVALCOPILOT_SIDECAR_FAILED {"version":1,"stage":"Elsewhere","reason":"StorageUnavailable","action":"RestoreStorageAccess"}"#.to_vec(),
            br#"DEVALCOPILOT_SIDECAR_FAILED {"version":1,"stage":"StorageSchema","reason":"Disk on fire","action":"RestoreStorageAccess"}"#.to_vec(),
            br#"DEVALCOPILOT_SIDECAR_FAILED {"version":1,"stage":"StorageSchema","reason":"StorageUnavailable","action":"RequestReviewedRepair"}"#.to_vec(),
            br#"DEVALCOPILOT_SIDECAR_FAILED {"version":1,"stage":"StorageSchema","reason":"StateIncoherent","action":"RestoreStorageAccess"}"#.to_vec(),
            br#"DEVALCOPILOT_SIDECAR_FAILED {"version":1,"stage":"StorageSchema","reason":"StateIncoherent","action":"RequestReviewedRepair","path":"C:\x"}"#.to_vec(),
            br#"DEVALCOPILOT_SIDECAR_FAILED ["StorageSchema"]"#.to_vec(),
            [b"DEVALCOPILOT_SIDECAR_FAILED ".to_vec(), vec![b' '; MAX_PROTOCOL_LINE_BYTES]].concat(),
            vec![0xff, 0xfe, 0x00],
        ];

        for case in cases {
            assert_eq!(parse_protocol_line(&case), ProtocolLine::Ignored);
        }
    }

    #[test]
    fn a_failure_frame_split_across_two_events_is_never_read_as_a_frame() {
        let whole = br#"DEVALCOPILOT_SIDECAR_FAILED {"version":1,"stage":"Workspaces","reason":"StateIncoherent","action":"RequestReviewedRepair"}"#;
        let (first, second) = whole.split_at(40);

        assert_eq!(parse_protocol_line(first), ProtocolLine::Ignored);
        assert_eq!(parse_protocol_line(second), ProtocolLine::Ignored);
    }

    #[test]
    fn a_ready_line_carries_its_port_and_closed_warnings_and_rejects_unknown_ones() {
        assert_eq!(
            parse_protocol_line(br#"DEVALCOPILOT_SIDECAR_READY {"port":5123,"warnings":["OptionalOutputDeferred"]}"#),
            ProtocolLine::Ready {
                port: 5123,
                warnings: vec!["OptionalOutputDeferred".to_string()]
            }
        );
        assert_eq!(
            parse_protocol_line(br#"DEVALCOPILOT_SIDECAR_READY {"port":5123}"#),
            ProtocolLine::Ready {
                port: 5123,
                warnings: Vec::new()
            }
        );
        for rejected in [
            &br#"DEVALCOPILOT_SIDECAR_READY {"port":5123,"warnings":["Everything is fine"]}"#[..],
            &br#"DEVALCOPILOT_SIDECAR_READY {"port":5123,"warnings":["OptionalOutputDeferred","OptionalOutputDeferred"]}"#[..],
            &br#"DEVALCOPILOT_SIDECAR_READY {"port":70000}"#[..],
            &br#"DEVALCOPILOT_SIDECAR_READY {"port":5123,"secret":"x"}"#[..],
        ] {
            assert_eq!(parse_protocol_line(rejected), ProtocolLine::Ignored);
        }
    }

    #[test]
    fn a_startup_failure_is_terminal_and_a_late_ready_never_restores_a_session() {
        let mut lifecycle = SidecarLifecycle::new();

        let owes_disconnect = lifecycle.apply(SidecarFact::StartupFailed(failure(
            "Workspaces",
            "StateIncoherent",
            "RequestReviewedRepair",
        )));
        assert!(!owes_disconnect);
        assert!(matches!(lifecycle.state, SidecarState::Failed(_)));

        lifecycle.apply(SidecarFact::BecameReady(fake_session("late")));
        lifecycle.apply(SidecarFact::Terminated);
        lifecycle.apply_stream_end();

        assert!(
            matches!(&lifecycle.state, SidecarState::Failed(kept) if kept.stage == "Workspaces")
        );
    }

    #[test]
    fn a_failure_frame_after_readiness_only_invalidates_the_session() {
        let mut lifecycle = SidecarLifecycle::new();
        lifecycle.apply(SidecarFact::BecameReady(fake_session("a")));

        let owes_disconnect = lifecycle.apply(SidecarFact::StartupFailed(failure(
            "Workspaces",
            "StateIncoherent",
            "RequestReviewedRepair",
        )));

        assert!(owes_disconnect);
        assert!(matches!(lifecycle.state, SidecarState::Exited));
    }

    #[tokio::test]
    async fn waiting_for_a_session_reports_the_startup_failure() {
        let (tx, rx) = watch::channel(SidecarState::Starting);

        let waiter = tokio::spawn(wait_for_session(rx, far_future_deadline()));
        tokio::task::yield_now().await;
        tx.send(SidecarState::Failed(failure(
            "StorageSchema",
            "Unclassified",
            "RequestReviewedRepair",
        )))
        .unwrap();

        let result = waiter.await.unwrap();

        assert_eq!(
            result.err(),
            Some(failure(
                "StorageSchema",
                "Unclassified",
                "RequestReviewedRepair"
            ))
        );
        let error = LaunchError::from(failure(
            "StorageSchema",
            "Unclassified",
            "RequestReviewedRepair",
        ));
        assert_eq!(
            serde_json::to_string(&error).unwrap(),
            r#"{"kind":"startup-failed","stage":"StorageSchema","reason":"Unclassified","action":"RequestReviewedRepair"}"#
        );
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
