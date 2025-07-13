namespace ShareAnywhere.Models
{
    public class FileRecord
    {
        public string Code { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public bool IsText { get; set; }
        public string Text { get; set; } = string.Empty;
        public int DeleteAfterCount { get; set; } = 1;
    }
}