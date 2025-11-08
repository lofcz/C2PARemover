using System;
using System.IO;
using System.Linq;
using C2PARemover;

namespace C2PARemover.Cli;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            PrintUsage();
            return 1;
        }

        string mode = args[0];

        if (mode == "check-dir")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Please specify a directory");
                return 1;
            }

            string dirPath = args[1];
            return CheckDirectory(dirPath);
        }

        if (args.Length < 2)
        {
            Console.WriteLine("Please specify an image file");
            return 1;
        }

        string filePath = args[1];

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Error: File not found: {filePath}");
            return 1;
        }

        byte[] data;
        try
        {
            data = File.ReadAllBytes(filePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading file: {ex.Message}");
            return 1;
        }

        switch (mode)
        {
            case "check":
                return CheckFile(data);

            case "remove":
                return RemoveC2PA(data, filePath);

            default:
                Console.WriteLine("Invalid mode. Use 'check', 'remove', or 'check-dir'");
                PrintUsage();
                return 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("Usage: c2paremover [check|remove] <image_file>");
        Console.WriteLine("Examples:");
        Console.WriteLine("  c2paremover check image.jpg");
        Console.WriteLine("  c2paremover remove image.jpg");
        Console.WriteLine("  c2paremover check-dir directory");
    }

    static int CheckFile(byte[] data)
    {
        if (C2PARemover.CheckC2PA(data))
        {
            Console.WriteLine("⚠️  C2PA metadata detected");
            return 1;
        }

        Console.WriteLine("✓ No C2PA metadata found");
        return 0;
    }

    static int RemoveC2PA(byte[] data, string filePath)
    {
        if (!C2PARemover.CheckC2PA(data))
        {
            Console.WriteLine("No C2PA metadata found, no changes needed");
            return 0;
        }

        Console.WriteLine("Removing C2PA metadata...");
        byte[] newData;
        try
        {
            newData = C2PARemover.RemoveC2PA(data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        if (newData.Length == data.Length && newData.SequenceEqual(data))
        {
            Console.WriteLine("No changes made");
            return 0;
        }

        string? directory = Path.GetDirectoryName(filePath);
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        string extension = Path.GetExtension(filePath);
        string cleanFileName = fileNameWithoutExt + "_cleaned" + extension;
        string cleanPath = directory != null ? Path.Combine(directory, cleanFileName) : cleanFileName;

        try
        {
            File.WriteAllBytes(cleanPath, newData);
            double sizePercent = (double)newData.Length / data.Length * 100;
            Console.WriteLine($"✓ Cleaned file saved as {cleanPath} ({sizePercent:F1}% of original size)");

            // Verify the cleaned file
            byte[] cleanData = File.ReadAllBytes(cleanPath);
            if (C2PARemover.CheckC2PA(cleanData))
            {
                Console.WriteLine("⚠️  Warning: C2PA metadata still detected in cleaned file");
                return 1;
            }

            Console.WriteLine("✓ Verification: No C2PA metadata in cleaned file");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving file: {ex.Message}");
            return 1;
        }
    }

    static int CheckDirectory(string dirPath)
    {
        if (!Directory.Exists(dirPath))
        {
            Console.WriteLine($"Error: Directory not found: {dirPath}");
            return 1;
        }

        string[] imageExtensions = [".jpg", ".jpeg", ".png"];
        string[] files = Directory.GetFiles(dirPath)
            .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToArray();

        int imagesChecked = 0;
        int imagesWithC2PA = 0;

        foreach (string filePath in files)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                imagesChecked++;
                bool hasC2PA = C2PARemover.CheckC2PA(data);

                if (hasC2PA)
                {
                    imagesWithC2PA++;
                    Console.WriteLine($"⚠️  {Path.GetFileName(filePath)}: C2PA metadata detected");
                }
                else
                {
                    Console.WriteLine($"✓ {Path.GetFileName(filePath)}: No C2PA metadata");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading file {filePath}: {ex.Message}");
            }
        }

        Console.WriteLine($"\nSummary: Checked {imagesChecked} images, found C2PA metadata in {imagesWithC2PA} images");
        return imagesWithC2PA > 0 ? 1 : 0;
    }
}
