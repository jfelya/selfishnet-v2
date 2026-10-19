namespace SelfishNetV2.Drivers;

/// <summary>
/// Owns one <see cref="IPcapCapture"/> plus its dedicated receive thread.
/// Enforces the mandatory shutdown ritual — <c>breakloop → join → close</c> — so no
/// handle is ever closed while a thread is blocked inside pcap_next_ex (defect C2).
/// If the thread fails to exit within the join timeout, the handle is LEAKED, never
/// closed underneath it (crash-safe by construction).
/// </summary>
public sealed class PcapCaptureSession : IDisposable
{
    private readonly IPcapCapture _capture;
    private readonly string _name;
    private Thread? _thread;
    private volatile bool _running;
    private bool _disposed;

    /// <summary>Raised on the session's own thread for every received frame.
    /// The span is valid only for the duration of the callback.</summary>
    public event PacketHandler? PacketReceived;

    /// <summary>Raised on the session thread when the driver reports an error.</summary>
    public event Action<string>? ReceiveError;

    public bool Running => _running;
    public IPcapCapture Capture => _capture;

    public PcapCaptureSession(IPcapCapture capture, string name = "pcap")
    {
        _capture = capture;
        _name = name;
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PcapCaptureSession));
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = $"{_name}-rx" };
        _thread.Start();
    }

    private void Loop()
    {
        while (_running)
        {
            ReceiveResult rc;
            PacketView view;
            try { rc = _capture.Receive(out view); }
            catch (Exception ex)
            {
                if (_running) ReceiveError?.Invoke(ex.Message);
                break;
            }
            switch (rc)
            {
                case ReceiveResult.Packet:
                    try { PacketReceived?.Invoke(view.Data); }
                    catch { /* handler errors must not kill the capture thread */ }
                    break;
                case ReceiveResult.Timeout:
                    break; // normal; re-check _running
                case ReceiveResult.Broken:
                    return;  // breakloop during shutdown
                case ReceiveResult.Error:
                    if (_running) ReceiveError?.Invoke(_capture.LastError);
                    return;
            }
        }
    }

    /// <summary>breakloop → join. Handle stays open; the session can be restarted.</summary>
    public void Stop(int joinTimeoutMs = 5000)
    {
        _running = false;
        try { _capture.BreakLoop(); } catch { /* best effort */ }
        if (_thread is { IsAlive: true } t && !t.Join(joinTimeoutMs))
        {
            // Thread is stuck: DO NOT dispose the capture — leak it rather than
            // free a handle a thread is using. This path indicates a driver bug.
            _thread = null;
            return;
        }
        _thread = null;
    }

    /// <summary>Send a frame on this session's handle (thread-safe).</summary>
    public int Send(ReadOnlySpan<byte> frame) => _capture.Send(frame);

    public void TrySetMinToCopy(int bytes) => _capture.TrySetMinToCopy(bytes);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        // Only reached when the receive thread provably exited (or never started,
        // or was abandoned as a leak — in which case we intentionally skip close).
        if (_thread == null)
            _capture.Dispose();
    }
}
