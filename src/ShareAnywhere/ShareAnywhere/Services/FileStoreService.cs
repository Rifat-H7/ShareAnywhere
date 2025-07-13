using ShareAnywhere.Models;
using ShareAnywhere.Services.Interfaces;
using System.Collections.Concurrent;

namespace ShareAnywhere.Services
{
    public class FileStoreService : IFileStoreService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ConcurrentDictionary<string, FileRecord> _fileMap = new();

        public FileStoreService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public FileRecord SaveFile(IFormFile file, int deleteAfterCount = 1)
        {
            var uploadFolder = Path.Combine(_env.WebRootPath, "Uploads");
            Directory.CreateDirectory(uploadFolder);

            var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
            var filePath = Path.Combine(uploadFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            var code = GenerateCode();
            var record = new FileRecord
            {
                Code = code,
                FilePath = filePath,
                FileName = file.FileName,
                IsText = false,
                DeleteAfterCount = deleteAfterCount
            };

            _fileMap[code] = record;
            return record;
        }

        public FileRecord? GetFile(string code)
        {
            if (_fileMap.TryGetValue(code.ToUpper(), out var record))
            {
                return record;
            }
            return null;
        }
        public void DeleteFile(string code)
        {
            if (_fileMap.TryRemove(code.ToUpper(), out var record))
            {
                if (File.Exists(record.FilePath))
                {
                    File.Delete(record.FilePath);
                }
            }
        }

        private string GenerateCode()
        {
            return Path.GetRandomFileName().Replace(".", "").Substring(0, 6).ToUpper();
        }

        public FileRecord? SaveText(string textContent, int deleteAfterCount)
        {
            // Generate a unique file name
            var code = GenerateCode();
            var fileName = $"{code}.txt";

            // Define the path where the file will be saved
            string folderPath = Path.Combine(_env.WebRootPath, "Uploads");
            Directory.CreateDirectory(folderPath); // Ensure folder exists

            string filePath = Path.Combine(folderPath, fileName);

            // Save the text to the file
            File.WriteAllText(filePath, textContent);


            var record = new FileRecord
            {
                Code = code,
                FilePath = filePath,
                FileName = fileName,
                IsText = true,
                DeleteAfterCount = deleteAfterCount
            };

            _fileMap[code] = record;
            return record;
        }

        public FileRecord? GetText(string code)
        {
            if (_fileMap.TryGetValue(code.ToUpper(), out var record))
            {
                if (!File.Exists(record.FilePath))
                    return null;

                record.Text = File.ReadAllText(record.FilePath);
                return record;
            }
            return null;
        }
    }
}
