import { useLocalDeliveryReceipt } from '../hooks/useLocalDeliveryReceipt'
import type { LocalDeliveryReceipt as Receipt, ReceiptSource } from '../localDeliveryReceipt'
import { toUtcText } from '../utcText'

const HISTORICAL_NOTE =
  'This is a historical record of the evidence this local commit was delivered against, read from what the host recorded. It is not the current workspace, branch or eligibility, and it does not show that these checks would pass now. Nothing was pushed, and the receipt says nothing about remote publication.'
const READING = 'Reading the local delivery receipt…'
const READ_FAILED = 'The local delivery receipt could not be read. The recorded local commit above is unchanged.'
const INCONSISTENT =
  'The local delivery receipt does not match this operation, so none of it is shown. The recorded local commit above is unchanged.'
const UNAVAILABLE =
  'A receipt cannot be reconstructed from the evidence recorded for this operation, so none is shown. The recorded local commit above is unchanged.'
const NO_SOURCE = 'This operation does not carry the facts needed to read its receipt, so none is shown.'

interface LocalDeliveryReceiptProps {
  /** The completed operation this receipt is read for; null when the operation lacks any identifying fact. */
  source: ReceiptSource | null
}

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </>
  )
}

function ReceiptDetails({ receipt }: { receipt: Receipt }) {
  return (
    <>
      <dl>
        <Fact label="Objective">{receipt.objective}</Fact>
        <Fact label="Local commit">{receipt.commitSha}</Fact>
        <Fact label="Parent">{receipt.parentCommitSha}</Fact>
        <Fact label="Tree">{receipt.treeSha}</Fact>
        <Fact label="Branch">{receipt.branchName}</Fact>
        <Fact label="Completed (UTC)">{toUtcText(receipt.completedAtUtc)}</Fact>
        <Fact label="Checkpoint">
          #{receipt.checkpoint.number} — {receipt.checkpoint.changedPathCount} changed paths
        </Fact>
        <Fact label="Checkpoint fingerprint">{receipt.checkpoint.fingerprintSha256}</Fact>
        <Fact label="Source execution report">{receipt.executionReportMessageId}</Fact>
        <Fact label="CodeReviewer approval">
          attempt #{receipt.codeReview.attemptNumber} ({receipt.codeReview.attemptId}), approval message {receipt.codeReview.approvalMessageId}
        </Fact>
        <Fact label="Human review">
          {receipt.humanReview.reviewId} — {receipt.humanReview.decision}
        </Fact>
      </dl>
      <p>Verification executions recorded for this delivery, in recorded order ({receipt.verification.length}):</p>
      <ol>
        {receipt.verification.map((member) => (
          <li key={member.executionId}>
            <strong>{member.commandName}</strong> — Passed, exit code 0 — execution #{member.executionNumber} ({member.executionId}), command{' '}
            {member.commandId}, completed {toUtcText(member.completedAtUtc)}
          </li>
        ))}
      </ol>
    </>
  )
}

/**
 * The recorded local-delivery receipt of a completed local commit (ADR-0032): what the host recorded when this delivery was
 * admitted and completed, read once for the committed operation. It is explicitly a historical, local-only record: loading, failed,
 * inconsistent, unavailable and valid states are distinct, and none of them offers a mutation or rewrites the operation's status.
 * Every value is rendered as text.
 */
export function LocalDeliveryReceipt({ source }: LocalDeliveryReceiptProps) {
  const { reading, loading, failure, reload } = useLocalDeliveryReceipt(source)

  let body
  if (source === null) {
    body = <p>{NO_SOURCE}</p>
  } else if (loading) {
    body = <p>{READING}</p>
  } else if (failure === 'read') {
    body = (
      <>
        <p role="alert">{READ_FAILED}</p>
        <button type="button" onClick={reload}>
          Read the receipt again
        </button>
      </>
    )
  } else if (failure === 'inconsistent' || reading === null) {
    body = <p role="alert">{INCONSISTENT}</p>
  } else if (reading.kind === 'Unavailable') {
    body = <p>{UNAVAILABLE}</p>
  } else {
    body = <ReceiptDetails receipt={reading.receipt} />
  }

  return (
    <section className="dc-local-delivery-receipt" aria-label="Local delivery receipt">
      <h4>Local delivery receipt</h4>
      <p className="dc-local-commit-note">{HISTORICAL_NOTE}</p>
      {body}
    </section>
  )
}
