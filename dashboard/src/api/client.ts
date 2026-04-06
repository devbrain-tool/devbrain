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

export interface Briefing {
  id: string;
  generatedAt: string;
  content: string;
  project?: string;
}

export interface AgentInfo {
  name: string;
  enabled: boolean;
  lastRun?: string;
  status: string;
}

export interface Settings {
  daemon: Record<string, unknown>;
  capture: Record<string, unknown>;
  storage: Record<string, unknown>;
  llm: Record<string, unknown>;
  agents: Record<string, unknown>;
}

// --- Fetch helpers ---

async function fetchJson<T>(path: string): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`);
  if (!res.ok) {
    throw new Error(`API error ${res.status}: ${res.statusText}`);
  }
  return res.json() as Promise<T>;
}

async function postJson<T>(path: string, body?: unknown): Promise<T> {
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

  briefingLatest: () => fetchJson<Briefing>('/briefings/latest'),

  briefings: () => fetchJson<Briefing[]>('/briefings'),

  briefingGenerate: () => postJson<Briefing>('/briefings/generate'),

  agents: () => fetchJson<AgentInfo[]>('/agents'),

  agentRun: (name: string) => postJson<AgentInfo>(`/agents/${encodeURIComponent(name)}/run`),

  settings: () => fetchJson<Settings>('/settings'),
};
