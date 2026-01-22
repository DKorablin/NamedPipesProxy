# Code Coverage Guide

This guide explains how to generate and view code coverage reports for the NamedPipesProxy solution using locally-installed tools without external dependencies.

## Overview

The solution uses **Coverlet** for code coverage collection. Coverlet is integrated as a NuGet package and generates coverage reports in multiple formats:

- **Cobertura XML** - Standard format compatible with many CI/CD tools
- **JSON Summary** - Machine-readable coverage metrics
- **OpenCover XML** - Microsoft OpenCover format for detailed analysis

## Prerequisites

- .NET 9 SDK (or .NET Framework 4.8 for legacy builds)
- PowerShell 7+ (for Windows scripts) or Bash (for Linux/macOS)

## Generating Coverage Reports Locally

### Windows (PowerShell)

Run the PowerShell script from the repository root:

Using default settings (Release configuration, net9 framework)
```powershell
.\.github\scripts\Generate-CoverageReport.ps1
```

With custom configuration
```powershell
.\.github\scripts\Generate-CoverageReport.ps1 -Configuration Debug -Framework net48
```

With custom output path
```powershell
.\.github\scripts\Generate-CoverageReport.ps1 -OutputPath "my-coverage"
```


**Parameters:**
- `-Configuration` (default: Release): Build configuration
- `-Framework` (default: net9): Target framework (net9, net48, etc.)
- `-OutputPath` (default: coverage): Output directory for coverage files