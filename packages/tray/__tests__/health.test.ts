import { HealthMonitor, HealthState } from "../src/health";

const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

describe("HealthMonitor", () => {
  let monitor: HealthMonitor;
  let states: HealthState[];

  beforeEach(() => {
    jest.useFakeTimers();
    states = [];
    monitor = new HealthMonitor(1000);
    monitor.on("stateChange", (s: HealthState) => states.push(s));
    mockFetch.mockReset();
  });

  afterEach(() => {
    monitor.stop();
    jest.useRealTimers();
  });

  it("starts in 'starting' state", () => {
    expect(monitor.state).toBe("starting");
  });

  it("performs immediate health check on start", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true });
    monitor.start();
    // Flush the immediate async check()
    await jest.advanceTimersByTimeAsync(0);
    expect(monitor.state).toBe("healthy");
    expect(states).toEqual(["healthy"]);
  });

  it("transitions to 'unhealthy' on failed health check", async () => {
    mockFetch.mockRejectedValueOnce(new Error("ECONNREFUSED"));
    monitor.start();
    await jest.advanceTimersByTimeAsync(0);
    expect(monitor.state).toBe("unhealthy");
    expect(states).toEqual(["unhealthy"]);
  });

  it("transitions healthy -> unhealthy -> healthy", async () => {
    mockFetch
      .mockResolvedValueOnce({ ok: true })     // immediate check
      .mockRejectedValueOnce(new Error("ECONNREFUSED")) // 1s interval
      .mockResolvedValueOnce({ ok: true });    // 2s interval
    monitor.start();
    await jest.advanceTimersByTimeAsync(0);     // immediate
    await jest.advanceTimersByTimeAsync(1000);  // first interval
    await jest.advanceTimersByTimeAsync(1000);  // second interval
    expect(states).toEqual(["healthy", "unhealthy", "healthy"]);
  });

  it("does not emit duplicate states", async () => {
    mockFetch
      .mockResolvedValueOnce({ ok: true })   // immediate check
      .mockResolvedValueOnce({ ok: true });  // 1s interval (same state)
    monitor.start();
    await jest.advanceTimersByTimeAsync(0);
    await jest.advanceTimersByTimeAsync(1000);
    expect(states).toEqual(["healthy"]); // Only one emission
  });

  it("stop() clears the polling interval", async () => {
    mockFetch.mockResolvedValue({ ok: true });
    monitor.start();
    await jest.advanceTimersByTimeAsync(0);
    monitor.stop();
    mockFetch.mockReset();
    await jest.advanceTimersByTimeAsync(5000);
    expect(mockFetch).not.toHaveBeenCalled();
  });
});
