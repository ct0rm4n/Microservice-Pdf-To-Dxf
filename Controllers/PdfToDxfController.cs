using FileManager.Services;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.IO.Compression;

[ApiController]
[Route("[controller]")]
public class PdfToDxfController : ControllerBase
{
    private readonly FileConverter _fileConverter;
    public PdfToDxfController(FileConverter fileConverter) { _fileConverter = fileConverter; }
    [HttpPost]
    public IActionResult ConvertToDxf(IFormFileCollection files)
    {
        FileStream stream = null;
        try {
            if(files == null || files.Count() == 0)
                return NotFound();
            string directoryPath = "C:\\Users\\usuario\\source\\repos\\FileManager\\PdfToDxf\\export"; // Replace with your directory path
            DeletFilesInPath(directoryPath);
            var pathGuid = Guid.NewGuid().ToString();
            directoryPath = Path.Combine(directoryPath);
            if (!Directory.Exists(directoryPath)) 
                Directory.CreateDirectory(directoryPath);
            var dxfPath = Path.Combine(directoryPath, "dxf");
            //Directory.CreateDirectory(dxfPath);
            string uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(files.First().FileName);
            string filePath = Path.Combine(directoryPath, uniqueFileName);
            if (System.IO.Directory.Exists(directoryPath))
            {
                try
                {
                    using (stream = new FileStream(filePath, FileMode.Create))
                    {
                        files.First().CopyTo(stream);
                        stream.Close();
                        stream.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error folder secrurity: {ex.InnerException}");
                }
            }
            
            string zipFileName = "archive.zip";
            string originalFileName = files.First().FileName;
            Console.WriteLine($"Saved file as: {uniqueFileName}");
            Console.WriteLine($"Original file name: {originalFileName}");
            if (Directory.Exists(dxfPath))
                Directory.CreateDirectory(dxfPath);
            //if (!Directory.Exists(dxfPath))
                //DeletFilesInPath(dxfPath);
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            _fileConverter.GetFromImages(directoryPath, uniqueFileName);
            // Create the ZIP archive in the temporary directory
            string zipPath = Path.Combine(tempDir, zipFileName);
            ZipFile.CreateFromDirectory(Path.Combine(directoryPath,"dxf"), zipPath);
            byte[] zipBytes = System.IO.File.ReadAllBytes(zipPath);            
            Directory.Delete(tempDir, true);
            return File(zipBytes, "application/zip", zipFileName);
        }
        catch(Exception ex)
        {
            if (stream != null)
            {
                stream.Close();
            }
            return Problem(ex.Message,null, statusCode: 500, "Exception");
        }
    }

    public static void DeletFilesInPath(string directoryPath)
    {
        string[] filesInDirectory = Directory.GetFiles(directoryPath);
        foreach (string f in filesInDirectory)
        {
            System.IO.File.Delete(f);
        }
    }
    static List<string> GetFilesInDirectory(string directoryPath)
    {
        List<string> files = new List<string>();

        if (Directory.Exists(directoryPath))
        {
            files.AddRange(Directory.GetFiles(directoryPath));
            files.AddRange(Directory.GetDirectories(directoryPath));

            foreach (var subDirectory in Directory.GetDirectories(directoryPath))
            {
                files.AddRange(GetFilesInDirectory(subDirectory));
            }
        }

        return files;
    }
    static void Zip()
    {
        string directoryPath = "C:\\Users\\usuario\\source\\repos\\FileManager\\export"; // Replace with your directory path
        string zipFilePath = "archive.zip"; // Replace with your desired ZIP file path

        List<string> files = GetFilesInDirectory(directoryPath);

        if (files.Count > 0)
        {
            CreateZipArchive(zipFilePath, files);

            Console.WriteLine($"Created ZIP archive: {zipFilePath}");
        }
        else
        {
            Console.WriteLine("No files found in the directory.");
        }
    }
    static void CreateZipArchive(string zipPath, IEnumerable<string> files)
    {
        using (FileStream zipStream = new FileStream(zipPath, FileMode.Create))
        {
            using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (string file in files)
                {
                    if (System.IO.File.Exists(file))
                    {
                        string entryName = Path.GetRelativePath(zipPath, file).Replace(Path.DirectorySeparatorChar, '/');
                        archive.CreateEntryFromFile(file, entryName);
                    }
                }
            }
        }
    }
}
