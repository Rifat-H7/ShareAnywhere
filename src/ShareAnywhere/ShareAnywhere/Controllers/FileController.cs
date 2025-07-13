using Microsoft.AspNetCore.Mvc;
using ShareAnywhere.Services;
using ShareAnywhere.Services.Interfaces;

namespace ShareAnywhere.Controllers
{
    public class FileController : Controller
    {
        private readonly IFileStoreService _fileService;

        public FileController(IFileStoreService fileService)
        {
            _fileService = fileService;
        }

        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Upload(IFormFile uploadedFile, int deleteAfterCount = 1)
        {
            if (uploadedFile == null || uploadedFile.Length == 0)
            {
                ModelState.AddModelError("", "Please select a file.");
                return View();
            }

            var record = _fileService.SaveFile(uploadedFile, deleteAfterCount);
            return RedirectToAction("UploadResult", new { code = record.Code, filename = record.FileName });
        }

        [HttpGet]
        public IActionResult UploadResult(string code, string filename)
        {
            ViewBag.Code = code;
            ViewBag.FileName = filename;
            ViewBag.DownloadLink = Url.Action("Download", "File", new { code }, Request.Scheme, Request.Host.ToString());
            return View();
        }

        [HttpGet("{code}")]
        public IActionResult Download(string code)
        {
            var record = _fileService.GetFile(code);
            if (record == null) return NotFound("Invalid download code.");

            var fileBytes = System.IO.File.ReadAllBytes(record.FilePath);

            // Decrement and delete after sending the file
            record.DeleteAfterCount--;
            if (record.DeleteAfterCount <= 0)
            {
                _fileService.DeleteFile(code);
            }

            return File(fileBytes, "application/octet-stream", record.FileName);
        }

        [HttpPost]
        public IActionResult UploadText(string textContent, int deleteAfterCount = 1)
        {
            if (string.IsNullOrWhiteSpace(textContent))
            {
                ModelState.AddModelError("", "Please enter text.");
                return View("Upload");
            }

            var record = _fileService.SaveText(textContent, deleteAfterCount);
            return RedirectToAction("UploadResult", new { code = record.Code, filename = "text-snippet" });
        }
    }
}
