using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("products")]
public class Product
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("price")]
    public decimal Price { get; set; }

    [Column("category")]
    public string Category { get; set; } = "";

    [Column("description")]
    public string Description { get; set; } = "";

    [Column("tags")]
    public string[] Tags { get; set; } = Array.Empty<string>();
}
