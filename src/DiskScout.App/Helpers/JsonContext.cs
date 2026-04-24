using System.Text.Json;
using System.Text.Json.Serialization;
using DiskScout.Models;

namespace DiskScout.Helpers;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ScanResult))]
[JsonSerializable(typeof(FileSystemNode))]
[JsonSerializable(typeof(InstalledProgram))]
[JsonSerializable(typeof(OrphanCandidate))]
[JsonSerializable(typeof(ScanProgress))]
[JsonSerializable(typeof(DeltaResult))]
[JsonSerializable(typeof(DeltaEntry))]
public sealed partial class DiskScoutJsonContext : JsonSerializerContext;
