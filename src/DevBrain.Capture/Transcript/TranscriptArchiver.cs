namespace DevBrain.Capture.Transcript;

public static class TranscriptArchiver
{
    public static string Archive(string transcriptPath, string sessionId, string archiveDir)
    {
        if (!File.Exists(transcriptPath))
            throw new FileNotFoundException($"Transcript not found: {transcriptPath}");

        Directory.CreateDirectory(archiveDir);

        var destPath = Path.Combine(archiveDir, $"{sessionId}.jsonl");
        File.Copy(transcriptPath, destPath, overwrite: true);

        return destPath;
    }
}
