import { useEffect, useState } from 'react';
import { api, type DbTableInfo, type DbTableDetail, type DbQueryResult } from '../api/client';

type Mode = 'browse' | 'sql';
type SortDir = 'ASC' | 'DESC';

export default function Database() {
  const [tables, setTables] = useState<DbTableInfo[]>([]);
  const [activeTable, setActiveTable] = useState<string | null>(null);
  const [tableDetail, setTableDetail] = useState<DbTableDetail | null>(null);
  const [queryResult, setQueryResult] = useState<DbQueryResult | null>(null);
  const [mode, setMode] = useState<Mode>('browse');
  const [sqlText, setSqlText] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [page, setPage] = useState(0);
  const [sortCol, setSortCol] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<SortDir>('ASC');

  const PAGE_SIZE = 50;

  // Load table list on mount
  useEffect(() => {
    api.db.tables().then(setTables).catch((e) => setError(String(e)));
  }, []);

  // Fetch only query results (used for page/sort changes)
  const fetchRows = (table: string, pageNum: number, col: string | null, dir: SortDir) => {
    setLoading(true);
    setError(null);

    const orderClause = col ? ` ORDER BY "${col}" ${dir}` : '';
    const sql = `SELECT * FROM "${table}"${orderClause} LIMIT ${PAGE_SIZE} OFFSET ${pageNum * PAGE_SIZE}`;

    api.db.query(sql)
      .then((result) => {
        setQueryResult(result);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  };

  // Fetch table detail + first page (used when selecting a new table)
  const browseTable = (table: string) => {
    setLoading(true);
    setError(null);

    const sql = `SELECT * FROM "${table}" LIMIT ${PAGE_SIZE} OFFSET 0`;

    Promise.all([
      api.db.table(table),
      api.db.query(sql),
    ])
      .then(([detail, result]) => {
        setTableDetail(detail);
        setQueryResult(result);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  };

  const handleTableClick = (name: string) => {
    setActiveTable(name);
    setMode('browse');
    setPage(0);
    setSortCol(null);
    setSortDir('ASC');
    browseTable(name);
  };

  const handleSort = (col: string) => {
    if (!activeTable) return;
    const newDir = sortCol === col && sortDir === 'ASC' ? 'DESC' : 'ASC';
    setSortCol(col);
    setSortDir(newDir);
    setPage(0);
    fetchRows(activeTable, 0, col, newDir);
  };

  const handlePageChange = (newPage: number) => {
    if (!activeTable) return;
    setPage(newPage);
    fetchRows(activeTable, newPage, sortCol, sortDir);
  };

  const handleRunSql = () => {
    if (!sqlText.trim()) return;
    setLoading(true);
    setError(null);
    setTableDetail(null);
    api.db.query(sqlText.trim())
      .then((result) => {
        setQueryResult(result);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setQueryResult(null);
        setLoading(false);
      });
  };

  const handleSqlKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      handleRunSql();
    }
  };

  return (
    <div style={styles.layout}>
      {/* Sidebar */}
      <aside style={styles.sidebar}>
        <button
          onClick={() => setMode(mode === 'sql' ? 'browse' : 'sql')}
          style={{
            ...styles.sqlToggle,
            ...(mode === 'sql' ? styles.sqlToggleActive : {}),
          }}
        >
          SQL Console
        </button>
        <div style={styles.tableList}>
          {tables.map((t) => (
            <button
              key={t.name}
              onClick={() => handleTableClick(t.name)}
              style={{
                ...styles.tableItem,
                ...(activeTable === t.name && mode === 'browse' ? styles.tableItemActive : {}),
              }}
            >
              <span>{t.name}</span>
              <span style={styles.badge}>{t.rowCount}</span>
            </button>
          ))}
        </div>
      </aside>

      {/* Main area */}
      <div style={styles.main}>
        {error && <div style={styles.error}>{error}</div>}

        {mode === 'sql' && (
          <div style={styles.sqlPanel}>
            <textarea
              value={sqlText}
              onChange={(e) => setSqlText(e.target.value)}
              onKeyDown={handleSqlKeyDown}
              placeholder="SELECT * FROM observations LIMIT 50"
              style={styles.sqlInput}
              rows={5}
            />
            <div style={styles.sqlControls}>
              <button onClick={handleRunSql} disabled={loading} style={styles.runButton}>
                {loading ? 'Running...' : 'Run (Ctrl+Enter)'}
              </button>
              {queryResult && !error && (
                <span style={styles.statusText}>
                  {queryResult.rowCount} rows in {queryResult.executionMs}ms
                </span>
              )}
            </div>
          </div>
        )}

        {mode === 'browse' && tableDetail && (
          <div style={styles.metaBar}>
            {tableDetail.columns.map((col) => (
              <span key={col.name} style={styles.metaChip}>
                {col.primaryKey && <span style={styles.pkBadge}>PK</span>}
                {col.name}: <span style={styles.metaType}>{col.type}</span>
                {col.nullable && <span style={styles.nullBadge}>?</span>}
              </span>
            ))}
          </div>
        )}

        {queryResult && (
          <>
            <div style={styles.tableWrapper}>
              <table style={styles.table}>
                <thead>
                  <tr>
                    {queryResult.columns.map((col) => (
                      <th
                        key={col}
                        style={{
                          ...styles.th,
                          cursor: mode === 'browse' ? 'pointer' : 'default',
                        }}
                        onClick={mode === 'browse' ? () => handleSort(col) : undefined}
                      >
                        {col}
                        {mode === 'browse' && sortCol === col && (
                          <span style={styles.sortArrow}>{sortDir === 'ASC' ? ' \u25B2' : ' \u25BC'}</span>
                        )}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {queryResult.rows.map((row, i) => (
                    <tr key={i}>
                      {row.map((cell, j) => (
                        <td key={j} style={styles.td}>
                          {cell === null ? <span style={styles.nullValue}>NULL</span> : String(cell)}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {mode === 'browse' && activeTable && (
              <div style={styles.pagination}>
                <button
                  onClick={() => handlePageChange(page - 1)}
                  disabled={page === 0}
                  style={styles.pageButton}
                >
                  Prev
                </button>
                <span style={styles.pageInfo}>
                  Showing {page * PAGE_SIZE + 1}-{page * PAGE_SIZE + queryResult.rowCount}
                  {tableDetail && ` of ${tableDetail.rowCount}`}
                </span>
                <button
                  onClick={() => handlePageChange(page + 1)}
                  disabled={queryResult.rowCount < PAGE_SIZE}
                  style={styles.pageButton}
                >
                  Next
                </button>
              </div>
            )}
          </>
        )}

        {!queryResult && !error && !loading && (
          <div style={styles.empty}>
            {mode === 'browse'
              ? 'Select a table from the sidebar to browse its data.'
              : 'Write a SQL query and press Run or Ctrl+Enter.'}
          </div>
        )}

        {loading && !queryResult && (
          <div style={styles.empty}>Loading...</div>
        )}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  layout: {
    display: 'flex',
    height: 'calc(100vh - 53px)',
    overflow: 'hidden',
  },
  sidebar: {
    width: 240,
    minWidth: 240,
    borderRight: '1px solid #2e303a',
    background: '#1a1a2e',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
  },
  sqlToggle: {
    padding: '0.6rem 1rem',
    background: 'transparent',
    color: '#9ca3af',
    border: 'none',
    borderBottom: '1px solid #2e303a',
    cursor: 'pointer',
    fontSize: '0.9rem',
    textAlign: 'left' as const,
  },
  sqlToggleActive: {
    color: '#e0e0ff',
    background: '#2a2a4a',
  },
  tableList: {
    flex: 1,
    overflowY: 'auto' as const,
  },
  tableItem: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    width: '100%',
    padding: '0.5rem 1rem',
    background: 'transparent',
    color: '#9ca3af',
    border: 'none',
    borderBottom: '1px solid #1f2028',
    cursor: 'pointer',
    fontSize: '0.85rem',
    textAlign: 'left' as const,
  },
  tableItemActive: {
    color: '#e0e0ff',
    background: '#2a2a4a',
  },
  badge: {
    fontSize: '0.75rem',
    background: '#2a2a4a',
    color: '#9ca3af',
    padding: '1px 6px',
    borderRadius: 4,
  },
  main: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    padding: '1rem',
  },
  error: {
    padding: '0.75rem 1rem',
    background: '#2d1b1b',
    border: '1px solid #ef4444',
    borderRadius: 6,
    color: '#ef4444',
    marginBottom: '0.75rem',
    fontSize: '0.85rem',
  },
  sqlPanel: {
    marginBottom: '0.75rem',
  },
  sqlInput: {
    width: '100%',
    padding: '0.75rem',
    background: '#1f2028',
    border: '1px solid #2e303a',
    borderRadius: 6,
    color: '#f3f4f6',
    fontFamily: 'monospace',
    fontSize: '0.85rem',
    resize: 'vertical' as const,
    outline: 'none',
    boxSizing: 'border-box' as const,
  },
  sqlControls: {
    display: 'flex',
    alignItems: 'center',
    gap: '0.75rem',
    marginTop: '0.5rem',
  },
  runButton: {
    padding: '0.5rem 1rem',
    background: '#2a2a4a',
    color: '#e0e0ff',
    border: '1px solid #3b3b6b',
    borderRadius: 6,
    cursor: 'pointer',
    fontSize: '0.85rem',
  },
  statusText: {
    color: '#9ca3af',
    fontSize: '0.8rem',
  },
  metaBar: {
    display: 'flex',
    flexWrap: 'wrap' as const,
    gap: '0.4rem',
    marginBottom: '0.75rem',
  },
  metaChip: {
    fontSize: '0.75rem',
    color: '#9ca3af',
    background: '#1f2028',
    border: '1px solid #2e303a',
    borderRadius: 4,
    padding: '2px 8px',
  },
  metaType: {
    color: '#60a5fa',
  },
  pkBadge: {
    color: '#c084fc',
    fontWeight: 700,
    marginRight: 4,
    fontSize: '0.7rem',
  },
  nullBadge: {
    color: '#6b7280',
    marginLeft: 2,
  },
  tableWrapper: {
    flex: 1,
    overflow: 'auto',
    borderRadius: 6,
    border: '1px solid #2e303a',
  },
  table: {
    width: '100%',
    borderCollapse: 'collapse' as const,
    fontSize: '0.8rem',
  },
  th: {
    textAlign: 'left' as const,
    padding: '0.5rem 0.75rem',
    borderBottom: '1px solid #2e303a',
    background: '#1a1a2e',
    color: '#9ca3af',
    fontWeight: 600,
    position: 'sticky' as const,
    top: 0,
    whiteSpace: 'nowrap' as const,
    userSelect: 'none' as const,
  },
  sortArrow: {
    fontSize: '0.7rem',
  },
  td: {
    padding: '0.4rem 0.75rem',
    borderBottom: '1px solid #1f2028',
    color: '#f3f4f6',
    maxWidth: 300,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap' as const,
  },
  nullValue: {
    color: '#6b7280',
    fontStyle: 'italic',
  },
  pagination: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: '1rem',
    padding: '0.75rem 0',
  },
  pageButton: {
    padding: '0.4rem 0.75rem',
    background: '#2a2a4a',
    color: '#e0e0ff',
    border: '1px solid #3b3b6b',
    borderRadius: 4,
    cursor: 'pointer',
    fontSize: '0.8rem',
  },
  pageInfo: {
    fontSize: '0.8rem',
    color: '#9ca3af',
  },
  empty: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    color: '#6b7280',
    fontSize: '0.9rem',
  },
};
