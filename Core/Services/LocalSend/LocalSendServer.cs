using System.Net;
using System.Net.Sockets;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.Core.Services.LocalSend;

/// <summary>
/// LocalSend HTTP server backed by a raw TcpListener so it works without
/// Windows URL ACL reservations or administrator privileges.
/// Handles only the LocalSend v1/v2 API surface we need.
/// </summary>
public sealed class LocalSendServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PrepareUploadRequestDto> _activeSessions = new();

    public LocalSendDeviceInfo DeviceInfo { get; set; } = new()
    {
        Alias = Environment.MachineName,
        DeviceModel = "Windows",
        DeviceType = "desktop",
        Port = 53317,
        Protocol = "http"
    };

    public string DownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public bool QuickSave { get; set; } = false;

    public event EventHandler<PrepareUploadRequestDto>? UploadRequested;
    public event EventHandler<(string FileId, string Path)>? FileReceived;
    public event EventHandler<LocalSendDeviceInfo>? DeviceRegistered;

    public int ActualPort { get; private set; }

    public void Start(int port = 53317)
    {
        if (_listener != null)
            return;

        _cts = new CancellationTokenSource();

        for (var p = port; p < port + 10; p++)
        {
            try
            {
                var listener = LocalSendServerHelper.TryCreateDualStackListener(p) ?? new TcpListener(IPAddress.Any, p);
                listener.Start();
                _listener = listener;
                ActualPort = p;
                DeviceInfo.Port = p;
                Logger.Log($"[LocalSendServer] Started on port {p} (dual-stack={listener.Server.DualMode})");
                break;
            }
            catch (Exception ex)
            {
                Logger.Log($"[LocalSendServer] Port {p} unavailable: {ex.Message}", LogLevel.Warn);
            }
        }

        if (_listener != null)
            _listenTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(200, token).ConfigureAwait(false); }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 10000;
                using var stream = client.GetStream();
                await LocalSendServerHandler.ProcessAsync(this, stream, client.Client.RemoteEndPoint, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"[LocalSendServer] Client handling error: {ex.Message}", LogLevel.Debug);
            }
        }
    }

    internal void RegisterActiveSession(string sessionId, PrepareUploadRequestDto dto)
    {
        _activeSessions[sessionId] = dto;
        UploadRequested?.Invoke(this, dto);
    }

    public event EventHandler<LocalSendProgressArgs>? ProgressChanged;
    public event EventHandler<string>? SessionCanceled;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _canceledSessions = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, byte>> _sessionCompletedFiles = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, long>> _sessionTransferredBytes = new();

    public void CancelSession(string sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId))
        {
            if (_activeSessions.TryGetValue(sessionId, out var prepareDto))
            {
                _ = Task.Run(() => LocalSendServerHelper.NotifySenderCanceledAsync(prepareDto.Info, sessionId));
            }

            if (_canceledSessions.TryAdd(sessionId, 0))
            {
                SessionCanceled?.Invoke(this, sessionId);
            }
        }
    }

    public bool IsSessionCanceled(string sessionId) => _canceledSessions.ContainsKey(sessionId);

    internal async Task HandleUploadAsync(
        Stream stream, Stream requestBody, string sessionId, string fileId, string token)
    {
        if (IsSessionCanceled(sessionId))
        {
            return;
        }

        var fileName = $"{fileId}.bin";
        var senderAlias = "LocalSend";
        long totalBytes = 0;
        var fileIndex = 1;
        var totalFiles = 1;

        if (_activeSessions.TryGetValue(sessionId, out var prepareDto))
        {
            senderAlias = prepareDto.Info.Alias;
            totalFiles = prepareDto.Files.Count;
            var keys = prepareDto.Files.Keys.ToList();
            fileIndex = Math.Max(1, keys.IndexOf(fileId) + 1);

            if (prepareDto.Files.TryGetValue(fileId, out var fileDto))
            {
                fileName = fileDto.FileName;
                totalBytes = fileDto.Size;
            }
        }

        if (!Directory.Exists(DownloadDirectory))
            Directory.CreateDirectory(DownloadDirectory);

        var normalizedRelativePath = fileName.Replace('\\', '/').TrimStart('/');
        var fullPathCandidate = Path.GetFullPath(Path.Combine(DownloadDirectory, normalizedRelativePath));

        var fullDownloadDir = Path.GetFullPath(DownloadDirectory);
        if (!fullPathCandidate.StartsWith(fullDownloadDir, StringComparison.OrdinalIgnoreCase))
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 403).ConfigureAwait(false);
            return;
        }

        var targetDir = Path.GetDirectoryName(fullPathCandidate) ?? fullDownloadDir;
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        var safeFileName = Path.GetFileName(fullPathCandidate);
        var targetPath = Path.Combine(targetDir, safeFileName);

        if (File.Exists(targetPath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(safeFileName);
            var ext = Path.GetExtension(safeFileName);
            var counter = 1;
            do
            {
                targetPath = Path.Combine(targetDir, $"{nameWithoutExt} ({counter}){ext}");
                counter++;
            } while (File.Exists(targetPath));
        }

        var buffer = new byte[1024 * 1024];
        long bytesReadTotal = 0;
        long lastFlushedBytes = 0;
        var progressStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var isSuccess = false;

        try
        {
            using (var dest = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
            {
                int bytesRead;
                while ((bytesRead = await requestBody.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    if (IsSessionCanceled(sessionId))
                    {
                        break;
                    }

                    await dest.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                    bytesReadTotal += bytesRead;
                    if (bytesReadTotal >= lastFlushedBytes + (10 * 1024 * 1024))
                    {
                        await dest.FlushAsync().ConfigureAwait(false);
                        lastFlushedBytes = bytesReadTotal;
                    }

                    var transferredDict = _sessionTransferredBytes.GetOrAdd(sessionId, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, long>());
                    transferredDict[fileId] = bytesReadTotal;

                    // ponytail: 30ms throttle prevents Dispatcher lag while keeping receiver progress aligned with socket speed.
                    if (progressStopwatch.ElapsedMilliseconds >= 30 || bytesReadTotal >= totalBytes)
                    {
                        progressStopwatch.Restart();
                        var sessionTransferred = transferredDict.Values.Sum();
                        var sessionTotal = prepareDto?.Files.Values.Sum(f => f.Size) ?? totalBytes;

                        ProgressChanged?.Invoke(this, new LocalSendProgressArgs(
                            sessionId, senderAlias, fileId, fileName, bytesReadTotal, totalBytes, fileIndex, totalFiles,
                            sessionBytesTransferred: sessionTransferred, sessionTotalBytes: sessionTotal));
                    }
                }

                await dest.FlushAsync().ConfigureAwait(false);
            }

            if (!IsSessionCanceled(sessionId) && (totalBytes == 0 || bytesReadTotal >= totalBytes))
            {
                isSuccess = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendServer] Error writing upload stream for {fileName}: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            if (!isSuccess) LocalSendServerHelper.TryDeleteFile(targetPath);
        }

        if (!isSuccess)
        {
            if (IsSessionCanceled(sessionId))
            {
                // Give sender's /api/localsend/v2/cancel HTTP notification time to settle on the wire before releasing TCP stream
                await Task.Delay(1500).ConfigureAwait(false);
            }
            else
            {
                await LocalSendServerHelper.WriteResponseAsync(stream, 500).ConfigureAwait(false);
            }
            return;
        }

        var completedSet = _sessionCompletedFiles.GetOrAdd(sessionId, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, byte>());
        completedSet[fileId] = 0;

        var isAllDone = completedSet.Count >= totalFiles;
        var displayIndex = isAllDone ? totalFiles : Math.Max(fileIndex, completedSet.Count);

        var rootSavedPath = Path.Combine(DownloadDirectory, normalizedRelativePath.Split('/')[0]);

        var finalDict = _sessionTransferredBytes.GetOrAdd(sessionId, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, long>());
        finalDict[fileId] = bytesReadTotal;
        var finalSessionTransferred = finalDict.Values.Sum();
        var finalSessionTotal = prepareDto?.Files.Values.Sum(f => f.Size) ?? totalBytes;

        Logger.Log($"[LocalSendServer] Received: {fileName} -> {targetPath} (size={bytesReadTotal}, {completedSet.Count}/{totalFiles})");
        ProgressChanged?.Invoke(this, new LocalSendProgressArgs(
            sessionId, senderAlias, fileId, fileName, bytesReadTotal, totalBytes, displayIndex, totalFiles,
            isFinished: true, isAllDone: isAllDone, savedPath: targetPath, rootSavedPath: rootSavedPath,
            sessionBytesTransferred: finalSessionTransferred, sessionTotalBytes: finalSessionTotal));
        FileReceived?.Invoke(this, (fileId, targetPath));

        await LocalSendServerHelper.WriteResponseAsync(stream, 200).ConfigureAwait(false);
    }

    internal void InvokeDeviceRegistered(LocalSendDeviceInfo dto) => DeviceRegistered?.Invoke(this, dto);

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    public void Dispose() => Stop();
}
