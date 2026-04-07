import { useEffect, useState } from 'react';
import { api, type SetupStatus, type SetupCheckResult } from '../api/client';

const STATUS_ICONS: Record<string, { symbol: string; color: string }> = {
  pass: { symbol: '\u25CF', color: '#22c55e' },
  fail: { symbol: '\u2715', color: '#ef4444' },
  warn: { symbol: '\u25B2', color: '#eab308' },
  skip: { symbol: '\u25CB', color: '#6b7280' },
};

const CATEGORIES = ['Claude Code', 'GitHub Copilot', 'LLM'];

export default function Setup() {
  const [status, setStatus] = useState<SetupStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [fixing, setFixing] = useState<string | null>(null);
  const [fixError, setFixError] = useState<Record<string, string>>({});
  const [expandedSections, setExpandedSections] = useState<Set<string>>(new Set());

  const loadStatus = () => {
    setLoading(true);
    setError(null);
    api.setup.status()
      .then((s) => {
        setStatus(s);
        setLoading(false);
        const expanded = new Set<string>();
        for (const check of s.checks) {
          if (check.status === 'fail') {
            if (check.id.startsWith('claude')) expanded.add('claude');
            if (check.id.startsWith('gh') || check.id.startsWith('copilot')) expanded.add('copilot');
            if (check.id === 'ollama') expanded.add('ollama');
          }
        }
        setExpandedSections(expanded);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  };

  useEffect(() => { loadStatus(); }, []);

  const handleFix = (checkId: string) => {
    setFixing(checkId);
    setFixError((prev) => ({ ...prev, [checkId]: '' }));
    api.setup.fix(checkId)
      .then(() => {
        setFixing(null);
        loadStatus();
      })
      .catch((e) => {
        setFixing(null);
        setFixError((prev) => ({ ...prev, [checkId]: String(e) }));
      });
  };

  const toggleSection = (id: string) => {
    setExpandedSections((prev) => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  };

  const bannerStyle = (): React.CSSProperties => {
    if (!status) return {};
    if (status.summary.fail > 0) return { ...styles.banner, background: '#450a0a', borderColor: '#ef4444' };
    if (status.summary.warn > 0) return { ...styles.banner, background: '#422006', borderColor: '#eab308' };
    return { ...styles.banner, background: '#14532d', borderColor: '#22c55e' };
  };

  const bannerText = (): string => {
    if (!status) return '';
    if (status.summary.fail > 0) return 'Setup incomplete';
    if (status.summary.warn > 0) return 'Some integrations need attention';
    return 'All integrations configured';
  };

  const groupedChecks = (category: string): SetupCheckResult[] =>
    status?.checks.filter((c) => c.category === category) ?? [];

  if (loading && !status) return <div style={styles.loading}>Running validation checks...</div>;
  if (error && !status) return <div style={styles.error}>Error: {error}</div>;

  return (
    <div style={styles.container}>
      <div style={styles.header}>
        <h1>Setup</h1>
        <button onClick={loadStatus} disabled={loading} style={styles.revalidateBtn}>
          {loading ? 'Validating...' : 'Re-validate'}
        </button>
      </div>

      {status && (
        <div style={bannerStyle()}>
          <strong>{bannerText()}</strong>
          <span style={styles.bannerSummary}>
            {status.summary.pass} passed, {status.summary.fail} failed,{' '}
            {status.summary.warn} warnings, {status.summary.skip} skipped
          </span>
        </div>
      )}

      {CATEGORIES.map((cat) => {
        const checks = groupedChecks(cat);
        if (checks.length === 0) return null;
        return (
          <div key={cat} style={styles.card}>
            <h3 style={styles.cardTitle}>{cat}</h3>
            {checks.map((check) => (
              <div key={check.id} style={styles.checkRow}>
                <div style={styles.checkHeader}>
                  <span style={{ color: STATUS_ICONS[check.status].color, fontSize: '0.85rem', width: 16 }}>
                    {STATUS_ICONS[check.status].symbol}
                  </span>
                  <span style={styles.statusLabel}>{check.status.toUpperCase()}</span>
                  <span style={styles.checkName}>{check.name}</span>
                  {check.fixable && check.status === 'fail' && (
                    <button
                      onClick={() => handleFix(check.id)}
                      disabled={fixing === check.id}
                      style={styles.fixBtn}
                    >
                      {fixing === check.id ? 'Fixing...' : 'Fix'}
                    </button>
                  )}
                </div>
                <div style={styles.checkDetail}>{check.detail}</div>
                {fixError[check.id] && (
                  <div style={styles.fixError}>{fixError[check.id]}</div>
                )}
              </div>
            ))}
          </div>
        );
      })}

      <h2 style={styles.sectionTitle}>Setup Instructions</h2>

      <InstructionPanel
        title="Install Claude Code"
        expanded={expandedSections.has('claude')}
        onToggle={() => toggleSection('claude')}
      >
        <p style={styles.instructionText}>Install the Claude Code CLI globally:</p>
        <CopyCommand command="npm install -g @anthropic-ai/claude-code" />
        <p style={styles.instructionText}>After installing, click <strong>Re-validate</strong> above.</p>
      </InstructionPanel>

      <InstructionPanel
        title="Install GitHub Copilot CLI"
        expanded={expandedSections.has('copilot')}
        onToggle={() => toggleSection('copilot')}
      >
        <p style={styles.instructionText}>1. Install the GitHub CLI:</p>
        <p style={styles.instructionLink}>
          <a href="https://cli.github.com/" target="_blank" rel="noreferrer" style={styles.link}>
            https://cli.github.com/
          </a>
        </p>
        <p style={styles.instructionText}>2. Authenticate:</p>
        <CopyCommand command="gh auth login" />
        <p style={styles.instructionText}>3. Install the Copilot extension:</p>
        <CopyCommand command="gh extension install github/gh-copilot" />
        <p style={styles.instructionText}>After installing, click <strong>Re-validate</strong> above.</p>
      </InstructionPanel>

      <InstructionPanel
        title="Install Ollama"
        expanded={expandedSections.has('ollama')}
        onToggle={() => toggleSection('ollama')}
      >
        <p style={styles.instructionText}>Download and install Ollama:</p>
        <p style={styles.instructionLink}>
          <a href="https://ollama.com/download" target="_blank" rel="noreferrer" style={styles.link}>
            https://ollama.com/download
          </a>
        </p>
        <p style={styles.instructionText}>Then pull the default model:</p>
        <CopyCommand command="ollama pull llama3.2:3b" />
        <p style={styles.instructionText}>After installing, click <strong>Re-validate</strong> above.</p>
      </InstructionPanel>
    </div>
  );
}

function InstructionPanel({
  title,
  expanded,
  onToggle,
  children,
}: {
  title: string;
  expanded: boolean;
  onToggle: () => void;
  children: React.ReactNode;
}) {
  return (
    <div style={styles.instructionCard}>
      <button onClick={onToggle} style={styles.instructionHeader}>
        <span>{expanded ? '\u25BC' : '\u25B6'}</span>
        <span>{title}</span>
      </button>
      {expanded && <div style={styles.instructionBody}>{children}</div>}
    </div>
  );
}

function CopyCommand({ command }: { command: string }) {
  const [copied, setCopied] = useState(false);

  const handleCopy = () => {
    navigator.clipboard.writeText(command).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  };

  return (
    <div style={styles.commandBlock}>
      <code style={styles.commandText}>{command}</code>
      <button onClick={handleCopy} style={styles.copyBtn}>
        {copied ? 'Copied' : 'Copy'}
      </button>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: { padding: '1.5rem', maxWidth: 900, margin: '0 auto' },
  header: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' },
  revalidateBtn: {
    padding: '0.5rem 1rem',
    background: '#2a2a4a',
    color: '#e0e0ff',
    border: '1px solid #3b3b6b',
    borderRadius: 6,
    cursor: 'pointer',
    fontSize: '0.85rem',
  },
  banner: {
    padding: '0.75rem 1rem',
    borderRadius: 6,
    border: '1px solid',
    marginBottom: '1.5rem',
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    color: '#f3f4f6',
  },
  bannerSummary: { fontSize: '0.8rem', color: '#9ca3af' },
  card: {
    background: '#1f2028',
    borderRadius: 8,
    padding: '1rem',
    border: '1px solid #2e303a',
    marginBottom: '1rem',
  },
  cardTitle: { marginTop: 0, marginBottom: '0.75rem', color: '#e0e0ff', fontSize: '1rem' },
  checkRow: {
    padding: '0.5rem 0',
    borderBottom: '1px solid #2e303a',
  },
  checkHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: '0.5rem',
  },
  statusLabel: { fontSize: '0.7rem', fontWeight: 700, color: '#9ca3af', width: 36 },
  checkName: { color: '#f3f4f6', fontSize: '0.85rem', flex: 1 },
  checkDetail: {
    fontSize: '0.75rem',
    color: '#6b7280',
    paddingLeft: '52px',
    paddingTop: '0.2rem',
  },
  fixBtn: {
    padding: '0.2rem 0.6rem',
    background: '#2a2a4a',
    color: '#e0e0ff',
    border: '1px solid #3b3b6b',
    borderRadius: 4,
    cursor: 'pointer',
    fontSize: '0.75rem',
  },
  fixError: {
    fontSize: '0.75rem',
    color: '#ef4444',
    paddingLeft: '52px',
    paddingTop: '0.2rem',
  },
  sectionTitle: { marginTop: '2rem', marginBottom: '0.75rem', color: '#e0e0ff' },
  instructionCard: {
    background: '#1f2028',
    borderRadius: 8,
    border: '1px solid #2e303a',
    marginBottom: '0.5rem',
    overflow: 'hidden',
  },
  instructionHeader: {
    display: 'flex',
    gap: '0.5rem',
    alignItems: 'center',
    width: '100%',
    padding: '0.75rem 1rem',
    background: 'transparent',
    color: '#e0e0ff',
    border: 'none',
    cursor: 'pointer',
    fontSize: '0.9rem',
    textAlign: 'left' as const,
  },
  instructionBody: { padding: '0 1rem 1rem 1rem' },
  instructionText: { color: '#9ca3af', fontSize: '0.85rem', margin: '0.5rem 0' },
  instructionLink: { margin: '0.5rem 0' },
  link: { color: '#60a5fa', textDecoration: 'none' },
  commandBlock: {
    display: 'flex',
    alignItems: 'center',
    gap: '0.5rem',
    background: '#1a1a2e',
    border: '1px solid #2e303a',
    borderRadius: 4,
    padding: '0.5rem 0.75rem',
    margin: '0.5rem 0',
  },
  commandText: { flex: 1, color: '#f3f4f6', fontFamily: 'monospace', fontSize: '0.8rem' },
  copyBtn: {
    padding: '0.2rem 0.5rem',
    background: '#2a2a4a',
    color: '#9ca3af',
    border: '1px solid #3b3b6b',
    borderRadius: 4,
    cursor: 'pointer',
    fontSize: '0.7rem',
  },
  loading: { padding: '2rem', textAlign: 'center' as const, color: '#9ca3af' },
  error: { padding: '2rem', textAlign: 'center' as const, color: '#ef4444' },
};
