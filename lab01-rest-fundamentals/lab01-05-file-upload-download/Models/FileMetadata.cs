using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

[Table("files")]
public class FileMetadata
{
    [Key]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("filename")]
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [Column("content_type")]
    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = "";

    [Column("size")]
    [JsonPropertyName("size")]
    public long Size { get; set; }

    [Column("object_key")]
    [JsonPropertyName("object_key")]
    public string ObjectKey { get; set; } = "";

    [Column("uploaded_at")]
    [JsonPropertyName("uploaded_at")]
    public DateTime UploadedAt { get; set; }
}
