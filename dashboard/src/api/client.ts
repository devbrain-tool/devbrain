const BASE_URL = '/api/v1';

// --- TypeScript interfaces matching C# models ---

export interface Observation {
  id: string;
  sessionId: string;
  threadId?: string;
  parentId?: string;
  timestamp: string;
  project: string;
  branch?: string;
  eventType: string;
  source: string;
  rawContent: string;
  summary?: string;
  tags: string[];
  filesInvolved: string[];
  createdAt: string;
}

export interface StorageHealth {
  sqliteSizeMb: number;
  lanceDbSizeMb: number;
  totalObservations: number;
}

export interface AgentHealth {
  lastRun?: string;
  status: string;
}

export interface LlmProviderHealth {
  status: string;
  model?: string;
  queueDepth?: number;
  requestsToday?: number;
  limit?: number;
}

export interface LlmHealth {
  local: LlmProviderHealth;
  cloud: LlmProviderHealth;
}

export interface HealthStatus {
  status: string;
  uptimeSeconds: number;
  storage: StorageHealth;
  agents: Record<string, AgentHealth>;
  llm: LlmHealth;
}

// Briefing list endpoint returns string[] (filenames)
// Briefing latest returns { file, content }
export interface BriefingLatest {
  file: string;
  content: string;
}

// Agent endpoint returns { name, schedule, priority, lastRun, status }
export interface AgentInfo {
  name: string;
  schedule: string;
  priority: string;
  lastRun?: string;
  status: string;
}

// Settings - typed to match C# Settings class
export interface DaemonSettings {
  port: number;
  logLevel: string;
  autoStart: boolean;
  dataPath: string;
}

export interface CaptureSettings {
  enabled: boolean;
  sources: string[];
  privacyMode: string;
  ignoredProjects: string[];
  maxObservationSizeKb: number;
  threadGapHours: number;
}

export interface StorageSettings {
  sqliteMaxSizeMb: number;
  vectorDimensions: number;
  compressionAfterDays: number;
  retentionDays: number;
}

export interface LocalLlmSettings {
  enabled: boolean;
  provider: string;
  model: string;
  endpoint: string;
  maxConcurrent: number;
}

export interface CloudLlmSettings {
  enabled: boolean;
  provider: string;
  model: string;
  apiKeyEnv: string;
  maxDailyRequests: number;
  tasks: string[];
}

export interface LlmSettings {
  local: LocalLlmSettings;
  cloud: CloudLlmSettings;
}

export interface BriefingAgentSettings {
  enabled: boolean;
  schedule: string;
  timezone: string;
}

export interface DeadEndAgentSettings {
  enabled: boolean;
  sensitivity: string;
}

export interface LinkerAgentSettings {
  enabled: boolean;
  debounceSeconds: number;
}

export interface CompressionAgentSettings {
  enabled: boolean;
  idleMinutes: number;
}

export interface PatternAgentSettings {
  enabled: boolean;
  idleMinutes: number;
  lookbackDays: number;
}

export interface AgentSettingsGroup {
  briefing: BriefingAgentSettings;
  deadEnd: DeadEndAgentSettings;
  linker: LinkerAgentSettings;
  compression: CompressionAgentSettings;
  pattern: PatternAgentSettings;
}

export interface Settings {
  daemon: DaemonSettings;
  capture: CaptureSettings;
  storage: StorageSettings;
  llm: LlmSettings;
  agents: AgentSettingsGroup;
}

// Thread model matching C# DevBrainThread
export type ThreadState = 'Active' | 'Paused' | 'Closed' | 'Archived';

export interface DevBrainThread {
  id: string;
  project: string;
  branch?: string;
  title?: string;
  state: ThreadState;
  startedAt: string;
  lastActivity: string;
  observationCount: number;
  summary?: string;
  createdAt: string;
}

// DeadEnd model matching C# DeadEnd
export interface DeadEnd {
  id: string;
  threadId?: string;
  project: string;
  description: string;
  approach: string;
  reason: string;
  filesInvolved: string[];
  detectedAt: string;
  createdAt: string;
}

// DejaVuAlert model
export interface DejaVuAlert {
  id: string;
  threadId: string;
  matchedDeadEndId: string;
  confidence: number;
  message: string;
  strategy: string;
  dismissed: boolean;
  createdAt: string;
}

// BlastRadius model
export interface BlastRadiusEntry {
  filePath: string;
  riskScore: number;
  chainLength: number;
  reason: string;
  decisionChain: string[];
}

export interface BlastRadius {
  sourceFile: string;
  affectedFiles: BlastRadiusEntry[];
  deadEndsAtRisk: string[];
  summary?: string;
  generatedAt: string;
}

// DecisionChain model
export interface DecisionStep {
  observationId: string;
  summary: string;
  timestamp: string;
  stepType: string;
  filesInvolved: string[];
}

export interface DecisionChain {
  id: string;
  rootNodeId: string;
  narrative: string;
  steps: DecisionStep[];
  generatedAt: string;
}

// SessionSummary model
export interface SessionSummary {
  id: string;
  sessionId: string;
  narrative: string;
  outcome: string;
  duration: string;
  observationCount: number;
  filesTouched: number;
  deadEndsHit: number;
  phases: string[];
  createdAt: string;
}

// Database explorer types
export interface DbTableInfo {
  name: string;
  rowCount: number;
}

export interface DbColumnInfo {
  name: string;
  type: string;
  primaryKey: boolean;
  nullable: boolean;
}

export interface DbTableDetail {
  name: string;
  rowCount: number;
  columns: DbColumnInfo[];
  indexes: string[];
}

export interface DbQueryResult {
  columns: string[];
  rows: (string | number | null)[][];
  rowCount: number;
  executionMs: number;
}

// Setup validation types
export interface SetupCheckResult {
  id: string;
  category: string;
  name: string;
  status: 'pass' | 'fail' | 'warn' | 'skip';
  detail: string;
  fixable: boolean;
}

export interface SetupStatusSummary {
  pass: number;
  fail: number;
  warn: number;
  skip: number;
}

export interface SetupStatus {
  checks: SetupCheckResult[];
  summary: SetupStatusSummary;
}

export interface SetupFixResult {
  success: boolean;
  detail: string;
}

// Context response
export interface FileContext {
  path: string;
  observations: Observation[];
  threads: DevBrainThread[];
  deadEnds: DeadEnd[];
}

// --- Fetch helpers ---

async function fetchJson<T>(path: string): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`);
  if (!res.ok) {
    throw new Error(`API error ${res.status}: ${res.statusText}`);
  }
  return res.json() as Promise<T>;
}

export async function postJson<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: body != null ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) {
    throw new Error(`API error ${res.status}: ${res.statusText}`);
  }
  return res.json() as Promise<T>;
}

// --- API client ---

export const api = {
  health: () => fetchJson<HealthStatus>('/health'),

  observations: (params?: { project?: string; limit?: number; offset?: number }) => {
    const sp = new URLSearchParams();
    if (params?.project) sp.set('project', params.project);
    if (params?.limit) sp.set('limit', String(params.limit));
    if (params?.offset) sp.set('offset', String(params.offset));
    const qs = sp.toString();
    return fetchJson<Observation[]>(`/observations${qs ? `?${qs}` : ''}`);
  },

  search: (q: string, limit = 20) =>
    fetchJson<Observation[]>(`/search?q=${encodeURIComponent(q)}&limit=${limit}`),

  searchExact: (q: string, limit = 20) =>
    fetchJson<Observation[]>(`/search/exact?q=${encodeURIComponent(q)}&limit=${limit}`),

  // Briefings list returns string[] (filenames like "2026-04-05.md")
  briefings: () => fetchJson<string[]>('/briefings'),

  // Latest briefing returns { file, content }
  briefingLatest: () => fetchJson<BriefingLatest>('/briefings/latest'),

  // Generate fires-and-forgets, returns 202 Accepted (no body)
  briefingGenerate: async () => {
    const res = await fetch(`${BASE_URL}/briefings/generate`, { method: 'POST' });
    if (!res.ok) {
      throw new Error(`API error ${res.status}: ${res.statusText}`);
    }
  },

  agents: () => fetchJson<AgentInfo[]>('/agents'),

  agentRun: async (name: string) => {
    const res = await fetch(`${BASE_URL}/agents/${encodeURIComponent(name)}/run`, {
      method: 'POST',
    });
    if (!res.ok) {
      throw new Error(`API error ${res.status}: ${res.statusText}`);
    }
  },

  settings: () => fetchJson<Settings>('/settings'),

  // Threads
  threads: () => fetchJson<DevBrainThread[]>('/threads'),

  thread: (id: string) => fetchJson<DevBrainThread & { observations: Observation[] }>(
    `/threads/${encodeURIComponent(id)}`
  ),

  // Dead ends
  deadEnds: (params?: { project?: string }) => {
    const sp = new URLSearchParams();
    if (params?.project) sp.set('project', params.project);
    const qs = sp.toString();
    return fetchJson<DeadEnd[]>(`/dead-ends${qs ? `?${qs}` : ''}`);
  },

  // Alerts
  alerts: () => fetchJson<DejaVuAlert[]>('/alerts'),

  alertsAll: () => fetchJson<DejaVuAlert[]>('/alerts/all'),

  alertDismiss: async (id: string) => {
    const res = await fetch(`${BASE_URL}/alerts/${encodeURIComponent(id)}/dismiss`, {
      method: 'POST',
    });
    if (!res.ok) throw new Error(`API error ${res.status}: ${res.statusText}`);
  },

  // Blast Radius
  blastRadius: (path: string, hops = 3) =>
    fetchJson<BlastRadius>(`/blast-radius/${encodeURIComponent(path)}?hops=${hops}`),

  // Decision Replay
  replayFile: (path: string) =>
    fetchJson<DecisionChain>(`/replay/file/${encodeURIComponent(path)}`),

  replayDecision: (nodeId: string) =>
    fetchJson<DecisionChain>(`/replay/decision/${encodeURIComponent(nodeId)}`),

  // Sessions
  sessions: (limit = 50) =>
    fetchJson<SessionSummary[]>(`/sessions?limit=${limit}`),

  sessionStory: (id: string) =>
    fetchJson<SessionSummary>(`/sessions/${encodeURIComponent(id)}/story`),

  // Context
  fileContext: (path: string) =>
    fetchJson<FileContext>(`/context/file/${encodeURIComponent(path)}`),

  // Database explorer
  db: {
    tables: () => fetchJson<DbTableInfo[]>('/db/tables'),

    table: (name: string) => fetchJson<DbTableDetail>(`/db/tables/${encodeURIComponent(name)}`),

    query: async (sql: string) => {
      const res = await fetch(`${BASE_URL}/db/query`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sql }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.error ?? `API error ${res.status}: ${res.statusText}`);
      }
      return res.json() as Promise<DbQueryResult>;
    },
  },

  // Setup validation
  setup: {
    status: () => fetchJson<SetupStatus>('/setup/status'),

    fix: async (checkId: string) => {
      const res = await fetch(`${BASE_URL}/setup/fix/${encodeURIComponent(checkId)}`, {
        method: 'POST',
      });
      const body = await res.json() as SetupFixResult;
      if (!res.ok) throw new Error(body.detail ?? `API error ${res.status}`);
      return body;
    },
  },
};
