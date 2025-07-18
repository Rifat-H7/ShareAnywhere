﻿using ShareAnywhere.Models;

namespace ShareAnywhere.Services.Interfaces
{
    public interface IFileStoreService
    {
        FileRecord SaveFile(IFormFile file, int deleteAfterCount);
        FileRecord? GetFile(string code);
        void DeleteFile(string code);
        FileRecord? SaveText(string textContent, int deleteAfterCount);
        FileRecord? GetText(string code);
    }
}
