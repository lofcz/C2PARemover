# C2PARemover CLI

A command-line tool for detecting and removing C2PA (Content Authenticity Initiative) metadata from JPEG and PNG images.

## Installation

### Install as .NET Tool

```bash
# Install from NuGet
dotnet tool install -g C2PARemover.Cli

# Or install from a local package
dotnet tool install -g --add-source ./nupkg C2PARemover.Cli
```

### Build from Source

```bash
# Build the tool
dotnet build C2PARemover.Cli/C2PARemover.Cli.csproj

# Run directly
dotnet run --project C2PARemover.Cli/C2PARemover.Cli.csproj -- check image.jpg
```

## Usage

### Check for C2PA Metadata

```bash
c2paremover check image.jpg
```

Output:
- `✓ No C2PA metadata found` (exit code 0)
- `⚠️  C2PA metadata detected` (exit code 1)

### Remove C2PA Metadata

```bash
c2paremover remove image.jpg
```

This creates a cleaned copy as `image.jpg.cleaned.jpg` and verifies the removal.

### Check Directory

```bash
c2paremover check-dir /path/to/directory
```

Scans all JPEG and PNG files in the directory and reports which ones contain C2PA metadata.

## Packaging as .NET Tool

### Create NuGet Package

```bash
# Pack the tool
dotnet pack C2PARemover.Cli/C2PARemover.Cli.csproj -c Release

# The package will be created in: C2PARemover.Cli/bin/Release/C2PARemover.Cli.1.0.0.nupkg
```

### Publish to NuGet

```bash
# Publish to NuGet.org (requires API key)
dotnet nuget push C2PARemover.Cli/bin/Release/C2PARemover.Cli.1.0.0.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```

### Install from Local Package

```bash
# Create package
dotnet pack C2PARemover.Cli/C2PARemover.Cli.csproj -c Release

# Install from local package
dotnet tool install -g --add-source ./C2PARemover.Cli/bin/Release C2PARemover.Cli
```

### Update Tool

```bash
dotnet tool update -g C2PARemover.Cli
```

### Uninstall Tool

```bash
dotnet tool uninstall -g C2PARemover.Cli
```

## Examples

```bash
# Check a single image
c2paremover check photo.jpg

# Remove C2PA from an image
c2paremover remove photo.jpg
# Creates: photo.jpg.cleaned.jpg

# Check all images in a directory
c2paremover check-dir ./images
```

## Exit Codes

- `0` - Success (no C2PA found, or removal successful)
- `1` - C2PA detected, or error occurred

