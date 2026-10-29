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
    private bool _abandoned;

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
        if (_abandoned)
            throw new InvalidOperationException(
                "the previous receive thread did not exit; this capture cannot be restarted safely");
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = $"{_name}-rx" };
        _thread.Start();
    }

    private void Loop()
    {
        try
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
        finally
        {
            _running = false; // a dead thread must never look alive
        }
    }

    /// <summary>breakloop → join. Returns false when the receive thread is still
    /// alive and the capture must be leaked, never closed underneath it.</summary>
    public bool Stop(int joinTimeoutMs = 5000)
    {
        _running = false;
        try { _capture.BreakLoop(); } catch { /* best effort */ }
        if (_thread is { IsAlive: true } t && !t.Join(joinTimeoutMs))
        {
            // Thread is stuck: mark abandoned. The handle is leaked rather than
            // freed while a thread may still be inside pcap_next_ex.
            _abandoned = true;
            _thread = null;
            return false;
        }
        _thread = null;
        return true;
    }

    /// <summary>Send a frame on this session's handle (thread-safe).</summary>
    public int Send(ReadOnlySpan<byte> frame) => _capture.Send(frame);

    public void TrySetMinToCopy(int bytes) => _capture.TrySetMinToCopy(bytes);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_abandoned) return; // the stuck thread still owns the handle
        Stop();
        // Only reached when the receive thread provably exited or never started.
        if (_thread == null && !_abandoned)
            _capture.Dispose();
    }
}
