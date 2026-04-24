/*
 * © 2026 RH-Factory
 * Author: Md. Zawad Hossain Rifat
 * All rights reserved.
 *
 * This source code is the property of RH-Factory.
 * Unauthorized copying or distribution is prohibited.
 */
namespace ShareAnywhere.Models
{
    public class FileRegistrationRequest
    {
        public required string FileName { get; set; }
        public string ContentType { get; set; } = "application/octet-stream";
        public long FileSize { get; set; }
        public int DeleteAfterCount { get; set; } = 1;
    }
}
