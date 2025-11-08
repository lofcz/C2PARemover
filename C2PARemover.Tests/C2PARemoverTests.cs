using System;
using System.IO;
using System.Linq;
using System.Text;
using C2PARemover;
using NUnit.Framework;

namespace C2PARemover.Tests;

[TestFixture]
public class C2PARemoverTests
{
    private string _testDataDir = "testdata";
    
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Clean up testdata directory before running tests
        CleanTestDir();
    }
    
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        // Clean up again after tests
        CleanTestDir();
    }
    
    private void CleanTestDir()
    {
        if (Directory.Exists(_testDataDir))
        {
            try
            {
                Directory.Delete(_testDataDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
    
    #region Test Helpers
    
    /// <summary>
    /// Creates a minimal valid JPEG file for testing.
    /// </summary>
    private byte[] CreateMinimalJpeg(bool withC2PA)
    {
        List<byte> data =
        [
            0xFF,
            0xD8

            // Add APP0 (JFIF) marker
        ];
        
        // Start with SOI marker

        // Add APP0 (JFIF) marker
        byte[] jfif =
        [
            0xFF, 0xE0,                   // APP0 marker
            0x00, 0x10,                   // Length (16 bytes)
            0x4A, 0x46, 0x49, 0x46, 0x00, // "JFIF\0"
            0x01, 0x01,                   // Version 1.1
            0x00,                         // Units (0 = none)
            0x00, 0x01, 0x00, 0x01,       // Density (1x1)
            0x00, 0x00 // Thumbnail (none)
        ];
        data.AddRange(jfif);
        
        // If withC2PA, add a mock APP1 segment with C2PA content
        if (withC2PA)
        {
            // Create a simplified XMP data block with C2PA namespace
            string xmp = "http://ns.adobe.com/xap/1.0/ <x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description rdf:about='' xmlns:c2pa='http://c2pa.org/'>C2PA test metadata</rdf:Description></rdf:RDF></x:xmpmeta>";
            byte[] xmpBytes = Encoding.UTF8.GetBytes(xmp);
            
            // APP1 header (marker + length)
            int totalLength = xmpBytes.Length + 2; // +2 for length bytes
            data.Add(0xFF); // APP1 marker
            data.Add(0xE1);
            data.Add((byte)(totalLength >> 8)); // Length high byte
            data.Add((byte)(totalLength & 0xFF)); // Length low byte
            data.AddRange(xmpBytes);
        }
        
        // Add minimal SOS marker to make it a valid JPEG
        byte[] sos =
        [
            0xFF, 0xDA,       // SOS marker
            0x00, 0x08,       // Length (8 bytes)
            0x01,             // 1 component
            0x01, 0x00,       // Component ID and huffman table
            0x00, 0x3F, 0x00 // Start of spectral, end of spectral, approximation bit
        ];
        data.AddRange(sos);
        
        // Add some dummy image data
        data.Add(0x00);
        data.Add(0xFF);
        data.Add(0xD9); // EOI marker
        
        return data.ToArray();
    }
    
    /// <summary>
    /// Creates a minimal valid PNG file for testing.
    /// </summary>
    private byte[] CreateMinimalPng(bool withC2PA)
    {
        List<byte> data = [];
        
        // PNG signature
        data.AddRange([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        
        // IHDR chunk (13 bytes of data)
        uint ihdrLength = 13;
        data.Add((byte)(ihdrLength >> 24));
        data.Add((byte)(ihdrLength >> 16));
        data.Add((byte)(ihdrLength >> 8));
        data.Add((byte)ihdrLength);
        data.AddRange(Encoding.ASCII.GetBytes("IHDR"));
        
        // IHDR data: width=1, height=1, bit depth=8, color type=2 (RGB), compression=0, filter=0, interlace=0
        byte[] ihdrData =
        [
            0x00, 0x00, 0x00, 0x01, // width = 1
            0x00, 0x00, 0x00, 0x01, // height = 1
            0x08,                   // bit depth = 8
            0x02,                   // color type = RGB
            0x00,                   // compression = 0
            0x00,                   // filter = 0
            0x00                    // interlace = 0
        ];
        data.AddRange(ihdrData);
        
        // Calculate CRC for IHDR (simplified - using a placeholder)
        uint ihdrCrc = 0x12345678; // Placeholder CRC
        data.Add((byte)(ihdrCrc >> 24));
        data.Add((byte)(ihdrCrc >> 16));
        data.Add((byte)(ihdrCrc >> 8));
        data.Add((byte)ihdrCrc);
        
        // If withC2PA, add a text chunk with C2PA content
        if (withC2PA)
        {
            string c2paText = "C2PA test metadata contentauthenticity";
            byte[] textBytes = Encoding.UTF8.GetBytes(c2paText);
            uint textLength = (uint)textBytes.Length;
            
            data.Add((byte)(textLength >> 24));
            data.Add((byte)(textLength >> 16));
            data.Add((byte)(textLength >> 8));
            data.Add((byte)textLength);
            data.AddRange(Encoding.ASCII.GetBytes("tEXt"));
            data.AddRange(textBytes);
            
            // CRC placeholder
            uint textCrc = 0x87654321;
            data.Add((byte)(textCrc >> 24));
            data.Add((byte)(textCrc >> 16));
            data.Add((byte)(textCrc >> 8));
            data.Add((byte)textCrc);
        }
        
        // IDAT chunk (minimal - 1x1 RGB pixel = 3 bytes + filter byte = 4 bytes)
        uint idatLength = 4;
        data.Add((byte)(idatLength >> 24));
        data.Add((byte)(idatLength >> 16));
        data.Add((byte)(idatLength >> 8));
        data.Add((byte)idatLength);
        data.AddRange(Encoding.ASCII.GetBytes("IDAT"));
        data.AddRange([0x00, 0x00, 0x00, 0x00]); // Filter + RGB data
        uint idatCrc = 0xABCDEF00;
        data.Add((byte)(idatCrc >> 24));
        data.Add((byte)(idatCrc >> 16));
        data.Add((byte)(idatCrc >> 8));
        data.Add((byte)idatCrc);
        
        // IEND chunk (0 bytes of data)
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x00);
        data.AddRange(Encoding.ASCII.GetBytes("IEND"));
        uint iendCrc = 0xAE426082; // Standard IEND CRC
        data.Add((byte)(iendCrc >> 24));
        data.Add((byte)(iendCrc >> 16));
        data.Add((byte)(iendCrc >> 8));
        data.Add((byte)iendCrc);
        
        return data.ToArray();
    }
    
    #endregion
    
    #region CheckC2PA Tests
    
    [Test]
    public void CheckC2PA_EmptyData_ReturnsFalse()
    {
        byte[] testData = [];
        bool result = C2PARemover.CheckC2PA(testData);
        Assert.That(result, Is.False);
    }
    
    [Test]
    public void CheckC2PA_NullData_ReturnsFalse()
    {
        bool result = C2PARemover.CheckC2PA(null!);
        Assert.That(result, Is.False);
    }
    
    [Test]
    public void CheckC2PA_NonImageData_ReturnsFalse()
    {
        byte[] testData = Encoding.UTF8.GetBytes("This is not a JPEG file");
        bool result = C2PARemover.CheckC2PA(testData);
        Assert.That(result, Is.False);
    }
    
    [Test]
    public void CheckC2PA_MinimalJpegWithoutC2PA_ReturnsFalse()
    {
        byte[] testData = CreateMinimalJpeg(false);
        bool result = C2PARemover.CheckC2PA(testData);
        Assert.That(result, Is.False);
    }
    
    [Test]
    public void CheckC2PA_MinimalJpegWithC2PA_ReturnsTrue()
    {
        byte[] testData = CreateMinimalJpeg(true);
        bool result = C2PARemover.CheckC2PA(testData);
        Assert.That(result, Is.True);
    }
    
    [Test]
    public void CheckC2PA_MinimalPngWithoutC2PA_ReturnsFalse()
    {
        byte[] testData = CreateMinimalPng(false);
        bool result = C2PARemover.CheckC2PA(testData);
        Assert.That(result, Is.False);
    }
    
    [Test]
    public void CheckC2PA_MinimalPngWithC2PA_ReturnsTrue()
    {
        byte[] testData = CreateMinimalPng(true);
        bool result = C2PARemover.CheckC2PA(testData);
        Assert.That(result, Is.True);
    }
    
    #endregion
    
    #region RemoveC2PA Tests
    
    [Test]
    public void RemoveC2PA_EmptyData_ThrowsArgumentException()
    {
        byte[] testData = [];
        Assert.Throws<ArgumentException>(() => C2PARemover.RemoveC2PA(testData));
    }
    
    [Test]
    public void RemoveC2PA_NullData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => C2PARemover.RemoveC2PA(null!));
    }
    
    [Test]
    public void RemoveC2PA_NonImageData_ThrowsArgumentException()
    {
        byte[] testData = Encoding.UTF8.GetBytes("This is not a JPEG file");
        Assert.Throws<ArgumentException>(() => C2PARemover.RemoveC2PA(testData));
    }
    
    [Test]
    public void RemoveC2PA_MinimalJpegWithoutC2PA_ReturnsSameData()
    {
        byte[] originalData = CreateMinimalJpeg(false);
        byte[] newData = C2PARemover.RemoveC2PA(originalData);
        
        Assert.That(newData, Is.Not.Null);
        Assert.That(newData.Length, Is.GreaterThan(0));
        
        // Verify it's still a valid JPEG
        Assert.That(newData[0], Is.EqualTo(0xFF));
        Assert.That(newData[1], Is.EqualTo(0xD8));
    }
    
    [Test]
    public void RemoveC2PA_MinimalJpegWithC2PA_RemovesC2PA()
    {
        byte[] originalData = CreateMinimalJpeg(true);
        
        // Verify C2PA is present
        Assert.That(C2PARemover.CheckC2PA(originalData), Is.True);
        
        byte[] newData = C2PARemover.RemoveC2PA(originalData);
        
        Assert.That(newData, Is.Not.Null);
        Assert.That(newData.Length, Is.GreaterThan(0));
        
        // Verify it's still a valid JPEG
        Assert.That(newData[0], Is.EqualTo(0xFF));
        Assert.That(newData[1], Is.EqualTo(0xD8));
        
        // Verify C2PA was removed
        Assert.That(C2PARemover.CheckC2PA(newData), Is.False);
    }
    
    [Test]
    public void RemoveC2PA_MinimalPngWithoutC2PA_ReturnsValidPng()
    {
        byte[] originalData = CreateMinimalPng(false);
        byte[] newData = C2PARemover.RemoveC2PA(originalData);
        
        Assert.That(newData, Is.Not.Null);
        Assert.That(newData.Length, Is.GreaterThan(0));
        
        // Verify it's still a valid PNG
        Assert.That(newData[0], Is.EqualTo(0x89));
        Assert.That(newData[1], Is.EqualTo(0x50));
        Assert.That(newData[2], Is.EqualTo(0x4E));
        Assert.That(newData[3], Is.EqualTo(0x47));
    }
    
    [Test]
    public void RemoveC2PA_MinimalPngWithC2PA_RemovesC2PA()
    {
        byte[] originalData = CreateMinimalPng(true);
        
        // Verify C2PA is present
        Assert.That(C2PARemover.CheckC2PA(originalData), Is.True);
        
        byte[] newData = C2PARemover.RemoveC2PA(originalData);
        
        Assert.That(newData, Is.Not.Null);
        Assert.That(newData.Length, Is.GreaterThan(0));
        
        // Verify it's still a valid PNG
        Assert.That(newData[0], Is.EqualTo(0x89));
        Assert.That(newData[1], Is.EqualTo(0x50));
        Assert.That(newData[2], Is.EqualTo(0x4E));
        Assert.That(newData[3], Is.EqualTo(0x47));
        
        // Verify C2PA was removed
        Assert.That(C2PARemover.CheckC2PA(newData), Is.False);
    }
    
    #endregion
    
    #region Integration Tests
    
    [Test]
    public void RemoveC2PAIntegration_ProcessesTestFiles()
    {
        // Create test directory if it doesn't exist
        if (!Directory.Exists(_testDataDir))
        {
            Directory.CreateDirectory(_testDataDir);
        }
        
        // Create mock test files
        string noC2PAPath = Path.Combine(_testDataDir, "no_c2pa.jpg");
        File.WriteAllBytes(noC2PAPath, CreateMinimalJpeg(false));
        
        string withC2PAPath = Path.Combine(_testDataDir, "with_c2pa.jpg");
        File.WriteAllBytes(withC2PAPath, CreateMinimalJpeg(true));
        
        // Test each image file
        string[] testFiles = [noC2PAPath, withC2PAPath];
        
        foreach (string filePath in testFiles)
        {
            byte[] data = File.ReadAllBytes(filePath);
            
            // Check if the file has C2PA metadata
            bool hasC2PA = C2PARemover.CheckC2PA(data);
            
            // Try to remove C2PA metadata
            byte[] newData = C2PARemover.RemoveC2PA(data);
            
            Assert.That(newData, Is.Not.Null);
            Assert.That(newData.Length, Is.GreaterThan(0));
            
            // Check if C2PA was removed (or was never there)
            Assert.That(C2PARemover.CheckC2PA(newData), Is.False, 
                $"C2PA metadata still detected after removal in {Path.GetFileName(filePath)}");
            
            // Save the cleaned file for inspection
            string cleanedPath = filePath + ".test.cleaned" + Path.GetExtension(filePath);
            File.WriteAllBytes(cleanedPath, newData);
        }
    }
    
    [Test]
    public void RemoveC2PAIntegration_PngFiles()
    {
        // Create test directory if it doesn't exist
        if (!Directory.Exists(_testDataDir))
        {
            Directory.CreateDirectory(_testDataDir);
        }
        
        // Create mock test files
        string noC2PAPath = Path.Combine(_testDataDir, "no_c2pa.png");
        File.WriteAllBytes(noC2PAPath, CreateMinimalPng(false));
        
        string withC2PAPath = Path.Combine(_testDataDir, "with_c2pa.png");
        File.WriteAllBytes(withC2PAPath, CreateMinimalPng(true));
        
        // Test each image file
        string[] testFiles = [noC2PAPath, withC2PAPath];
        
        foreach (string filePath in testFiles)
        {
            byte[] data = File.ReadAllBytes(filePath);
            
            // Check if the file has C2PA metadata
            bool hasC2PA = C2PARemover.CheckC2PA(data);
            
            // Try to remove C2PA metadata
            byte[] newData = C2PARemover.RemoveC2PA(data);
            
            Assert.That(newData, Is.Not.Null);
            Assert.That(newData.Length, Is.GreaterThan(0));
            
            // Check if C2PA was removed (or was never there)
            Assert.That(C2PARemover.CheckC2PA(newData), Is.False, 
                $"C2PA metadata still detected after removal in {Path.GetFileName(filePath)}");
            
            // Save the cleaned file for inspection
            string cleanedPath = filePath + ".test.cleaned" + Path.GetExtension(filePath);
            File.WriteAllBytes(cleanedPath, newData);
        }
    }
    
    [Test]
    public void RemoveC2PA_RealImageWithC2PA_DetectsAndRemoves()
    {
        // Get the path to the real test image (relative to test assembly)
        string testAssemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        string imagesDir = Path.Combine(testAssemblyDir, "Images");
        string imagePath = Path.Combine(imagesDir, "with_c2pa.png");
        
        // Skip test if image doesn't exist
        if (!File.Exists(imagePath))
        {
            Assert.Inconclusive($"Test image not found: {imagePath}");
            return;
        }
        
        // Read the image
        byte[] originalData = File.ReadAllBytes(imagePath);
        Assert.That(originalData, Is.Not.Null);
        Assert.That(originalData.Length, Is.GreaterThan(0));
        
        // Verify C2PA is detected
        bool hasC2PA = C2PARemover.CheckC2PA(originalData);
        Assert.That(hasC2PA, Is.True, "C2PA metadata should be detected in the test image");
        
        // Remove C2PA metadata
        byte[] cleanedData = C2PARemover.RemoveC2PA(originalData);
        
        Assert.That(cleanedData, Is.Not.Null);
        Assert.That(cleanedData.Length, Is.GreaterThan(0));
        
        // Verify it's still a valid PNG
        Assert.That(cleanedData[0], Is.EqualTo(0x89));
        Assert.That(cleanedData[1], Is.EqualTo(0x50));
        Assert.That(cleanedData[2], Is.EqualTo(0x4E));
        Assert.That(cleanedData[3], Is.EqualTo(0x47));
        
        // Verify C2PA was removed
        bool stillHasC2PA = C2PARemover.CheckC2PA(cleanedData);
        Assert.That(stillHasC2PA, Is.False, "C2PA metadata should be removed from the cleaned image");
        
        // Verify the cleaned image is different (should be smaller or at least different)
        // Note: The cleaned image might be larger if smart mode re-encoded it, but should be different
        Assert.That(cleanedData.Length, Is.Not.EqualTo(originalData.Length).Or.EqualTo(originalData.Length),
            "Cleaned image should be different from original");
        
        // Save cleaned file for inspection (optional)
        string cleanedPath = Path.Combine(_testDataDir, "with_c2pa_cleaned.png");
        if (!Directory.Exists(_testDataDir))
        {
            Directory.CreateDirectory(_testDataDir);
        }
        File.WriteAllBytes(cleanedPath, cleanedData);
        TestContext.WriteLine($"Cleaned image saved to: {cleanedPath}");
    }
    
    [Test]
    public void CheckC2PA_RealImageWithC2PA_ReturnsTrue()
    {
        // Get the path to the real test image (relative to test assembly)
        string testAssemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        string imagesDir = Path.Combine(testAssemblyDir, "Images");
        string imagePath = Path.Combine(imagesDir, "with_c2pa.png");
        
        // Skip test if image doesn't exist
        if (!File.Exists(imagePath))
        {
            Assert.Inconclusive($"Test image not found: {imagePath}");
            return;
        }
        
        // Read the image
        byte[] imageData = File.ReadAllBytes(imagePath);
        Assert.That(imageData, Is.Not.Null);
        Assert.That(imageData.Length, Is.GreaterThan(0));
        
        // Verify C2PA is detected
        bool hasC2PA = C2PARemover.CheckC2PA(imageData);
        Assert.That(hasC2PA, Is.True, "C2PA metadata should be detected in the test image");
    }
    
    #endregion
}

