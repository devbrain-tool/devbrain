namespace DevBrain.Capture.Transcript;

public static class TranscriptArchiver
{
    public static string Archive(string transcriptPath, string sessionId, string archiveDir)
    {
        if (!File.Exists(transcriptPath))
            throw new FileNotFoundException($"Transcript not found: {transcriptPath}");

        Directory.CreateDirectory(archiveDir);

        // Sanitize sessionId to prevent path traversal
        var safeId = Path.GetFileName(sessionId);
        if (string.IsNullOrEmpty(safeId) || safeId != sessionId)
            throw new ArgumentException($"Invalid session ID: {sessionId}");

        var destPath = Path.Combine(archiveDir, $"{safeId}.jsonl");
        File.Copy(transcriptPath, destPath, overwrite: true);

        return destPath;
    }
}
