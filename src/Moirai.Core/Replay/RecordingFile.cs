using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moirai.Core.Replay;

// Gzipped JSON. Vector3 is fields-only, hence IncludeFields; enums are written by name so a
// recording stays readable by eye and survives an enum member being added.
public static class RecordingFile
{
    public const string Extension = ".json.gz";

    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter(), new ReadOnlySetConverterFactory() },
    };

    // System.Text.Json reads IReadOnlyList and IReadOnlyDictionary but not IReadOnlySet, which
    // the selection blacklist is
    private sealed class ReadOnlySetConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type t)
            => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlySet<>);

        public override JsonConverter CreateConverter(Type t, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(
                typeof(ReadOnlySetConverter<>).MakeGenericType(t.GetGenericArguments()[0]))!;
    }

    private sealed class ReadOnlySetConverter<T> : JsonConverter<IReadOnlySet<T>>
    {
        public override IReadOnlySet<T> Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<HashSet<T>>(ref reader, options) ?? [];

        public override void Write(Utf8JsonWriter writer, IReadOnlySet<T> value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.ToList(), options);
    }

    private static readonly JsonSerializerOptions Pretty = new(Options) { WriteIndented = true };

    public static string ToJson(Recording recording) => JsonSerializer.Serialize(recording, Pretty);

    public static Recording FromJson(string json)
        => JsonSerializer.Deserialize<Recording>(json, Options)
           ?? throw new InvalidDataException("not a recording");

    public static void Write(Recording recording, Stream destination)
    {
        using var gzip = new GZipStream(destination, CompressionLevel.Optimal, leaveOpen: true);
        JsonSerializer.Serialize(gzip, recording, Options);
    }

    public static Recording Read(Stream source)
    {
        using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true);
        return JsonSerializer.Deserialize<Recording>(gzip, Options)
               ?? throw new InvalidDataException("not a recording");
    }
}
