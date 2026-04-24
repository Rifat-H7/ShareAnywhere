namespace ShareAnywhere.Models
{
    public class FileRecord
    {
        public required string Code { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public required string FileName { get; set; }
        public bool IsText { get; set; }
        public string Text { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
        public string SenderToken { get; set; } = string.Empty;
        public int DeleteAfterCount { get; set; } = 1;
    }
}
