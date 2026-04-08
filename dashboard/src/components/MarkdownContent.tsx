import { useState, type ReactNode } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';

interface MarkdownContentProps {
  content: string;
  maxHeight?: string;
  collapsible?: boolean;
  collapseAfterLines?: number;
}

export default function MarkdownContent({
  content,
  maxHeight = '70vh',
  collapsible = false,
  collapseAfterLines = 10,
}: MarkdownContentProps) {
  const [copied, setCopied] = useState(false);
  const [expanded, setExpanded] = useState(!collapsible);

  const handleCopy = () => {
    navigator.clipboard.writeText(content);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const shouldCollapse =
    collapsible && content.split('\n').length > collapseAfterLines;

  const displayContent =
    shouldCollapse && !expanded
      ? content.split('\n').slice(0, collapseAfterLines).join('\n') + '\n...'
      : content;

  return (
    <div style={styles.wrapper}>
      <button
        onClick={handleCopy}
        style={styles.copyBtn}
        title="Copy as Markdown"
      >
        {copied ? 'Copied!' : 'Copy'}
      </button>

      <div
        style={{
          ...styles.markdown,
          maxHeight: expanded ? maxHeight : undefined,
          overflow: expanded ? 'auto' : 'hidden',
        }}
      >
        <ReactMarkdown
          remarkPlugins={[remarkGfm]}
          components={{
            code: CodeBlock,
            h1: ({ children }) => <h1 style={styles.h1}>{children}</h1>,
            h2: ({ children }) => <h2 style={styles.h2}>{children}</h2>,
            h3: ({ children }) => <h3 style={styles.h3}>{children}</h3>,
            p: ({ children }) => <p style={styles.p}>{children}</p>,
            ul: ({ children }) => <ul style={styles.ul}>{children}</ul>,
            ol: ({ children }) => <ol style={styles.ol}>{children}</ol>,
            li: ({ children }) => <li style={styles.li}>{children}</li>,
            blockquote: ({ children }) => (
              <blockquote style={styles.blockquote}>{children}</blockquote>
            ),
            table: ({ children }) => (
              <table style={styles.table}>{children}</table>
            ),
            th: ({ children }) => <th style={styles.th}>{children}</th>,
            td: ({ children }) => <td style={styles.td}>{children}</td>,
            hr: () => <hr style={styles.hr} />,
            a: ({ href, children }) => (
              <a href={href} style={styles.a} target="_blank" rel="noopener noreferrer">
                {children}
              </a>
            ),
          }}
        >
          {displayContent}
        </ReactMarkdown>
      </div>

      {shouldCollapse && (
        <button
          onClick={() => setExpanded(!expanded)}
          style={styles.expandBtn}
        >
          {expanded ? 'Show less' : 'Show more'}
        </button>
      )}
    </div>
  );
}

function CodeBlock({
  children,
  className,
}: {
  children?: ReactNode;
  className?: string;
}) {
  const [copied, setCopied] = useState(false);
  const isInline = !className;
  const code = String(children).replace(/\n$/, '');

  if (isInline) {
    return <code style={styles.inlineCode}>{children}</code>;
  }

  const handleCopy = () => {
    navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div style={styles.codeBlockWrapper}>
      <button onClick={handleCopy} style={styles.codeCopyBtn}>
        {copied ? 'Copied!' : 'Copy'}
      </button>
      <pre style={styles.codeBlock}>
        <code>{code}</code>
      </pre>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  wrapper: {
    position: 'relative',
    background: '#1f2028',
    border: '1px solid #2e303a',
    borderRadius: 8,
    padding: '1.25rem',
  },
  copyBtn: {
    position: 'absolute',
    top: 8,
    right: 8,
    padding: '3px 10px',
    background: '#374151',
    color: '#d1d5db',
    border: '1px solid #4b5563',
    borderRadius: 4,
    cursor: 'pointer',
    fontSize: '0.7rem',
    zIndex: 1,
  },
  markdown: {
    color: '#d1d5db',
    fontSize: '0.9rem',
    lineHeight: 1.6,
  },
  h1: {
    fontSize: '1.4rem',
    fontWeight: 700,
    color: '#f3f4f6',
    margin: '0 0 0.75rem 0',
    borderBottom: '1px solid #2e303a',
    paddingBottom: '0.5rem',
  },
  h2: {
    fontSize: '1.15rem',
    fontWeight: 600,
    color: '#e5e7eb',
    margin: '1.25rem 0 0.5rem 0',
  },
  h3: {
    fontSize: '1rem',
    fontWeight: 600,
    color: '#d1d5db',
    margin: '1rem 0 0.4rem 0',
  },
  p: { margin: '0 0 0.75rem 0' },
  ul: { margin: '0 0 0.75rem 0', paddingLeft: '1.5rem' },
  ol: { margin: '0 0 0.75rem 0', paddingLeft: '1.5rem' },
  li: { marginBottom: '0.3rem' },
  blockquote: {
    margin: '0 0 0.75rem 0',
    padding: '0.5rem 1rem',
    borderLeft: '3px solid #6366f1',
    background: '#161620',
    borderRadius: '0 4px 4px 0',
  },
  table: {
    width: '100%',
    borderCollapse: 'collapse' as const,
    margin: '0 0 0.75rem 0',
    fontSize: '0.85rem',
  },
  th: {
    textAlign: 'left' as const,
    padding: '0.4rem 0.75rem',
    borderBottom: '2px solid #374151',
    color: '#e5e7eb',
    fontWeight: 600,
  },
  td: {
    padding: '0.4rem 0.75rem',
    borderBottom: '1px solid #2e303a',
  },
  hr: {
    border: 'none',
    borderTop: '1px solid #2e303a',
    margin: '1rem 0',
  },
  a: { color: '#60a5fa', textDecoration: 'none' },
  inlineCode: {
    background: '#161620',
    color: '#a5b4fc',
    padding: '1px 5px',
    borderRadius: 3,
    fontSize: '0.85em',
    fontFamily: 'monospace',
  },
  codeBlockWrapper: {
    position: 'relative',
    margin: '0 0 0.75rem 0',
  },
  codeCopyBtn: {
    position: 'absolute',
    top: 6,
    right: 6,
    padding: '2px 8px',
    background: '#374151',
    color: '#9ca3af',
    border: '1px solid #4b5563',
    borderRadius: 3,
    cursor: 'pointer',
    fontSize: '0.65rem',
  },
  codeBlock: {
    background: '#161620',
    border: '1px solid #2e303a',
    borderRadius: 6,
    padding: '1rem',
    color: '#e5e7eb',
    fontSize: '0.8rem',
    lineHeight: 1.5,
    fontFamily: 'monospace',
    overflowX: 'auto' as const,
    margin: 0,
  },
  expandBtn: {
    display: 'block',
    width: '100%',
    padding: '0.4rem',
    marginTop: '0.5rem',
    background: 'transparent',
    color: '#60a5fa',
    border: '1px solid #2e303a',
    borderRadius: 4,
    cursor: 'pointer',
    fontSize: '0.8rem',
  },
};
