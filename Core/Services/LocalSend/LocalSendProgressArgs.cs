namespace SwiftList.Core.Services.LocalSend;

/// <summary>
/// Event arguments for LocalSend file transfer progress.
/// </summary>
public sealed class LocalSendProgressArgs : EventArgs
{
    public string SessionId { get; }
    public string SenderAlias { get; }
    public string FileId { get; }
    public string FileName { get; }
    public long BytesTransferred { get; }
    public long TotalBytes { get; }
    public int CurrentFileIndex { get; }
    public int TotalFiles { get; }
    public bool IsFinished { get; }
    public bool IsAllDone { get; }
    public string? SavedPath { get; }

    public LocalSendProgressArgs(
        string sessionId,
        string senderAlias,
        string fileId,
        string fileName,
        long bytesTransferred,
        long totalBytes,
        int currentFileIndex,
        int totalFiles,
        bool isFinished = false,
        bool isAllDone = false,
        string? savedPath = null)
    {
        SessionId = sessionId;
        SenderAlias = senderAlias;
        FileId = fileId;
        FileName = fileName;
        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
        CurrentFileIndex = currentFileIndex;
        TotalFiles = totalFiles;
        IsFinished = isFinished;
        IsAllDone = isAllDone;
        SavedPath = savedPath;
    }
}
