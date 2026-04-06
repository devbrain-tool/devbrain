import { useEffect, useState } from 'react';
import { api, type HealthStatus } from '../api/client';

export default function Health() {
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.health().then(setHealth).catch((e) => setError(String(e)));
  }, []);

  if (error) return <div style={styles.error}>Error: {error}</div>;
  if (!health) return <div style={styles.loading}>Loading health status...</div>;

  const formatUptime = (s: number) => {
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    return `${h}h ${m}m`;
  };

  return (
    <div style={styles.container}>
      <h1>System Health</h1>

      <section style={styles.section}>
        <h2>Daemon</h2>
        <div style={styles.grid}>
          <StatusCard label="Status" value={health.status} isStatus />
          <StatusCard label="Uptime" value={formatUptime(health.uptimeSeconds)} />
          <StatusCard label="SQLite Size" value={`${health.storage.sqliteSizeMb} MB`} />
          <StatusCard label="Observations" value={String(health.storage.totalObservations)} />
        </div>
      </section>

      <section style={styles.section}>
        <h2>LLM Providers</h2>
        <div style={styles.grid}>
          <StatusCard
            label={`Local (Ollama)${health.llm.local.model ? ' - ' + health.llm.local.model : ''}`}
            value={health.llm.local.status}
            isStatus
          />
          <StatusCard
            label={`Cloud (Anthropic)${health.llm.cloud.model ? ' - ' + health.llm.cloud.model : ''}`}
            value={health.llm.cloud.status}
            isStatus
          />
        </div>
      </section>

      <section style={styles.section}>
        <h2>Agents</h2>
        <table style={styles.table}>
          <thead>
            <tr>
              <th style={styles.th}>Name</th>
              <th style={styles.th}>Last Run</th>
              <th style={styles.th}>Status</th>
            </tr>
          </thead>
          <tbody>
            {Object.entries(health.agents).map(([name, agent]) => (
              <tr key={name}>
                <td style={styles.td}>{name}</td>
                <td style={styles.td}>
                  {agent.lastRun ? new Date(agent.lastRun).toLocaleString() : 'Never'}
                </td>
                <td style={styles.td}>
                  <StatusDot status={agent.status} /> {agent.status}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}

function StatusDot({ status }: { status: string }) {
  const color =
    status === 'ok' || status === 'healthy' || status === 'running'
      ? '#22c55e'
      : status === 'warning' || status === 'idle'
        ? '#eab308'
        : '#ef4444';
  return (
    <span
      style={{
        display: 'inline-block',
        width: 8,
        height: 8,
        borderRadius: '50%',
        background: color,
        marginRight: 6,
      }}
    />
  );
}

function StatusCard({
  label,
  value,
  isStatus,
}: {
  label: string;
  value: string;
  isStatus?: boolean;
}) {
  return (
    <div style={styles.card}>
      <div style={styles.cardLabel}>{label}</div>
      <div style={styles.cardValue}>
        {isStatus && <StatusDot status={value} />}
        {value}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: { padding: '1.5rem', maxWidth: 900, margin: '0 auto' },
  section: { marginBottom: '2rem' },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: '1rem' },
  card: {
    background: '#1f2028',
    borderRadius: 8,
    padding: '1rem',
    border: '1px solid #2e303a',
  },
  cardLabel: { fontSize: '0.8rem', color: '#9ca3af', marginBottom: '0.5rem' },
  cardValue: { fontSize: '1.2rem', color: '#f3f4f6', fontWeight: 600 },
  table: { width: '100%', borderCollapse: 'collapse' as const, marginTop: '0.5rem' },
  th: {
    textAlign: 'left' as const,
    padding: '0.5rem 1rem',
    borderBottom: '1px solid #2e303a',
    color: '#9ca3af',
    fontSize: '0.85rem',
  },
  td: {
    padding: '0.5rem 1rem',
    borderBottom: '1px solid #1f2028',
    color: '#f3f4f6',
  },
  loading: { padding: '2rem', textAlign: 'center' as const, color: '#9ca3af' },
  error: { padding: '2rem', textAlign: 'center' as const, color: '#ef4444' },
};
