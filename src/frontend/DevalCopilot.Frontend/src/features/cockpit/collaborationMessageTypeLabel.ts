/** Mirrors the backend's `CollaborationMessageType` enum with a readable label, so a card or
 * evidence entry never falls back to showing the raw enum identifier as its type. Shared by every
 * cockpit view that describes a message by its type (the timeline in `AgentCollaboration.tsx` and
 * the recorded-collaboration-input drill-down in `describeAttemptInputMessages.ts`), kept in its
 * own non-component module so importing it never affects either component's fast-refresh
 * boundary. */
const TYPE_LABEL: Record<string, string> = {
  Proposal: 'Proposal',
  Acceptance: 'Acceptance',
  Challenge: 'Challenge',
  Question: 'Question',
  Decision: 'Decision',
  ExecutionReport: 'Execution report',
  ReviewFinding: 'Review finding',
  RevisionResponse: 'Revision response',
  Escalation: 'Escalation',
  ReviewApproval: 'Review approval',
  HumanInstruction: 'Human instruction',
}

export function typeLabelFor(type: string): string {
  return TYPE_LABEL[type] ?? type
}
