using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace C2PARemover;

/// <summary>
/// Provides functionality to detect and remove C2PA (Content Authenticity Initiative) metadata from JPEG and PNG images.
/// </summary>
public partial class C2PARemover
{
    // C2PA metadata markers
    private const string C2PA_NAMESPACE = "http://c2pa.org/";
    private const string C2PA_MANIFEST_TAG = "c2pa:manifest";
    private const string C2PA_CLAIM_TAG = "c2pa:claim";
    
    // JPEG specific markers
    private const ushort MARKER_SOI = 0xFFD8;  // Start of Image
    private const byte MARKER_APP1 = 0xE1;    // APP1 marker for XMP/EXIF data
    private const byte MARKER_APP11 = 0xEB;   // APP11 marker where C2PA also lives
    private const byte MARKER_SOS = 0xDA;     // Start of Scan
    private const byte MARKER_EOI = 0xD9;     // End of Image
    
    // PNG signature
    private static readonly byte[] PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    
    // JPEG signature
    private static readonly byte[] JPEG_SIGNATURE = [0xFF, 0xD8];
    
    // C2PA detection regex pattern
#if NET8_0_OR_GREATER
    private static readonly Regex C2PA_REGEX = GeneratedC2PaRegex();
#else
    private static readonly Regex C2PA_REGEX = new Regex(@"(?i)c2pa|contentauthenticity|contentcredentials|cai", RegexOptions.Compiled);
#endif
    
    /// <summary>
    /// Checks if an image has C2PA metadata.
    /// </summary>
    /// <param name="data">The image file data as a byte array.</param>
    /// <returns>True if C2PA metadata is detected, false otherwise.</returns>
    public static bool CheckC2PA(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return false;
        }
        
        // Detect image format and use appropriate checker
        if (IsJpeg(data))
        {
            return CheckC2PAJpeg(data);
        }

        if (IsPng(data))
        {
            return CheckC2PAPng(data);
        }

        // Unsupported format
        return false;
    }
    
    /// <summary>
    /// Removes C2PA metadata from an image.
    /// </summary>
    /// <param name="data">The image file data as a byte array.</param>
    /// <returns>The cleaned image data without C2PA metadata.</returns>
    /// <exception cref="ArgumentException">Thrown when the image format is unsupported.</exception>
    /// <exception cref="InvalidOperationException">Thrown when removal fails.</exception>
    public static byte[] RemoveC2PA(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Image data cannot be null or empty", nameof(data));
        }
        
        // Detect image format
        string format;
        if (IsJpeg(data))
        {
            format = "jpeg";
        }
        else if (IsPng(data))
        {
            format = "png";
        }
        else
        {
            throw new ArgumentException("Unsupported image format", nameof(data));
        }
        
        // Try standard library re-encoding method first (Smart Mode)
        try
        {
            // Decode image using SkiaSharp
            using SKBitmap? bitmap = SKBitmap.Decode(data);
            if (bitmap == null)
            {
                throw new InvalidOperationException("Failed to decode image");
            }
            
            // Create SKImage from bitmap
            using SKImage? image = SKImage.FromBitmap(bitmap);
            if (image == null)
            {
                throw new InvalidOperationException("Failed to create image from bitmap");
            }
            
            // Re-encode the image without metadata based on format
            SKData? encodedData = format switch
            {
                "jpeg" => image.Encode(SKEncodedImageFormat.Jpeg, 95),
                "png" => image.Encode(SKEncodedImageFormat.Png, 100),
                _ => null
            };

            if (encodedData != null)
            {
                byte[]? cleanedData = encodedData.ToArray();
                encodedData.Dispose();
                
                // Check if C2PA metadata is still present
                if (!CheckC2PA(cleanedData))
                {
                    return cleanedData;
                }
            }
        }
        catch
        {
            // Fall through to fallback method
        }

        return format switch
        {
            // Fallback to custom segment parsing if standard re-encoding fails or doesn't remove C2PA
            "jpeg" => RemoveC2PAFallbackJpeg(data),
            "png" => RemoveC2PAFallbackPng(data),
            _ => throw new InvalidOperationException($"No suitable removal method for format: {format}")
        };
    }
    
    private static bool IsJpeg(byte[] data)
    {
        return data.Length >= 2 && data[0] == JPEG_SIGNATURE[0] && data[1] == JPEG_SIGNATURE[1];
    }
    
    private static bool IsPng(byte[] data)
    {
        if (data.Length < PNG_SIGNATURE.Length)
        {
            return false;
        }
        
        for (int i = 0; i < PNG_SIGNATURE.Length; i++)
        {
            if (data[i] != PNG_SIGNATURE[i])
            {
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Checks if a JPEG image has C2PA metadata.
    /// </summary>
    private static bool CheckC2PAJpeg(byte[] data)
    {
        // Check all APP1 and APP11 segments
        int pos = 2; // Skip SOI marker
        
        while (pos < data.Length - 4)
        {
            // Check for marker
            if (data[pos] != 0xFF)
            {
                pos++;
                continue;
            }
            
            byte markerType = data[pos + 1];
            
            // If we've reached SOS, we're done checking metadata segments
            if (markerType == MARKER_SOS)
            {
                break;
            }

            switch (markerType)
            {
                // Check if it's an APP1 segment with XMP data
                // Get segment length (includes length bytes but not marker)
                case MARKER_APP1 when pos + 4 > data.Length:
                    pos += 2;
                    continue;
                case MARKER_APP1:
                {
                    ushort length = (ushort)((data[pos + 2] << 8) | data[pos + 3]);
                
                    if (pos + 2 + length > data.Length)
                    {
                        // Invalid length
                        pos += 2;
                        continue;
                    }
                
                    byte[] segmentData = new byte[length - 2];
                    Array.Copy(data, pos + 4, segmentData, 0, segmentData.Length);
                
                    // Check if it's XMP data
                    const string xmpPrefix = "http://ns.adobe.com/xap/1.0/";
                    if (segmentData.Length >= xmpPrefix.Length)
                    {
                        string segmentString = Encoding.UTF8.GetString(segmentData, 0, Math.Min(segmentData.Length, xmpPrefix.Length + 1000));
                    
                        if (segmentString.StartsWith(xmpPrefix))
                        {
                            string xmpString = Encoding.UTF8.GetString(segmentData);
                        
                            // Check for C2PA namespace
                            if (xmpString.Contains(C2PA_NAMESPACE))
                            {
                                return true;
                            }
                        
                            // Check for C2PA manifest or claim tags
                            if (xmpString.Contains(C2PA_MANIFEST_TAG) || xmpString.Contains(C2PA_CLAIM_TAG))
                            {
                                return true;
                            }
                        
                            // Use regex to check for C2PA related content
                            if (C2PA_REGEX.IsMatch(xmpString))
                            {
                                return true;
                            }
                        }
                    }
                
                    // Skip to next segment
                    pos += 2 + length;
                    break;
                }
                // APP11 - where C2PA data can also be found
                case MARKER_APP11:
                    // APP11 segment can contain C2PA data directly
                    return true;
                // Skip other APP segments
                case >= 0xE0 and <= 0xEF when pos + 4 > data.Length:
                    pos += 2;
                    continue;
                case >= 0xE0 and <= 0xEF:
                {
                    ushort length = (ushort)((data[pos + 2] << 8) | data[pos + 3]);
                    if (pos + 2 + length > data.Length)
                    {
                        pos += 2;
                        continue;
                    }
                    pos += 2 + length;
                    break;
                }
                default:
                    // Skip other markers
                    pos += 2;
                    break;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if a PNG image has C2PA metadata.
    /// </summary>
    private static bool CheckC2PAPng(byte[] data)
    {
        // Check for C2PA related strings in the PNG data
        // Look for common C2PA identifiers in iTXt or tEXt chunks
        string dataString = Encoding.UTF8.GetString(data);
        string lowerDataString = dataString.ToLowerInvariant();
        
        return lowerDataString.Contains("c2pa") ||
               lowerDataString.Contains("cai:") ||
               lowerDataString.Contains("contentauthenticity") ||
               lowerDataString.Contains("contentcredentials");
    }
    
    /// <summary>
    /// Fallback method for removing C2PA metadata from JPEG using custom segment parsing.
    /// </summary>
    private static byte[] RemoveC2PAFallbackJpeg(byte[] data)
    {
        // First check if this is a JPEG image
        if (data.Length < 2 || data[0] != 0xFF || data[1] != 0xD8)
        {
            throw new ArgumentException("Not a valid JPEG file", nameof(data));
        }
        
        // Parse using the SOI marker in first two bytes
        List<byte> result = [0xFF, 0xD8]; // Start with SOI marker
        
        // For each segment, decide whether to keep it or discard it
        int pos = 2; // Skip SOI marker
        bool foundSOS = false;
        
        while (pos < data.Length - 1)
        {
            // Check for marker starting with 0xFF
            if (data[pos] != 0xFF)
            {
                // Skip unexpected bytes until we find the start of a marker
                pos++;
                continue;
            }
            
            // Ensure there's enough data to read marker type
            if (pos + 1 >= data.Length)
            {
                break; // Reached end of data
            }
            
            byte markerType = data[pos + 1];
            
            // If we've reached SOS, copy the rest of the file
            if (markerType == MARKER_SOS) // Start of Scan
            {
                foundSOS = true;
                // Add SOS marker and copy the rest of the file (image data)
                for (int i = pos; i < data.Length; i++)
                {
                    result.Add(data[i]);
                }
                break;
            }
            
            // If it's EOI, we've reached the end
            if (markerType == MARKER_EOI) // End of Image
            {
                result.Add(0xFF);
                result.Add(MARKER_EOI);
                break;
            }
            
            // Handle markers that don't have length
            if (markerType is >= 0xD0 and <= 0xD7 or 0x01)
            {
                result.Add(0xFF);
                result.Add(markerType);
                pos += 2;
                continue;
            }
            
            // All other markers should have a length field
            if (pos + 4 > data.Length)
            {
                break; // Not enough data for length
            }
            
            // Get segment length (includes length bytes but not marker)
            ushort length = (ushort)((data[pos + 2] << 8) | data[pos + 3]);
            if (length < 2)
            {
                // Invalid length, skip marker and continue
                pos += 2;
                continue;
            }
            
            // Make sure there's enough data for the full segment
            if (pos + 2 + length > data.Length)
            {
                // Not enough data, skip to end
                break;
            }
            
            // Check if it's an APP1 or APP11 segment potentially containing C2PA data
            if (markerType == MARKER_APP1) // APP1 marker
            {
                bool containsC2PA = false;
                
                // Only check XMP segments containing namespace
                byte[] segmentData = new byte[length - 2];
                Array.Copy(data, pos + 4, segmentData, 0, segmentData.Length);
                
                const string xmpPrefix = "http://ns.adobe.com/xap/1.0/";
                if (segmentData.Length >= xmpPrefix.Length)
                {
                    string segmentString = Encoding.UTF8.GetString(segmentData, 0, Math.Min(segmentData.Length, xmpPrefix.Length + 1000));
                    
                    if (segmentString.StartsWith(xmpPrefix))
                    {
                        string xmpString = Encoding.UTF8.GetString(segmentData);
                        
                        // Check for C2PA namespace or tags
                        if (xmpString.Contains(C2PA_NAMESPACE) ||
                            xmpString.Contains(C2PA_MANIFEST_TAG) ||
                            xmpString.Contains(C2PA_CLAIM_TAG))
                        {
                            containsC2PA = true;
                        }
                        
                        // Also use regex for more comprehensive detection
                        if (C2PA_REGEX.IsMatch(xmpString))
                        {
                            containsC2PA = true;
                        }
                    }
                }
                
                // Only keep segment if it doesn't contain C2PA data
                if (!containsC2PA)
                {
                    for (int i = pos; i < pos + 2 + length; i++)
                    {
                        result.Add(data[i]);
                    }
                }
            }
            else if (markerType == MARKER_APP11) // APP11 marker, which often contains C2PA data
            {
                // Skip this segment as it might contain C2PA data
            }
            else
            {
                // Keep other segments
                for (int i = pos; i < pos + 2 + length; i++)
                {
                    result.Add(data[i]);
                }
            }
            
            // Move to next segment
            pos += 2 + length;
        }
        
        // If we didn't find the SOS marker, make sure we have an EOI marker at the end
#if NET8_0_OR_GREATER
        if (!foundSOS && (result.Count < 2 || result[^2] != 0xFF || result[^1] != MARKER_EOI))
#else
        if (!foundSOS && (result.Count < 2 || result[result.Count - 2] != 0xFF || result[result.Count - 1] != MARKER_EOI))
#endif
        {
            result.Add(0xFF);
            result.Add(MARKER_EOI); // Add EOI marker to ensure valid JPEG
        }
        
        return result.ToArray();
    }
    
    /// <summary>
    /// PNG chunk structure.
    /// </summary>
    private struct PngChunk
    {
        public uint Length;
        public string ChunkType;
        public byte[] Data;
        public uint Crc;
    }
    
    /// <summary>
    /// Extracts chunks from PNG data.
    /// </summary>
    private static List<PngChunk> ExtractPngChunks(byte[] data)
    {
        List<PngChunk> chunks = [];
        
        // Skip the PNG signature (8 bytes)
        int pos = 8;
        
        while (pos + 12 <= data.Length) // Minimum chunk size: 4 (length) + 4 (type) + 0 (data) + 4 (CRC)
        {
            // Read chunk length (4 bytes, big-endian)
            uint length = ((uint)data[pos] << 24) | ((uint)data[pos + 1] << 16) | ((uint)data[pos + 2] << 8) | data[pos + 3];
            pos += 4;
            
            // Read chunk type (4 bytes)
            string chunkType = Encoding.ASCII.GetString(data, pos, 4);
            pos += 4;
            
            // Check if there's enough data for the chunk
            if (pos + (int)length + 4 > data.Length)
            {
                break;
            }
            
            // Read chunk data
            byte[] chunkData = new byte[length];
            Array.Copy(data, pos, chunkData, 0, (int)length);
            pos += (int)length;
            
            // Read CRC (4 bytes)
            uint crc = ((uint)data[pos] << 24) | ((uint)data[pos + 1] << 16) | ((uint)data[pos + 2] << 8) | data[pos + 3];
            pos += 4;
            
            chunks.Add(new PngChunk
            {
                Length = length,
                ChunkType = chunkType,
                Data = chunkData,
                Crc = crc
            });
            
            // Break if we've reached the IEND chunk
            if (chunkType == "IEND")
            {
                break;
            }
        }
        
        return chunks;
    }
    
    /// <summary>
    /// Removes C2PA metadata from PNG by selectively copying non-C2PA chunks.
    /// </summary>
    private static byte[] RemoveC2PAFallbackPng(byte[] data)
    {
        // Extract all PNG chunks
        List<PngChunk> chunks = ExtractPngChunks(data);
        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("Failed to parse PNG chunks");
        }
        
        // Create a new buffer for the cleaned PNG
        using MemoryStream buf = new MemoryStream();
        
        // Write PNG signature
        buf.Write(PNG_SIGNATURE, 0, PNG_SIGNATURE.Length);
        
        bool removed = false;
        
        // Copy all chunks except those containing C2PA data
        foreach (PngChunk chunk in chunks)
        {
            // Check text chunks for C2PA content
            bool isC2PAChunk = false;
            
            if (chunk.ChunkType is "iTXt" or "tEXt")
            {
                string chunkData = Encoding.UTF8.GetString(chunk.Data);
                string lowerChunkData = chunkData.ToLowerInvariant();
                
                if (lowerChunkData.Contains("c2pa") ||
                    lowerChunkData.Contains("contentauthenticity") ||
                    lowerChunkData.Contains("cai:"))
                {
                    isC2PAChunk = true;
                    removed = true;
                }
            }
            
            // Skip C2PA chunks
            if (isC2PAChunk)
            {
                continue;
            }
            
            // Write chunk length (4 bytes, big-endian)
            buf.WriteByte((byte)(chunk.Length >> 24));
            buf.WriteByte((byte)(chunk.Length >> 16));
            buf.WriteByte((byte)(chunk.Length >> 8));
            buf.WriteByte((byte)chunk.Length);
            
            // Write chunk type (4 bytes)
            byte[] chunkTypeBytes = Encoding.ASCII.GetBytes(chunk.ChunkType);
            buf.Write(chunkTypeBytes, 0, 4);
            
            // Write chunk data
            buf.Write(chunk.Data, 0, chunk.Data.Length);
            
            // Write CRC (4 bytes, big-endian)
            buf.WriteByte((byte)(chunk.Crc >> 24));
            buf.WriteByte((byte)(chunk.Crc >> 16));
            buf.WriteByte((byte)(chunk.Crc >> 8));
            buf.WriteByte((byte)chunk.Crc);
        }
        
        if (!removed)
        {
            // No C2PA chunks found, return original data
            return data;
        }
        
        byte[] cleanedData = buf.ToArray();

        // Verify removal was successful
        return CheckC2PA(cleanedData) ? throw new InvalidOperationException("PNG fallback removal failed verification check") : cleanedData;
    }

#if NET8_0_OR_GREATER
    [GeneratedRegex(@"(?i)c2pa|contentauthenticity|contentcredentials|cai", RegexOptions.Compiled, "en-US")]
    private static partial Regex GeneratedC2PaRegex();
#endif
}

