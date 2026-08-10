using System.ComponentModel.DataAnnotations;

namespace Raindrop.AppConfiguration.Tests;

public class DataAnnotationsValidatorTests
{
    private readonly DataAnnotationsValidator _sut = new();

    public class ValidConfig
    {
        [Required]
        public string Name { get; set; } = "Default";

        [Range(1, 100)]
        public int Count { get; set; } = 10;
    }

    public class InvalidConfig
    {
        [Required]
        public string? Name { get; set; }

        [Range(1, 100)]
        public int Count { get; set; } = 999;
    }

    public class NestedConfig
    {
        [Required]
        public string Name { get; set; } = "Root";

        public SubConfig Sub { get; set; } = new();
    }

    public class SubConfig
    {
        [Required]
        [StringLength(10, MinimumLength = 3)]
        public string? Label { get; set; }
    }

    public class CyclicalConfig
    {
        public string Name { get; set; } = "Cyclic";

        public CyclicalConfig? SelfRef { get; set; }
    }

    public class CollectionConfig
    {
        public List<ItemConfig> Items { get; set; } = [];
    }

    public class ItemConfig
    {
        [Required]
        public string? Id { get; set; }
    }

    [Fact]
    public void TryValidateObjectRecursive_ValidObject_ReturnsTrue()
    {
        var obj = new ValidConfig { Name = "Test", Count = 50 };
        var results = new List<ValidationResult>();

        var valid = _sut.TryValidateObjectRecursive(obj, results);

        Assert.True(valid);
        Assert.Empty(results);
    }

    [Fact]
    public void TryValidateObjectRecursive_InvalidObject_ReturnsFalse()
    {
        var obj = new InvalidConfig { Name = null, Count = 999 };
        var results = new List<ValidationResult>();

        var valid = _sut.TryValidateObjectRecursive(obj, results);

        Assert.False(valid);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void TryValidateObjectRecursive_NestedValidation_FindsNestedErrors()
    {
        var obj = new NestedConfig
        {
            Name = "Root",
            Sub = new SubConfig { Label = "AB" }
        };
        var results = new List<ValidationResult>();

        var valid = _sut.TryValidateObjectRecursive(obj, results);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Any(m => m.Contains("Label")));
    }

    [Fact]
    public void TryValidateObjectRecursive_CyclicalGraph_DoesNotStackOverflow()
    {
        var obj = new CyclicalConfig();
        obj.SelfRef = obj;

        var results = new List<ValidationResult>();

        var valid = _sut.TryValidateObjectRecursive(obj, results);

        Assert.True(valid);
    }

    [Fact]
    public void TryValidateObjectRecursive_CollectionItems_ValidatesEach()
    {
        var obj = new CollectionConfig
        {
            Items = [new ItemConfig { Id = "valid" }, new ItemConfig { Id = null }]
        };
        var results = new List<ValidationResult>();

        var valid = _sut.TryValidateObjectRecursive(obj, results);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Any(m => m.StartsWith("Items")));
    }

    [Fact]
    public void TryValidateObjectRecursive_NullObject_ReturnsTrue()
    {
        var results = new List<ValidationResult>();

        var valid = _sut.TryValidateObjectRecursive<object?>(null, results);

        Assert.True(valid);
    }
}
