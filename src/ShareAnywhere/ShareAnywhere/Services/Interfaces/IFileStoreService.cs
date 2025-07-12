using ShareAnywhere.Models;

namespace ShareAnywhere.Services.Interfaces
{
    public interface IFileStoreService
    {
        FileRecord SaveFile(IFormFile file);
        FileRecord? GetFile(string code);
        void DeleteFile(string code);
    }
}
