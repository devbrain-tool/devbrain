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

  it("transitions to 'healthy' on successful health check", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true });
    monitor.start();
    await jest.advanceTimersByTimeAsync(1000);
    expect(monitor.state).toBe("healthy");
    expect(states).toEqual(["healthy"]);
  });

  it("transitions to 'unhealthy' on failed health check", async () => {
    mockFetch.mockRejectedValueOnce(new Error("ECONNREFUSED"));
    monitor.start();
    await jest.advanceTimersByTimeAsync(1000);
    expect(monitor.state).toBe("unhealthy");
    expect(states).toEqual(["unhealthy"]);
  });

  it("transitions healthy -> unhealthy -> healthy", async () => {
    mockFetch
      .mockResolvedValueOnce({ ok: true })
      .mockRejectedValueOnce(new Error("ECONNREFUSED"))
      .mockResolvedValueOnce({ ok: true });
    monitor.start();
    await jest.advanceTimersByTimeAsync(1000);
    await jest.advanceTimersByTimeAsync(1000);
    await jest.advanceTimersByTimeAsync(1000);
    expect(states).toEqual(["healthy", "unhealthy", "healthy"]);
  });

  it("does not emit duplicate states", async () => {
    mockFetch
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({ ok: true });
    monitor.start();
    await jest.advanceTimersByTimeAsync(1000);
    await jest.advanceTimersByTimeAsync(1000);
    expect(states).toEqual(["healthy"]);
  });

  it("stop() clears the polling interval", async () => {
    mockFetch.mockResolvedValue({ ok: true });
    monitor.start();
    await jest.advanceTimersByTimeAsync(1000);
    monitor.stop();
    mockFetch.mockReset();
    await jest.advanceTimersByTimeAsync(5000);
    expect(mockFetch).not.toHaveBeenCalled();
  });
});
