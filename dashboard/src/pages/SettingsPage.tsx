import { useEffect, useState } from 'react';
import { api, type Settings } from '../api/client';

export default function SettingsPage() {
  const [settings, setSettings] = useState<Settings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .settings()
      .then((data) => {
        setSettings(data);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  }, []);

  if (error) return <div style={styles.error}>Error: {error}</div>;
  if (loading) return <div style={styles.loading}>Loading settings...</div>;
  if (!settings) return null;

  return (
    <div style={styles.container}>
      <h1>Settings</h1>
      <p style={styles.subtitle}>Current configuration (read-only)</p>

      <SettingsSection title="Daemon" data={settings.daemon} />
      <SettingsSection title="Capture" data={settings.capture} />
      <SettingsSection title="Storage" data={settings.storage} />

      <section style={styles.section}>
        <h2 style={styles.sectionTitle}>LLM</h2>
        <h3 style={styles.subsectionTitle}>Local</h3>
        <DefinitionTable data={settings.llm.local} />
        <h3 style={styles.subsectionTitle}>Cloud</h3>
        <DefinitionTable data={settings.llm.cloud} />
      </section>

      <section style={styles.section}>
        <h2 style={styles.sectionTitle}>Agents</h2>
        {Object.entries(settings.agents).map(([name, config]) => (
          <div key={name}>
            <h3 style={styles.subsectionTitle}>{name}</h3>
            <DefinitionTable data={config as object} />
          </div>
        ))}
      </section>
    </div>
  );
}

function SettingsSection({
  title,
  data,
}: {
  title: string;
  data: object;
}) {
  return (
    <section style={styles.section}>
      <h2 style={styles.sectionTitle}>{title}</h2>
      <DefinitionTable data={data} />
    </section>
  );
}

function DefinitionTable({ data }: { data: object }) {
  const entries = Object.entries(data as Record<string, unknown>);
  return (
    <table style={styles.table}>
      <tbody>
        {entries.map(([key, value]) => (
          <tr key={key}>
            <td style={styles.keyCell}>{key}</td>
            <td style={styles.valueCell}>{formatValue(value)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function formatValue(value: unknown): string {
  if (value === null || value === undefined) return '--';
  if (typeof value === 'boolean') return value ? 'true' : 'false';
  if (Array.isArray(value)) return value.length === 0 ? '[]' : value.join(', ');
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

const styles: Record<string, React.CSSProperties> = {
  container: { padding: '1.5rem', maxWidth: 800, margin: '0 auto' },
  subtitle: {
    color: '#6b7280',
    fontSize: '0.9rem',
    marginBottom: '1.5rem',
  },
  section: {
    marginBottom: '2rem',
  },
  sectionTitle: {
    fontSize: '1.1rem',
    color: '#f3f4f6',
    marginBottom: '0.75rem',
    borderBottom: '1px solid #2e303a',
    paddingBottom: '0.4rem',
  },
  subsectionTitle: {
    fontSize: '0.9rem',
    color: '#60a5fa',
    marginTop: '0.75rem',
    marginBottom: '0.5rem',
  },
  table: {
    width: '100%',
    borderCollapse: 'collapse' as const,
    marginBottom: '0.5rem',
  },
  keyCell: {
    padding: '0.4rem 1rem 0.4rem 0',
    fontSize: '0.85rem',
    color: '#9ca3af',
    fontFamily: 'monospace',
    verticalAlign: 'top',
    width: '40%',
    borderBottom: '1px solid #1f2028',
  },
  valueCell: {
    padding: '0.4rem 0',
    fontSize: '0.85rem',
    color: '#f3f4f6',
    fontFamily: 'monospace',
    borderBottom: '1px solid #1f2028',
  },
  loading: { padding: '2rem', textAlign: 'center' as const, color: '#9ca3af' },
  error: { padding: '2rem', textAlign: 'center' as const, color: '#ef4444' },
};
