using Microsoft.AspNetCore.Mvc;
using ShareAnywhere.Services;

namespace ShareAnywhere.Controllers
{
    public class FileController : Controller
    {
        private readonly FileStoreService _fileService;

        public FileController(FileStoreService fileService)
        {
            _fileService = fileService;
        }

        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Upload(IFormFile uploadedFile)
        {
            if (uploadedFile == null || uploadedFile.Length == 0)
            {
                ModelState.AddModelError("", "Please select a file.");
                return View();
            }

            var record = _fileService.SaveFile(uploadedFile);
            return RedirectToAction("UploadResult", new { code = record.Code });
        }

        [HttpGet]
        public IActionResult UploadResult(string code)
        {
            ViewBag.Code = code;
            ViewBag.DownloadLink = Url.Action("Download", new { code });
            return View();
        }

        [HttpGet("Download/{code}")]
        public IActionResult Download(string code)
        {
            var record = _fileService.GetFile(code);
            if (record == null) return NotFound("Invalid download code.");

            var fileBytes = System.IO.File.ReadAllBytes(record.FilePath);
            return File(fileBytes, "application/octet-stream", record.FileName);
        }

        [HttpGet("AutoDownload/{code}")]
        public IActionResult AutoDownload(string code)
        {
            var record = _fileService.GetFile(code);
            if (record == null) return NotFound("Invalid code.");

            ViewBag.FileUrl = Url.Action("Download", new { code });
            return View("Download");
        }
    }
}
