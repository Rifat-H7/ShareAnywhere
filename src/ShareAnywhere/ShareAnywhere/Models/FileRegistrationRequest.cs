namespace ShareAnywhere.Models
{
    public class FileRegistrationRequest
    {
        public required string FileName { get; set; }
        public string ContentType { get; set; } = "application/octet-stream";
        public int DeleteAfterCount { get; set; } = 1;
    }
}
