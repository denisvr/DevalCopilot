import * as signalR from '@microsoft/signalr'
import { getApiBaseUrl } from './httpClient'
import { getLaunchSession } from './session'

export interface RunAdvancedNotification {
  runId: string
  latestSequence: number
}

/**
 * LongPolling only: this lets the browser send the launch secret as a normal
 * Authorization header on every poll. WebSocket/SSE transports would force the
 * secret into an `access_token` query string, which this application never accepts.
 */
export function createRunNotificationConnection(): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${getApiBaseUrl()}/hubs/run`, {
      transport: signalR.HttpTransportType.LongPolling,
      accessTokenFactory: () => getLaunchSession()?.secret ?? '',
    })
    .withAutomaticReconnect()
    .build()
}
